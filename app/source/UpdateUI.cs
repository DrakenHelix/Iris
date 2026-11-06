using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Iris {
    public sealed partial class NotchWindow {
        void BuildUpdateSettings(StackPanel panel) {
            SettingsLabel(panel,L.T("Updates"));
            var row=new StackPanel{Margin=new Thickness(13,9,13,11)};
            var state=Text(L.T("Iris prüft auf Wunsch nach neuen Versionen auf GitHub."),12,ink);
            state.TextWrapping=TextWrapping.Wrap;row.Children.Add(state);
            var notes=Text("",11,muted);
            notes.TextWrapping=TextWrapping.Wrap;notes.Margin=new Thickness(0,5,0,0);notes.Visibility=Visibility.Collapsed;row.Children.Add(notes);
            Button action=null;
            IrisUpdate available=null;
            CancellationTokenSource operation=null;
            row.Unloaded+=delegate{if(operation!=null)operation.Cancel();};
            action=Btn(L.T("Nach Updates suchen"),async delegate {
                if(uiTesting||shuttingDown||operation!=null)return;
                var cancel=new CancellationTokenSource();operation=cancel;action.IsEnabled=false;
                bool installed=DataLocation.IsInstalled(root);
                string staged=null;
                try {
                    if(available==null){
                        state.Text=L.T("Suche nach Updates …");
                        available=await IrisUpdate.CheckAsync(installed,cancel.Token);
                        if(cancel.IsCancellationRequested)return;
                        if(available==null){state.Text=L.T("Iris ist auf dem neuesten Stand.");return;}
                        state.Text=L.T("Neue Version verfügbar: ")+available.Version;
                        string excerpt=string.Join(Environment.NewLine,available.Notes.Replace("\r","").Split('\n')
                            .Select(line=>line.Trim()).Where(line=>line.Length>0).Take(5));
                        notes.Text=excerpt.Length>420?excerpt.Substring(0,420)+"…":excerpt;
                        notes.Visibility=notes.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
                        action.Content=L.T("Herunterladen und installieren");
                        return;
                    }
                    state.Text=L.T("Update wird heruntergeladen …");
                    var progress=new Progress<int>(value=>{
                        if(operation==cancel&&!cancel.IsCancellationRequested)
                            state.Text=value<100?L.T("Download läuft: ")+value+" %":L.T("Update wird geprüft …");
                    });
                    staged=await available.DownloadAsync(root,installed,progress,cancel.Token);
                    if(cancel.IsCancellationRequested||shuttingDown)return;
                    using(var current=Process.GetCurrentProcess()){
                        string arguments=(installed?"--update ":"--apply-portable-update ")+IrisUpdateProcess.Arguments(current);
                        arguments+=" "+L.Language;
                        using(var helper=Process.Start(new ProcessStartInfo(staged,arguments){
                            WorkingDirectory=Path.GetDirectoryName(staged),UseShellExecute=false
                        })){if(helper==null)throw new IOException("Iris update process did not start.");}
                    }
                    staged=null;
                    Close();
                } catch(Exception error){
                    if(!cancel.IsCancellationRequested&&!shuttingDown){
                        state.Text=L.T("Das Update konnte nicht vorbereitet werden.");
                        notes.Text=error is OperationCanceledException?L.T("Zeitüberschreitung. Versuche es erneut."):error.Message;
                        notes.Visibility=Visibility.Visible;
                    }
                } finally {
                    if(staged!=null)IrisUpdate.Discard(Path.GetDirectoryName(staged));
                    operation=null;cancel.Dispose();action.IsEnabled=true;
                }
            });
            action.Height=32;action.HorizontalAlignment=HorizontalAlignment.Left;action.Margin=new Thickness(0,8,0,0);
            row.Children.Add(action);SettingsGroup(panel,row);
        }
    }
}
