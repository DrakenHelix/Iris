using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Text;using System.Threading;using System.Web.Script.Serialization;using System.Windows.Automation;
namespace Iris {
    static class SteamProgressHost {
        static readonly object gate=new object();static SteamCounterTarget[] targets=new SteamCounterTarget[0];static readonly ManualResetEvent stop=new ManualResetEvent(false);static long completed=DateTime.UtcNow.Ticks;
        static string Line(int maximum){var line=new StringBuilder();int value;while((value=Console.In.Read())>=0&&value!='\n'){if(line.Length>=maximum)throw new InvalidDataException();if(value!='\r')line.Append((char)value);}return value<0&&line.Length==0?null:line.ToString();}
        static int Main(string[] args){Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);var json=new JavaScriptSerializer{MaxJsonLength=32768};bool qa=args.Contains("--qa");
            new Thread(delegate(){try{string line;while((line=Line(8192))!=null){var request=json.Deserialize<SteamCounterRequest>(line);if(request==null||request.Stop)break;var valid=(request.Items??new SteamCounterTarget[0]).Where(t=>t!=null&&(t.Id==ChromeDownloadCounter.TargetId||System.Text.RegularExpressions.Regex.IsMatch(t.Id??"",@"^steam:[0-9]{1,10}$"))&&!string.IsNullOrWhiteSpace(t.Name)&&t.Name.Length<=160).Take(8).ToArray();lock(gate)targets=valid;}}catch{}finally{stop.Set();}}){IsBackground=true}.Start();
            var worker=new Thread(delegate(){while(!stop.WaitOne(0)){try{SteamCounterTarget[] current;lock(gate)current=targets;var result=new List<SteamCounterValue>();if(qa&&args.Contains("--qa-hang"))Thread.Sleep(Timeout.Infinite);foreach(var target in current){var value=target.Id==ChromeDownloadCounter.TargetId?(qa?ChromeDownloadCounter.Parse("6,5 MB/s - 1,4 GB von 5,8 GB, 12 min übrig",DateTime.UtcNow):ReadChrome()):qa?new SteamCounterValue{Id=target.Id,Received=375,Total=1000,At=DateTime.UtcNow.ToString("o")}:Read(target);if(value!=null)result.Add(value);}Console.WriteLine(json.Serialize(new SteamCounterReply{Items=result.ToArray()}));Interlocked.Exchange(ref completed,DateTime.UtcNow.Ticks);}catch{try{Console.WriteLine("{\"Items\":[]}");}catch{stop.Set();}Interlocked.Exchange(ref completed,DateTime.UtcNow.Ticks);}if(stop.WaitOne(1500))break;}}){IsBackground=true,Name="Iris Steam counter UIA"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();
            while(!stop.WaitOne(200)){if(DateTime.UtcNow-new DateTime(Interlocked.Read(ref completed),DateTimeKind.Utc)>TimeSpan.FromSeconds(5))break;}stop.Set();return 0;
        }
        static SteamCounterValue Read(SteamCounterTarget target){SteamCounterValue result=null;EnumWindows(delegate(IntPtr window,IntPtr ignored){if(!IsWindowVisible(window)||!SteamWindow(window))return true;try{var root=AutomationElement.FromHandle(window);var titles=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,target.Name,PropertyConditionFlags.IgnoreCase));foreach(AutomationElement title in titles){var parent=title;for(int level=0;level<4&&parent!=null;level++,parent=TreeWalker.ControlViewWalker.GetParent(parent)){if(parent.Current.ControlType==ControlType.Document||parent.Current.ControlType==ControlType.Window)break;var children=parent.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Text));var text=new List<string>();foreach(AutomationElement child in children){string label=child.Current.Name;if(label!=null&&label.Length<=240)text.Add(label);if(text.Count>80)break;}var buttons=parent.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));foreach(AutomationElement button in buttons){string name=button.Current.Name??"";if(System.Text.RegularExpressions.Regex.IsMatch(name,@"\b(Fortsetzen|Resume|Jetzt herunterladen|Download now)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))text.Add(name);}double? percentage=null;var bars=parent.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ProgressBar));foreach(AutomationElement bar in bars){object pattern;string name=bar.Current.Name??"";if(System.Text.RegularExpressions.Regex.IsMatch(name,"Download|Herunterladen",System.Text.RegularExpressions.RegexOptions.IgnoreCase)&&bar.TryGetCurrentPattern(RangeValuePattern.Pattern,out pattern)){var range=((RangeValuePattern)pattern).Current;if(range.Maximum>range.Minimum)percentage=(range.Value-range.Minimum)/(range.Maximum-range.Minimum);}}var value=SteamProgressCounter.Parse(target.Id,text,percentage,DateTime.UtcNow);if(value!=null){result=value;return false;}}}}catch{}return result==null;},IntPtr.Zero);return result;}
        internal static SteamCounterValue ReadChrome(ChromeReadTrace trace = null) {
            SteamCounterValue found = null;
            bool ambiguous = false;
            EnumWindows(delegate(IntPtr window, IntPtr ignored) {
                if (!IsWindowVisible(window) || !MatchesProcess(window, "chrome.exe")) return true;
                try {
                    if (trace != null) trace.ChromeWindows++;
                    SteamCounterValue candidate = null;
                    try { candidate = ChromeDownloadView.Read(AutomationElement.FromHandle(window), trace); } catch { }
                    string route = "UIA";
                    if (candidate == null) {
                        candidate = ChromeDownloadMsaa.Read(window, trace);
                        route = "MSAA";
                    }
                    if (candidate != null && trace != null) trace.Route = route;
                    if (candidate == null) return true;
                    if (found != null) { ambiguous = true; return false; }
                    found = candidate;
                } catch { }
                return true;
            }, IntPtr.Zero);
            return ambiguous ? null : found;
        }
        static bool SteamWindow(IntPtr window){return MatchesProcess(window,"steam.exe","steamwebhelper.exe");}
        static bool MatchesProcess(IntPtr window,params string[] names){uint pid;GetWindowThreadProcessId(window,out pid);IntPtr process=OpenProcess(0x1000,false,pid);if(process==IntPtr.Zero)return false;try{var path=new StringBuilder(32768);int count=path.Capacity;if(!QueryFullProcessImageName(process,0,path,ref count))return false;string name=Path.GetFileName(path.ToString());return names.Contains(name,StringComparer.OrdinalIgnoreCase);}finally{CloseHandle(process);}}
        delegate bool EnumProc(IntPtr window,IntPtr ignored);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr ignored);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder name,ref int length);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    }
}
