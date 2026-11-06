using System;using System.Globalization;using System.IO;using System.Linq;using System.Windows;using System.Windows.Controls;using System.Windows.Media;using System.Windows.Threading;using Microsoft.Win32;
namespace Iris {
    static class DownloadBridge {
        static readonly object extensionGate = new object();
        static readonly string[] extensionFiles = { "worker.js", "popup.js", "popup.html", "popup.css", "icon16.png", "icon24.png", "icon32.png", "icon48.png", "icon128.png", "extension-id.txt", "manifest.json" };
        public static string Root {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Iris", "ChromeDownloadBridge"); }
        }
        public static string DataDirectory { get { return Path.Combine(Root, "Data"); } }
        public static string Inbox { get { return Path.Combine(DataDirectory, "BrowserDownloads"); } }
        public static string ExtensionDirectory { get { return Path.Combine(Root, "BrowserExtension"); } }

        static void CopyExtension(string source, string target) {
            Directory.CreateDirectory(target);
            foreach (string name in extensionFiles)
                CopyChanged(Path.Combine(source, name), Path.Combine(target, name));
        }

        internal static bool RefreshExistingExtension(string package, string destination) {
            string source = Path.Combine(package, "BrowserExtension");
            string target = Path.Combine(destination, "BrowserExtension");
            string identity = Path.Combine(target, "extension-id.txt");
            string installedManifest = Path.Combine(target, "manifest.json");
            lock (extensionGate) {
                if (!File.Exists(identity) || !File.Exists(installedManifest) ||
                    File.ReadAllText(identity).Trim() != File.ReadAllText(Path.Combine(source, "extension-id.txt")).Trim()) return false;
                Version installed, bundled;
                if (!Version.TryParse(IntegrationJson.Text(IntegrationJson.Parse(File.ReadAllText(installedManifest)), "version"), out installed) ||
                    !Version.TryParse(IntegrationJson.Text(IntegrationJson.Parse(File.ReadAllText(Path.Combine(source, "manifest.json"))), "version"), out bundled) ||
                    installed >= bundled) return false;
                CopyExtension(source, target);
                return true;
            }
        }

        public static string Stage(string package, string destination) {
            string extension = Path.Combine(destination, "BrowserExtension");
            lock (extensionGate) CopyExtension(Path.Combine(package, "BrowserExtension"), extension);
            string id = File.ReadAllText(Path.Combine(extension, "extension-id.txt")).Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-p]{32}$")) throw new InvalidDataException(L.T("Chrome-Erweiterung beschädigt. Iris vollständig entpacken."));
            string host = Path.Combine(destination, "Iris.DownloadHost.exe");
            CopyChanged(Path.Combine(package, "Iris.DownloadHost.exe"), host);
            string manifest = Path.Combine(destination, "browser-native-host.json");
            string json = IntegrationJson.Stringify(new { name = "de.iris.downloads", description = "Iris local download progress",
                path = host, type = "stdio", allowed_origins = new[] { "chrome-extension://" + id + "/" } });
            if (!File.Exists(manifest) || File.ReadAllText(manifest) != json) Write(manifest, System.Text.Encoding.UTF8.GetBytes(json));
            return manifest;
        }

        static void CopyChanged(string source, string target) {
            byte[] content = File.ReadAllBytes(source);
            if (File.Exists(target) && new FileInfo(target).Length == content.Length && File.ReadAllBytes(target).SequenceEqual(content)) return;
            Write(target, content);
        }

