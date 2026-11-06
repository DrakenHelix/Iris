using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace Iris {
    public sealed class AppleSeekResult {public bool Success;public string Message,Diagnostic;}
    public static class AppleMusicSeek {
        static readonly object gate=new object();
        static AutomationElement cached;
        static IntPtr cachedWindow;
        static string diagnostic="";
        public static int CandidateScore(string name,double min,double max,double duration,bool longHeader){
            if(max<=min||Regex.IsMatch(name??"",@"volume|lautst.rke|download|laden|quality|qualit",RegexOptions.IgnoreCase))return -1;
            if(Regex.IsMatch(name??"",@"playback|wiedergabe|position|fortschritt|seek|timeline|progress|scrub",RegexOptions.IgnoreCase))return 100;
            if(duration>0&&Math.Abs(max-min-duration)<2)return 60;
            if(longHeader&&(Math.Abs(max-min-1)<.001||Math.Abs(max-min-100)<.001))return 20;
            return -1;
        }
        static AutomationElement FindTimeline(double duration){
            diagnostic="";
            foreach(var process in Process.GetProcessesByName("AppleMusic")){using(process){
                IntPtr handle=process.MainWindowHandle;if(handle==IntPtr.Zero)continue;
                if(cached!=null&&handle==cachedWindow){try{object pattern;if(cached.TryGetCurrentPattern(RangeValuePattern.Pattern,out pattern)&&!((RangeValuePattern)pattern).Current.IsReadOnly)return cached;}catch{}cached=null;}
                var root=AutomationElement.FromHandle(handle);if(root==null)continue;diagnostic+="Window: "+root.Current.Name+". ";if(!root.Current.Name.Equals("Apple Music",StringComparison.OrdinalIgnoreCase))continue;
                var bounds=root.Current.BoundingRectangle;
                var controls=root.FindAll(TreeScope.Descendants,new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Slider),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.ProgressBar)));
                AutomationElement selected=null;int best=-1;bool ambiguous=false;
                foreach(AutomationElement control in controls){
                    object value;if(!control.Current.IsEnabled||!control.TryGetCurrentPattern(RangeValuePattern.Pattern,out value))continue;var range=((RangeValuePattern)value).Current;diagnostic+="Range: "+control.Current.Name+" ["+range.Minimum+","+range.Maximum+"] readOnly="+range.IsReadOnly+". ";if(range.IsReadOnly)continue;
                    Rect rect=control.Current.BoundingRectangle;bool header=!bounds.IsEmpty&&!rect.IsEmpty&&rect.Width>bounds.Width*.25&&rect.Top>=bounds.Top&&rect.Bottom<bounds.Top+bounds.Height*.25;
                    int score=CandidateScore(control.Current.Name,range.Minimum,range.Maximum,duration,header);if(score<0)continue;
                    if(score>best){best=score;selected=control;ambiguous=false;}else if(score==best)ambiguous=true;
                }
                if(selected!=null&&!ambiguous){cached=selected;cachedWindow=handle;return selected;}
            }}return null;
        }
        public static AppleSeekResult Seek(double seconds,double duration,DateTime deadline){
            lock(gate){try{
                if(duration<=0||DateTime.UtcNow>=deadline)return new AppleSeekResult {Message=L.T("Die Spulanfrage ist abgelaufen.")};
                var control=FindTimeline(duration);if(control==null)return new AppleSeekResult {Message=L.T("Apple Music stellt seine Zeitleiste nicht für die direkte Steuerung bereit. Über das Cover kannst du den Player öffnen."),Diagnostic=diagnostic};
                if(DateTime.UtcNow>=deadline)return new AppleSeekResult {Message=L.T("Die Spulanfrage ist abgelaufen.")};
                var pattern=(RangeValuePattern)control.GetCurrentPattern(RangeValuePattern.Pattern);var range=pattern.Current;double target=range.Minimum+(range.Maximum-range.Minimum)*Math.Max(0,Math.Min(1,seconds/duration));
                pattern.SetValue(target);
                for(int i=0;i<3;i++){if(Math.Abs(pattern.Current.Value-target)<=Math.Max(.005*(range.Maximum-range.Minimum),range.SmallChange*.6))return new AppleSeekResult {Success=true,Diagnostic=diagnostic};if(DateTime.UtcNow>=deadline)break;Thread.Sleep(60);}
                return new AppleSeekResult {Message=L.T("Apple Music hat die Position noch nicht bestätigt.")};
            }catch{cached=null;return new AppleSeekResult {Message=L.T("Die Zeitleiste von Apple Music konnte nicht gesteuert werden. Über das Cover kannst du den Player öffnen.")};}}
        }
    }
}
