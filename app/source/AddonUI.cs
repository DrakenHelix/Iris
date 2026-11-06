using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        AddonDeck addonDeck=new AddonDeck();Grid addonPage;string addonLoadError="";bool addonReadOnly;
        readonly Dictionary<int,Button> addonButtons=new Dictionary<int,Button>();
        readonly Dictionary<int,TextBlock> addonLabels=new Dictionary<int,TextBlock>();
        readonly Dictionary<int,AddonStatusMark> addonMarks=new Dictionary<int,AddonStatusMark>();
        readonly HashSet<int> addonBusy=new HashSet<int>();
        readonly Dictionary<int,DispatcherTimer> addonTimers=new Dictionary<int,DispatcherTimer>();
        Window addonWindow;SettingsPageMotion addonDialogPages;UiSurfaceMotion addonDialogMotion;bool addonMayClose;
        string addonDialogPage;IAddonHost addonTestHost;
        string AddonPath {get{return System.IO.Path.Combine(root,dataFolder,"addons.json");}}
        void LoadAddons(){try{addonDeck=AddonCatalog.Load(AddonPath);}catch(Exception ex){addonLoadError=ex.Message;addonReadOnly=true;}addonSavedDeckJson=serializer.Serialize(addonDeck);}
        bool SaveAddons(){if(addonReadOnly)return false;try{AddonCatalog.Save(AddonPath,addonDeck);addonSavedDeckJson=serializer.Serialize(addonDeck);addonLoadError="";return true;}catch(Exception ex){addonLoadError=L.T("Speichern fehlgeschlagen: ")+ex.Message;if(!string.IsNullOrEmpty(addonSavedDeckJson))addonDeck=serializer.Deserialize<AddonDeck>(addonSavedDeckJson);return false;}}
        Grid BuildAddons(){
            var view=new Grid{Width=438,Height=148};var tiles=new Grid{Width=370,Height=96,Margin=new Thickness(34,34,34,18)};
            for(int i=0;i<4;i++)tiles.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<2;i++)tiles.RowDefinitions.Add(new RowDefinition());view.Children.Add(tiles);
            for(int i=0;i<8;i++){
                int index=i;var body=new Grid{Width=82,Height=39};var stack=new StackPanel{VerticalAlignment=VerticalAlignment.Center};body.Children.Add(stack);
                var icon=new Grid{Height=20,Width=32};stack.Children.Add(icon);var symbol=new AddonActionIcon();symbol.Set(null,null);symbol.HorizontalAlignment=HorizontalAlignment.Center;icon.Children.Add(symbol);
                var label=Text(L.T("Hinzufügen"),10,muted);label.HorizontalAlignment=HorizontalAlignment.Center;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxWidth=80;label.Margin=new Thickness(0,2,0,0);stack.Children.Add(label);addonLabels[i]=label;
                var mark=new AddonStatusMark(uiMotion){Width=13,Height=13,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};body.Children.Add(mark);addonMarks[i]=mark;
                var button=Btn(body,delegate{if(!addonTileDragging)RunAddon(index);});button.Height=44;button.Margin=new Thickness(3,2,3,2);button.Padding=new Thickness(2);button.Background=new SolidColorBrush(Color.FromRgb(22,22,24));Grid.SetRow(button,i/4);Grid.SetColumn(button,i%4);tiles.Children.Add(button);addonButtons[i]=button;
                AttachAddonReorder(button,index);button.Tag=symbol;button.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.F2){ShowAddonEditor(index);e.Handled=true;}};
                var menu=new ContextMenu();UiMenus.Style(menu);var edit=new MenuItem{Header=L.T("Kachel bearbeiten")};edit.Click+=delegate{ShowAddonEditor(index);};menu.Items.Add(edit);var clear=new MenuItem{Header=L.T("Kachel leeren")};clear.Click+=delegate{ClearAddonSlot(index);};menu.Items.Add(clear);var cancel=new MenuItem{Header=L.T("Ausführung abbrechen")};cancel.Click+=delegate{CancelAddonAt(index);};menu.Items.Add(cancel);new UiMenuMotion(cancel,uiMotion);var moves=new MenuItem{Header=L.T("Mit Kachel tauschen")};menu.Items.Add(moves);
                for(int j=0;j<8;j++){int destination=j;if(j==i)continue;var move=new MenuItem{Header=L.T("Kachel ")+(j+1)};move.Click+=delegate{SwapAddonSlots(index,destination);};moves.Items.Add(move);new UiMenuMotion(move,uiMotion);}button.ContextMenu=menu;foreach(var item in new[]{edit,clear,moves})new UiMenuMotion(item,uiMotion);menu.Opened+=delegate{var binding=addonDeck.At(index);cancel.IsEnabled=binding!=null&&addonEngine!=null&&addonEngine.IsRunning(binding.BindingId);clear.IsEnabled=addonDeck.At(index)!=null;var motion=new UiSurfaceMotion(uiMotion,menu,null,true);motion.Show(true);};
            }
            SetupAddonReorder(view);RefreshAddonTiles();return view;
        }
        static string AddonGlyph(string kind){if(kind=="screenshot")return "\uE722";if(kind=="folders")return "\uE8B7";if(kind=="steam")return "\uE7FC";if(kind=="discord-mute")return "\uE720";if(kind=="discord-deafen")return "\uE767";if(kind=="discord-share")return "\uE7F4";if(kind=="website")return "\uE774";if(kind=="hotkey")return "\uE765";var definition=AddonRegistry.Find(kind);return definition==null?"\uE8A5":definition.Icon;}
        Brush AddonColor(string package){return new SolidColorBrush(package=="iris.discord"?Color.FromRgb(149,154,255):package=="iris.steam"?Color.FromRgb(133,193,234):package=="iris.windows"?Color.FromRgb(130,202,255):Color.FromRgb(209,175,255));}
        void RefreshAddonTiles(){foreach(var item in addonButtons){int i=item.Key;var slot=addonDeck.At(i);bool enabled=addonDeck.Enabled(slot);var button=item.Value;var symbol=(AddonActionIcon)button.Tag;PaintSteamTileIcon(symbol,slot==null?null:slot.Action);symbol.Opacity=slot==null||enabled?1:.4;addonLabels[i].Text=slot==null?L.T("Hinzufügen"):AddonCatalog.DisplayTitle(slot.Action);addonLabels[i].Foreground=enabled?ink:muted;button.ToolTip=addonReadOnly?addonLoadError:slot==null?L.T("Kachel ")+(i+1)+" belegen":AddonCatalog.DisplayTitle(slot.Action)+"\n"+AddonCatalog.Detail(slot.Action)+(enabled?"":L.T("\nAdd-on deaktiviert"))+L.T("\nRechtsklick oder F2 zum Bearbeiten");System.Windows.Automation.AutomationProperties.SetName(button,L.T("Kachel ")+(i+1)+": "+(slot==null?L.T("Hinzufügen"):AddonCatalog.DisplayTitle(slot.Action))+(slot!=null&&!enabled?" · deaktiviert":""));}WarmSteamTileIcons();}
        void ClearAddonSlot(int index){if(addonReadOnly)return;CancelAddonAt(index);addonDeck.Slots.RemoveAll(s=>s.Position==index);SaveAddons();ResetAddonFeedback(index);RefreshAddonTiles();RefreshAddonSettings();}
        bool SwapAddonSlots(int a,int b){if(addonReadOnly)return false;var first=addonDeck.At(a);var second=addonDeck.At(b);if(first!=null)first.Position=b;if(second!=null)second.Position=a;bool saved=SaveAddons();ResetAddonFeedback(a);ResetAddonFeedback(b);RefreshAddonTiles();RefreshAddonSettings();return saved;}
        void ResetAddonFeedback(int index){DispatcherTimer timer;if(addonTimers.TryGetValue(index,out timer)){timer.Stop();addonTimers.Remove(index);}addonMarks[index].Set(0);}
        void RunAddon(int index){BeginAddonAction(index,false);}
        void AddonFeedback(int index,string text,int state){ResetAddonFeedback(index);addonMarks[index].Set(state);addonLabels[index].Text=state==3?L.T("Nicht ausgeführt"):L.T("Angefordert");addonButtons[index].ToolTip=text+L.T("\nRechtsklick zum Bearbeiten");var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(state==3?6:2)};addonTimers[index]=timer;timer.Tick+=delegate{timer.Stop();if(shuttingDown)return;addonTimers.Remove(index);addonMarks[index].Set(0);RefreshAddonTiles();};timer.Start();}
        void RefreshAddonSettings(){if(settings!=null&&settingsSection=="Add-ons")RenderSettingsSection();}
        void BuildAddonSettings(StackPanel panel){
            if(!string.IsNullOrEmpty(addonLoadError)){var error=Text(addonLoadError,11,new SolidColorBrush(Color.FromRgb(255,136,145)));error.TextWrapping=TextWrapping.Wrap;error.Margin=new Thickness(0,0,0,8);panel.Children.Add(error);}
            SettingsLabel(panel,L.T("Deine Kacheln · 4 × 2"));var slots=new Grid{Height=118,Margin=new Thickness(0,0,0,15)};for(int i=0;i<4;i++)slots.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<2;i++)slots.RowDefinitions.Add(new RowDefinition());
            for(int i=0;i<8;i++){int index=i;var slot=addonDeck.At(i);var box=new StackPanel();box.Children.Add(Text((i+1).ToString("00"),10,muted));var title=Text(slot==null?L.T("+ Belegen"):AddonCatalog.DisplayTitle(slot.Action),11,ink);title.TextTrimming=TextTrimming.CharacterEllipsis;box.Children.Add(title);var button=Btn(box,delegate{ShowAddonEditor(index);});button.Margin=new Thickness(2);button.Padding=new Thickness(8);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Background=new SolidColorBrush(Color.FromRgb(39,39,41));button.IsEnabled=!addonReadOnly;Grid.SetRow(button,i/4);Grid.SetColumn(button,i%4);slots.Children.Add(button);}panel.Children.Add(slots);
            SettingsLabel(panel,L.T("Erweiterungen"));var count=addonDeck.Packages.Count;SettingsGroup(panel,SettingsInfo(count==0?L.T("Noch keine Add-ons hinzugefügt"):count+" Add-ons · "+addonDeck.Packages.Count(p=>p.Enabled)+L.T(" aktiv"),L.T("Windows, Discord, Steam, ChatGPT und eigene Aktionen. Suche, Abhängigkeiten und sichere Ausführung.")));
            var manage=Btn(L.T("Add-ons verwalten"),ShowAddonLibrary);manage.Height=34;manage.Background=new SolidColorBrush(Color.FromRgb(64,64,67));panel.Children.Add(manage);var hint=Text(L.T("In der Notch: Kachel anklicken zum Ausführen. Rechtsklick oder F2 zum Bearbeiten, Leeren oder Tauschen."),11,muted);hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new Thickness(0,12,0,0);panel.Children.Add(hint);
        }
        void CloseAddonWindow(){if(addonWindow!=null)addonWindow.Close();}
        void OpenAddonWindow(string title){
            if(addonWindow!=null){addonWindow.Activate();return;}modalOpen=true;collapse.Stop();addonMayClose=false;addonDialogPage=null;
            addonWindow=new Window{Title="Iris · "+title,Width=590,Height=550,WindowStartupLocation=WindowStartupLocation.CenterScreen,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Foreground=ink,FontFamily=new FontFamily("Segoe UI"),ResizeMode=ResizeMode.NoResize,Topmost=true,ShowInTaskbar=false,Owner=settings??this,UseLayoutRounding=true};addonWindow.Resources=Resources;
            var shell=new Border{Background=new SolidColorBrush(Color.FromRgb(28,28,30)),BorderBrush=new SolidColorBrush(Color.FromRgb(55,55,57)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),Padding=new Thickness(22,16,22,20)};addonWindow.Content=shell;
            var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(32)});layout.RowDefinitions.Add(new RowDefinition());shell.Child=layout;
            var heading=Text(title,17,ink);heading.FontWeight=FontWeights.SemiBold;heading.VerticalAlignment=VerticalAlignment.Top;layout.Children.Add(heading);heading.MouseLeftButtonDown+=delegate{addonWindow.DragMove();};var close=Btn("×",CloseAddonWindow);close.Width=28;close.Height=26;close.HorizontalAlignment=HorizontalAlignment.Right;close.VerticalAlignment=VerticalAlignment.Top;close.Padding=new Thickness(0);close.Background=Brushes.Transparent;layout.Children.Add(close);
            addonDialogPages=new SettingsPageMotion(uiMotion);Grid.SetRow(addonDialogPages,1);layout.Children.Add(addonDialogPages);Window window=addonWindow;
            addonDialogMotion=new UiSurfaceMotion(uiMotion,shell,delegate{Dispatcher.BeginInvoke(new Action(delegate{if(addonWindow==window&&addonDialogMotion.Closing){addonMayClose=true;window.Close();}}));},true);
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){StopDiscordPicker();if(!addonMayClose&&!shuttingDown){e.Cancel=true;addonDialogMotion.Close();}};
            window.Closed+=delegate{DisposeAddonDialogState();addonDialogMotion.Dispose();addonDialogPages.Dispose();addonWindow=null;addonDialogPages=null;addonDialogMotion=null;addonNameBox=addonTargetBox=addonArgumentsBox=addonProcessBox=addonShortcutBox=addonPathsBox=null;addonSaveButton=null;addonEditorError=null;modalOpen=false;if(!shuttingDown&&!PointerInsideNotch())collapse.Start();};
            window.PreviewMouseDown+=delegate{if(addonDialogMotion.Closing)addonDialogMotion.Show(false);};window.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape)CloseAddonWindow();};addonDialogMotion.Show(true);window.Show();
        }
        void ShowAddonDialogPage(string key,FrameworkElement view,int direction){if(key!="discord-channels")StopDiscordPicker();if(key!="download-install")StopDownloadGuide();if(key!="steam-games")StopSteamPicker();addonDialogPages.Show(key,view,addonDialogPage==null?0:direction);addonDialogPage=key;}
        StackPanel AddonPanel(){return new StackPanel{Margin=new Thickness(0,7,0,0)};}
        TextBlock AddonNote(string value){var text=Text(value,12,muted);text.TextWrapping=TextWrapping.Wrap;text.Margin=new Thickness(0,0,0,12);return text;}
        Button AddonChoice(string title,string detail,Action action){var stack=new StackPanel();stack.Children.Add(Text(title,13,ink));var note=Text(detail,11,muted);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,3,0,0);stack.Children.Add(note);var button=Btn(stack,action);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(13,10,13,10);button.Margin=new Thickness(0,0,0,8);button.Background=new SolidColorBrush(Color.FromRgb(39,39,41));return button;}
        FrameworkElement AddonScroll(UIElement child){return new MixerScroll(uiMotion){Content=child,Height=429,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};}
        void ShowAddonLibrary(){if(addonWindow!=null){addonWindow.Activate();return;}OpenAddonWindow("Add-ons");RenderAddonLibrary();RefreshAddonEnvironment();}
        void RenderAddonLibrary(){RenderAddonLibrary021();}
        void ShowAddonPackageTools(){var panel=AddonPanel();panel.Children.Add(AddonChoice(L.T("‹ Zurück"),L.T("Alle Erweiterungen"),RenderAddonLibrary));foreach(var entry in addonDeck.Packages.ToArray()){var package=entry;var row=new Grid{Margin=new Thickness(0,0,0,8)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(94)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(94)});row.Children.Add(Text(AddonCatalog.DisplayName(package),13,ink));var export=Btn(L.T("Exportieren"),delegate{var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Iris Add-on|*.iris-addon.json",FileName=package.Id+".iris-addon.json"};if(dialog.ShowDialog(addonWindow)==true){try{File.WriteAllText(dialog.FileName,serializer.Serialize(AddonCatalog.ForExport(package,addonDeck)));}catch(Exception ex){MessageBox.Show(addonWindow,ex.Message,L.T("Export fehlgeschlagen"));}}});Grid.SetColumn(export,1);row.Children.Add(export);var remove=Btn(L.T("Entfernen"),delegate{if(addonReadOnly)return;CancelAddonPackage(package.Id);addonDeck.Packages.Remove(package);addonDeck.Slots.RemoveAll(s=>s.PackageId==package.Id);SaveAddons();RefreshAddonTiles();RefreshAddonSettings();ShowAddonPackageTools();});remove.IsEnabled=!addonReadOnly;Grid.SetColumn(remove,2);row.Children.Add(remove);panel.Children.Add(row);}ShowAddonDialogPage("packages",AddonScroll(panel),1);}
        void ImportAddon(){
            if(addonReadOnly)return;var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Iris Add-on|*.iris-addon.json|JSON|*.json"};if(dialog.ShowDialog(addonWindow)!=true)return;
            try{ReviewAddonPackage(AddonCatalog.ReadPackage(dialog.FileName));}catch(Exception ex){var panel=AddonPanel();panel.Children.Add(AddonNote(L.T("Import nicht möglich: ")+ex.Message));panel.Children.Add(AddonChoice(L.T("‹ Zurück"),L.T("Es wurde nichts verändert"),RenderAddonLibrary));ShowAddonDialogPage("import-error",panel,1);}
        }
        void ReviewAddonPackage(AddonPackage package){var panel=AddonPanel();panel.Children.Add(Text(package.Name+" · "+package.Version,17,ink));panel.Children.Add(AddonNote(package.Description));panel.Children.Add(AddonNote(L.T("Diese Aktionen werden hinzugefügt. Erst ein Klick auf eine belegte Kachel führt sie aus.")));foreach(var action in package.Actions)panel.Children.Add(AddonIconRow(SettingsInfo(action.Title,action.Kind+" · "+AddonCatalog.Detail(action)),action.Kind,null));bool duplicate=addonDeck.Packages.Any(p=>p.Id==package.Id);panel.Children.Add(AddonChoice(duplicate?L.T("Bereits installiert"):L.T("Add-on hinzufügen"),duplicate?L.T("Die vorhandene Version und Belegung bleiben erhalten."):L.T("Anschließend kannst du die Aktionen Kacheln zuweisen."),delegate{if(!duplicate&&!addonReadOnly){if(addonDeck.Packages.Count>=40)return;addonDeck.Packages.Add(package);SaveAddons();RefreshAddonSettings();}RenderAddonLibrary();}));panel.Children.Add(AddonChoice(L.T("Abbrechen"),L.T("Zur Erweiterungsübersicht"),RenderAddonLibrary));ShowAddonDialogPage("review",AddonScroll(panel),1);}
    }
    public sealed class AddonStatusMark : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;int state;bool shown;double phase,velocity,busy,busyVelocity,success,successVelocity,error,errorVelocity;public double Phase{get{return phase;}}
        public AddonStatusMark(UiMotionClock clock){this.clock=clock;clock.Register(this);}
        public void Set(int value){shown=value!=0;if(shown)state=value;clock.Wake(this);}
        public bool Step(double dt,bool animate){
            if(animate){PlayerSpring.Step(ref phase,ref velocity,shown?1:0,dt,.24,1);PlayerSpring.Step(ref busy,ref busyVelocity,state==1?1:0,dt,.22,1);PlayerSpring.Step(ref success,ref successVelocity,state==2?1:0,dt,.22,1);PlayerSpring.Step(ref error,ref errorVelocity,state==3?1:0,dt,.22,1);}
            else{phase=shown?1:0;busy=state==1?1:0;success=state==2?1:0;error=state==3?1:0;velocity=busyVelocity=successVelocity=errorVelocity=0;}
            InvalidateVisual();return Math.Abs(phase-(shown?1:0))>.001||Math.Abs(velocity)>.01||Math.Abs(busy-(state==1?1:0))>.001||Math.Abs(busyVelocity)>.01||Math.Abs(success-(state==2?1:0))>.001||Math.Abs(successVelocity)>.01||Math.Abs(error-(state==3?1:0))>.001||Math.Abs(errorVelocity)>.01;
        }
        public void Snap(){Step(0,false);}
        protected override void OnRender(DrawingContext dc){
            if(phase<.001)return;dc.PushOpacity(Math.Max(0,Math.Min(1,phase)));dc.PushTransform(new ScaleTransform(.6+.4*phase,.6+.4*phase,6.5,6.5));dc.DrawEllipse(Brushes.Black,null,new Point(6.5,6.5),6.5,6.5);
            var pen=new Pen(Brushes.LightGray,1.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};dc.PushOpacity(Math.Max(0,Math.Min(1,busy)));dc.DrawEllipse(null,pen,new Point(6.5,6.5),3,3);dc.Pop();
            pen=new Pen(new SolidColorBrush(Color.FromRgb(157,244,208)),1.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};dc.PushOpacity(Math.Max(0,Math.Min(1,success)));dc.DrawGeometry(null,pen,Geometry.Parse("M 3,6.5 L 5.5,9 L 10,4"));dc.Pop();
            pen=new Pen(Brushes.LightCoral,1.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};dc.PushOpacity(Math.Max(0,Math.Min(1,error)));dc.DrawLine(pen,new Point(4,4),new Point(9,9));dc.DrawLine(pen,new Point(4,9),new Point(9,4));dc.Pop();dc.Pop();dc.Pop();
        }
    }
}
