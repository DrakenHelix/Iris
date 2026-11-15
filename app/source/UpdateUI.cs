using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        DispatcherTimer updateTimer;
        DateTime nextUpdateCheck, updateApplyAfter;
        CancellationTokenSource updateOperation;
        IrisUpdate availableUpdate;
        string stagedUpdate, updateStatus, updateError;
        Button updateAction, headerUpdate;
        TextBlock updateState, updateNotes, compactUpdate, lyricUpdate;

        void StartAutomaticUpdates(){
            if(uiTesting||preview)return;
            updateTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(15)};
            updateTimer.Tick+=async delegate{await RunAutomaticUpdate();};
            if(prefs.AutomaticUpdates)updateTimer.Start();
        }

        void SetAutomaticUpdates(bool enabled){
            prefs.AutomaticUpdates=enabled;
            if(enabled){nextUpdateCheck=DateTime.MinValue;if(updateTimer!=null)updateTimer.Start();}
            else StopAutomaticUpdates();
            RefreshUpdateSettings();
        }

        void StopAutomaticUpdates(){
            if(updateTimer!=null)updateTimer.Stop();
            if(updateOperation!=null)updateOperation.Cancel();
            if(stagedUpdate!=null){IrisUpdate.Discard(Path.GetDirectoryName(stagedUpdate));stagedUpdate=null;}
        }

        bool CanApplyAutomaticUpdate(){
            return !settingsReadOnly&&!isOpen&&!cursorAvoiding&&!designFullscreen&&!gamingVisible
                &&!countdown.Running&&!timerFinished&&!isPlaying&&!modalOpen&&settings==null&&addonWindow==null
                &&(addonEngine==null||addonEngine.ActiveCount==0)&&IdleSeconds()>=120;
        }

        async Task RunAutomaticUpdate(){
            if(shuttingDown||!prefs.AutomaticUpdates||updateOperation!=null)return;
            var now=DateTime.UtcNow;
            if(stagedUpdate!=null){
                if(now>=updateApplyAfter&&CanApplyAutomaticUpdate())ApplyUpdate(true);
            }else if(now>=nextUpdateCheck){
                nextUpdateCheck=now.AddHours(6);
                await PrepareUpdate(true);
            }
        }

        async Task PrepareUpdate(bool automatic){
            if(uiTesting||shuttingDown||updateOperation!=null)return;
            var cancel=new CancellationTokenSource();updateOperation=cancel;
            string prepared=null;
            updateError=null;
            try{
                bool installed=DataLocation.IsInstalled(root);
                if(automatic||availableUpdate==null){
                    if(!automatic)updateStatus=L.T("Suche nach Updates …");
                    RefreshUpdateSettings();
                    var release=await IrisUpdate.CheckAsync(installed,cancel.Token);
                    if(cancel.IsCancellationRequested||shuttingDown)return;
                    bool versionChanged=(availableUpdate==null?null:availableUpdate.Version)!=(release==null?null:release.Version);
                    availableUpdate=release;
                    updateStatus=release==null?L.T("Iris ist auf dem neuesten Stand."):null;
                    if(versionChanged)RefreshUpdateIndicators();
                    if(release==null||!automatic||prefs.LastAutomaticUpdateAttempt==release.Version.ToString())return;
                }
                updateStatus=L.T("Update wird heruntergeladen …");
                RefreshUpdateSettings();
                var progress=new Progress<int>(value=>{
                    if(updateOperation!=cancel||cancel.IsCancellationRequested)return;
                    updateStatus=value<100?L.T("Download läuft: ")+value+" %":L.T("Update wird geprüft …");
                    RefreshUpdateSettings();
                });
                prepared=await availableUpdate.DownloadAsync(root,installed,progress,cancel.Token);
                if(cancel.IsCancellationRequested||shuttingDown)return;
                stagedUpdate=prepared;prepared=null;
                updateApplyAfter=DateTime.UtcNow.AddSeconds(30);
                updateStatus=null;
            }catch(Exception error){
                if(!cancel.IsCancellationRequested&&!shuttingDown){
                    if(automatic)nextUpdateCheck=DateTime.UtcNow.AddHours(1);
                    if(!automatic||availableUpdate!=null)SetUpdateError(error);
                }
            }finally{
                if(prepared!=null)IrisUpdate.Discard(Path.GetDirectoryName(prepared));
                if(cancel.IsCancellationRequested)updateStatus=null;
                updateOperation=null;cancel.Dispose();
                if(!shuttingDown)RefreshUpdateSettings();
            }
            if(!automatic&&stagedUpdate!=null)ApplyUpdate(false);
        }

        void ApplyUpdate(bool automatic){
            if(stagedUpdate==null||shuttingDown||uiTesting)return;
            string previousAttempt=prefs.LastAutomaticUpdateAttempt;
            try{
                if(automatic){
                    prefs.LastAutomaticUpdateAttempt=availableUpdate.Version.ToString();
                    Save();
                    if(settingsStorageNote.Length>0)throw new IOException(settingsStorageNote);
                }
                bool installed=DataLocation.IsInstalled(root);
                using(var current=Process.GetCurrentProcess()){
                    string command=installed?"--update ":"--apply-portable-update ";
                    string arguments=command+IrisUpdateProcess.Arguments(current)+" "+L.Language;
                    using(var helper=Process.Start(new ProcessStartInfo(stagedUpdate,arguments){
                        WorkingDirectory=Path.GetDirectoryName(stagedUpdate),UseShellExecute=false
                    })){if(helper==null)throw new IOException("Iris update process did not start.");}
                }
                stagedUpdate=null;
                Close();
            }catch(Exception error){
                if(automatic){
                    prefs.LastAutomaticUpdateAttempt=previousAttempt;
                    Save();
                    updateApplyAfter=DateTime.UtcNow.AddHours(1);
                }
                SetUpdateError(error);
                RefreshUpdateSettings();
            }
        }

        void SetUpdateError(Exception error){
            updateStatus=L.T("Das Update konnte nicht vorbereitet werden.");
            updateError=error is OperationCanceledException?L.T("Zeitüberschreitung. Versuche es erneut."):error.Message;
        }

        bool HasUpdateNotice(){return availableUpdate!=null;}

        void BuildUpdateIndicators(Grid headerParent){
            compactUpdate=Glyph("\uE777",14,mint);
            compactUpdate.HorizontalAlignment=HorizontalAlignment.Center;
            compactUpdate.Visibility=Visibility.Collapsed;
            compact.Children.Add(compactUpdate);
            headerUpdate=Icon("\uE777","",delegate{
                ShowSettings();settingsSection="Über Iris";RenderSettingsSection();
            });
            ((TextBlock)headerUpdate.Content).Foreground=mint;
            headerUpdate.Height=28;headerUpdate.HorizontalAlignment=HorizontalAlignment.Right;
            headerUpdate.VerticalAlignment=VerticalAlignment.Top;headerUpdate.Margin=new Thickness(0,0,56,0);
            headerUpdate.Visibility=Visibility.Collapsed;
            Panel.SetZIndex(headerUpdate,6);headerParent.Children.Add(headerUpdate);
        }

        void BuildLyricUpdateIndicator(Grid lyrics){
            lyrics.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});
            lyricUpdate=Glyph("\uE777",14,mint);
            lyricUpdate.HorizontalAlignment=HorizontalAlignment.Center;
            lyricUpdate.Visibility=Visibility.Collapsed;
            Grid.SetColumn(lyricUpdate,3);lyrics.Children.Add(lyricUpdate);
        }

        void RefreshUpdateIndicators(){
            bool show=HasUpdateNotice();
            string label=show?L.T("Neue Version verfügbar: ")+availableUpdate.Version:null;
            headerUpdate.ToolTip=label;
            AutomationProperties.SetName(headerUpdate,label??"");
            AutomationProperties.SetName(compactUpdate,label??"");
            AutomationProperties.SetName(lyricUpdate,label??"");
            headerUpdate.Visibility=lyricUpdate.Visibility=show?Visibility.Visible:Visibility.Collapsed;
            ((Grid)compactLyrics.Child).ColumnDefinitions[3].Width=new GridLength(show?22:0);
            if(show)ShowEyes(false);
            UpdateCompactLyrics();
        }

        void BuildUpdateSettings(StackPanel panel){
            SettingsLabel(panel,L.T("Updates"));
            var row=new StackPanel{Margin=new Thickness(13,9,13,11)};
            updateState=Text("",12,ink);updateState.TextWrapping=TextWrapping.Wrap;row.Children.Add(updateState);
            updateNotes=Text("",11,muted);updateNotes.TextWrapping=TextWrapping.Wrap;
            updateNotes.Margin=new Thickness(0,5,0,0);row.Children.Add(updateNotes);
            updateAction=Btn(L.T("Nach Updates suchen"),async delegate{
                if(stagedUpdate!=null)ApplyUpdate(false);else await PrepareUpdate(false);
            });
            updateAction.Height=32;updateAction.HorizontalAlignment=HorizontalAlignment.Left;
            updateAction.Margin=new Thickness(0,8,0,0);row.Children.Add(updateAction);
            SettingsGroup(panel,
                SettingRow(L.T("Automatische Updates"),L.T("Iris lädt Updates im Hintergrund und installiert sie, wenn du Iris gerade nicht nutzt."),prefs.AutomaticUpdates,SetAutomaticUpdates),
                row);
            RefreshUpdateSettings();
        }

        void RefreshUpdateSettings(){
            if(updateAction==null)return;
            string available=HasUpdateNotice()?L.T("Neue Version verfügbar: ")+availableUpdate.Version:null;
            updateState.Text=updateStatus??(stagedUpdate!=null?L.T("Update bereit zur Installation."):available)
                ??L.T(prefs.AutomaticUpdates?"Iris sucht auf GitHub nach neuen Versionen.":"Iris prüft auf Wunsch nach neuen Versionen auf GitHub.");
            string notes=updateError??(availableUpdate==null?"":string.Join(Environment.NewLine,availableUpdate.Notes.Replace("\r","").Split('\n')
                .Select(line=>line.Trim()).Where(line=>line.Length>0).Take(5)));
            updateNotes.Text=notes.Length>420?notes.Substring(0,420)+"…":notes;
            updateNotes.Visibility=notes.Length>0?Visibility.Visible:Visibility.Collapsed;
            updateAction.Content=L.T(stagedUpdate!=null?"Jetzt installieren":availableUpdate!=null?"Herunterladen und installieren":"Nach Updates suchen");
            updateAction.IsEnabled=updateOperation==null;
        }
    }
}