        static void Write(string path, byte[] content) {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllBytes(temp, content);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public static class DownloadLinkReceipt {
        public static bool Read(string directory,string browser,DateTime since,out DateTime at){at=DateTime.MinValue;if(browser!="Chrome"&&browser!="Edge")return false;try{string file=Path.Combine(directory,"BrowserDownloads","link-"+browser+".json");if(!File.Exists(file)||new FileInfo(file).Length>2048)return false;var data=IntegrationJson.Parse(File.ReadAllText(file));DateTime timestamp;if(IntegrationJson.Text(data,"source")!=browser||!DateTime.TryParse(IntegrationJson.Text(data,"utc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out timestamp))return false;timestamp=timestamp.ToUniversalTime();if(timestamp<since||timestamp>DateTime.UtcNow.AddSeconds(2))return false;at=timestamp;return true;}catch{return false;}}
    }
    public sealed partial class NotchWindow {
        DispatcherTimer downloadGuideWatch;int downloadGuideGeneration;DateTime downloadGuideStarted;
        void StopDownloadGuide(){downloadGuideGeneration++;if(downloadGuideWatch!=null){downloadGuideWatch.Stop();downloadGuideWatch=null;}}
        void ShowDownloadSetup(){
            if(addonWindow!=null){addonWindow.Activate();return;}
            OpenAddonWindow(L.T("Chrome einmal verbinden"));
            addonWindow.Topmost=false;
            addonWindow.ShowInTaskbar=true;
            var settingsWindow=settings;
            if(settingsWindow!=null&&settingsWindow.Topmost){
                settingsWindow.Topmost=false;
                addonWindow.Closed+=delegate{if(settings==settingsWindow)settingsWindow.Topmost=true;};
            }
            BeginDownloadGuide("Chrome");
        }
        void RefreshInstalledDownloadExtension(){
            if (uiTesting) return;
            System.Threading.Tasks.Task.Run(delegate {
                try { DownloadBridge.RefreshExistingExtension(root, DownloadBridge.Root); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            });
        }
        static string DownloadBrowserPath(string browser){string exe=browser=="Edge"?"msedge.exe":"chrome.exe";foreach(var hive in new[]{Registry.CurrentUser,Registry.LocalMachine})try{using(var key=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\"+exe)){string path=key==null?"":Convert.ToString(key.GetValue("")).Trim('"');if(Path.IsPathRooted(path)&&File.Exists(path)&&string.Equals(Path.GetFileName(path),exe,StringComparison.OrdinalIgnoreCase))return path;}}catch{}
            foreach(string basePath in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}){string path=Path.Combine(basePath,browser=="Edge"?@"Microsoft\Edge\Application\msedge.exe":@"Google\Chrome\Application\chrome.exe");if(File.Exists(path))return path;}return "";
        }
        void RegisterDownloadBrowser(){
            string manifest=DownloadBridge.Stage(root,DownloadBridge.Root);
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Google\Chrome\NativeMessagingHosts\de.iris.downloads"))
                key.SetValue("",manifest,RegistryValueKind.String);
        }
        void OpenDownloadBrowser(string browser,string path){if(uiTesting)return;if(string.IsNullOrEmpty(path))throw new FileNotFoundException(browser+L.T(" wurde nicht gefunden. Ist der Browser installiert?"));nativeAddonHost.Launch(path,browser=="Edge"?"edge://extensions/":"chrome://extensions/","");}
        StackPanel DownloadGuideStep(int number,string title,string instruction,UIElement action=null){
            var step=new StackPanel{Margin=new Thickness(13,8,13,8)};
            var heading=Text(number+". "+title,14,ink);heading.FontWeight=FontWeights.SemiBold;
            heading.TextWrapping=TextWrapping.Wrap;step.Children.Add(heading);
            if(!string.IsNullOrEmpty(instruction)){
                var note=AddonNote(instruction);note.Foreground=new SolidColorBrush(Color.FromRgb(182,182,189));
                note.Margin=new Thickness(0,3,0,0);step.Children.Add(note);
            }
            if(action!=null)step.Children.Add(action);
            return step;
        }
        void BeginDownloadGuide(string browser){
            StopDownloadGuide();downloadGuideStarted=DateTime.UtcNow;
            var panel=AddonPanel();
            var firstTime=new StackPanel();
            var alreadyInstalled=new StackPanel{Visibility=Visibility.Collapsed};
            Button route=null;
            route=Btn(L.T("Iris ist schon in Chrome"),delegate{
                bool showExisting=alreadyInstalled.Visibility!=Visibility.Visible;
                firstTime.Visibility=showExisting?Visibility.Collapsed:Visibility.Visible;
                alreadyInstalled.Visibility=showExisting?Visibility.Visible:Visibility.Collapsed;
                route.Content=showExisting?L.T("Zur ersten Einrichtung"):L.T("Iris ist schon in Chrome");
            });
            route.Height=30;route.Margin=new Thickness(0,0,0,8);
            panel.Children.Add(route);

            var status=AddonNote(L.T("Iris bestätigt die Verbindung, sobald Chrome antwortet."));
            var open=Btn(L.T("Chrome-Erweiterungsseite öffnen"),delegate{
                try{OpenDownloadBrowser(browser,uiTesting?"fixture-browser.exe":DownloadBrowserPath(browser));}
                catch{status.Text=L.T("Öffne Chrome über das Startmenü und folge Schritt 1.");}
            });
            open.Height=30;open.Margin=new Thickness(0,6,0,0);open.HorizontalAlignment=HorizontalAlignment.Left;
            SettingsGroup(panel,DownloadGuideStep(1,L.T("Erweiterungen in Chrome öffnen"),
                L.T("Öffne Chrome. Klicke oben rechts auf ⋮ → Erweiterungen → Erweiterungen verwalten."),open));

            var copyBox=new StackPanel{Margin=new Thickness(0,6,0,0)};
            var copyStatus=AddonNote("");copyStatus.Visibility=Visibility.Collapsed;copyStatus.Margin=new Thickness(0,4,0,0);
            var copy=Btn(L.T("Ordnerpfad kopieren"),delegate{
                copyStatus.Visibility=Visibility.Visible;
                try{
                    if(!uiTesting)System.Windows.Clipboard.SetText(DownloadBridge.ExtensionDirectory);
                    copyStatus.Text=L.T("Ordnerpfad kopiert.");
                }catch{copyStatus.Text=L.T("Kopieren ging nicht. Ordner: ")+DownloadBridge.ExtensionDirectory;}
            });
            copy.Height=30;copy.HorizontalAlignment=HorizontalAlignment.Left;copyBox.Children.Add(copy);copyBox.Children.Add(copyStatus);
            SettingsGroup(firstTime,
                DownloadGuideStep(2,L.T("Entwicklermodus einschalten"),L.T("Schalte oben rechts auf der Erweiterungsseite den Entwicklermodus ein.")),
                DownloadGuideStep(3,L.T("Pfad zur Iris-Erweiterung kopieren"),null,copyBox),
                DownloadGuideStep(4,L.T("Entpackte Erweiterung laden"),
                    L.T("Klicke in Chrome auf „Entpackte Erweiterung laden“. Füge den Pfad oben in die Adresszeile des Ordnerfensters ein, drücke Enter und klicke auf „Ordner auswählen“.")));
            SettingsGroup(alreadyInstalled,DownloadGuideStep(2,L.T("Iris neu laden"),
                L.T("Suche Iris in der Erweiterungsübersicht. Schalte Iris ein, falls es deaktiviert ist. Aktiviere oben rechts den Entwicklermodus und klicke bei Iris auf das runde Pfeilsymbol zum Neuladen.")));
            panel.Children.Add(firstTime);panel.Children.Add(alreadyInstalled);
            panel.Children.Add(AddonNote(L.T("Iris erscheint in Chromes Erweiterungsmenü (Puzzleteil). Falls die Verbindung ausbleibt, öffne dort das Iris-Popup und klicke auf „Verbinden“.")));
            panel.Children.Add(status);
            ShowAddonDialogPage("download-install",new MixerScroll(uiMotion){Content=panel,Height=475,
                VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},1);

            if(!uiTesting){try{RegisterDownloadBrowser();}catch(Exception ex){status.Text=ex.Message;}}
            var owner=addonWindow;int generation=downloadGuideGeneration;
            downloadGuideWatch=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
            downloadGuideWatch.Tick+=delegate{
                if(generation!=downloadGuideGeneration)return;
                if(addonWindow!=owner||addonDialogPage!="download-install"||shuttingDown){StopDownloadGuide();return;}
                if(addonDialogMotion!=null&&addonDialogMotion.Closing)return;
                DateTime at;
                if(DownloadLinkReceipt.Read(DownloadBridge.DataDirectory,browser,downloadGuideStarted,out at))ShowDownloadGuideDone(browser,at);
            };
            if(!uiTesting)downloadGuideWatch.Start();
        }
        void ShowDownloadGuideDone(string browser,DateTime at){StopDownloadGuide();prefs.DownloadsEnabled=true;prefs.BrowserDownloads=true;RestartDownloads();Save();var panel=AddonPanel();var check=Text("✓",36,new SolidColorBrush(Color.FromRgb(93,220,137)));panel.Children.Add(check);panel.Children.Add(Text(browser+L.T(" ist mit Iris verbunden"),20,ink));panel.Children.Add(AddonNote(L.T("Chrome meldet aktive Downloads ab jetzt im Hintergrund. Iris zeigt den Fortschritt neben der Uhr, sobald Chrome die Gesamtgröße kennt.")));panel.Children.Add(AddonNote(L.T("Verbindung geprüft um ")+at.ToLocalTime().ToString("HH:mm")+L.T(". Die Verbindung liegt im Windows-Benutzerkonto und bleibt bei Iris-Updates erhalten.")));panel.Children.Add(Btn(L.T("Fertig"),CloseAddonWindow));ShowAddonDialogPage("download-ready",AddonScroll(panel),1);}
        void RemoveDownloadLinks(){if(uiTesting)return;foreach(string path in new[]{@"Software\Google\Chrome\NativeMessagingHosts\de.iris.downloads",@"Software\Microsoft\Edge\NativeMessagingHosts\de.iris.downloads"})try{using(var key=Registry.CurrentUser.OpenSubKey(path)){string registered=key==null?"":Convert.ToString(key.GetValue(""));if(!string.Equals(registered,Path.Combine(root,dataFolder,"browser-native-host.json"),StringComparison.OrdinalIgnoreCase))continue;}Registry.CurrentUser.DeleteSubKeyTree(path,false);}catch{}}
    }
}
