using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Iris {
    sealed class LyricsLookupFixture : HttpMessageHandler {
        public readonly List<string> Requests=new List<string>();public string Results;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests.Add(request.RequestUri.ToString());return Task.FromResult(new HttpResponseMessage(request.RequestUri.AbsolutePath=="/api/get"?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=new StringContent(request.RequestUri.AbsolutePath=="/api/get"?"{}":Results)});}
    }
    public static class MediaMetadata {
        public static string[] Clean(string artist,string album,string albumArtist,string source){
            artist=(artist??"").Trim();album=(album??"").Trim();albumArtist=(albumArtist??"").Trim();
            if((source??"").IndexOf("AppleMusic",StringComparison.OrdinalIgnoreCase)>=0){
                int split=artist.IndexOf(" — ",StringComparison.Ordinal);
                if(split>0&&(album.Length==0||artist.Substring(split+3).Trim().Equals(album,StringComparison.OrdinalIgnoreCase))){if(album.Length==0)album=artist.Substring(split+3).Trim();artist=artist.Substring(0,split).Trim();}
                if(album.Length>0&&albumArtist.EndsWith(" — "+album,StringComparison.OrdinalIgnoreCase))albumArtist=albumArtist.Substring(0,albumArtist.Length-album.Length-3).Trim();
            }
            if(artist.Length==0)artist=albumArtist;
            return new[]{artist,album,albumArtist};
        }
    }
    public static class MediaSourceLauncher {
        [ComImport,Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]class ActivationManager {}
        [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IActivationManager {
            [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string id,[MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint pid);
            [PreserveSig]int ActivateForFile(IntPtr id,IntPtr items,IntPtr verb,out uint pid);
            [PreserveSig]int ActivateForProtocol(IntPtr id,IntPtr items,out uint pid);
        }
        [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
        public static bool IsAppId(string source){return !string.IsNullOrWhiteSpace(source)&&Regex.IsMatch(source,@"^[A-Za-z0-9_.-]+![A-Za-z0-9_.-]+$");}
        public static string ProcessName(string source){
            if(string.IsNullOrWhiteSpace(source)||IsAppId(source))return "";
            string name=Path.GetFileName(source);if(name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-4);
            if(name.Equals("MicrosoftEdge",StringComparison.OrdinalIgnoreCase))name="msedge";
            if(name.Equals("Google.Chrome",StringComparison.OrdinalIgnoreCase))name="chrome";
            return Regex.IsMatch(name,@"^[A-Za-z0-9_.-]+$")?name:"";
        }
        public static string DisplayName(string source){if((source??"").IndexOf("AppleMusic",StringComparison.OrdinalIgnoreCase)>=0)return "Apple Music";if((source??"").IndexOf("Spotify",StringComparison.OrdinalIgnoreCase)>=0)return "Spotify";if(IsAppId(source))return L.T("Musikplayer");return string.IsNullOrWhiteSpace(source)?L.T("Musikplayer"):ProcessName(source);}
        public static bool Open(string source){
            if(IsAppId(source)){var manager=(IActivationManager)new ActivationManager();try{uint pid;return manager.ActivateApplication(source,null,2,out pid)>=0;}finally{Marshal.ReleaseComObject(manager);}}
            string name=ProcessName(source);if(name.Length==0)return false;
            foreach(var process in Process.GetProcessesByName(name)){using(process){try{if(process.MainWindowHandle!=IntPtr.Zero){ShowWindowAsync(process.MainWindowHandle,9);return SetForegroundWindow(process.MainWindowHandle);}}catch{}}}
            return false;
        }
    }
    public sealed partial class NotchWindow {
        Button artworkButton;
        string mediaSource="",mediaAlbumArtist="",lastSourceOpenRequest="";
        double mediaTimelineStart,mediaSeekMinimum,mediaSeekMaximum;
        bool seekPending,seekAcknowledged;
        bool reportedSeekSupport=true,appleSeekRunning,appleSeekApplied;
        string seekCommand,seekIdentity;
        double seekTarget;
        DateTime seekRequested,seekDeadline;
        async void OpenMediaSource(){
            if(!mediaAvailable||string.IsNullOrWhiteSpace(mediaSource))return;
            lastSourceOpenRequest=mediaSource;if(uiTesting)return;
            string source=mediaSource;
            try{if(!await Task.Run(()=>MediaSourceLauncher.Open(source)))artworkButton.ToolTip=L.T("Der Player konnte nicht geöffnet werden.");}catch{artworkButton.ToolTip=L.T("Der Player konnte nicht geöffnet werden.");}
        }
        string SeekIdentity(){return mediaTitle+"|"+mediaArtist+"|"+mediaSource;}
        double DisplayMediaPosition(){return Math.Max(0,seekTarget+(isPlaying?Math.Max(0,(DateTime.UtcNow-seekRequested).TotalSeconds):0));}
        void RequestSeek(double seconds){
            if(!mediaAvailable||mediaDuration<=0)return;
            seekTarget=Math.Max(mediaSeekMinimum,Math.Min(mediaSeekMaximum,seconds));seekRequested=DateTime.UtcNow;seekDeadline=seekRequested.AddSeconds(3);seekIdentity=SeekIdentity();seekPending=true;seekAcknowledged=false;appleSeekRunning=false;appleSeekApplied=false;
            try{seekCommand=SendMediaCommand("seek",mediaTimelineStart+seekTarget);mediaSeek.ToolTip=L.T("Position anklicken oder ziehen");}
            catch{seekPending=false;mediaSeek.ToolTip=L.T("Spulauftrag konnte nicht gesendet werden.");}
            if(seekPending&&!reportedSeekSupport&&mediaSource.IndexOf("AppleMusic",StringComparison.OrdinalIgnoreCase)>=0&&!uiTesting)SeekAppleTimeline(seekCommand,seekIdentity,seekTarget,mediaDuration);
            RefreshMediaProgress();
        }
        async void SeekAppleTimeline(string command,string identity,double target,double duration){
            appleSeekRunning=true;var deadline=seekDeadline;
            var result=await Task.Run(()=>AppleMusicSeek.Seek(target,duration,deadline));
            if(shuttingDown||command!=seekCommand||identity!=SeekIdentity()||!mediaAvailable)return;
            try{File.WriteAllText(System.IO.Path.Combine(root,dataFolder,"last-seek.json"),serializer.Serialize(new Dictionary<string,object>{{"time",DateTime.UtcNow.ToString("o")},{"direct",result.Success},{"target",target},{"message",result.Message},{"diagnostic",result.Diagnostic}}));}catch{}
            appleSeekRunning=false;
            if(result.Success){appleSeekApplied=true;seekAcknowledged=true;seekPending=true;seekDeadline=DateTime.UtcNow.AddSeconds(3);ConfirmSeekSnapshot();}
            else if(!string.IsNullOrWhiteSpace(result.Message))mediaSeek.ToolTip=result.Message;
            RefreshMediaProgress();
        }
        void ReadSeekCommandResult(Dictionary<string,object> data){
            if(!seekPending||!data.ContainsKey("id")||Convert.ToString(data["id"])!=seekCommand||!data.ContainsKey("success"))return;
            if(Convert.ToBoolean(data["success"]))seekAcknowledged=true;
            else if(!appleSeekRunning&&!appleSeekApplied){seekPending=false;mediaSeek.ToolTip=L.T("Dieser Player hat das Spulen abgelehnt. Klicke auf das Cover, um ihn zu öffnen.");RefreshMediaProgress();}
        }
        void ConfirmSeekSnapshot(){
            if(!seekPending)return;
            if(seekIdentity!=SeekIdentity()){seekPending=false;return;}
            double confirmed=mediaPosition+(isPlaying?Math.Max(0,(DateTime.UtcNow-mediaUpdated).TotalSeconds):0);
            if(seekAcknowledged&&Math.Abs(confirmed-DisplayMediaPosition())<=2)seekPending=false;
            else if(DateTime.UtcNow>=seekDeadline){seekPending=false;mediaSeek.ToolTip=L.T("Spulposition wurde vom Player nicht bestätigt. Über das Cover kannst du den Player öffnen.");}
        }
        void RefreshSeekCapability(Dictionary<string,object> data){
            reportedSeekSupport=!data.ContainsKey("canSeek")||Convert.ToBoolean(data["canSeek"]);
            mediaSeekMinimum=data.ContainsKey("minSeek")?Math.Max(0,Convert.ToDouble(data["minSeek"])):0;
            mediaSeekMaximum=data.ContainsKey("maxSeek")?Math.Min(mediaDuration,Convert.ToDouble(data["maxSeek"])):mediaDuration;
            if(mediaSeekMaximum<=mediaSeekMinimum){mediaSeekMinimum=0;mediaSeekMaximum=mediaDuration;}
            // Some players publish a false capability flag but still handle seek requests.
            mediaSeek.IsEnabled=mediaDuration>0&&mediaSeekMaximum>mediaSeekMinimum;
        }
        void ReadInteractionFixture(double position,double start,double min,double max,bool canSeek){
            ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"available",true},{"title","Eigener Spultest"},{"artist","Testkünstler — Testalbum"},{"albumArtist","Testkünstler — Testalbum"},{"album",""},{"source","AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"},{"playing",false},{"start",start},{"position",position},{"duration",288},{"minSeek",min},{"maxSeek",max},{"canSeek",canSeek},{"artChanged",false}}));
        }
        void VerifyMediaInteraction(List<string> results){
            ReadInteractionFixture(21,0,0,288,false);
            if(!mediaSeek.IsEnabled||mediaArtist!="Testkünstler"||mediaAlbum!="Testalbum")throw new Exception("Apple Music metadata/capability handling is incorrect");
            mediaSeek.Value=.5;mediaSeeking=true;CommitSeek();string first=seekCommand;
            var command=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(System.IO.Path.Combine(bridgeDirectory,first+".json")));
            if(!seekPending||Convert.ToString(command["action"])!="seek"||Math.Abs(Convert.ToDouble(command["seconds"])-144)>.001)throw new Exception("Disabled capability flag blocks the actual seek command");
            ReadInteractionFixture(21,0,0,288,false);if(Math.Abs(CurrentMediaPosition()-144)>.01)throw new Exception("A stale snapshot resets an in-flight seek");
            Seek(90);string second=seekCommand;ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",first},{"success",false}}));if(!seekPending||seekCommand!=second)throw new Exception("An old failure resets a newer seek");
            ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",second},{"success",true}}));ReadInteractionFixture(90,0,0,288,false);if(seekPending||Math.Abs(CurrentMediaPosition()-90)>.01)throw new Exception("The confirmed seek does not settle");
            Seek(200);ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",seekCommand},{"success",false}}));if(seekPending||Math.Abs(CurrentMediaPosition()-90)>.01)throw new Exception("Rejected seek leaves a fake position");
            Seek(200);seekDeadline=DateTime.UtcNow.AddSeconds(-1);StepPlayer(.016);if(seekPending)throw new Exception("Unconfirmed seek has no recovery");
            ReadInteractionFixture(30,100,20,120,false);Seek(200);command=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(System.IO.Path.Combine(bridgeDirectory,seekCommand+".json")));if(Math.Abs(Convert.ToDouble(command["seconds"])-220)>.001)throw new Exception("Native start offset or seek bounds are wrong");
            lastSourceOpenRequest="";artworkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(lastSourceOpenRequest!=mediaSource||!MediaSourceLauncher.IsAppId(lastSourceOpenRequest))throw new Exception("Cover click does not target the real media source");
            ReadMedia("{\"available\":false}");lastSourceOpenRequest="";artworkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(artworkButton.IsEnabled||lastSourceOpenRequest.Length!=0||seekPending)throw new Exception("Missing session leaves source action or seek state active");
            var handler=new LyricsLookupFixture();handler.Results=serializer.Serialize(new[]{new Dictionary<string,object>{{"trackName","Testsong"},{"artistName","Falscher Künstler"},{"albumName","Album"},{"duration",180},{"syncedLyrics","[00:01.00]Eigene Testzeile"}},new Dictionary<string,object>{{"trackName","Testsong"},{"artistName","Testartist"},{"albumName","Andere Edition"},{"duration",180},{"syncedLyrics","[00:01.00]Eigene Testzeile"}}});
            using(var http=new HttpClient(handler)){var data=LyricsService.GetUsing(http,"Testsong (feat. Gast)","Testartist & Gast","Deluxe Album",180,CancellationToken.None).GetAwaiter().GetResult();if(data==null||data.Lines.Count!=1||handler.Requests.Count!=3||!handler.Requests.Last().Contains("/api/search?"))throw new Exception("Exact lookup failures do not fall back to validated lyric search");}
            var mismatch=LyricsService.SelectSearchResult(handler.Results,"Testsong","Testartist","Album",200);if(mismatch==null||mismatch.Lines.Count!=0||string.IsNullOrWhiteSpace(mismatch.Plain))throw new Exception("A different recording is incorrectly synchronized");
            var normalized=LyricsService.Normalize("Song - Remastered","Artist");if(normalized[1]!="Artist")throw new Exception("A title suffix overwrites the real artist");
            if(AppleMusicSeek.CandidateScore(L.T("Lautstärke"),0,100,288,true)>=0||AppleMusicSeek.CandidateScore("Volume",0,288,288,true)>=0||AppleMusicSeek.CandidateScore(L.T("Qualität"),0,100,288,true)>=0||AppleMusicSeek.CandidateScore(L.T("Wiedergabeposition"),0,100,288,true)<0)throw new Exception("Apple Music timeline selection can target volume/quality controls");
            ReadMediaFixture();
            results.Add("PASS: Apple Music false seek flag remains interactive; seek bounds, native time offsets, stale/old replies, confirmations, rejection and timeout reconcile. Cover clicks target the session app and are inactive without media.");
            results.Add("PASS: Apple Music combined artist/album metadata is split; mocked exact lyric misses fall back to validated search, reject wrong artists and disable sync for different durations.");
        }
    }
}
