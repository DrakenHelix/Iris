using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Security.Cryptography;using System.Text;using System.Threading;
namespace Iris {
    public sealed class BrowserDownloadProfile {public string Source,Path;public bool Firefox;internal string Stamp;internal BrowserDownloadRow[] Rows=new BrowserDownloadRow[0];}
    public sealed class BrowserCounterTracker {
        sealed class Seen {public double Received,Total;public DateTime Changed;public bool Confirmed;}
        readonly Dictionary<string,Seen> seen=new Dictionary<string,Seen>();
        public static readonly TimeSpan Fresh=TimeSpan.FromSeconds(20);
        public ActiveDownload[] Observe(string profile,string source,IEnumerable<BrowserDownloadRow> rows,DateTime now){var result=new List<ActiveDownload>();var present=new HashSet<string>();foreach(var row in rows){string id=profile+":"+row.Id;present.Add(id);if(row.State!=0||!Valid(row.Received,row.Total)||row.Total>0&&row.Received>=row.Total){seen.Remove(id);continue;}Seen before;if(!seen.TryGetValue(id,out before)){before=new Seen{Received=row.Received,Total=row.Total,Changed=now,Confirmed=row.StartedUtc>=now.AddSeconds(-15)&&row.StartedUtc<=now.AddSeconds(2)};seen[id]=before;}else if(row.Received!=before.Received||row.Total!=before.Total){before.Received=row.Received;before.Total=row.Total;before.Changed=now;before.Confirmed=true;}if(!before.Confirmed||now-before.Changed>Fresh)continue;result.Add(new ActiveDownload{Id="browser-native:"+id,Name=string.IsNullOrWhiteSpace(row.Name)?"Browser-Download":row.Name,Source=source,Received=row.Received,Total=row.Total>0?row.Total:-1,CounterUtc=before.Changed,Utc=before.Changed,FreshForSeconds=20,ActivitySeconds=20,ProgressNote=row.Total>0?L.T("Lokaler Downloadzähler"):L.T("Gesamtgröße unbekannt")});}foreach(string key in seen.Keys.Where(k=>k.StartsWith(profile+":",StringComparison.Ordinal)&&!present.Contains(k)).ToArray())seen.Remove(key);return result.ToArray();}
        static bool Valid(double received,double total){return !double.IsNaN(received)&&!double.IsInfinity(received)&&!double.IsNaN(total)&&!double.IsInfinity(total)&&received>=0&&received<=1e16&&total>=-1&&total<=1e16;}
    }
    public sealed class NativeBrowserDownloads : IDisposable {
        readonly BrowserDownloadProfile[] profiles;
        readonly BrowserFileActivity fileActivity;readonly BrowserCounterTracker tracker=new BrowserCounterTracker();readonly CancellationTokenSource cancellation=new CancellationTokenSource();bool disposed;public int ProfilesRead{get;private set;}public int ProfilesFound{get{return profiles.Length;}}public string Status{get;private set;}
        public NativeBrowserDownloads() {
            profiles = Discover().Take(96).ToArray();
            fileActivity = new BrowserFileActivity(BrowserFileActivity.Discover(profiles));
        }
        public NativeBrowserDownloads(IEnumerable<BrowserDownloadProfile> profiles, IEnumerable<string> downloadFolders = null) {
            this.profiles = profiles.Take(96).ToArray();
            if (downloadFolders != null) fileActivity = new BrowserFileActivity(downloadFolders);
        }
        public static BrowserDownloadProfile[] Discover(){string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);var result=new List<BrowserDownloadProfile>();string[][] roots={new[]{"Chrome",Path.Combine(local,@"Google\Chrome\User Data")},new[]{"Edge",Path.Combine(local,@"Microsoft\Edge\User Data")},new[]{"Brave",Path.Combine(local,@"BraveSoftware\Brave-Browser\User Data")},new[]{"Vivaldi",Path.Combine(local,@"Vivaldi\User Data")},new[]{"Opera",Path.Combine(roaming,@"Opera Software\Opera Stable")},new[]{"Opera GX",Path.Combine(roaming,@"Opera Software\Opera GX Stable")}};foreach(var root in roots)try{if(!Directory.Exists(root[1]))continue;var folders=new List<string>{root[1],Path.Combine(root[1],"Default")};folders.AddRange(Directory.EnumerateDirectories(root[1],"Profile *").Take(16));foreach(string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase)){string file=Path.Combine(folder,"History");if(File.Exists(file))result.Add(new BrowserDownloadProfile{Source=root[0],Path=file});}}catch{}
            string firefox=Path.Combine(roaming,@"Mozilla\Firefox\Profiles");try{if(Directory.Exists(firefox))foreach(string profile in Directory.EnumerateDirectories(firefox).Take(32)){string file=Path.Combine(profile,"downloads.json");result.Add(new BrowserDownloadProfile{Source="Firefox",Path=file,Firefox=true});}}catch{}return result.ToArray();}
        static string Key(string path){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant()))).Replace("-","").Substring(0,20);}
        internal static BrowserDownloadRow[] FirefoxRows(string file){var result=new List<BrowserDownloadRow>();var info=new FileInfo(file);if(!info.Exists||info.Length>4*1024*1024)return result.ToArray();string text;using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream))text=reader.ReadToEnd();var data=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4*1024*1024,RecursionLimit=24}.DeserializeObject(text);foreach(var row in IntegrationJson.Array(data,"list").Take(128)){if(IntegrationJson.Bool(row,"succeeded")||IntegrationJson.Bool(row,"canceled")||IntegrationJson.Get(row,"errorObj")!=null||IntegrationJson.Get(row,"error")!=null||IntegrationJson.Bool(row,"hasBlockedData"))continue;object saver=IntegrationJson.Get(row,"saver");if(saver!=null&&!(saver is string&&Convert.ToString(saver)=="copy"))continue;var target=IntegrationJson.Get(row,"target");string part=IntegrationJson.Text(target,"partFilePath"),path=IntegrationJson.Text(target,"path");if(!Path.IsPathRooted(part)||part.StartsWith(@"\\")||!part.EndsWith(".part",StringComparison.OrdinalIgnoreCase))continue;var partial=new FileInfo(part);if(!partial.Exists)continue;DateTime started;if(!DateTime.TryParse(IntegrationJson.Text(row,"startTime"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out started))started=DateTime.MinValue;result.Add(new BrowserDownloadRow{Id=Key(part),Name=Path.GetFileName(path),Received=partial.Length,Total=IntegrationJson.Number(row,"totalBytes"),State=0,StartedUtc=started.ToUniversalTime()});}return result.ToArray();}
        public ActiveDownload[] Read(DateTime now) {
            if (disposed) return new ActiveDownload[0];
            var result = new List<ActiveDownload>();
            int read = 0;
            bool hasChromiumDownload = false;
            foreach (var profile in profiles) {
                if (cancellation.IsCancellationRequested) break;
                try {
                    if (profile.Firefox) {
                        profile.Rows = FirefoxRows(profile.Path);
                    } else {
                        var info = new FileInfo(profile.Path);
                        string stamp = info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "missing";
                        foreach (string suffix in new[] { "-wal", "-journal" }) {
                            var side = new FileInfo(profile.Path + suffix);
                            stamp += "|" + suffix + ":" + (side.Exists ? side.LastWriteTimeUtc.Ticks + ":" + side.Length : "missing");
                        }
                        if (stamp != profile.Stamp) {
                            profile.Rows = BrowserDownloadSqlite.Read(profile.Path, cancellation.Token);
                            profile.Stamp = stamp;
                        }
                    }
                    read++;
                } catch (OperationCanceledException) { break; }
                catch {
                    // Retry a failed query on the next poll. Cached counters keep their original age.
                    profile.Stamp = null;
                }
                var active = tracker.Observe(Key(profile.Path), profile.Source, profile.Rows, now);
                result.AddRange(active);
                if (!profile.Firefox && active.Length > 0) hasChromiumDownload = true;
            }
            ProfilesRead = read;
            Status = profiles.Length == 0 ? L.T("Keine unterstützten Browserprofile gefunden.")
                : read + " von " + profiles.Length + L.T(" Browserprofilen lesbar · ohne Erweiterung");
            if (fileActivity != null && fileActivity.Available) {
                var activity = fileActivity.Read(now, cancellation.Token);
                if (activity != null && !hasChromiumDownload) result.Add(activity);
                if (read < profiles.Length || profiles.Length == 0) Status += L.T(" Dateierkennung läuft.");
            }
            return result.ToArray();
        }
        public void Dispose(){disposed=true;cancellation.Cancel();}
    }
}
