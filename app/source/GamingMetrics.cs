using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Iris {
    public sealed class GameTarget {
        public int Pid;public long Started;public string Path="",Name="";public bool Fullscreen;
    }
    public static class GameCatalog {
        static readonly string[] excluded={"chrome","msedge","firefox","discord","steam","steamwebhelper","gameoverlayui","explorer","dwm","iris","iris.fpshost","presentmon","vlc","mpv","medal","obs64"};
        public static bool IsGame(string path,IEnumerable<string> registered){
            if(string.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathRooted(path))return false;
            if(registered!=null&&registered.Any(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase)))return true;
            if(excluded.Contains(System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant()))return false;
            // Steam libraries retain this structure even on a different drive.
            return path.Replace('/','\\').IndexOf("\\steamapps\\common\\",StringComparison.OrdinalIgnoreCase)>=0;
        }
        public static GameTarget Resolve(int pid,bool fullscreen,IEnumerable<string> games){string path;long started;if(!GameProcessDiscovery.Identity(pid,out path,out started)||!IsGame(path,games))return null;return new GameTarget{Pid=pid,Started=started,Path=path,Name=System.IO.Path.GetFileNameWithoutExtension(path),Fullscreen=fullscreen};}
    }
    public sealed class FpsSample {public double Fps;public int Pid;public string Mode="";public DateTime At;}
    public sealed class FpsFrames {
        const double WindowSeconds=1, MaxAgeSeconds=1.2;
        const double ComparableShare=.85;
        struct Frame {public double Ms,Time;public bool Displayed;}
        sealed class Chain {
            public readonly Queue<Frame> Frames=new Queue<Frame>();
            public string Mode="";
            public DateTime Received;
            public double LatestTime;
            public int DisplayedFrames;
            public void DiscardBefore(double cutoff){
                while(Frames.Count>600||Frames.Count>0&&Frames.Peek().Time<cutoff){
                    if(Frames.Dequeue().Displayed)DisplayedFrames--;
                }
            }
        }
        readonly object gate=new object();
        readonly Dictionary<string,Chain> chains=new Dictionary<string,Chain>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string,int> columns;
        int target;
        bool useQpc,useCaptureTime;
        double latestTime;
        Chain selected;
        public void Target(int pid){lock(gate){target=pid;columns=null;chains.Clear();selected=null;latestTime=0;}}
        public static List<string> Csv(string line){var result=new List<string>();var part=new System.Text.StringBuilder();bool quote=false;for(int i=0;i<line.Length;i++){char c=line[i];if(c=='"'){if(quote&&i+1<line.Length&&line[i+1]=='"'){part.Append('"');i++;}else quote=!quote;}else if(c==','&&!quote){result.Add(part.ToString());part.Clear();}else part.Append(c);}result.Add(part.ToString());return result;}
        public void Read(string line,DateTime now){
            if(string.IsNullOrEmpty(line)||line.Length>16384)return;
            var fields=Csv(line.TrimStart('\uFEFF'));
            for(int i=0;i<fields.Count;i++)fields[i]=fields[i].Trim();
            lock(gate){
                if(fields.Any(f=>string.Equals(f,"ProcessID",StringComparison.OrdinalIgnoreCase))&&fields.Any(f=>string.Equals(f,"msBetweenPresents",StringComparison.OrdinalIgnoreCase))){
                    columns=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
                    for(int i=0;i<fields.Count;i++)columns[fields[i]]=i;
                    useQpc=columns.ContainsKey("QPCTime");
                    useCaptureTime=useQpc||columns.ContainsKey("TimeInSeconds");
                    chains.Clear();selected=null;latestTime=0;
                    return;
                }
                if(columns==null||target<=0)return;
                Func<string,string> get=delegate(string column){int i;return columns.TryGetValue(column,out i)&&i<fields.Count?fields[i]:"";};
                int pid;double ms;
                if(!int.TryParse(get("ProcessID"),out pid)||pid!=target||!Number(get("msBetweenPresents"),out ms)||ms<=0||ms>5000)return;
                string key=get("SwapChainAddress");
                if(key.Length==0)return;
                double time;
                if(useQpc){
                    long ticks;
                    if(!long.TryParse(get("QPCTime"),NumberStyles.None,CultureInfo.InvariantCulture,out ticks)||ticks<=0)return;
                    time=ticks/(double)Stopwatch.Frequency;
                }else if(useCaptureTime){
                    if(!Number(get("TimeInSeconds"),out time))return;
                }else time=now.Ticks/(double)TimeSpan.TicksPerSecond;

                if(time<latestTime-WindowSeconds)return;
                Chain chain;
                if(!chains.TryGetValue(key,out chain)){
                    if(chains.Count>=8)chains.Remove(chains.OrderBy(p=>p.Value.LatestTime).First().Key);
                    chains[key]=chain=new Chain();
                }else if(useCaptureTime&&time<=chain.LatestTime)return;
                bool displayed=!columns.ContainsKey("Dropped")||get("Dropped")=="0";
                chain.Mode=get("PresentMode");chain.Received=now;chain.LatestTime=time;
                chain.Frames.Enqueue(new Frame{Ms=ms,Time=time,Displayed=displayed});
                if(displayed)chain.DisplayedFrames++;
                latestTime=Math.Max(latestTime,time);
                chain.DiscardBefore(latestTime-WindowSeconds);
            }
        }
        static bool Number(string text,out double value){
            return double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value);
        }
        public FpsSample Latest(DateTime now){return Latest(now,Stopwatch.GetTimestamp());}
        internal FpsSample Latest(DateTime now,long qpcNow){
            lock(gate){
                foreach(var chain in chains.Values)chain.DiscardBefore(latestTime-WindowSeconds);
                double oldestLiveQpc=qpcNow/(double)Stopwatch.Frequency-MaxAgeSeconds;
                var candidates=chains.Values.Where(c=>c.Frames.Count>=2&&c.DisplayedFrames>0&&
                    (now-c.Received).TotalSeconds<=MaxAgeSeconds&&(!useQpc||c.LatestTime>=oldestLiveQpc)).ToArray();
                if(candidates.Length==0){selected=null;return null;}

                // Find the visible game stream first. Then measure its application presents,
                // including dropped ones, so compositor cadence cannot halve render FPS.
                double visibleCutoff=candidates.Max(c=>c.DisplayedFrames)*ComparableShare;
                var preferred=candidates.Where(c=>c.DisplayedFrames>=visibleCutoff)
                    .OrderByDescending(c=>c.Frames.Count).ThenByDescending(c=>c.DisplayedFrames).First();
                if(!candidates.Contains(selected)||selected.DisplayedFrames<visibleCutoff||selected.Frames.Count<preferred.Frames.Count*ComparableShare)
                    selected=preferred;
                // Count intervals between observed frames, excluding the interval before
                // the first one. A stall before the window must not halve recovered FPS.
                double fps=useCaptureTime
                    ?(selected.Frames.Count-1)/(selected.LatestTime-selected.Frames.Peek().Time)
                    :1000*selected.Frames.Count/selected.Frames.Sum(f=>f.Ms);
                if(fps<.2||fps>10000)return null;
                return new FpsSample{Fps=fps,Pid=target,Mode=selected.Mode,At=selected.Received};
            }
        }
    }
}
