using System;using System.Collections.Generic;using System.Diagnostics;using System.Globalization;using System.IO;using System.Linq;using System.Text.RegularExpressions;using System.Threading;using Microsoft.Win32;
namespace Iris {
    public sealed class ActiveDownload {
        public string Id,Name,Source,ProgressNote,CounterOrigin;public double Received,Total;public double? ReportedFraction;public bool Rounded,ChromeCounterEligible;public int FreshForSeconds=3,ActivitySeconds;public DateTime Utc;public DateTime? CounterUtc;
        public bool Fresh {get{return !CounterUtc.HasValue||CounterUtc.Value<=DateTime.UtcNow.AddSeconds(2)&&DateTime.UtcNow-CounterUtc.Value<=TimeSpan.FromSeconds(FreshForSeconds);}}
        public bool CurrentCounter {get{return Fresh&&(Total>0&&Received>=0&&Received<=Total||ReportedFraction.HasValue&&ReportedFraction.Value>=0&&ReportedFraction.Value<=1);}}
        public double? Fraction {get{return !CurrentCounter?(double?)null:ReportedFraction??Math.Max(0,Math.Min(1,Received/Total));}}
    }
    public sealed class DownloadSnapshot {
        public ActiveDownload[] Items=new ActiveDownload[0];public bool Active{get{return Items.Length>0;}}
        public double? Fraction{get{if(Items.Length==1)return Items[0].Fraction;return Items.Length==0||Items.Any(i=>!i.CurrentCounter||i.Total<=0||i.Rounded)?(double?)null:Math.Min(1,Items.Sum(i=>i.Received)/Items.Sum(i=>i.Total));}}
        public string Description{get{return string.Join("\n",Items.Select(i=>i.Source+" · "+i.Name+" · "+(i.Fraction.HasValue?Math.Floor(i.Fraction.Value*100+1e-9)+" %"+(i.Total>0?" · "+Bytes(i.Received,i.Rounded&&i.Source=="Chrome")+" von "+Bytes(i.Total,i.Rounded&&i.Source=="Chrome"):"")+(i.Rounded?L.T(" (gerundet aus ")+i.Source+")":""):(i.Fresh&&i.Received>0?Bytes(i.Received)+L.T(" empfangen · "):"")+(i.ProgressNote??L.T("Gesamtgröße unbekannt")))));}}
        public static string Bytes(double value,bool binary=false){string[] units={"B","KB","MB","GB","TB"};int unit=0;int divisor=binary?1024:1000;while(value>=divisor&&unit<4){value/=divisor;unit++;}return value.ToString(unit==0?"0":"0.0",L.Culture)+" "+units[unit];}
    }
    public static class PassiveSlots {
        // Selection is by priority; placement keeps surviving indicators on their current side.
        public static string[] Select(IEnumerable<string> active,string[] previous){var order=new[]{"alert","fps","download","timer","music","battery"};var chosen=order.Where(active.Contains).Take(2).ToArray();var slots=new string[2];if(previous!=null)for(int i=0;i<2;i++)if(chosen.Contains(previous[i]))slots[i]=previous[i];foreach(var id in chosen.Where(c=>!slots.Contains(c))){int preferred=id=="music"||id=="download"?1:0;if(slots[preferred]!=null)preferred=1-preferred;slots[preferred]=id;}return slots;}
    }
    public sealed class DownloadWatcher : IDisposable {
        readonly string directory,sharedBrowserInbox;NativeBrowserDownloads nativeBrowser;readonly SteamProgressClient steamProgress;readonly Func<NativeBrowserDownloads> browserFactory;readonly string steamOverride;Timer poll;public string BrowserStatus{get;private set;}readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();readonly object gate=new object();ActiveDownload[] items=new ActiveDownload[0];readonly HashSet<string> manifests=new HashSet<string>(StringComparer.OrdinalIgnoreCase);Timer debounce;bool disposed,initialized;string steam="";readonly object refreshGate=new object();bool steamDirty=true;long steamLogLength=-1;DateTime steamLogWritten;ActiveDownload[] steamCached=new ActiveDownload[0];
        public DownloadWatcher(string directory,bool browser,bool steamEnabled):this(directory,browser,steamEnabled,null,null,false,DownloadBridge.Inbox){}
        internal DownloadWatcher(string directory,bool browser,bool steamEnabled,Func<NativeBrowserDownloads> browserFactory,string steamOverride,bool recording,string sharedBrowserInbox=null){this.sharedBrowserInbox=sharedBrowserInbox;this.browserFactory=browserFactory;this.steamOverride=steamOverride;steamProgress=new SteamProgressClient(recording);this.directory=directory;BrowserStatus=browser?L.T("Browserprofile werden geprüft …"):L.T("Browser-Erkennung ausgeschaltet.");if(!browser&&!steamEnabled){initialized=true;return;}debounce=new Timer(_=>Refresh(browser,steamEnabled),null,Timeout.Infinite,Timeout.Infinite);poll=new Timer(_=>Refresh(browser,steamEnabled),null,Timeout.Infinite,Timeout.Infinite);System.Threading.Tasks.Task.Factory.StartNew(delegate{try{Initialize(browser,steamEnabled);}catch{BrowserStatus=L.T("Download-Erkennung konnte nicht vollständig vorbereitet werden.");}finally{lock(gate){if(!disposed){initialized=true;debounce.Change(0,Timeout.Infinite);poll.Change(1000,1000);}}}});}
        void Initialize(bool browser,bool steamEnabled){lock(gate)if(disposed)return;Directory.CreateDirectory(Path.Combine(directory,"BrowserDownloads"));if(browser)Watch(Path.Combine(directory,"BrowserDownloads"),"*.json",false);if(steamEnabled){steam=steamOverride??"";if(steamOverride==null)try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))steam=key==null?"":Convert.ToString(key.GetValue("SteamPath"));}catch{}if(string.IsNullOrWhiteSpace(steam))steam=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");try{if(Directory.Exists(Path.Combine(steam,"logs")))Watch(Path.Combine(steam,"logs"),"content_log.txt",false);}catch{}var libraries=new List<string>{steam};string folders=Path.Combine(steam,"steamapps","libraryfolders.vdf");try{if(File.Exists(folders)&&new FileInfo(folders).Length<1024*1024)foreach(Match m in Regex.Matches(File.ReadAllText(folders),"\"path\"\\s*\"((?:\\\\.|[^\"])*)\"")){string path=m.Groups[1].Value.Replace(@"\\",@"\");if(Path.IsPathRooted(path)&&!path.StartsWith(@"\\"))libraries.Add(path);}}catch{}foreach(string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase)){string apps=Path.Combine(library,"steamapps");if(!Directory.Exists(apps))continue;try{foreach(var file in Directory.EnumerateFiles(apps,"appmanifest_*.acf").Take(2048))lock(gate)manifests.Add(file);Watch(apps,"appmanifest_*.acf",true);}catch{}}}}

        void Watch(string path,string filter,bool manifest){lock(gate){if(disposed)return;var watcher=new FileSystemWatcher(path,filter){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};FileSystemEventHandler changed=delegate(object sender,FileSystemEventArgs e){lock(gate){if(disposed)return;if(manifest)manifests.Add(e.FullPath);if(manifest||string.Equals(Path.GetFileName(e.FullPath),"content_log.txt",StringComparison.OrdinalIgnoreCase))steamDirty=true;if(debounce!=null)debounce.Change(180,Timeout.Infinite);}};watcher.Changed+=changed;watcher.Created+=changed;watcher.Deleted+=changed;watcher.Renamed+=delegate(object s,RenamedEventArgs e){changed(s,e);};watcher.Error+=delegate{lock(gate)if(!disposed&&debounce!=null)debounce.Change(180,Timeout.Infinite);};watchers.Add(watcher);watcher.EnableRaisingEvents=true;}}
        public static ActiveDownload ParseSteam(string text,DateTime written,DateTime now){if(text.Length>262144||now-written>TimeSpan.FromSeconds(45)||written>now.AddSeconds(2))return null;Func<string,string> value=key=>{var m=Regex.Match(text,"\""+key+"\"\\s*\"([^\"]*)\"",RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value:"";};long flags;double received,total;if(!long.TryParse(value("StateFlags"),out flags)||(flags&(1L<<9))!=0||(flags&(1L<<20))==0||!double.TryParse(value("BytesDownloaded"),NumberStyles.None,CultureInfo.InvariantCulture,out received)||!double.TryParse(value("BytesToDownload"),NumberStyles.None,CultureInfo.InvariantCulture,out total)||received<0||total<=0||received>=total)return null;string id=value("appid");if(!Regex.IsMatch(id,@"^\d{1,10}$"))return null;return new ActiveDownload{Id="steam:"+id,Source="Steam",Name=value("name"),Received=received,Total=total,Utc=written};}
        void Refresh(bool browser,bool steamEnabled){if(!Monitor.TryEnter(refreshGate))return;try{lock(gate)if(disposed||!initialized)return;RefreshCore(browser,steamEnabled);}catch{}finally{Monitor.Exit(refreshGate);}}
        void RefreshCore(bool browser,bool steamEnabled){var result=new List<ActiveDownload>();DateTime now=DateTime.UtcNow;if(browser){if(nativeBrowser==null){var reader=browserFactory==null?new NativeBrowserDownloads():browserFactory();lock(gate){if(disposed){reader.Dispose();return;}nativeBrowser=reader;}}result.AddRange(nativeBrowser.Read(now));BrowserStatus=nativeBrowser.Status;}if(browser) {
                var packets = ReadBrowserPackets(sharedBrowserInbox, now);
                foreach (var legacy in ReadBrowserPackets(Path.Combine(directory, "BrowserDownloads"), now))
                    if (!packets.ContainsKey(legacy.Key)) packets.Add(legacy.Key, legacy.Value);
                foreach (var packet in packets) {
                    result.RemoveAll(item => item.Source == packet.Key || item.Id == BrowserFileActivity.Id);
                    result.AddRange(packet.Value);
                }
            }
            if(steamEnabled){string[] paths;bool changed;var info=new FileInfo(Path.Combine(steam,"logs","content_log.txt"));long length=info.Exists?info.Length:0;DateTime written=info.Exists?info.LastWriteTimeUtc:DateTime.MinValue;lock(gate){paths=manifests.ToArray();changed=steamDirty||length!=steamLogLength||written!=steamLogWritten;steamDirty=false;steamLogLength=length;steamLogWritten=written;}if(changed)steamCached=SteamDownloads.Read(steam,paths,now);var activeSteam=steamCached.Where(i=>now-i.Utc<TimeSpan.FromSeconds(75)).ToArray();result.AddRange(activeSteam);}
            steamProgress.Configure(result);
            var merged=steamProgress.Merge(result.ToArray(),now);
            lock(gate){if(!disposed)items=merged;}}
        internal static Dictionary<string,List<ActiveDownload>> ReadBrowserPackets(string inbox,DateTime now) {
            var result = new Dictionary<string,List<ActiveDownload>>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(inbox) || !Directory.Exists(inbox)) return result;
            try {
                foreach (string file in Directory.EnumerateFiles(inbox,"browser-*.json").Take(64)) {
                    try {
                        if (new FileInfo(file).Length > 65536) continue;
                        var data = IntegrationJson.Parse(File.ReadAllText(file));
                        DateTime utc;
                        if (!DateTime.TryParse(IntegrationJson.Text(data,"utc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out utc)
                            || now-utc > TimeSpan.FromSeconds(8) || utc > now.AddSeconds(2)) continue;
                        string source = IntegrationJson.Text(data,"source");
                        if (source != "Chrome" && source != "Edge" || IntegrationJson.Get(data,"items") == null) continue;
                        List<ActiveDownload> items;
                        if (!result.TryGetValue(source,out items)) result.Add(source,items=new List<ActiveDownload>());
                        foreach (var item in IntegrationJson.Array(data,"items").Take(64)) {
                            double received = IntegrationJson.Number(item,"received"), total = IntegrationJson.Number(item,"total");
                            if (double.IsNaN(received) || double.IsInfinity(received) || double.IsNaN(total) || double.IsInfinity(total)
                                || received < 0 || total < -1 || received > 1e16 || total > 1e16 || total > 0 && received >= total) continue;
                            items.Add(new ActiveDownload { Id=file+":"+items.Count,Source=source,Name=Path.GetFileName(IntegrationJson.Text(item,"name")),
                                Received=received,Total=total,Utc=utc,CounterUtc=utc,FreshForSeconds=8,CounterOrigin="Browser-Erweiterung" });
                        }
                    } catch { }
                }
            } catch { }
            return result;
        }
        public DownloadSnapshot Latest(){DateTime now=DateTime.UtcNow;lock(gate)return new DownloadSnapshot{Items=items.Where(i=>now-i.Utc<TimeSpan.FromSeconds(i.ActivitySeconds>0?i.ActivitySeconds:i.Source=="Steam"?75:8)).ToArray()};}
        public void Dispose(){lock(gate){disposed=true;if(debounce!=null)debounce.Dispose();if(poll!=null)poll.Dispose();if(nativeBrowser!=null)nativeBrowser.Dispose();steamProgress.Dispose();foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();items=new ActiveDownload[0];}}
    }
}
