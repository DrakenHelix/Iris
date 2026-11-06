using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Text;using System.Text.RegularExpressions;
namespace Iris {
    public static class SteamDownloads {
        sealed class State {public string Id;public bool Active;public DateTime At,CounterAt;public double Received,Total;}
        static readonly Regex app=new Regex(@"AppID (\d{1,10}) (.*)$",RegexOptions.Compiled);
        static readonly Regex counters=new Regex(@"update (?:started|progress)\s*:\s*download (\d+)/(\d+)",RegexOptions.Compiled|RegexOptions.IgnoreCase);
        static readonly Regex rate=new Regex(@"Current download rate:\s*([0-9.]+) Mbps",RegexOptions.Compiled|RegexOptions.IgnoreCase);
        public static readonly TimeSpan Fresh=TimeSpan.FromSeconds(75);
        const int CounterFreshSeconds=8;
        static string Value(string text,string key){var m=Regex.Match(text,"\""+key+"\"\\s*\"([^\"]*)\"",RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value:"";}
        public static ActiveDownload[] Parse(string log,DateTime now,Func<string,Tuple<string,DateTime>> manifest){
            var states=new Dictionary<string,State>();foreach(var line in log.Split('\n')){if(line.Length<23||line.Length>8192)continue;DateTime local;if(!DateTime.TryParseExact(line.Substring(1,19),"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out local))continue;DateTime at=DateTime.SpecifyKind(local,DateTimeKind.Local).ToUniversalTime();if(at>now.AddSeconds(2))continue;var match=app.Match(line);
                if(match.Success){string id=match.Groups[1].Value,body=match.Groups[2].Value;State state;if(!states.TryGetValue(id,out state)){state=new State{Id=id};states.Add(id,state);}var bytes=counters.Match(body);if(bytes.Success){double received,total;if(double.TryParse(bytes.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out received)&&double.TryParse(bytes.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out total)&&received<=total&&total>0){state.Received=received;state.Total=total;state.CounterAt=at;if(state.Active&&received<total)state.At=at;}}
                    if(body.StartsWith("App update changed :",StringComparison.Ordinal)){state.Active=body.Contains("Downloading")&&!body.Contains("Stopping");state.At=at;}
                    if(body.StartsWith("update canceled",StringComparison.Ordinal)||body.StartsWith("scheduler finished",StringComparison.Ordinal)||body.Contains("(Suspended)")||body.StartsWith("state changed : Fully Installed,",StringComparison.Ordinal)&&!body.Contains("Update Running")){state.Active=false;state.At=at;}
                }else{var speed=rate.Match(line);double mbps;if(speed.Success&&double.TryParse(speed.Groups[1].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out mbps)&&mbps>0){var active=states.Values.Where(s=>s.Active).ToArray();if(active.Length==1)active[0].At=at;}}
            }
            var result=new List<ActiveDownload>();foreach(var state in states.Values.Where(s=>s.Active&&now-s.At<=Fresh)){
                string name=L.T("Steam-Download ")+state.Id;double received=state.Received,total=state.Total;DateTime counterAt=state.CounterAt;
                var saved=manifest==null?null:manifest(state.Id);if(saved!=null&&saved.Item1.Length<262144){string text=saved.Item1;string title=Value(text,"name");if(title.Length>0)name=title;double savedReceived,savedTotal;if(double.TryParse(Value(text,"BytesDownloaded"),NumberStyles.None,CultureInfo.InvariantCulture,out savedReceived)&&double.TryParse(Value(text,"BytesToDownload"),NumberStyles.None,CultureInfo.InvariantCulture,out savedTotal)&&savedReceived>received&&savedReceived<savedTotal&&saved.Item2>=counterAt&&saved.Item2<=now.AddSeconds(2)){received=savedReceived;total=savedTotal;counterAt=saved.Item2;}}
                // The install manifest is a checkpoint, not a live counter. Never extrapolate from Mbps.
                bool current=counterAt!=DateTime.MinValue&&now-counterAt<=TimeSpan.FromSeconds(CounterFreshSeconds)&&total>0&&received<total;
                result.Add(new ActiveDownload{Id="steam:"+state.Id,Name=name,Source="Steam",Received=current?received:0,Total=current?total:-1,CounterUtc=current?(DateTime?)counterAt:null,FreshForSeconds=CounterFreshSeconds,Utc=state.At,ProgressNote=L.T("Download läuft · Steam liefert gerade keinen aktuellen Prozentwert")});
            }return result.ToArray();
        }
        public static ActiveDownload[] Read(string steam,string[] manifests,DateTime now){string file=Path.Combine(steam,"logs","content_log.txt");try{if(!File.Exists(file))return new ActiveDownload[0];string text;using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){long offset=Math.Max(0,stream.Length-2*1024*1024);stream.Position=offset;using(var reader=new StreamReader(stream,Encoding.UTF8,true,4096)){if(offset>0)reader.ReadLine();text=reader.ReadToEnd();}}return Parse(text,now,id=>{string expected="appmanifest_"+id+".acf";string path=manifests.FirstOrDefault(p=>string.Equals(Path.GetFileName(p),expected,StringComparison.OrdinalIgnoreCase));if(path==null)return null;try{var info=new FileInfo(path);if(!info.Exists||info.Length>262144)return null;return Tuple.Create(File.ReadAllText(path),info.LastWriteTimeUtc);}catch{return null;}});}catch{return new ActiveDownload[0];}}
    }
}
