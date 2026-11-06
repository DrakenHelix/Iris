using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Windows;using System.Windows.Controls;
namespace Iris {
    public static class SteamDownloadQa {
        static void Need(bool condition,string message){if(!condition)throw new Exception(message);}
        static string Line(DateTime utc,string text){return "["+utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"] "+text+"\n";}
        static void VerifySteamSampleGaps(){
            var now=DateTime.UtcNow;
            var steam=new SteamCounterValue{Id="steam:42",Received=350,Total=1000,At=now.ToString("o")};
            var chrome=new SteamCounterValue{Id=ChromeDownloadCounter.TargetId,Received=50,Total=100,At=now.ToString("o")};
            var previous=new SteamCounterReply{Items=new[]{steam,chrome}};
            var retained=SteamProgressClient.RetainSteamSamples(previous,new SteamCounterReply()).Items;
            Need(retained.Length==1&&retained[0]==steam&&retained[0].At==steam.At,"Missing Steam reply discarded a fresh sample or renewed its timestamp");
            var next=new SteamCounterValue{Id=steam.Id,Received=400,Total=1000,At=now.AddSeconds(1).ToString("o")};
            Need(SteamProgressClient.RetainSteamSamples(previous,new SteamCounterReply{Items=new[]{next}}).Items.Single()==next,"A newer Steam reply did not replace the retained sample");

            string log=Line(now.AddSeconds(-5),"AppID 42 update started : download 350/1000, store 0/0")+
                       Line(now.AddSeconds(-5),"AppID 42 App update changed : Running Update,Downloading,Staging,");
            var sample=SteamDownloads.Parse(log,now,null).Single();
            Need(sample.Total==1000&&sample.Received==350&&sample.Fresh,"Recent actual Steam checkpoint disappeared between polls");
            Need(SteamDownloads.Parse(log,now.AddSeconds(5),null).Single().Total<0,"Expired Steam checkpoint kept showing a percentage");
        }
        public static void Verify(List<string> results){VerifySteamSampleGaps();DateTime now=DateTime.UtcNow;string log=Line(now.AddMinutes(-3),"AppID 42 update started : download 100/1000, store 0/0")+Line(now.AddMinutes(-3),"AppID 42 App update changed : Running Update,Downloading,Staging,")+Line(now.AddSeconds(-5),"Current download rate: 50.000 Mbps");string manifest="\"name\" \"Fixture game\" \"StateFlags\" \"1026\" \"BytesDownloaded\" \"100\" \"BytesToDownload\" \"1000\"";var active=SteamDownloads.Parse(log,now,id=>Tuple.Create(manifest,now.AddMinutes(-2)));Need(active.Length==1&&active[0].Total<0&&active[0].Name=="Fixture game","Real 1026 checkpoint regression: active missing or stale percentage claimed");Need(SteamDownloads.Parse(log+Line(now,"AppID 42 update canceled : Disabled (Suspended)"),now,null).Length==0,"Paused Steam job remains active");Need(SteamDownloads.Parse(log+Line(now,"AppID 42 App update changed : Running Update,Downloading,Staging,Stopping,"),now,null).Length==0,"Stopping is downloading");Need(SteamDownloads.Parse(log,now.AddMinutes(2),null).Length==0,"Stale heartbeat remains active");var fresh=SteamDownloads.Parse(log+Line(now,"AppID 42 update progress : download 350/1000"),now,null);Need(fresh.Length==1&&fresh[0].Received==350&&fresh[0].Total==1000,"Fresh real counter rejected");var complete=SteamDownloads.Parse(log+Line(now,"AppID 42 App update changed : None"),now,null);Need(complete.Length==0,"Completed Steam job shown");var expired=new DownloadSnapshot{Items=new[]{new ActiveDownload{Source="Steam",Received=12,Total=100,Utc=now,CounterUtc=now.AddSeconds(-5)}}};Need(!expired.Fraction.HasValue&&!expired.Description.Contains("12 %"),"Cached Steam counter did not expire independently of activity");results.Add("PASS: Steam 1026 checkpoint plus actual Downloading log and heartbeat is recognized; stale checkpoint never becomes fake live percent; fresh explicit counters accepted; suspended/stopping/none/stale data removed. Fixtures.");}
        public static void Probe(string directory){Directory.CreateDirectory(directory);string steam=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");var paths=new List<string>();var roots=new List<string>{steam};string libraries=Path.Combine(steam,"steamapps","libraryfolders.vdf");if(File.Exists(libraries))foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(libraries),"\"path\"\\s*\"((?:\\\\.|[^\"])*)\""))roots.Add(m.Groups[1].Value.Replace(@"\\",@"\"));foreach(string library in roots.Distinct(StringComparer.OrdinalIgnoreCase))try{paths.AddRange(Directory.EnumerateFiles(Path.Combine(library,"steamapps"),"appmanifest_*.acf"));}catch{}var snapshot=SteamDownloads.Read(steam,paths.ToArray(),DateTime.UtcNow);int watched=-1;string observerError="";try{using(var observer=new DownloadWatcher(directory,false,true)){System.Threading.Thread.Sleep(700);watched=observer.Latest().Items.Length;}}catch(Exception ex){observerError=ex.ToString();}var lines=new List<string>{"Read-only LIVE Steam log probe; no UI input, game, download, screenshot, credentials or files modified.","Observed UTC: "+DateTime.UtcNow.ToString("o"),"Active downloads: "+snapshot.Length};lines.Add("Live watcher detected: "+watched);if(observerError.Length>0)lines.Add("Observer failure: "+observerError);foreach(var value in snapshot)lines.Add(value.Name+" | "+(value.Total>0?Math.Floor(value.Received*100/value.Total)+" %":"Active; no fresh percentage counter available")+" | heartbeat "+value.Utc.ToString("o"));File.WriteAllLines(Path.Combine(directory,"steam-download-live.txt"),lines);}
    }
    public sealed partial class NotchWindow {
        void RunDownloadSetupQa() {
            var log = new List<string>();
            try {
                uiMotion.PauseAutomatic();
                SetReducedMotion(false);
                ShowSettings();
                settingsSection = "Downloads";
                RenderSettingsSection();
                uiMotion.SnapAll();
                settings.UpdateLayout();
                if (settingsPages.Current.RenderSize.Height > settingsBody.ActualHeight + .5)
                    throw new Exception("Chrome setup does not fit Downloads settings.");
                Descendants<Button>(settingsPages.Current).Single(b => Convert.ToString(b.Content) == L.T("Chrome einmal verbinden …"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AdvanceUi(4);
                addonWindow.UpdateLayout();
                if (addonDialogPage != "download-install" || MotionEnabled() && (addonDialogMotion.Value <= 0 || addonDialogMotion.Value >= 1))
                    throw new Exception("Chrome guide bypasses its opening transition or claims success early.");
                var frame = new System.Windows.Media.Imaging.RenderTargetBitmap((int)addonWindow.Width,(int)addonWindow.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                frame.Render(addonWindow);
                byte[] pixel = new byte[4];
                frame.CopyPixels(new Int32Rect((int)addonWindow.Width/2,(int)addonWindow.Height/2,1,1),pixel,4,0);
                if (pixel[3] == 0) throw new Exception("Chrome guide intermediate frame is empty.");
                SetReducedMotion(true);
                uiMotion.SnapAll();
                addonWindow.UpdateLayout();
                var guide=addonDialogPages.Current;
                var headings=Descendants<TextBlock>(guide).Where(t=>t.IsVisible&&t.Text.Length>2&&char.IsDigit(t.Text[0])&&t.Text[1]=='.').ToArray();
                var expected=new[]{L.T("Erweiterungen in Chrome öffnen"),L.T("Entwicklermodus einschalten"),L.T("Pfad zur Iris-Erweiterung kopieren"),L.T("Entpackte Erweiterung laden")};
                if(!headings.Select(h=>h.Text).SequenceEqual(expected.Select((title,i)=>(i+1)+". "+title))||
                    headings.Any(h=>h.Foreground!=ink||h.FontWeight!=FontWeights.SemiBold))
                    throw new Exception("Chrome guide step order or heading contrast is inconsistent.");
                if(addonWindow.Topmost||!addonWindow.ShowInTaskbar)throw new Exception("Guide cannot switch cleanly to Chrome.");
                if(Environment.GetEnvironmentVariable("IRIS_GUIDE_QA_RENDER")=="1"){
                    var rendered=new System.Windows.Media.Imaging.RenderTargetBitmap((int)addonWindow.Width,(int)addonWindow.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                    rendered.Render(addonWindow);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));
                    using(var stream=new MemoryStream()){encoder.Save(stream);Console.WriteLine(Convert.ToBase64String(stream.ToArray()));}
                }
                var copy = Descendants<Button>(guide).Single(b => Convert.ToString(b.Content) == L.T("Ordnerpfad kopieren"));
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!Descendants<TextBlock>(guide).Any(t=>t.IsVisible&&t.Text==L.T("Ordnerpfad kopiert.")))throw new Exception("Copy action feedback missing.");
                var route=Descendants<Button>(guide).Single(b=>Convert.ToString(b.Content)==L.T("Iris ist schon in Chrome"));
                route.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addonWindow.UpdateLayout();
                if(copy.IsVisible||!Descendants<TextBlock>(guide).Any(t=>t.IsVisible&&t.Text=="2. "+L.T("Iris neu laden"))||downloadGuideWatch==null)
                    throw new Exception("Existing-extension route still shows fresh install steps or stopped watching.");
                route.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                addonWindow.UpdateLayout();
                if(!copy.IsVisible)throw new Exception("First-time route cannot be restored.");

                string fixture = Path.Combine(root,"Data-qa","chrome-bridge-"+Guid.NewGuid().ToString("N"));
                string manifest = DownloadBridge.Stage(root,fixture);
                DateTime written = File.GetLastWriteTimeUtc(manifest);
                if (DownloadBridge.Stage(root,fixture) != manifest || File.GetLastWriteTimeUtc(manifest) != written)
                    throw new Exception("Repeated setup needlessly replaced the installed bridge.");
                var registration = IntegrationJson.Parse(File.ReadAllText(manifest));
                if (IntegrationJson.Text(registration,"path") != Path.Combine(fixture,"Iris.DownloadHost.exe")
                    || IntegrationJson.Array(registration,"allowed_origins").Length != 1)
                    throw new Exception("Manifest host or origin is wrong.");

                string stagedExtension = Path.Combine(fixture,"BrowserExtension");
                string stagedManifest = Path.Combine(stagedExtension,"manifest.json");
                string bundledManifest = File.ReadAllText(stagedManifest);
                var olderManifest = IntegrationJson.Parse(bundledManifest);
                olderManifest["version"] = "0.22.12";
                File.WriteAllText(stagedManifest,IntegrationJson.Stringify(olderManifest));
                File.Delete(Path.Combine(stagedExtension,"popup.css"));
                if (!DownloadBridge.RefreshExistingExtension(root,fixture) || File.ReadAllText(stagedManifest) != bundledManifest ||
                    !File.Exists(Path.Combine(stagedExtension,"popup.css")) || !File.Exists(Path.Combine(stagedExtension,"icon16.png")) ||
                    DownloadBridge.RefreshExistingExtension(root,fixture) || File.GetLastWriteTimeUtc(manifest) != written)
                    throw new Exception("Extension refresh did not preserve its connection and update the popup assets.");

                string bridgeData = Path.Combine(fixture,"Data"), inbox = Path.Combine(bridgeData,"BrowserDownloads");
                Directory.CreateDirectory(inbox);
                DateTime since = DateTime.UtcNow.AddSeconds(-1), at;
                if (DownloadLinkReceipt.Read(bridgeData,"Chrome",since,out at)) throw new Exception("Missing proof accepted.");
                string receipt = Path.Combine(inbox,"link-Chrome.json");
                File.WriteAllText(receipt,IntegrationJson.Stringify(new { source="Chrome", utc=DateTime.UtcNow.AddMinutes(-5).ToString("o") }));
                if (DownloadLinkReceipt.Read(bridgeData,"Chrome",since,out at)) throw new Exception("Stale proof accepted.");
                File.WriteAllText(receipt,IntegrationJson.Stringify(new { source="Chrome", utc=DateTime.UtcNow.ToString("o") }));
                if (!DownloadLinkReceipt.Read(bridgeData,"Chrome",since,out at)) throw new Exception("Fresh proof rejected.");
                ShowDownloadGuideDone("Chrome",at);
                if (downloadGuideWatch != null || addonDialogPage != "download-ready") throw new Exception("Receipt did not complete setup.");

                string snapshot = Path.Combine(inbox,"browser-fixture.json");
                File.WriteAllText(snapshot,IntegrationJson.Stringify(new { source="Chrome", utc=DateTime.UtcNow.ToString("o"),
                    items=new[] { new { name="fixture.zip", received=64, total=100 } } }));
                using (var watcher = new DownloadWatcher(Path.Combine(fixture,"another-iris-data-folder"),true,false,
                    () => new NativeBrowserDownloads(new BrowserDownloadProfile[0]),null,true,inbox)) {
                    var deadline = DateTime.UtcNow.AddSeconds(4);
                    while (watcher.Latest().Fraction != .64 && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(40);
                    if (watcher.Latest().Fraction != .64) throw new Exception("Separate Iris data directory cannot read the stable bridge.");
                }
                log.Add("ALL DOWNLOAD SETUP CHECKS PASSED");
            } catch (Exception ex) {
                Environment.ExitCode = 1;
                log.Add("FAILED: " + ex);
            } finally {
                StopDownloadGuide();
                if (addonWindow != null) CloseAddonWindow();
                if (settings != null) settings.Close();
                File.WriteAllLines(Path.Combine(root,"download-setup-test.txt"),log);
                Close();
            }
        }
    }
}
