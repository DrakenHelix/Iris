using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.IO;using System.Linq;using System.Threading;using System.Windows;using System.Windows.Controls;using System.Windows.Media;
namespace Iris {
    public static class NativeDownloadQa {
        static void Need(bool value,string message){if(!value)throw new Exception(message);}
        static byte[] SharedBytes(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var copy=new MemoryStream()){stream.CopyTo(copy);return copy.ToArray();}}
        static void VerifyChromeViewCounter(List<string> log) {
            var now = DateTime.UtcNow;
            const string shown = "6,5 MB/s - 1,4 GB von 5,8 GB, 12 min übrig";
            var value = ChromeDownloadCounter.Parse(shown, now);
            Need(value != null && value.Rounded && (int)Math.Floor(value.Received * 100 / value.Total) == 24,
                "Chrome screenshot progress line does not yield 24 percent");
            Need(ChromeDownloadCounter.Parse(shown, now, .25).Fraction == .25, "Chrome provider percentage ignored");
            var mixed = ChromeDownloadCounter.Parse("3.5 MB/s – 512 MB of 2 GB, 7 min left", now);
            Need(mixed != null && mixed.Received / mixed.Total == .25, "Mixed Chrome byte units incorrect");
            foreach (string invalid in new[] { "6,5 MB/s", "1,4 GB von 5,8 GB", "0 B/s - 1,4 GB von 5,8 GB", shown + " · Pausiert", "3 MB/s - 5 GB of 5 GB" })
                Need(ChromeDownloadCounter.Parse(invalid, now) == null, "Inactive or ambiguous line accepted");
            Need(ChromeDownloadCounter.IsDownloadsPage("chrome://downloads/")
                && !ChromeDownloadCounter.IsDownloadsPage("https://downloads.example/")
                && !ChromeDownloadCounter.IsDownloadsPage("chrome://downloads/other"), "Chrome page verification is too broad");
            using (var helper = new SteamProgressClient(true)) {
                var fallback = new ActiveDownload { Id = ChromeDownloadCounter.TargetId, Name = "Download-Aktivität",
                    Source = "Browser", Total = -1, Utc = DateTime.UtcNow, ActivitySeconds = 6, ChromeCounterEligible = true };
                helper.Configure(new[] { fallback });
                ActiveDownload result = null;
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed.TotalSeconds < 6) {
                    Thread.Sleep(75);
                    result = helper.Merge(new[] { fallback }, DateTime.UtcNow).Single();
                    if (result.Fraction.HasValue) break;
                }
                Need(result != null && result.Source == "Chrome" && result.Fraction.HasValue
                    && result.ActivitySeconds == 6 && result.Utc == fallback.Utc,
                    "Chrome view counter not merged or extended file activity lifetime");
                Need(new DownloadSnapshot { Items = new[] { result } }.Description.Contains("1,4 GB von 5,8 GB"),
                    "Chrome displayed units not preserved");
                Need(helper.Merge(new[] { fallback }, DateTime.UtcNow.AddSeconds(5)).Single().Fraction == null,
                    "Old Chrome provider counter retained");
                fallback.ChromeCounterEligible = false;
                Need(helper.Merge(new[] { fallback }, DateTime.UtcNow).Single().Fraction == null,
                    "Ambiguous file activity got an unrelated counter");
            }
            log.Add("PASS: Chrome screenshot format 24%, mixed units, paused/rate-only rejection, page guard, isolated helper merge and stale expiry. Provider fixture, no live Chrome UI.");
        }
        static void VerifyLockedChromium(string fixture, List<string> log) {
            string db = Path.Combine(fixture, "LockedHistory");
            string folder = Path.Combine(fixture, "ChromiumDownloads");
            Directory.CreateDirectory(folder);
            string partial = Path.Combine(folder, "fixture.zip.crdownload");
            var now = DateTime.UtcNow;
            long stamp = (now - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10;
            BrowserDownloadSqlite.Fixture(db, "CREATE TABLE downloads(id INTEGER,target_path TEXT,received_bytes INTEGER,total_bytes INTEGER,state INTEGER,start_time INTEGER);"
                + "INSERT INTO downloads VALUES(1,'fixture.zip',375,1000,0," + stamp + ");");
            File.WriteAllBytes(partial, new byte[128]);
            File.SetLastWriteTimeUtc(partial, now.AddMinutes(-1));

            using (var browser = new NativeBrowserDownloads(new[] { new BrowserDownloadProfile { Source = "Chrome", Path = db } }, new[] { folder })) {
                BrowserDownloadSqlite.Fixture(db, "PRAGMA locking_mode=EXCLUSIVE; BEGIN EXCLUSIVE;", delegate {
                    Need(browser.Read(now).Length == 0 && browser.ProfilesRead == 0, "Exclusive lock fixture not reproduced or stale file shown");
                    using (var writer = new FileStream(partial, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) {
                        writer.Write(new byte[256], 0, 256);
                        writer.Flush();
                        var activity = browser.Read(now.AddSeconds(1)).Single();
                        Need(activity.Id == BrowserFileActivity.Id && activity.Received == 0 && activity.Fraction == null,
                            "Growing Chromium partial file not recognized or fabricated progress");
                        Need(browser.Read(now.AddSeconds(8)).Length == 0, "Stalled Chromium partial file remains active");
                        writer.WriteByte(0);
                        writer.Flush();
                        Need(browser.Read(now.AddSeconds(9)).Single().Id == BrowserFileActivity.Id, "Resumed Chromium write not recognized");
                    }
                    File.Move(partial, Path.Combine(folder, "fixture.zip"));
                    Need(browser.Read(now.AddSeconds(10)).Length == 0, "Final rename retains Chromium activity");
                });
                var recovered = browser.Read(DateTime.UtcNow).Single();
                Need(browser.ProfilesRead == 1 && recovered.Source == "Chrome" && recovered.Fraction == .375,
                    "Actual browser counters not restored after lock release");
            }
            log.Add("PASS: Exclusive Chrome-style SQLite lock reproduced; shared file growth with writer open yields indeterminate activity, stale/paused expires, resumed write reappears, final rename removes it, valid browser counter recovers after unlock.");
        }
        public static void Run(bool probe){string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa");Directory.CreateDirectory(directory);var log=new List<string>();try{if(probe){using(var reader=new NativeBrowserDownloads()){reader.Read(DateTime.UtcNow);Thread.Sleep(1200);var active=reader.Read(DateTime.UtcNow);log.Add("READ ONLY: "+reader.Status);log.Add("Active downloads: "+active.Length+"; current percentages: "+active.Count(i=>i.Fraction.HasValue));log.Add("No browser started, no download started, no URL/history copied, no extension or registry changed.");}SteamDownloadQa.Probe(directory);}else{
            DownloadCoreQa.Verify(log);SteamDownloadQa.Verify(log);DateTime now=DateTime.UtcNow;
            string fixture=Path.Combine(directory,"native-downloads-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);string db=Path.Combine(fixture,"History");long stamp=(now-new DateTime(1601,1,1,0,0,0,DateTimeKind.Utc)).Ticks/10;
            BrowserDownloadSqlite.Fixture(db,"CREATE TABLE downloads(id INTEGER PRIMARY KEY,target_path TEXT,received_bytes INTEGER,total_bytes INTEGER,state INTEGER,start_time INTEGER);INSERT INTO downloads VALUES(1,'C:\\Downloads\\Grüße.zip',375,1000,0,"+stamp+");INSERT INTO downloads VALUES(2,'C:\\Downloads\\fertig.zip',1000,1000,1,"+stamp+");");
            byte[] original=File.ReadAllBytes(db);var rows=BrowserDownloadSqlite.Read(db,CancellationToken.None);Need(rows.Length==1&&rows[0].Name=="Grüße.zip"&&rows[0].Received==375&&rows[0].Total==1000,"Actual Windows SQLite read / basename / active filter");Need(original.SequenceEqual(File.ReadAllBytes(db)),"Read changed original database");string missing=Path.Combine(fixture,"missing");Need(BrowserDownloadSqlite.Read(missing,CancellationToken.None).Length==0&&!File.Exists(missing),"Missing profile created");
            var tracker=new BrowserCounterTracker();rows[0].StartedUtc=now.AddHours(-3);Need(tracker.Observe("p","Chrome",rows,now).Length==0,"Old crashed row shown as active");rows[0].Received=400;var found=tracker.Observe("p","Chrome",rows,now.AddSeconds(1));Need(found.Length==1&&Math.Abs(found[0].Fraction.Value-.4)<.001,"Changed byte counter not recognized");Need(tracker.Observe("p","Chrome",rows,now.AddSeconds(22)).Length==0,"Stalled counter did not expire");rows[0].Received=450;rows[0].Total=-1;Need(tracker.Observe("p","Chrome",rows,now.AddSeconds(23)).Single().Fraction==null,"Unknown size became percentage");rows[0].State=1;Need(tracker.Observe("p","Chrome",rows,now.AddSeconds(24)).Length==0,"Completed row remains active");
            using(var browser=new NativeBrowserDownloads(new[]{new BrowserDownloadProfile{Source="Chrome",Path=db}})){Need(browser.Read(now).Length==1,"Native profile discovery fixture");BrowserDownloadSqlite.Fixture(db,"UPDATE downloads SET received_bytes=500 WHERE id=1;");Need(Math.Abs(browser.Read(now.AddSeconds(1)).Single().Fraction.Value-.5)<.001,"Database update not refreshed");BrowserDownloadSqlite.Fixture(db,"UPDATE downloads SET state=1 WHERE id=1;");Need(browser.Read(now.AddSeconds(2)).Length==0,"Completed SQL row remains visible");}
            BrowserDownloadSqlite.Fixture(db,"PRAGMA journal_mode=WAL;INSERT INTO downloads VALUES(3,'C:\\Downloads\\wal.zip',700,1000,0,"+stamp+");",delegate{Need(File.Exists(db+"-wal"),"No real WAL fixture");byte[] main=SharedBytes(db),wal=SharedBytes(db+"-wal");Need(BrowserDownloadSqlite.Read(db,CancellationToken.None).Single().Received==700,"Live committed WAL counter not read");Need(main.SequenceEqual(SharedBytes(db))&&wal.SequenceEqual(SharedBytes(db+"-wal")),"WAL reader changed data");});
            VerifyLockedChromium(fixture,log);VerifyChromeViewCounter(log);
            var canceled=new CancellationTokenSource();canceled.Cancel();bool stopped=false;try{BrowserDownloadSqlite.Read(db,canceled.Token);}catch(OperationCanceledException){stopped=true;}Need(stopped,"Canceled reader ran query");
            string part=Path.Combine(fixture,"example.part"),json=Path.Combine(fixture,"downloads.json");File.WriteAllBytes(part,new byte[250]);File.WriteAllText(json,IntegrationJson.Stringify(new{list=new[]{new{target=new{path=Path.Combine(fixture,"example.zip"),partFilePath=part},startTime=now.ToString("o"),totalBytes=1000,succeeded=false,canceled=false}}}));
            using(var firefox=new NativeBrowserDownloads(new[]{new BrowserDownloadProfile{Source="Firefox",Path=json,Firefox=true}})){Need(Math.Abs(firefox.Read(now).Single().Fraction.Value-.25)<.001,"Firefox exact partial bytes");File.WriteAllBytes(part,new byte[500]);Need(Math.Abs(firefox.Read(now.AddSeconds(1)).Single().Fraction.Value-.5)<.001,"Firefox partial growth");File.WriteAllText(json,File.ReadAllText(json).Replace("\"canceled\":false","\"canceled\":true"));Need(firefox.Read(now.AddSeconds(2)).Length==0,"Canceled Firefox remains visible");}
            var steam=SteamProgressCounter.Parse("steam:42",new[]{"Spiel 2","Heruntergeladen","12,3 GB / 50,0 GB"},null,now);Need(steam!=null&&steam.Received==12300000000&&Math.Abs(steam.Received/steam.Total-.246)<.001,"German Steam quantities or game title contaminated number");Need(SteamProgressCounter.Parse("steam:42",new[]{"Download","375 B von 1000 B"},null,now).Received==375,"Steam raw bytes");var percent=SteamProgressCounter.Parse("steam:42",new[]{"Download","37 %"},null,now);Need(percent!=null&&percent.Total<0&&percent.Received<0&&percent.Fraction==.37,"Provider percentage converted to invented bytes");
            foreach(string label in new[]{"Eingeplant 12 GB / 50 GB","Pausiert 12 GB / 50 GB","Rabatt 37 %","12 MB/s / 50 MB/s","Installiert 37 %","Download 37 % 24 %","Download 1 GB / 2 GB 3 GB / 4 GB"})Need(SteamProgressCounter.Parse("steam:42",new[]{label},null,now)==null,"Invalid Steam counter: "+label);
            using(var helper=new SteamProgressClient(true)){var target=new ActiveDownload{Id="steam:42",Name="Fixture",Source="Steam",Received=0,Total=-1,Utc=now};helper.Configure(new[]{target});ActiveDownload[] merged=null;var watch=Stopwatch.StartNew();while(watch.Elapsed.TotalSeconds<6){Thread.Sleep(100);merged=helper.Merge(new[]{target},DateTime.UtcNow);if(merged[0].Fraction.HasValue)break;}Need(merged!=null&&merged[0].Fraction==.375,"Isolated persistent helper fixture");Need(helper.Merge(new[]{target},DateTime.UtcNow.AddSeconds(10))[0].Fraction==null,"Stale helper sample shown");helper.Configure(new ActiveDownload[0]);}
            using(var hang=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Iris.SteamProgressHost.exe"),"--qa --qa-hang"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true})){Need(hang.WaitForExit(7000)&&hang.ExitCode==0,"Isolated UIA watchdog did not self-terminate");}
            log.Add("PASS: UIA watchdog self-terminates a hung recording provider without process kill.");
            log.Add("PASS: Windows SQLite original opened read-only; Unicode names, byte updates, stale/crashed/completed/unknown records, Firefox real fixture partial-file growth; original bytes unchanged.");log.Add("PASS: Steam quantities and direct provider percentage, rejection of scheduled/paused/rate/discount/ambiguous counters; isolated recording helper, stale reply expiry, graceful stop. No foreign program action.");
        }log.Add("ALL NATIVE DOWNLOAD CHECKS PASSED");}catch(Exception ex){Environment.ExitCode=1;log.Add("FAILED: "+ex);}File.WriteAllLines(Path.Combine(directory,probe?"native-download-live.txt":"native-download-test.txt"),log);}
    }
    public sealed partial class NotchWindow {
        void VerifyTraySettingsClick(){
            if(tray.Text!="Iris")throw new Exception("Wrong tray tooltip");
            var click=tray.GetType().GetMethod("OnMouseClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            foreach(var button in new[]{System.Windows.Forms.MouseButtons.Right,System.Windows.Forms.MouseButtons.Left}){
                click.Invoke(tray,new object[]{new System.Windows.Forms.MouseEventArgs(button,1,0,0,0)});
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(delegate{}));
                if((settings!=null)!=(button==System.Windows.Forms.MouseButtons.Left))
                    throw new Exception("Tray settings did not respond correctly to "+button);
            }
        }
        static void VerifyDownloadInk(DownloadIndicator indicator,double scale,bool arrowOnly=false,bool needsPercent=false){indicator.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(delegate{}));int width=(int)Math.Ceiling(indicator.ActualWidth*scale),height=(int)Math.Ceiling(indicator.ActualHeight*scale);var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.PushTransform(new ScaleTransform(scale,scale));dc.DrawDrawing(VisualTreeHelper.GetDrawing(indicator));dc.Pop();}var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);byte[] pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);int min=width,max=-1;for(int y=0;y<height;y++)for(int x=0;x<width;x++){int p=(y*width+x)*4;if(pixels[p+3]>100&&pixels[p]>100&&pixels[p+1]>100&&pixels[p+2]>100){min=Math.Min(min,x);max=Math.Max(max,x);}}if(arrowOnly&&max-min+1>11*scale)throw new Exception("Steam arrow-only indicator still has a label: "+min+".."+max+" / "+width);if(!arrowOnly&&max-min+1<=(needsPercent?29:11)*scale)throw new Exception("Expected download label: "+min+".."+max+" / "+width+", percent="+needsPercent);if(max<min||Math.Abs((min+max+1)/2.0-width/2.0)>1.5*scale)throw new Exception("Rendered download ink is not centered: "+min+".."+max+" / "+width);}
        void RunNativeDownloadUiQa(){var log=new List<string>();try{SetReducedMotion(true);prefs.DownloadsEnabled=true;CloseNotch(true);foreach(double? fraction in new double?[]{null,0,.09,.37,.99}){var item=new ActiveDownload{Id="fixture",Source="Chrome",Name="Beispiel.zip",Received=fraction.HasValue?fraction.Value*100:20,Total=fraction.HasValue?100:-1,Utc=DateTime.UtcNow};ApplyDownloads(new DownloadSnapshot{Items=new[]{item}});uiMotion.SnapAll();UpdateLayout();VerifyDownloadInk(compactDownload,1);VerifyDownloadInk(compactDownload,1.5);var center=clock.TranslatePoint(new Point(clock.ActualWidth/2,clock.ActualHeight/2),canvas);if(Math.Abs(center.X-SceneWidth/2)>.6)throw new Exception("Clock moved");}
            foreach(string source in new[]{"Chrome","Steam","Both","Steam","Chrome"}){
                var steam=new ActiveDownload{Id="steam:42",Source="Steam",Received=20,Total=-1,Utc=DateTime.UtcNow};
                var chrome=new ActiveDownload{Id="chrome:1",Source="Chrome",Received=37,Total=source=="Both"?100:-1,Utc=DateTime.UtcNow};
                ApplyDownloads(new DownloadSnapshot{Items=source=="Both"?new[]{steam,chrome}:new[]{source=="Steam"?steam:chrome}});
                uiMotion.SnapAll();UpdateLayout();
                VerifyDownloadInk(compactDownload,1,source=="Steam",source=="Both");
                VerifyDownloadInk(compactDownload,1.5,source=="Steam",source=="Both");
            }
            log.Add("PASS: Steam arrow alone is centered; Chrome wins when both are active and retains its own unknown-size label.");
            SetNotchScale(1.5);uiMotion.SnapAll();UpdateLayout();VerifyDownloadInk(compactDownload,1.5);SetNotchScale(1);uiMotion.SnapAll();VerifyTraySettingsClick();settingsSection="Downloads";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();var labels=string.Join(" ",Descendants<TextBlock>(settings).Select(t=>t.Text));if(!labels.Contains("einmaliger Einrichtung"))throw new Exception("Chrome setup note missing");settings.Close();VerifyDownloadUi(log);VerifyCursorAvoidance(log);log.Add("PASS: Actual rasterized arrow/text ink bounds centered for unknown/0/9/37/99 percent at 100 and 150 percent; rendered in memory.");log.Add("ALL NATIVE DOWNLOAD UI CHECKS PASSED");}catch(Exception ex){Environment.ExitCode=1;log.Add("FAILED: "+ex);}finally{File.WriteAllLines(Path.Combine(root,"native-download-ui-test.txt"),log);Close();}}
    }
}
