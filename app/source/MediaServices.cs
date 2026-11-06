using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Iris {
    public sealed class LyricLine {public double Seconds;public string Text;}
    public sealed class LyricsData {
        public bool Instrumental;
        public string Plain;
        public double Duration;
        public string Track,Artist,Album;
        public List<LyricLine> Lines=new List<LyricLine>();
    }
    public static partial class LyricsService {
        static readonly HttpClient client=CreateClient();
        static HttpClient CreateClient(){ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;var c=new HttpClient {Timeout=TimeSpan.FromSeconds(12)};c.DefaultRequestHeaders.UserAgent.ParseAdd("IrisNotch/0.22.2 (personal Windows media overlay)");return c;}
        public static List<LyricLine> Parse(string lrc){
            var result=new List<LyricLine>();if(string.IsNullOrWhiteSpace(lrc))return result;
            double offset=0;var offsetMatch=Regex.Match(lrc,@"\[offset:([+-]?\d+)\]",RegexOptions.IgnoreCase);
            if(offsetMatch.Success)offset=double.Parse(offsetMatch.Groups[1].Value,CultureInfo.InvariantCulture)/1000;
            foreach(var row in lrc.Split('\n')){
                var stamps=Regex.Matches(row,@"\[(\d{1,3}):(\d{2}(?:\.\d{1,3})?)\]");
                if(stamps.Count==0)continue;
                string text=Regex.Replace(row,@"^(?:\[[^\]]+\])+","").Trim();
                foreach(Match stamp in stamps)result.Add(new LyricLine {Seconds=Math.Max(0,int.Parse(stamp.Groups[1].Value)*60+double.Parse(stamp.Groups[2].Value,CultureInfo.InvariantCulture)-offset),Text=text});
            }
            return result.OrderBy(l=>l.Seconds).ToList();
        }
        public static int ActiveLine(List<LyricLine> lines,double seconds){int active=-1;for(int i=0;i<lines.Count;i++){if(lines[i].Seconds>seconds)break;active=i;}return active;}
    }
    public static class NativeVolume {
        [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class Enumerator {}
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IEnumerator {[PreserveSig]int Enum(int a,int b,out IntPtr collection);[PreserveSig]int Default(int flow,int role,out IDevice device);}
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDevice {[PreserveSig]int Activate(ref Guid id,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object value);}
        [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IVolume {
            [PreserveSig]int Register(IntPtr p);[PreserveSig]int Unregister(IntPtr p);[PreserveSig]int ChannelCount(out uint n);[PreserveSig]int SetDb(float v,ref Guid context);[PreserveSig]int SetScalar(float v,ref Guid context);[PreserveSig]int GetDb(out float v);[PreserveSig]int GetScalar(out float v);
            [PreserveSig]int SetChannelDb(uint channel,float value,ref Guid context);[PreserveSig]int SetChannelScalar(uint channel,float value,ref Guid context);[PreserveSig]int GetChannelDb(uint channel,out float value);[PreserveSig]int GetChannelScalar(uint channel,out float value);[PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);
        }
        static IVolume Endpoint(){return Endpoint(false);}
        static IVolume Endpoint(bool input){var e=(IEnumerator)new Enumerator();IDevice d=null;try{Marshal.ThrowExceptionForHR(e.Default(input?1:0,input?2:1,out d));Guid id=typeof(IVolume).GUID;object value;Marshal.ThrowExceptionForHR(d.Activate(ref id,23,IntPtr.Zero,out value));return (IVolume)value;}finally{if(d!=null)Marshal.ReleaseComObject(d);Marshal.ReleaseComObject(e);}}
        public static bool ToggleMute(bool input){var v=Endpoint(input);try{bool muted;Marshal.ThrowExceptionForHR(v.GetMute(out muted));Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetMute(!muted,ref context));Marshal.ThrowExceptionForHR(v.GetMute(out muted));return muted;}finally{Marshal.ReleaseComObject(v);}}
        public static double Get(){var v=Endpoint();try{float scalar;Marshal.ThrowExceptionForHR(v.GetScalar(out scalar));return scalar;}finally{Marshal.ReleaseComObject(v);}}
        public static void Set(double scalar){var v=Endpoint();try{Guid context=Guid.Empty;Marshal.ThrowExceptionForHR(v.SetScalar((float)Math.Max(0,Math.Min(1,scalar)),ref context));}finally{Marshal.ReleaseComObject(v);}}
    }
}
