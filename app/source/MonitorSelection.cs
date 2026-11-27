using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace Iris {
    public sealed class MonitorPreference {
        public string Id,Name;
    }

    internal sealed class IrisMonitor {
        internal string Id,Name,Device;
        internal System.Drawing.Rectangle Bounds;
        internal bool Primary,Stable;
    }

    internal static class MonitorTopology {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct DisplayDevice {
            internal int Size;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]internal string Name;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]internal string Description;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]internal string Id;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]internal string Key;
        }
        [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="EnumDisplayDevicesW")]
        [return:MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumDevices(string device,uint index,ref DisplayDevice result,uint flags);

        internal static IrisMonitor[] Read(){
            return Forms.Screen.AllScreens.Select(screen=>{
                var monitor=new IrisMonitor{Device=screen.DeviceName,Bounds=screen.Bounds,Primary=screen.Primary,
                    Id="gdi:"+screen.DeviceName,Name=screen.DeviceName};
                for(uint i=0;i<32;i++){
                    var info=new DisplayDevice{Size=Marshal.SizeOf(typeof(DisplayDevice))};
                    if(!EnumDevices(screen.DeviceName,i,ref info,1))break;
                    if((info.Flags&1)==0||string.IsNullOrWhiteSpace(info.Id))continue;
                    monitor.Id=info.Id;monitor.Stable=true;
                    if(!string.IsNullOrWhiteSpace(info.Description))monitor.Name=info.Description;
                    break;
                }
                return monitor;
            }).ToArray();
        }

        internal static IrisMonitor Choose(IrisMonitor[] online,List<MonitorPreference> order,int legacyIndex){
            if(order!=null&&order.Count>0){
                foreach(var saved in order.Where(p=>p!=null).Take(64)){
                    var found=online.FirstOrDefault(m=>string.Equals(m.Id,saved.Id,StringComparison.OrdinalIgnoreCase));
                    if(found!=null)return found;
                }
            }else if(legacyIndex>=0&&legacyIndex<online.Length)return online[legacyIndex];
            return online.FirstOrDefault(m=>m.Primary)??online.FirstOrDefault();
        }

        internal static List<MonitorPreference> Remember(IrisMonitor[] online,List<MonitorPreference> saved,int legacyIndex){
            var result=(saved??new List<MonitorPreference>()).Where(m=>m!=null&&!string.IsNullOrWhiteSpace(m.Id)&&m.Id.Length<=256)
                .GroupBy(m=>m.Id,StringComparer.OrdinalIgnoreCase).Select(g=>new MonitorPreference{Id=g.Key,Name=g.First().Name}).Take(64).ToList();
            if(result.Count==0){
                var preferred=Choose(online,null,legacyIndex);
                if(preferred!=null&&preferred.Stable)result.Add(new MonitorPreference{Id=preferred.Id,Name=preferred.Name});
            }
            foreach(var monitor in online.Where(m=>m.Stable)){
                var existing=result.FirstOrDefault(p=>string.Equals(p.Id,monitor.Id,StringComparison.OrdinalIgnoreCase));
                if(existing!=null)existing.Name=monitor.Name;
                else if(result.Count<64)result.Add(new MonitorPreference{Id=monitor.Id,Name=monitor.Name});
            }
            return result;
        }
    }

    public sealed partial class NotchWindow {
        IrisMonitor[] monitorScreens=new IrisMonitor[0];
        DispatcherTimer monitorRefresh;
        int monitorGeneration;
        StackPanel monitorRows;
        TextBlock monitorStatus;
        readonly Dictionary<string,Grid> monitorRowViews=new Dictionary<string,Grid>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,UiSelectionMotion> monitorSelections=new Dictionary<string,UiSelectionMotion>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,Button> monitorChoices=new Dictionary<string,Button>(StringComparer.OrdinalIgnoreCase);

        void SetupMonitors(){
            monitorRefresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};
            monitorRefresh.Tick+=async delegate{monitorRefresh.Stop();await RefreshMonitors();};
            if(!uiTesting)monitorRefresh.Start();
        }

        void QueueMonitorRefresh(){
            if(shuttingDown||uiTesting||monitorRefresh==null)return;
            monitorGeneration++;
            monitorRefresh.Stop();monitorRefresh.Start();
        }

        async Task RefreshMonitors(){
            int generation=++monitorGeneration;
            try{
                var screens=await Task.Run(()=>MonitorTopology.Read());
                if(shuttingDown||generation!=monitorGeneration||screens.Length==0)return;
                ApplyMonitorTopology(screens);
            }catch(InvalidOperationException){}
        }

        void ApplyMonitorTopology(IrisMonitor[] screens){
            monitorScreens=screens;
            var remembered=MonitorTopology.Remember(screens,prefs.MonitorPriority,prefs.Monitor);
            bool changed=serializer.Serialize(remembered)!=serializer.Serialize(prefs.MonitorPriority);
            prefs.MonitorPriority=remembered;
            if(changed)Save();
            CancelCursorAvoidance();Place();RefreshMonitorRows();
        }

        IrisMonitor SelectedMonitor(){return MonitorTopology.Choose(monitorScreens,prefs.MonitorPriority,prefs.Monitor);}
        System.Drawing.Rectangle SelectedMonitorBounds(){
            var chosen=SelectedMonitor();
            if(chosen!=null)return chosen.Bounds;
            // Initial placement while Windows device identities are being read.
            var fallback=Forms.Screen.PrimaryScreen;
            return fallback==null?new System.Drawing.Rectangle(0,0,1040,320):fallback.Bounds;
        }

        void BuildMonitorSettings(StackPanel panel){
            SettingsLabel(panel,L.T("Bildschirm-Reihenfolge"));
            monitorRows=new StackPanel();monitorRowViews.Clear();monitorSelections.Clear();monitorChoices.Clear();
            monitorStatus=Text("",11,muted);monitorStatus.TextWrapping=TextWrapping.Wrap;
            monitorStatus.Margin=new Thickness(13,7,13,10);
            var content=new StackPanel();
            var note=Text(L.T("Oben steht deine erste Wahl. Mit den Pfeilen legst du die Ersatzbildschirme fest. Iris kehrt automatisch zurück."),11,muted);
            note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(13,10,13,5);
            content.Children.Add(note);content.Children.Add(monitorRows);content.Children.Add(monitorStatus);
            SettingsGroup(panel,content);RefreshMonitorRows();
        }

        void MoveMonitor(string id,int target){
            if(settingsReadOnly)return;
            var saved=prefs.MonitorPriority;
            int current=saved==null?-1:saved.FindIndex(m=>string.Equals(m.Id,id,StringComparison.OrdinalIgnoreCase));
            if(current<0||target<0||target>=saved.Count||current==target)return;
            var next=saved.ToList();var item=next[current];next.RemoveAt(current);next.Insert(target,item);
            prefs.MonitorPriority=next;Save();
            if(settingsStorageNote.Length>0){prefs.MonitorPriority=saved;RefreshMonitorRows();monitorStatus.Text=settingsStorageNote;return;}
            CancelCursorAvoidance();Place();RefreshMonitorRows();
        }

        void RefreshMonitorRows(){
            if(monitorRows==null||settings==null||settingsSection!="Anzeige")return;
            var order=prefs.MonitorPriority??new List<MonitorPreference>();
            var selected=SelectedMonitor();
            for(int i=0;i<order.Count;i++){
                var preference=order[i];string id=preference.Id;
                Grid row;
                if(!monitorRowViews.TryGetValue(id,out row)){
                    row=new Grid{Margin=new Thickness(5,2,5,2)};
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                    row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                    var choose=Btn("",delegate{MoveMonitor(id,0);});choose.Height=42;choose.Padding=new Thickness(8,2,8,2);
                    choose.HorizontalContentAlignment=HorizontalAlignment.Left;
                    var selection=new UiSelectionMotion(choose,i==0);uiMotion.Register(selection);
                    monitorSelections[id]=selection;monitorChoices[id]=choose;row.Children.Add(choose);
                    for(int direction=-1;direction<=1;direction+=2){
                        int step=direction;
                        var button=Icon(direction<0?"\uE70E":"\uE70D",L.T(direction<0?"Bildschirm nach oben":"Bildschirm nach unten"),delegate{
                            int priorityIndex=prefs.MonitorPriority.FindIndex(m=>string.Equals(m.Id,id,StringComparison.OrdinalIgnoreCase));MoveMonitor(id,priorityIndex+step);
                        });
                        button.Width=28;button.Height=34;button.VerticalAlignment=VerticalAlignment.Center;
                        AutomationProperties.SetName(button,Convert.ToString(button.ToolTip));
                        Grid.SetColumn(button,direction<0?1:2);row.Children.Add(button);
                    }
                    monitorRowViews[id]=row;monitorRows.Children.Add(row);
                }
                // Move the existing row so its button state and animation stay alive.
                int position=monitorRows.Children.IndexOf(row);
                if(position!=i){monitorRows.Children.Remove(row);monitorRows.Children.Insert(i,row);}
                var online=monitorScreens.FirstOrDefault(m=>string.Equals(m.Id,id,StringComparison.OrdinalIgnoreCase));
                string name=string.IsNullOrWhiteSpace(preference.Name)?L.T("Bildschirm"):preference.Name;
                string detail=online==null?L.T("Nicht verbunden"):online.Device.Replace(@"\\.\","")+" · "+online.Bounds.Width+" × "+online.Bounds.Height+(online.Primary?L.T(" · Hauptbildschirm"):"");
                string text=(i+1)+". "+name;
                var label=new StackPanel();
                label.Children.Add(new TextBlock{Text=text,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis});
                label.Children.Add(new TextBlock{Text=detail,FontSize=10,Foreground=muted,TextTrimming=TextTrimming.CharacterEllipsis});
                var choice=monitorChoices[id];choice.Content=label;choice.ToolTip=text+"\n"+detail;
                AutomationProperties.SetName(choice,text+" · "+detail);choice.IsEnabled=!settingsReadOnly;
                monitorSelections[id].Select(i==0);uiMotion.Wake(monitorSelections[id]);
                ((Button)row.Children[1]).IsEnabled=!settingsReadOnly&&i>0;
                ((Button)row.Children[2]).IsEnabled=!settingsReadOnly&&i<order.Count-1;
            }
            monitorStatus.Text=selected==null?L.T("Bildschirme werden ermittelt …"):L.T("Aktuell auf: ")+selected.Device.Replace(@"\\.\","")+" · "+selected.Name
                +(selected.Stable?"":L.T(" · Gerätekennung nicht verfügbar"));
        }
    }
}
