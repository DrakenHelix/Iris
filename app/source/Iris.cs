using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Markup;
using Forms = System.Windows.Forms;

namespace Iris {
    public class Preferences {
        public int Minutes = 5;
        public int Seconds = 0;
        public string Language="de";
        public int NotchSize=0;
        public bool MinimalNotch=false;
        public bool CursorAvoidance=false;
        public bool BatteryEnabled=false;
        public bool GamingEnabled=false;
        public bool DownloadsEnabled=true,BrowserDownloads=true,SteamDownloads=true;
        public bool GamingWindowed=true,GamingFullscreen=true;
        public List<string> GamingGames=new List<string>();
        public bool Mascot = true;
        public bool HideFullscreen = false;
        public string NotchDesign="black",FullscreenDesign="inherit";
        public int DesignSchema=0;
        public bool Music = true;
        public bool Shelf = true;
        public bool Pinned = false;
        public bool ReduceMotion = false;
        public int Monitor = 0;
        public double Offset = 0;
        public string Page = "Timer";
        public List<string> Files = new List<string>();
        public bool TimerRunning = false;
        public DateTime TimerEndUtc;
        public double Remaining = 300;
    }

    public sealed class Countdown {
        public double Remaining;
        public bool Running;
        public DateTime Ends;
        public Countdown(double seconds) { Remaining = seconds; }
        public void Start(DateTime now) { if (Remaining <= 0) return; Ends = now.AddSeconds(Remaining); Running = true; }
        public void Pause(DateTime now) { Update(now); Running = false; }
        public bool Update(DateTime now) {
            if (!Running) return false;
            Remaining = Math.Max(0, (Ends - now).TotalSeconds);
            if (Remaining <= 0) { Running = false; return true; }
            return false;
        }
        public void Reset(double seconds) { Running = false; Remaining = seconds; }
    }

    public sealed partial class NotchSurface : FrameworkElement {
        StreamGeometry visibleGeometry;
        public static readonly DependencyProperty SpanProperty = DependencyProperty.Register("Span", typeof(double), typeof(NotchSurface), new FrameworkPropertyMetadata(192.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty DepthProperty = DependencyProperty.Register("Depth", typeof(double), typeof(NotchSurface), new FrameworkPropertyMetadata(36.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Span { get { return (double)GetValue(SpanProperty); } set { SetValue(SpanProperty,value); } }
        public double Depth { get { return (double)GetValue(DepthProperty); } set { SetValue(DepthProperty,value); } }
        protected override void OnRender(DrawingContext dc) {PaintSurface(dc);}
        public Geometry GetOutline() {
            double w = Math.Max(67.5,Span), h = Math.Max(3.5,Depth), x = ((ActualWidth>0?ActualWidth:Width)-w)/2, s=13+5*Math.Max(0,Math.Min(1,(Span-198)/322)), r=Math.Min(38,h*0.44), shoulder=Math.Min(22,h*.48), controlX=8,controlY=5;
            if(h<8){double t=Math.Max(0,(h-3.5)/4.5);t=t*t*t*(t*(6*t-15)+10);s*=t;shoulder*=t;controlX*=t;controlY*=t;r=3.5+(r-3.5)*t;}
            var g = new StreamGeometry();
            using (var c = g.Open()) {
                c.BeginFigure(new Point(x,0),true,true);
                c.BezierTo(new Point(x+controlX,0),new Point(x+s,controlY),new Point(x+s,shoulder),true,false);
                c.LineTo(new Point(x+s,h-r),true,false);
                c.BezierTo(new Point(x+s,h-r+r*.55228475),new Point(x+s+r-r*.55228475,h),new Point(x+s+r,h),true,false);
                c.LineTo(new Point(x+w-s-r,h),true,false);
                c.BezierTo(new Point(x+w-s-r+r*.55228475,h),new Point(x+w-s,h-r+r*.55228475),new Point(x+w-s,h-r),true,false);
                c.LineTo(new Point(x+w-s,shoulder),true,false);
                c.BezierTo(new Point(x+w-s,controlY),new Point(x+w-controlX,0),new Point(x+w,0),true,false);
            }
            g.Freeze();
            visibleGeometry=g;
            return g;
        }
        public bool ContainsPoint(Point point) {return visibleGeometry!=null&&visibleGeometry.FillContains(point);}
        protected override HitTestResult HitTestCore(PointHitTestParameters parameters) {return ContainsPoint(parameters.HitPoint)?new PointHitTestResult(this,parameters.HitPoint):null;}
    }
    // Small code-native symbols keep the outline, stroke weight and optical alignment consistent.
    public sealed class DesignSymbol : FrameworkElement {
        readonly string kind;Brush color;
        public DesignSymbol(string value,Brush brush){kind=value;color=brush;SnapsToDevicePixels=false;}
        public Brush Ink {get{return color;}set{color=value;InvalidateVisual();}}
        protected override void OnRender(DrawingContext dc){
            dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));var pen=new Pen(color,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            if(kind=="left"||kind=="right"){pen.Thickness=2.2;double a=kind=="left"?14:10,b=kind=="left"?9:15;dc.DrawLine(pen,new Point(a,5),new Point(b,12));dc.DrawLine(pen,new Point(b,12),new Point(a,19));}
            else if(kind=="timer"){dc.DrawEllipse(null,pen,new Point(12,13.6),8.2,8.2);dc.DrawLine(pen,new Point(12,13.6),new Point(12,8.2));dc.DrawLine(pen,new Point(12,5.4),new Point(12,2));dc.DrawLine(pen,new Point(8.3,1.8),new Point(15.7,1.8));dc.DrawLine(pen,new Point(18.5,6.8),new Point(21,4.3));}
            else if(kind=="music"){dc.DrawGeometry(null,pen,Geometry.Parse("M 9,17 L 9,5 L 20,2.5 L 20,14.5 M 9,8 L 20,5.5"));dc.DrawEllipse(color,null,new Point(6,18),3,2.3);dc.DrawEllipse(color,null,new Point(17,15.5),3,2.3);}
            else if(kind=="shelf"){dc.DrawGeometry(null,pen,Geometry.Parse("M 4,9 L 6.5,3 L 17.5,3 L 20,9 L 20,20 L 4,20 Z M 4,10 L 8,10 L 9.5,13 L 14.5,13 L 16,10 L 20,10"));}
            else if(kind=="mixer"){dc.DrawGeometry(null,pen,Geometry.Parse("M 5,3 L 5,7 M 5,13 L 5,21 M 12,3 L 12,13 M 12,19 L 12,21 M 19,3 L 19,6 M 19,12 L 19,21"));dc.DrawRoundedRectangle(null,pen,new Rect(2,7,6,5),1.5,1.5);dc.DrawRoundedRectangle(null,pen,new Rect(9,13,6,5),1.5,1.5);dc.DrawRoundedRectangle(null,pen,new Rect(16,6,6,5),1.5,1.5);}
            else if(kind=="addons"){for(int row=0;row<2;row++)for(int col=0;col<2;col++)dc.DrawRoundedRectangle(null,pen,new Rect(3+col*10,3+row*10,7,7),2,2);}
            else if(kind=="brand"){dc.DrawEllipse(null,new Pen(color,2.6),new Point(12,12),9,9);dc.DrawGeometry(color,null,Geometry.Parse("M 13,5.5 L 8.5,12.5 L 12,12.5 L 11,18.5 L 16,10.5 L 12.5,10.5 Z"));}
            else if(kind=="reset"){var geometry=Geometry.Parse("M 18.2,6 A 8,8 0 1 1 10,4 M 10,1 L 10,7 L 15,5");dc.DrawGeometry(null,pen,geometry);}
            else if(kind=="lyrics"){dc.DrawGeometry(null,pen,Geometry.Parse("M 6,3.5 L 18,3.5 Q 21,3.5 21,6.5 L 21,14 Q 21,17 18,17 L 12,17 L 7,21 L 7,17 L 6,17 Q 3,17 3,14 L 3,6.5 Q 3,3.5 6,3.5 Z M 8,8 L 7,8 L 7,11 L 9,11 L 9,13 M 14,8 L 13,8 L 13,11 L 15,11 L 15,13"));}
            else if(kind=="volume"){dc.DrawGeometry(color,null,Geometry.Parse("M 2,9 L 6,9 L 11,5 L 11,19 L 6,15 L 2,15 Z"));dc.DrawGeometry(null,pen,Geometry.Parse("M 14,8 Q 18,12 14,16 M 17,5 Q 24,12 17,19"));}
            dc.Pop();
        }
    }

    public sealed class TransportSymbol : FrameworkElement {
        string mode;public bool AnimateChanges=true;
        double actionPhase,actionVelocity,actionTarget,pause,pauseVelocity,pauseTarget;
        public double ActionPhase {get{return actionPhase;}}
        public bool ActionRunning {get{return Math.Abs(actionTarget-actionPhase)>.0001||Math.Abs(actionVelocity)>.001||Math.Abs(pauseTarget-pause)>.0001||Math.Abs(pauseVelocity)>.001;}}
        public void TriggerAction(bool animate){if(mode!="previous"&&mode!="next")return;actionTarget=Math.Floor(actionTarget+1.00001);if(!animate){actionPhase=actionTarget;actionVelocity=0;}InvalidateVisual();}
        public void StepAction(double dt,bool animate){if(!ActionRunning)return;if(animate){PlayerSpring.Step(ref actionPhase,ref actionVelocity,actionTarget,dt,.30,1);PlayerSpring.Step(ref pause,ref pauseVelocity,pauseTarget,dt,.38,1);}else{actionPhase=actionTarget;actionVelocity=0;pause=pauseTarget;pauseVelocity=0;}InvalidateVisual();}
        void Arrow(DrawingContext dc,double center,double size,double alpha){if(size<.001||alpha<.001)return;dc.PushOpacity(alpha);dc.PushTransform(new ScaleTransform(size,size,center,9));Triangle(dc,center+4,9,center-4,3,center-4,15);dc.Pop();dc.Pop();}
        void DrawSkip(DrawingContext dc){double p=actionPhase-Math.Floor(actionPhase);if(mode=="previous")dc.PushTransform(new ScaleTransform(-1,1,11,9));Arrow(dc,8+8*p,1,1);Arrow(dc,16+6*p,1-p,1-p);Arrow(dc,4+4*p,p,p);if(mode=="previous")dc.Pop();}
        public double Pause {get{return pause;}}
        public TransportSymbol(string value){mode=value;Width=value=="previous"||value=="next"?28:24;Height=value=="previous"||value=="next"?24:22;}
        public string Mode {get{return mode;}set{mode=value;pauseTarget=value=="pause"?1:0;if(!AnimateChanges){pause=pauseTarget;pauseVelocity=0;}InvalidateVisual();}}
        void Triangle(DrawingContext dc,double a,double b,double c,double d,double e,double f){var shape=new StreamGeometry();using(var s=shape.Open()){s.BeginFigure(new Point(a,b),true,true);s.LineTo(new Point(c,d),true,false);s.LineTo(new Point(e,f),true,false);}shape.Freeze();dc.DrawGeometry(Brushes.White,null,shape);}
        Point Blend(double ax,double ay,double bx,double by,double p){return new Point(ax+(bx-ax)*p,ay+(by-ay)*p);}
        void DrawToggle(DrawingContext dc){
            // Two joined portions of the triangle separate into the pause bars.
            double p=Math.Max(0,Math.Min(1,pause));var shape=new StreamGeometry {FillRule=FillRule.Nonzero};
            using(var g=shape.Open()){
                g.BeginFigure(Blend(5,2,5,3,p),true,true);g.LineTo(Blend(10.5,4.961538,9,3,p),true,false);g.LineTo(Blend(10.5,13.038462,9,15,p),true,false);g.LineTo(Blend(5,16,5,15,p),true,false);
                g.BeginFigure(Blend(10.5,4.961538,13,3,p),true,true);g.LineTo(Blend(18,9,17,3,p),true,false);g.LineTo(Blend(18,9,17,15,p),true,false);g.LineTo(Blend(10.5,13.038462,13,15,p),true,false);
            }shape.Freeze();dc.DrawGeometry(Brushes.White,null,shape);
        }
        protected override void OnRender(DrawingContext dc){dc.PushTransform(new ScaleTransform(ActualWidth/22,ActualHeight/18));if(mode=="previous"||mode=="next")DrawSkip(dc);else DrawToggle(dc);dc.Pop();}
    }

    public class App : Application {
        static Mutex instance;
        internal static bool ReopenSettingsAfterRestart;
        [STAThread] public static void Main(string[] args) {
            if(args.Length>0&&args[0]=="--apply-portable-update"){IrisUpdate.RunPortable(args);return;}
            if (args.Length > 0 && args[0] == "--self-test") { SelfTest(args.Length>1?args[1]:"self-test.txt"); return; }
            if(args.Contains("--stability-observer-soak")){StabilityQa.Soak();return;}if(args.Contains("--stability-core-test")){StabilityQa.Core();return;}if(args.Contains("--native-download-test")){NativeDownloadQa.Run(false);return;}if(args.Contains("--native-download-probe")){NativeDownloadQa.Run(true);return;}if(args.Contains("--navigation-core-test")){NavigationQa.Core(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}if(args.Contains("--discord-icon-probe")){NavigationQa.IconProbe(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}if(args.Contains("--discord-core-test")){DiscordQa.Core(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}if(args.Contains("--steam-library-probe")){SteamPickerQa.Probe(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}if(args.Contains("--steam-library-core-test")){SteamPickerQa.Core(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}if(args.Contains("--steam-download-probe")){SteamDownloadQa.Probe(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}if(args.Contains("--phase4-core-test")){AddonPhase4Qa.Run(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}
            if(args.Contains("--addon-core-test")){AddonCoreQa.Run(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}
            if(args.Contains("--addon-inspect-qa")){AddonCoreQa.InspectNative(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data-qa"));return;}
            bool created;
            instance = new Mutex(true,ReleaseVersion.InstanceMutex+(args.Contains("--qa")?".qa":""),out created);
            if (!created) { return; }
            if(args.Length>0&&args[0]=="--finish-update")IrisUpdate.CleanAfterRestart(args,false);
            if(args.Length>0&&args[0]=="--finish-installed-update")IrisUpdate.CleanAfterRestart(args,true);
            try {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                var app = new App();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e) {
                    WriteError(e.Exception);
                    e.Handled = true;
                };
                app.Run(new NotchWindow(args.Contains("--preview")));
            } catch (Exception e) {
                WriteError(e);
                MessageBox.Show(L.T("Iris konnte nicht starten. Details stehen in error.log.\n")+e.Message,"Iris");
            } finally { instance.ReleaseMutex(); instance.Dispose(); }
            if(ReopenSettingsAfterRestart){try{Process.Start(new ProcessStartInfo(typeof(App).Assembly.Location,"--open-settings"){UseShellExecute=false,CreateNoWindow=true});}catch(Exception e){MessageBox.Show(e.Message,"Iris");}}
        }
        static void WriteError(Exception exception){
            try{
                string path=DataLocation.ErrorLog(AppDomain.CurrentDomain.BaseDirectory);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.AppendAllText(path,DateTime.Now+" "+exception+Environment.NewLine);
            }catch{}
        }
        static void SelfTest(string path) {
            var log = new List<string>();
            try {
                var t = new Countdown(90); var now = new DateTime(2026,10,28,12,0,0,DateTimeKind.Utc);
                t.Start(now); t.Update(now.AddSeconds(35)); Require(Math.Abs(t.Remaining-55)<0.01,"Timer counts elapsed time"); log.Add("PASS: Countdown uses elapsed time, not tick count.");
                t.Pause(now.AddSeconds(40)); t.Update(now.AddHours(1)); Require(t.Remaining==50 && !t.Running,"Pause stable"); log.Add("PASS: Paused timer remains stable.");
                t.Start(now.AddHours(1)); Require(t.Update(now.AddHours(1).AddSeconds(51)) && t.Remaining==0 && !t.Running,"Timer finishes"); Require(!t.Update(now.AddHours(2)),"Only finish once"); log.Add("PASS: Completion fires once and clamps at zero.");
                t.Reset(3600); Require(t.Remaining==3600 && !t.Running,"Reset");
                var p = new Preferences(); p.Minutes=42; p.Seconds=17; p.Files.Add("C:\\Beispiel mit Leerzeichen\\Datei.txt");
                var json = new JavaScriptSerializer(); var roundtrip=json.Deserialize<Preferences>(json.Serialize(p));
                Require(roundtrip.Minutes==42 && roundtrip.Seconds==17 && roundtrip.Files[0]==p.Files[0],"Persistence"); log.Add("PASS: Custom timer and file paths survive serialization.");
                var lyrics=LyricsService.Parse("[offset:100]\n[00:02.30][00:01.20]Eigene Testzeile\n[00:04.00]Nächste Testzeile");Require(lyrics.Count==3&&Math.Abs(lyrics[0].Seconds-1.1)<.001&&Math.Abs(lyrics[1].Seconds-2.2)<.001,"LRC timestamps and offset");Require(LyricsService.ActiveLine(lyrics,0)==-1&&LyricsService.ActiveLine(lyrics,2.3)==1,"Lyrics active line");log.Add("PASS: LRC multiple timestamps, offset, sorting and active-line selection.");
                var normalized=LyricsService.Normalize("Beispielartist - Beispielsong (Official Video)","BeispielartistVEVO");Require(normalized[0]=="Beispielsong"&&normalized[1]=="Beispielartist","YouTube music title normalization");log.Add("PASS: Common YouTube title decorations normalize to song and artist.");
                log.Add("ALL CHECKS PASSED");
            } catch (Exception e) { log.Add("FAILED: "+e); Environment.ExitCode=1; }
            File.WriteAllLines(path,log);
        }
        static void Require(bool value,string message) { if (!value) throw new Exception(message); }
    }

    public sealed partial class NotchWindow : Window {
        const double OpenSpan=520, OpenDepth=160, ClosedSpan=198, ClosedDepth=34;
        readonly SolidColorBrush ink = new SolidColorBrush(Color.FromRgb(242,244,248));
        readonly SolidColorBrush muted = new SolidColorBrush(Color.FromRgb(139,139,142));
        readonly SolidColorBrush mint = new SolidColorBrush(Color.FromRgb(157,244,208));
        readonly string root=AppDomain.CurrentDomain.BaseDirectory;
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer {MaxJsonLength=12*1024*1024};
        Preferences prefs;
        Countdown countdown;
        Canvas canvas;
        NotchSurface surface;
        Grid compact;
        StackPanel compactText;
        StackPanel eyes;
        Border eyeLeft, eyeRight;
        TextBlock clock, compactTimer, date, pageLabel, timerText, timerHint, trackTitle, trackArtist, trackPosition, emptyShelf;
        Button startButton;
        Button previousPageButton,nextPageButton;
        Button quickSettingsButton;
        Border expanded, pageHost;
        StackPanel nav;
        Border quickBar;
        Grid navigation;
        Border navigationTrigger;
        StackPanel pageDots;
        RowDefinition navigationRow;
        bool quickExpanded, lyricsVisible, mediaAvailable, mediaSeeking, volumeUpdating;
        Border lyricsPanel;
        StackPanel lyricsLines;
        ScrollViewer lyricsScroll;
        TextBlock lyricsStatus, musicDate, trackRemaining;
        Grid headerChrome;
        Canvas pageStrip;
        sealed class TabMotion {
            public Grid View; public string Name; public TranslateTransform Shift=new TranslateTransform();
            public double X,Velocity,TargetX,Alpha,FromAlpha,TargetAlpha,Started,Delay;
            public double BlurRadius,BlurVelocity;
            public BlurEffect ExitBlur=new BlurEffect {Radius=0,KernelType=KernelType.Gaussian,RenderingBias=RenderingBias.Performance};
        }
        readonly List<TabMotion> tabMotions=new List<TabMotion>();
        readonly Stopwatch tabClock=Stopwatch.StartNew();
        const double TabTravel=520, TabResponse=.55;
        LinearGradientBrush tabEdgeMask;
        readonly GradientStop[] tabLeftFade=new GradientStop[11],tabRightFade=new GradientStop[11];
        double tabFeatherWidth=12;
        readonly TranslateTransform pageLabelShift=new TranslateTransform();
        readonly TranslateTransform shelfAddShift=new TranslateTransform();
        Button shelfAddButton;
        double quickReveal, quickVelocity;
        ScaleTransform quickScale; TranslateTransform quickShift;
        Popup durationPopup;
        StackPanel settingsSidebar; Border settingsBody; string settingsSection="Allgemein";
        readonly Dictionary<string,Button> settingsTabs=new Dictionary<string,Button>();
        Image artwork;
        TextBlock artworkPlaceholder;
        MotionSlider mediaSeek, volumeSlider;
        CancellationTokenSource lyricsRequest;
        LyricsData lyricsData;
        int lyricIndex=-2;
        string mediaAlbum="",lyricsKey="",bridgeDirectory;
        string mediaIdentity="";
        readonly Dictionary<string,LyricsData> lyricCache=new Dictionary<string,LyricsData>();
        Canvas navBackdrop;
        Border navIndicator;
        TranslateTransform navSlide;
        List<string> builtNavPages = new List<string>();
        List<string> builtStripPages = new List<string>();
        readonly Dictionary<string,DesignSymbol> navLabels = new Dictionary<string,DesignSymbol>();
        double spanVelocity, depthVelocity, navVelocity, targetSpan=ClosedSpan, targetDepth=ClosedDepth, targetNav;
        bool morphing;
        readonly Stopwatch morphClock = new Stopwatch();
        Grid timerPage, musicPage, shelfPage;
        TextBox minutesBox, secondsBox;
        StackPanel fileList;
        Window settings;
        Forms.NotifyIcon tray;
        DispatcherTimer tick, collapse, hoverWatch, blinkTimer, blinkEnd, mascotTimer, finishedTimer;
        Process media;
        bool isOpen, shuttingDown, timerFinished, preview, isPlaying, editorFocused;
        bool modalOpen;
        bool qaPointerOutside;
        DateTime lastCloseStartedUtc;
        bool uiTesting = Environment.GetCommandLineArgs().Contains("--qa");
        readonly string dataFolder = DataLocation.Resolve(AppDomain.CurrentDomain.BaseDirectory,Environment.GetCommandLineArgs().Contains("--qa"));string settingsStorageNote="";bool settingsReadOnly;int dataSelectionGeneration;
        string page, mediaTitle="", mediaArtist="";
        double mediaDuration, mediaPosition;
        DateTime mediaUpdated;
        TransportSymbol playGlyph;
        DateTime lastMediaOutput;
        int refreshTicks;

        public NotchWindow(bool previewMode) {
            preview=previewMode;
            Title="Iris Notch"; Width=1040; Height=320; WindowStyle=WindowStyle.None;
            AllowsTransparency=true; Background=null; Topmost=true; ShowInTaskbar=previewMode;
            ResizeMode=ResizeMode.NoResize; ShowActivated=false; UseLayoutRounding=true;
            var data=System.IO.Path.Combine(root,dataFolder); Directory.CreateDirectory(data);
            var path=System.IO.Path.Combine(data,"settings.json");firstLaunchDesign=!File.Exists(path)&&!File.Exists(path+".bak");
            prefs=PreferencesStore.Read(path,out settingsStorageNote,out settingsReadOnly);if(firstLaunchDesign)prefs.Monitor=Math.Max(0,Array.FindIndex(Forms.Screen.AllScreens,s=>s.Primary));
            if(prefs==null)prefs=new Preferences();L.Set(prefs.Language);settingsStorageNote=L.T(settingsStorageNote);gamingState=L.T("FPS-Messung aus"); if(prefs.Files==null)prefs.Files=new List<string>();
            if(prefs.GamingGames==null)prefs.GamingGames=new List<string>();prefs.GamingGames=prefs.GamingGames.Where(v=>!string.IsNullOrWhiteSpace(v)).Take(80).ToList();
            MigrateDesign(prefs);prefs.NotchSize=ValidNotchSize(prefs.NotchSize);
            prefs.Pinned=false; // The notch always follows hover; old pinned preferences are retired.
            prefs.Minutes=Math.Max(0,Math.Min(999,prefs.Minutes)); prefs.Seconds=Math.Max(0,Math.Min(59,prefs.Seconds));
            countdown=new Countdown(Math.Max(0,prefs.Remaining));
            if(prefs.TimerRunning) { countdown.Ends=prefs.TimerEndUtc; countdown.Running=true; timerFinished=countdown.Update(DateTime.UtcNow); }
            page = prefs.Page; if(page!="Musik" && page!="Ablage" && page!="Mixer" && page!="Add-ons")page="Timer";
            if(page=="Musik"&&!prefs.Music || page=="Ablage"&&!prefs.Shelf)page="Timer";
            LoadAddons();InstallStyles(); SetupAppMotion();Build(); SetupPlayerAnimations();SetupShelfFeedback();SetupAudio();SetupAppearance();SetupGaming();SetupCursorAvoidance();SetupDesign();SetupDownloads();Place();
            SourceInitialized += delegate {
                var hwnd=new WindowInteropHelper(this).Handle;InstallAddonWindowContext(hwnd);
                if(!preview)SetWindowLong(hwnd,-20,GetWindowLong(hwnd,-20)|0x80);
                Place();
            };
            Loaded += delegate {
                SetupTray(); SetupMedia();RefreshAddonTiles();
                if(preview) { Open(); }
                else mascotTimer.Start();
                if(timerFinished) Finish();
                if(uiTesting){try{if(Environment.GetCommandLineArgs().Contains("--release-ui-qa"))RunReleaseUiQa();else if(Environment.GetCommandLineArgs().Contains("--stability-ui-qa"))RunStabilityUiQa();else if(Environment.GetCommandLineArgs().Contains("--native-download-ui-qa"))RunNativeDownloadUiQa();else if(Environment.GetCommandLineArgs().Contains("--tile-input-qa"))RunTileInputQa();else if(Environment.GetCommandLineArgs().Contains("--navigation-ui-qa"))RunNavigationUiQa();else if(Environment.GetCommandLineArgs().Contains("--discord-ui-qa"))RunDiscordUiQa();else if(Environment.GetCommandLineArgs().Contains("--addon-menu-qa"))RunAddonMenuQa();else if(Environment.GetCommandLineArgs().Contains("--steam-picker-qa"))RunSteamPickerQa();else if(Environment.GetCommandLineArgs().Contains("--download-setup-qa"))RunDownloadSetupQa();else if(Environment.GetCommandLineArgs().Contains("--downloads-demo"))RunDownloadsDemo();else if(Environment.GetCommandLineArgs().Contains("--phase4-ui-qa"))RunPhase4UiQa();else if(Environment.GetCommandLineArgs().Contains("--action-icon-qa"))RunActionIconQaOnly();else if(Environment.GetCommandLineArgs().Contains("--addon-module-qa"))RunAddonModuleQaOnly();else if(Environment.GetCommandLineArgs().Contains("--addon-modules-demo"))RunAddonModulesDemo();else if(Environment.GetCommandLineArgs().Contains("--timer-progress-demo"))RunTimerProgressDemo();else if(Environment.GetCommandLineArgs().Contains("--design-demo"))RunDesignDemo();else if(Environment.GetCommandLineArgs().Contains("--gaming-ui-qa"))RunGamingUiQa();else if(Environment.GetCommandLineArgs().Contains("--gaming-probe"))RunGamingProbe();else if(Environment.GetCommandLineArgs().Contains("--gaming-demo"))RunGamingDemo();else if(Environment.GetCommandLineArgs().Contains("--dwell-demo"))RunDwellDemo();else if(Environment.GetCommandLineArgs().Contains("--appearance-demo"))RunAppearanceDemo();else if(Environment.GetCommandLineArgs().Contains("--compact-timer-demo"))RunCompactTimerDemo();else if(Environment.GetCommandLineArgs().Contains("--quick-settings-demo"))RunQuickSettingsDemo();else if(Environment.GetCommandLineArgs().Contains("--addon-demo"))RunAddonDemo();else if(Environment.GetCommandLineArgs().Contains("--iris-demo"))RunIrisDemo();else if(Environment.GetCommandLineArgs().Contains("--mixer-scroll-demo"))RunMixerScrollDemo();else if(Environment.GetCommandLineArgs().Contains("--audio-probe"))RunAudioProbe();else if(Environment.GetCommandLineArgs().Contains("--feature-demo"))RunFeatureDemo();else if(Environment.GetCommandLineArgs().Contains("--app-motion-demo"))RunAppMotionDemo();else if(Environment.GetCommandLineArgs().Contains("--finish-demo"))RunFinishDemo();else if(Environment.GetCommandLineArgs().Contains("--press-demo"))RunPressDemo();else if(Environment.GetCommandLineArgs().Contains("--player-demo"))RunPlayerDemo();else if(Environment.GetCommandLineArgs().Contains("--tab-demo"))RunTabDemo();else if(Environment.GetCommandLineArgs().Contains("--motion-demo"))RunMotionDemo();else RunQa();}catch(Exception ex){Environment.ExitCode=1;File.WriteAllText(System.IO.Path.Combine(root,"ui-test.txt"),"FAILED: "+ex);Close();}}
                else if(firstLaunchDesign&&!preview)ShowDesignSettings();else if(Environment.GetCommandLineArgs().Contains("--open-settings"))ShowSettings();
            };
            MouseEnter += delegate { if(!uiTesting&&!cursorAvoiding&&PointerInsideNotch()){collapse.Stop();Open();} };
            MouseLeave += delegate { if(!uiTesting&&!prefs.Pinned&&!modalOpen)collapse.Start(); };
            MouseWheel += delegate(object s,MouseWheelEventArgs e) { if(!isOpen||(lyricsVisible&&lyricsPanel.IsMouseOver)||(page=="Mixer"&&mixerScroll.IsMouseOver))return; Cycle(e.Delta<0?1:-1); e.Handled=true; };
            KeyDown += delegate(object s,KeyEventArgs e) {
                if(mediaSeek!=null&&mediaSeek.IsKeyboardFocusWithin||volumeSlider!=null&&volumeSlider.IsKeyboardFocusWithin||page=="Mixer"&&mixerScroll.IsKeyboardFocusWithin)return;
                if(e.Key==Key.Escape) { Keyboard.ClearFocus(); editorFocused=false; CloseNotch(true); e.Handled=true; }
                else if(!editorFocused && e.Key==Key.Right) { Cycle(1); e.Handled=true; }
                else if(!editorFocused && e.Key==Key.Left) { Cycle(-1); e.Handled=true; }
            };
            AllowDrop=true;
            DragEnter += HandleFileDrag;DragOver += HandleFileDrag;DragLeave += LeaveFileDrag;
            Drop += OnDrop;
            collapse=new DispatcherTimer(DispatcherPriority.Input) { Interval=TimeSpan.FromMilliseconds(60) };
            collapse.Tick+=delegate { collapse.Stop(); if(!PointerInsideNotch() && !prefs.Pinned && !modalOpen && !timerFinished&&!addonTileDragging&&!PlayerDragging())CloseNotch(false); };
            hoverWatch=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(25) };
            hoverWatch.Tick+=delegate { if(PointerInsideNotch())collapse.Stop();else if(!collapse.IsEnabled&&!prefs.Pinned&&!modalOpen&&!timerFinished&&!PlayerDragging())collapse.Start(); };
            tick=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
            tick.Tick+=OnTick; tick.Start();
            blinkTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4.7)};
            blinkTimer.Tick+=delegate { Blink(); }; blinkTimer.Start();
            blinkEnd=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(160)};
            blinkEnd.Tick+=delegate { blinkEnd.Stop(); EyeScale(1,0.14); };
            mascotTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(7)};
            mascotTimer.Tick+=delegate { mascotTimer.Stop(); if(!isOpen && !countdown.Running && prefs.Mascot)ShowEyes(true); };
            finishedTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(12)};
            finishedTimer.Tick+=delegate { DismissTimerNotice(true); };
            Closed+=OnClosed;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=OnDisplayChanged;
        }

        void InstallStyles() {
            string x=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
              <Style TargetType='Button'><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Foreground' Value='#EDF0F6'/><Setter Property='Background' Value='#1B1E25'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='12,6'/><Setter Property='FontFamily' Value='Segoe UI'/><Setter Property='FontSize' Value='12'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='12' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='.85'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='.65'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='.35'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
              <Style TargetType='TextBox'><Setter Property='Foreground' Value='#F4F6FA'/><Setter Property='Background' Value='#191C23'/><Setter Property='BorderBrush' Value='#343A45'/><Setter Property='CaretBrush' Value='#9DF4D0'/><Setter Property='Padding' Value='8,4'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='FontFamily' Value='Segoe UI'/></Style>
              <Style TargetType='CheckBox'><Setter Property='Foreground' Value='#E8ECF3'/><Setter Property='Margin' Value='0,10,0,10'/><Setter Property='FontSize' Value='14'/></Style>
              <Style TargetType='ToolTip'><Setter Property='Background' Value='#202630'/><Setter Property='Foreground' Value='#F3F5FA'/><Setter Property='BorderBrush' Value='#3A4350'/></Style>
              <Style TargetType='ProgressBar'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ProgressBar'><Grid x:Name='PART_Track' ClipToBounds='True'><Border Background='{TemplateBinding Background}' CornerRadius='2'/><Border x:Name='PART_Indicator' HorizontalAlignment='Left' Background='{TemplateBinding Foreground}' CornerRadius='2'/></Grid></ControlTemplate></Setter.Value></Setter></Style>
              <Style x:Key='MediaSeek' TargetType='Slider'><Setter Property='Height' Value='18'/><Setter Property='IsMoveToPointEnabled' Value='True'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Border Background='Transparent'><Grid><Border Height='3' CornerRadius='2' Background='#444444'/><Track x:Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}'><RepeatButton.Template><ControlTemplate><Border Height='3' CornerRadius='2' Background='White'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb x:Name='SeekThumb' Width='0' Height='7' Opacity='0'><Thumb.Template><ControlTemplate><Ellipse Fill='White'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}'><RepeatButton.Template><ControlTemplate><Border Height='3' CornerRadius='2' Background='#444444'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='SeekThumb' Property='Opacity' Value='1'/><Setter TargetName='SeekThumb' Property='Width' Value='7'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
            </ResourceDictionary>";
            Resources=(ResourceDictionary)XamlReader.Parse(x);
        }
        TextBlock Text(string value,double size,Brush color) { return new TextBlock {Text=value,FontSize=size,Foreground=color,FontFamily=new FontFamily("Segoe UI"),VerticalAlignment=VerticalAlignment.Center}; }
        TextBlock Glyph(string value,double size,Brush color) { var t=Text(value,size,color); t.FontFamily=new FontFamily("Segoe MDL2 Assets");return t; }
        Button Btn(object label,Action action) { var b=new Button {Content=label};b.Click+=delegate {action();};AttachButtonMotion(b);return b; }
        new Button Icon(string glyph,string tip,Action action) { var b=Btn(Glyph(glyph,15,ink),action);b.Width=32;b.Height=30;b.Padding=new Thickness(0);b.ToolTip=tip;b.Background=Brushes.Transparent;return b; }
        Border Card(UIElement child) { return new Border {Background=Brushes.Black,CornerRadius=new CornerRadius(16),Padding=new Thickness(14),Child=child}; }
        void Build() {
            canvas=new Canvas {Width=SceneWidth,Height=SceneHeight,Background=null,ClipToBounds=false,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top};sceneHost=new Grid{Background=null};sceneHost.Children.Add(canvas);Content=sceneHost;
            surface=new NotchSurface {Width=SceneWidth,Height=SceneHeight,Span=ClosedSpan,Depth=ClosedDepth};
            canvas.Children.Add(surface);
            compact=new Grid {Width=154,Height=34};Canvas.SetLeft(compact,(SceneWidth-154)/2);canvas.Children.Add(compact);
            compact.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});compact.ColumnDefinitions.Add(new ColumnDefinition());compact.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});
            compactText=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetColumn(compactText,1);compact.Children.Add(compactText);
            clock=Text(DateTime.Now.ToString("HH:mm"),18,ink);clock.FontWeight=FontWeights.Bold;compactText.Children.Add(clock);
            compactTimer=Text("",18,Brushes.White);compactTimer.FontWeight=FontWeights.Bold;compactText.Children.Add(compactTimer);
            eyes=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Opacity=0,Visibility=Visibility.Collapsed};Grid.SetColumn(eyes,1);compact.Children.Add(eyes);
            eyeLeft=Eye();eyeRight=Eye();eyes.Children.Add(eyeLeft);eyes.Children.Add(eyeRight);
            expanded=new Border {Width=438,Height=184,Opacity=0,Visibility=Visibility.Collapsed,RenderTransform=new TranslateTransform(0,-5)};Canvas.SetLeft(expanded,(SceneWidth-438)/2);Canvas.SetTop(expanded,3);canvas.Children.Add(expanded);
            var grid=new Grid();expanded.Child=grid;
            grid.RowDefinitions.Add(new RowDefinition {Height=new GridLength(28)});grid.RowDefinitions.Add(new RowDefinition {Height=new GridLength(94)});navigationRow=new RowDefinition {Height=new GridLength(62)};grid.RowDefinitions.Add(navigationRow);
            pageHost=new Border {Width=SceneWidth,Height=SceneHeight,ClipToBounds=false};canvas.Children.Insert(canvas.Children.IndexOf(expanded),pageHost);
            pageHost.SetBinding(OpacityProperty,new Binding("Opacity"){Source=expanded});pageHost.SetBinding(VisibilityProperty,new Binding("Visibility"){Source=expanded});pageHost.SetBinding(IsHitTestVisibleProperty,new Binding("IsHitTestVisible"){Source=expanded});
            var viewportCanvas=new Canvas {Width=SceneWidth,Height=SceneHeight};pageHost.Child=viewportCanvas;
            pageStrip=new Canvas {Height=148,Width=438,RenderTransform=expanded.RenderTransform};Canvas.SetLeft(pageStrip,Canvas.GetLeft(expanded));Canvas.SetTop(pageStrip,Canvas.GetTop(expanded));viewportCanvas.Children.Add(pageStrip);
            tabEdgeMask=new LinearGradientBrush {MappingMode=BrushMappingMode.Absolute,StartPoint=new Point(0,0),EndPoint=new Point(SceneWidth,0)};
            tabEdgeMask.GradientStops.Add(new GradientStop(Colors.Transparent,0));
            for(int i=0;i<=10;i++){
                double t=i/10.0,alpha=t*t*t*(t*(6*t-15)+10);var color=Color.FromArgb((byte)Math.Round(alpha*255),255,255,255);
                tabLeftFade[i]=new GradientStop(color,0);tabRightFade[i]=new GradientStop(color,1);
                tabEdgeMask.GradientStops.Add(tabLeftFade[i]);tabEdgeMask.GradientStops.Add(tabRightFade[i]);
            }
            tabEdgeMask.GradientStops.Add(new GradientStop(Colors.Transparent,1));pageHost.OpacityMask=tabEdgeMask;
            timerPage=BuildTimer();musicPage=BuildMusic();shelfPage=BuildShelf();mixerPage=BuildMixer();addonPage=BuildAddons();
            var header=new Grid {Height=28,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};headerChrome=header;Panel.SetZIndex(header,5);grid.Children.Add(header);
            pageLabel=Text("Timer",17,ink);pageLabel.FontWeight=FontWeights.SemiBold;pageLabel.HorizontalAlignment=HorizontalAlignment.Left;pageLabel.RenderTransform=pageLabelShift;header.Children.Add(pageLabel);
            var headerClock=new Grid {Width=256,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(133,0,0,0),Background=materialContentMask};header.Children.Add(headerClock);
            musicDate=Text("",14,ink);musicDate.HorizontalAlignment=HorizontalAlignment.Left;musicDate.Margin=new Thickness(32,0,0,0);headerClock.Children.Add(musicDate);UpdateMusicDate();date=musicDate;
            navigation=new Grid {Background=null,VerticalAlignment=VerticalAlignment.Top,Height=14,Margin=new Thickness(0,17,0,0)};Grid.SetRow(navigation,2);Panel.SetZIndex(navigation,10);grid.Children.Add(navigation);
            navigationTrigger=new Border {Width=72,Height=8,Background=Brushes.Transparent,VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,0)};navigation.Children.Add(navigationTrigger);
            pageDots=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,6,0,0)};navigation.Children.Add(pageDots);
            quickScale=new ScaleTransform(.4,.15);quickShift=new TranslateTransform(0,-8);var quickTransform=new TransformGroup();quickTransform.Children.Add(quickScale);quickTransform.Children.Add(quickShift);
            quickBar=new Border {Background=new SolidColorBrush(Color.FromRgb(22,22,23)),CornerRadius=new CornerRadius(16),Height=32,Padding=new Thickness(4,0,4,0),Margin=new Thickness(0,31,0,0),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Opacity=0,Visibility=Visibility.Collapsed,RenderTransformOrigin=new Point(.5,0),RenderTransform=quickTransform};
            var quickContent=new Grid();quickBar.Child=quickContent;navigation.Children.Add(quickBar);
            navBackdrop=new Canvas {Height=28,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};quickContent.Children.Add(navBackdrop);
            navSlide=new TranslateTransform();
            navIndicator=new Border {Width=28,Height=26,Margin=new Thickness(0,1,0,0),CornerRadius=new CornerRadius(13),Background=new SolidColorBrush(Color.FromRgb(48,48,50)),RenderTransform=navSlide};navBackdrop.Children.Add(navIndicator);
            nav=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};quickContent.Children.Add(nav);
            navigation.MouseEnter+=delegate{if(!uiTesting)SetQuickBar(true);};navigation.MouseLeave+=delegate{if(!uiTesting)SetQuickBar(false);};
            lyricsPanel=new Border {Width=300,Height=148,Background=materialContentMask,Opacity=0,Visibility=Visibility.Collapsed,Padding=new Thickness(12,4,12,4)};Canvas.SetLeft(lyricsPanel,SceneWidth/2+72);Canvas.SetTop(lyricsPanel,5);canvas.Children.Add(lyricsPanel);
            var lyricGrid=new Grid();lyricsPanel.Child=lyricGrid;lyricsStatus=Text(L.T("Liedtext wird geladen …"),13,muted);lyricsStatus.TextWrapping=TextWrapping.Wrap;lyricsStatus.HorizontalAlignment=HorizontalAlignment.Center;lyricsStatus.TextAlignment=TextAlignment.Center;lyricsStatus.Cursor=Cursors.Hand;lyricsStatus.ToolTip=L.T("Liedtext erneut suchen");lyricsStatus.MouseLeftButtonUp+=delegate{LoadLyrics();};lyricGrid.Children.Add(lyricsStatus);
            lyricsLines=new StackPanel();lyricsScroll=new AnimatedLyricsScroll {Content=lyricsLines,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};lyricGrid.Children.Add(lyricsScroll);
            previousPageButton=ArrowButton(false,delegate{Cycle(-1);});nextPageButton=ArrowButton(true,delegate{Cycle(1);});
            foreach(var arrow in new Button[]{previousPageButton,nextPageButton}){arrow.Visibility=Visibility.Collapsed;canvas.Children.Add(arrow);}
            SwitchPage(page,false);
            RefreshTimer();
        }
        Border Eye() { return new Border {Width=7,Height=12,CornerRadius=new CornerRadius(4),Background=Brushes.White,Margin=new Thickness(5,0,5,0),RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(1,1)}; }
        Grid BuildTimer() {
            var g=new Grid {Width=438,Height=148};var layout=new Canvas();g.Children.Add(layout);
            var ring=new Grid {Width=82,Height=82};Canvas.SetLeft(ring,53);Canvas.SetTop(ring,40);layout.Children.Add(ring);
            timerProgress=new TimerProgressRing();ring.Children.Add(timerProgress);
            var timerIcon=new DesignSymbol("timer",Brushes.White){Width=48,Height=48,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};ring.Children.Add(timerIcon);
            timerText=Text("05:00",34,Brushes.White);timerText.FontWeight=FontWeights.Bold;timerText.TextAlignment=TextAlignment.Center;timerText.Width=178;var timeButton=Btn(timerText,delegate{OpenDurationEditor(timerText);});timeButton.Width=178;timeButton.Height=46;timeButton.Padding=new Thickness(0);timeButton.Background=Brushes.Transparent;timeButton.VerticalContentAlignment=VerticalAlignment.Top;timeButton.ToolTip=L.T("Timerdauer einstellen");Canvas.SetLeft(timeButton,182);Canvas.SetTop(timeButton,34);layout.Children.Add(timeButton);
            timerText.Cursor=Cursors.Hand;timerText.ToolTip=L.T("Timerdauer einstellen");
            timerHint=Text("Timer",12,muted);timerHint.TextAlignment=TextAlignment.Center;timerHint.TextTrimming=TextTrimming.CharacterEllipsis;timerHint.Width=206;Canvas.SetLeft(timerHint,179);Canvas.SetTop(timerHint,78);layout.Children.Add(timerHint);
            var reset=SymbolButton("reset",L.T("Zurücksetzen"),delegate{countdown.Reset(ConfiguredSeconds());timerFinished=false;finishedTimer.Stop();Save();RefreshTimer();});reset.Width=27;reset.Height=27;reset.Background=new SolidColorBrush(Color.FromRgb(26,26,27));Canvas.SetLeft(reset,358);Canvas.SetTop(reset,39);layout.Children.Add(reset);
            var duration=Btn(L.T("Dauer"),delegate{OpenDurationEditor(null);});durationAnchor=duration;duration.Width=82;duration.Height=29;duration.Padding=new Thickness(0);duration.Background=new SolidColorBrush(Color.FromRgb(24,24,25));Canvas.SetLeft(duration,205);Canvas.SetTop(duration,105);layout.Children.Add(duration);
            timerPlayGlyph=new TransportSymbol("play"){Width=18,Height=16};timerGlyphMotion=new UiGlyphMotion(timerPlayGlyph);uiMotion.Register(timerGlyphMotion);startButton=Btn(timerPlayGlyph,ToggleTimer);ExcludeButtonMotion(startButton);startButton.Width=62;startButton.Height=30;startButton.Padding=new Thickness(0);startButton.Background=new SolidColorBrush(Color.FromRgb(246,53,84));startButton.Foreground=Brushes.White;startButton.FontSize=15;Canvas.SetLeft(startButton,297);Canvas.SetTop(startButton,104);layout.Children.Add(startButton);

            minutesBox=DurationBox(prefs.Minutes.ToString("00"),L.T("Minuten (0–999)"),58);secondsBox=DurationBox(prefs.Seconds.ToString("00"),L.T("Sekunden (0–59)"),50);
            var editor=new StackPanel();var title=Text(L.T("Timerdauer"),14,ink);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,12);editor.Children.Add(title);
            var row=new StackPanel {Orientation=Orientation.Horizontal};row.Children.Add(minutesBox);var min=Text("min",12,muted);min.Margin=new Thickness(6,0,12,0);row.Children.Add(min);row.Children.Add(secondsBox);var sec=Text("s",12,muted);sec.Margin=new Thickness(6,0,0,0);row.Children.Add(sec);editor.Children.Add(row);
            var apply=Btn(L.T("Übernehmen"),delegate{if(ApplyDuration(true))CloseDurationEditor();});apply.Margin=new Thickness(0,12,0,0);editor.Children.Add(apply);
            durationPopup=new Popup {PlacementTarget=duration,Placement=PlacementMode.Bottom,AllowsTransparency=true,StaysOpen=false,Child=new Border {Background=new SolidColorBrush(Color.FromRgb(30,30,32)),CornerRadius=new CornerRadius(14),Padding=new Thickness(16),Child=editor}};
            durationPopup.Opened+=delegate{modalOpen=true;OpenDurationMotion();};durationPopup.Closed+=delegate{if(durationMotion!=null){durationMotion.Dispose();durationMotion=null;}modalOpen=false;Keyboard.ClearFocus();editorFocused=false;if(!PointerInsideNotch())collapse.Start();};
            BuildTimerNotice(g,layout);
            return g;
        }

        TextBox DurationBox(string value,string tip,double width) {
            var b=new TextBox {Text=value,Width=width,Height=27,FontSize=14,FontWeight=FontWeights.SemiBold,ToolTip=tip,TextAlignment=TextAlignment.Center,MaxLength=3};
            b.PreviewTextInput+=delegate(object s,TextCompositionEventArgs e){e.Handled=e.Text.Any(ch=>!char.IsDigit(ch));};
            b.GotKeyboardFocus+=delegate { editorFocused=true;collapse.Stop();b.SelectAll(); };
            b.LostKeyboardFocus+=delegate {editorFocused=false;ApplyDuration(false);if(!IsMouseOver&&!prefs.Pinned)collapse.Start();};
            b.KeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Enter){ApplyDuration(true);Keyboard.ClearFocus();editorFocused=false;e.Handled=true;}};
            new UiFocusMotion(b,uiMotion);return b;
        }
        double ConfiguredSeconds() {return prefs.Minutes*60+prefs.Seconds;}
        bool ApplyDuration(bool feedback) {
            int m,s;
            if(!int.TryParse(minutesBox.Text,out m)||!int.TryParse(secondsBox.Text,out s)||m<0||m>999||s<0||s>59||m*60+s==0) {
                timerHint.Text=L.T("Bitte 0–999 min und 0–59 s eingeben (mindestens 1 s).");timerHint.Foreground=new SolidColorBrush(Color.FromRgb(255,174,158));return false;
            }
            bool changed=prefs.Minutes!=m||prefs.Seconds!=s;
            prefs.Minutes=m;prefs.Seconds=s;
            if(changed) {countdown.Reset(ConfiguredSeconds());timerFinished=false;finishedTimer.Stop();}
            if(feedback||changed){timerHint.Text="Timer";timerHint.Foreground=muted;}
            Save();RefreshTimer();return true;
        }
        void ToggleTimer() {
            if(countdown.Running) {countdown.Pause(DateTime.UtcNow);}
            else {if(!ApplyDuration(false))return;if(countdown.Remaining<=0)countdown.Reset(ConfiguredSeconds());timerFinished=false;finishedTimer.Stop();countdown.Start(DateTime.UtcNow);}
            ShowEyes(false);Save();RefreshTimer();
        }
        static string TimeText(double seconds) {
            var v=TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0,seconds)));
            return v.TotalHours>=1 ? ((int)v.TotalHours).ToString("00")+":"+v.Minutes.ToString("00")+":"+v.Seconds.ToString("00") : ((int)v.TotalMinutes).ToString("00")+":"+v.Seconds.ToString("00");
        }
        void RefreshTimer() {
            if(timerText==null)return;
            UpdateTimerText(TimeText(countdown.Remaining));UpdateTimerGlyph();startButton.ToolTip=countdown.Running?L.T("Pausieren"):L.T("Timer starten / fortsetzen");
            compactTimer.Text=countdown.Running?TimeText(countdown.Remaining):"";
            clock.Visibility=countdown.Running?Visibility.Collapsed:Visibility.Visible;
            if(countdown.Running)timerHint.Text=L.T("Timer läuft");
            else if(timerFinished)timerHint.Text=L.T("Zeit ist um");
            else if(countdown.Remaining<ConfiguredSeconds()&&countdown.Remaining>0)timerHint.Text=L.T("Pausiert");else if(timerHint.Foreground==muted)timerHint.Text="Timer";
            minutesBox.IsEnabled=!countdown.Running;secondsBox.IsEnabled=!countdown.Running;
            SetTimerNotice(timerFinished);SyncTimerProgress();UpdateCompactLyrics();
        }
        void Finish() {
            timerFinished=true;if(durationPopup!=null)durationPopup.IsOpen=false;SwitchPage("Timer",false);SetQuickBar(false);Open();RefreshTimer();
            if(!uiTesting){System.Media.SystemSounds.Asterisk.Play();if(tray!=null)tray.ShowBalloonTip(6000,"Iris · Timer",L.T("Deine eingestellte Zeit ist abgelaufen."),Forms.ToolTipIcon.Info);}
            finishedTimer.Stop();finishedTimer.Start();Save();
        }

        Grid BuildMusic() {
            var g=new Grid {Width=438,Height=148,VerticalAlignment=VerticalAlignment.Top};g.RowDefinitions.Add(new RowDefinition {Height=new GridLength(90)});g.RowDefinitions.Add(new RowDefinition {Height=new GridLength(24)});g.RowDefinitions.Add(new RowDefinition {Height=new GridLength(30)});
            var top=new Grid {Margin=new Thickness(30,0,30,0)};top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(89)});top.ColumnDefinitions.Add(new ColumnDefinition());g.Children.Add(top);
            var art=new Grid{Width=70,Height=70,Margin=new Thickness(0,11,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Clip=new RectangleGeometry(new Rect(0,0,70,70),10,10)};
            artRoot=art;art.Margin=new Thickness(0);artRoot.RenderTransformOrigin=new Point(.5,.5);var artTransform=new TransformGroup();artTransform.Children.Add(artScale);artTransform.Children.Add(artTilt);artRoot.RenderTransform=artTransform;
            artwork=new Image {Stretch=Stretch.UniformToFill};art.Children.Add(artwork);artworkPlaceholder=Glyph("\uE8D6",28,mint);artworkPlaceholder.HorizontalAlignment=HorizontalAlignment.Center;art.Children.Add(artworkPlaceholder);artworkButton=Btn(art,OpenMediaSource);artworkButton.Width=70;artworkButton.Height=70;artworkButton.Padding=new Thickness(0);artworkButton.Margin=new Thickness(0,11,0,0);artworkButton.HorizontalAlignment=HorizontalAlignment.Left;artworkButton.VerticalAlignment=VerticalAlignment.Top;artworkButton.Background=Brushes.Transparent;artworkButton.ToolTip=L.T("Musikplayer öffnen");artworkButton.IsEnabled=false;top.Children.Add(artworkButton);
            var info=new StackPanel {VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,39,0,0)};Grid.SetColumn(info,1);top.Children.Add(info);
            trackTitle=Text(L.T("Noch keine Wiedergabe"),17,ink);trackTitle.FontWeight=FontWeights.SemiBold;trackTitle.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(trackTitle);
            trackArtist=Text(L.T("Starte Musik oder ein Video in deinem Player."),13,muted);trackArtist.TextTrimming=TextTrimming.CharacterEllipsis;info.Children.Add(trackArtist);
            var timeline=new Grid {Margin=new Thickness(30,0,30,0)};timeline.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(60)});timeline.ColumnDefinitions.Add(new ColumnDefinition());timeline.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(66)});Grid.SetRow(timeline,1);g.Children.Add(timeline);
            trackPosition=Text("0:00",18,ink);timeline.Children.Add(trackPosition);
            mediaSeek=new MotionSlider {Minimum=0,Maximum=1,Value=0,Cursor=Cursors.Hand,IsEnabled=false};Grid.SetColumn(mediaSeek,1);timeline.Children.Add(mediaSeek);
            mediaSeek.PreviewMouseLeftButtonDown+=delegate{mediaSeeking=true;};mediaSeek.PreviewMouseLeftButtonUp+=delegate{CommitSeek();};mediaSeek.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler(delegate{CommitSeek();}));
            mediaSeek.PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Left||e.Key==Key.Right||e.Key==Key.Home||e.Key==Key.End)mediaSeeking=true;};
            mediaSeek.KeyUp+=delegate(object s,KeyEventArgs e){if(mediaAvailable&&(e.Key==Key.Left||e.Key==Key.Right||e.Key==Key.Home||e.Key==Key.End)){mediaSeeking=false;Seek(mediaSeek.Value*mediaDuration);}};
            trackRemaining=Text("−0:00",18,ink);trackRemaining.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(trackRemaining,2);timeline.Children.Add(trackRemaining);
            var controls=new Grid {Margin=new Thickness(30,0,30,0)};controls.ColumnDefinitions.Add(new ColumnDefinition());controls.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});controls.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(controls,2);g.Children.Add(controls);
            var lyrics=SymbolButton("lyrics",L.T("Liedtext ein-/ausblenden"),ToggleLyrics);lyrics.HorizontalAlignment=HorizontalAlignment.Left;lyrics.Margin=new Thickness(16,0,0,0);controls.Children.Add(lyrics);AnimateMediaButton(lyrics);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(buttons,1);controls.Children.Add(buttons);
            buttons.Children.Add(TransportButton(new TransportSymbol("previous"),L.T("Vorheriger Titel"),delegate{MediaAction("previous");}));
            playGlyph=new TransportSymbol("play");buttons.Children.Add(TransportButton(playGlyph,L.T("Wiedergabe / Pause"),delegate{MediaAction("toggle");}));
            buttons.Children.Add(TransportButton(new TransportSymbol("next"),L.T("Nächster Titel"),delegate{MediaAction("next");}));
            BuildInlineVolume(controls);
            return g;
        }
        void OpenVolume(){RefreshPlayerVolume();if(uiTesting&&volumeSlider.Value==0)volumeSlider.Value=.72;volumeOpen=true;volumeLeave=DateTime.MinValue;}

        Button TransportButton(TransportSymbol symbol,string name,Action action){var b=Btn(symbol,action);b.Width=52;b.Height=30;b.Padding=new Thickness(0);b.Background=Brushes.Transparent;b.ToolTip=name;System.Windows.Automation.AutomationProperties.SetName(b,name);AnimateMediaButton(b);return b;}
        void CommitSeek(){if(!mediaSeeking)return;mediaSeeking=false;if(mediaAvailable&&mediaDuration>0)Seek(mediaSeek.Value*mediaDuration);}
        void Seek(double seconds){RequestSeek(seconds);}
        void MediaAction(string action){
            if(action=="next")skipDirection=1;else if(action=="previous")skipDirection=-1;
            if(mediaAvailable){
                if(action=="toggle")BeginImmediatePlaybackToggle();
                try{string id=SendMediaCommand(action,0);if(action=="toggle")playToggleCommand=id;}
                catch{if(action=="toggle"){playTogglePending=false;ApplyPlaybackGlyph();}musicDate.ToolTip=L.T("Der Medienauftrag konnte nicht gesendet werden.");}
            }else if(!uiTesting)MediaKey(action=="next"?(byte)0xB0:action=="previous"?(byte)0xB1:(byte)0xB3);
        }
        string SendMediaCommand(string action,double seconds){
            if(bridgeDirectory==null){bridgeDirectory=System.IO.Path.Combine(root,dataFolder,"media-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(bridgeDirectory);}
            string id=DateTime.UtcNow.Ticks.ToString("D19")+Guid.NewGuid().ToString("N"),file=System.IO.Path.Combine(bridgeDirectory,id+".json");
            var data=new Dictionary<string,object>{{"id",id},{"action",action},{"seconds",seconds}};
            File.WriteAllText(file+".tmp",serializer.Serialize(data),Encoding.UTF8);File.Move(file+".tmp",file);return id;
        }
        void SetupMedia() {
            if(!prefs.Music||uiTesting)return;
            try {
                bridgeDirectory=System.IO.Path.Combine(root,dataFolder,"media-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(bridgeDirectory);
                var script=File.ReadAllText(System.IO.Path.Combine(root,"MediaBridge.ps1")).Replace("#COMMAND_DIRECTORY#",bridgeDirectory.Replace("'","''"));
                var psi=new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe"),"-NoLogo -NoProfile -NonInteractive -Command -") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
                media=new Process {StartInfo=psi,EnableRaisingEvents=true};
                media.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e){if(e.Data==null)return;var line=e.Data;Dispatcher.BeginInvoke(new Action(delegate{ReadMedia(line);}));};
                media.ErrorDataReceived+=delegate{};
                media.Start();media.BeginOutputReadLine();media.BeginErrorReadLine();media.StandardInput.WriteLine(script);media.StandardInput.WriteLine();media.StandardInput.Close();
            } catch {trackArtist.Text=L.T("Medientasten sind verfügbar; Titelabruf nicht erreichbar.");}
        }
        void ReadMedia(string line) {
            if(shuttingDown)return;
            try {
                var d=serializer.Deserialize<Dictionary<string,object>>(line);if(!d.ContainsKey("available")){ReadPlaybackCommandResult(d);ReadSeekCommandResult(d);if(d.ContainsKey("kind")&&Convert.ToString(d["kind"])=="command"&&d.ContainsKey("success")&&!(bool)d["success"])musicDate.ToolTip=L.T("Der Player unterstützt diese Aktion gerade nicht.");return;}
                lastMediaOutput=DateTime.UtcNow;
                if(!(bool)d["available"]) { playTogglePending=false;seekPending=false;mediaSource="";RefreshPlayerVolume();artworkButton.IsEnabled=false;mediaAvailable=false;mediaIdentity="";mediaTitle="";isPlaying=false;trackTitle.Text=L.T("Noch keine Wiedergabe");trackArtist.Text=d.ContainsKey("error")?L.T("Windows-Mediensitzung nicht verfügbar"):L.T("Starte Musik oder ein Video in deinem Player.");mediaSeek.Value=0;mediaSeek.IsEnabled=false;trackPosition.Text="0:00";trackRemaining.Text="−0:00";playGlyph.AnimateChanges=MotionEnabled();playGlyph.Mode="play";ChangeArtwork(null,false);UpdateCompactLyrics();if(lyricsRequest!=null)lyricsRequest.Cancel();if(lyricsStatus!=null){lyricsLines.Children.Clear();lyricsStatus.Text=L.T("Starte zuerst einen Musiktitel.");lyricsStatus.Visibility=Visibility.Visible;}return; }
                mediaAvailable=true;string oldIdentity=mediaIdentity,oldArtIdentity=mediaTitle+"|"+mediaArtist+"|"+mediaAlbum;
                mediaTitle=Convert.ToString(d["title"]);mediaSource=d.ContainsKey("source")?Convert.ToString(d["source"]):"";RefreshPlayerVolume();
                var metadata=MediaMetadata.Clean(Convert.ToString(d["artist"]),d.ContainsKey("album")?Convert.ToString(d["album"]):"",d.ContainsKey("albumArtist")?Convert.ToString(d["albumArtist"]):"",mediaSource);mediaArtist=metadata[0];mediaAlbum=metadata[1];mediaAlbumArtist=metadata[2];isPlaying=(bool)d["playing"];
                artworkButton.IsEnabled=!string.IsNullOrWhiteSpace(mediaSource);artworkButton.ToolTip=MediaSourceLauncher.DisplayName(mediaSource)+L.T(" öffnen");System.Windows.Automation.AutomationProperties.SetName(artworkButton,Convert.ToString(artworkButton.ToolTip));
                mediaTimelineStart=d.ContainsKey("start")?Convert.ToDouble(d["start"],CultureInfo.InvariantCulture):0;
                mediaPosition=Convert.ToDouble(d["position"],CultureInfo.InvariantCulture);mediaDuration=Convert.ToDouble(d["duration"],CultureInfo.InvariantCulture);mediaUpdated=DateTime.UtcNow;
                DateTime updated;if(d.ContainsKey("updated")&&DateTime.TryParse(Convert.ToString(d["updated"]),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out updated)&&updated.Year>2020)mediaUpdated=updated.ToUniversalTime();
                trackTitle.Text=string.IsNullOrWhiteSpace(mediaTitle)?L.T("Wiedergabe"):mediaTitle;
                trackArtist.Text=string.IsNullOrWhiteSpace(mediaArtist)?Convert.ToString(d["source"]):mediaArtist;
                ConfirmPlaybackSnapshot();if(isPlaying)ShowEyes(false);
                RefreshSeekCapability(d);ConfirmSeekSnapshot();
                mediaIdentity=CurrentLyricsKey();
                bool newArtworkIdentity=oldArtIdentity!=mediaTitle+"|"+mediaArtist+"|"+mediaAlbum;
                if(newArtworkIdentity)AnimateTrackText();
                if(newArtworkIdentity||(d.ContainsKey("artChanged")&&Convert.ToBoolean(d["artChanged"])))SetMediaArtwork(d,newArtworkIdentity);
                if(oldIdentity!=mediaIdentity){lyricsData=null;lyricIndex=-2;lyricsKey="";if(lyricsVisible)LoadLyrics();}
                RefreshMediaProgress();
            } catch {}
        }
        void RefreshMediaProgress() {
            if(mediaTitle.Length==0)return;
            double pos=mediaSeeking&&mediaDuration>0?mediaSeek.Value*mediaDuration:CurrentMediaPosition();
            if(!mediaSeeking&&!mediaSeek.Dragging)mediaSeek.Value=mediaDuration>0?Math.Min(1,Math.Max(0,pos/mediaDuration)):0;
            trackPosition.Text=TimeText(Math.Floor(mediaDuration>0?Math.Min(pos,mediaDuration):pos));trackRemaining.Text="−"+TimeText(Math.Max(0,mediaDuration-pos));
            UpdateLyricsPosition(pos);
        }
        double CurrentMediaPosition(){return seekPending?DisplayMediaPosition():Math.Max(0,mediaPosition+(isPlaying?Math.Max(0,(DateTime.UtcNow-mediaUpdated).TotalSeconds):0));}
        void ToggleLyrics(){
            lyricsVisible=!lyricsVisible;
            if(lyricsVisible){if(page!="Musik")SwitchPage("Musik",false);lyricsPanel.Visibility=Visibility.Visible;LoadLyrics();}
            else{if(lyricsRequest!=null)lyricsRequest.Cancel();lyricsPanel.Visibility=Visibility.Collapsed;lyricsPanel.IsHitTestVisible=false;}
            LayoutExpanded();AnimateSurface(isOpen?(lyricsVisible?OpenSpan+344:OpenSpan):RestingSpan(),isOpen?ExpandedDepth():RestingDepth(),true);
        }
        async void LoadLyrics(){
            if(lyricsStatus==null)return;
            if(lyricsRequest!=null)lyricsRequest.Cancel();lyricsRequest=new CancellationTokenSource();var token=lyricsRequest.Token;
            string key=CurrentLyricsKey();lyricsKey=key;lyricIndex=-2;lyricsData=null;lyricsLines.Children.Clear();lyricsStatus.Visibility=Visibility.Visible;
            if(string.IsNullOrWhiteSpace(mediaTitle)){lyricsStatus.Text=L.T("Starte zuerst einen Musiktitel.");return;}
            lyricsStatus.Text=L.T("Liedtext wird geladen …");
            try{
                LyricsData data;
                if(!lyricCache.TryGetValue(key,out data)){if(uiTesting)return;data=await LyricsService.Get(mediaTitle,mediaArtist,mediaAlbum,mediaDuration,token);token.ThrowIfCancellationRequested();if(lyricCache.Count>=30)lyricCache.Remove(lyricCache.Keys.First());if(data!=null)lyricCache[key]=data;}
                if(token.IsCancellationRequested||key!=lyricsKey||shuttingDown)return;
                lyricsData=data;DisplayLyrics();
            }catch(OperationCanceledException){if(!token.IsCancellationRequested&&key==lyricsKey){lyricsStatus.Text=L.T("Die Liedtextsuche dauert zu lange. Zum Wiederholen anklicken.");lyricsStatus.Visibility=Visibility.Visible;}}catch{if(!token.IsCancellationRequested&&key==lyricsKey){lyricsStatus.Text=L.T("Liedtext gerade nicht erreichbar. Zum Wiederholen anklicken.");lyricsStatus.Visibility=Visibility.Visible;}}
        }
        void DisplayLyrics(){
            lyricsLines.Children.Clear();lyricIndex=-2;
            if(lyricsData==null){lyricsStatus.Text=L.T("Kein Liedtext für diesen Titel gefunden.");lyricsStatus.Visibility=Visibility.Visible;return;}
            if(lyricsData.Instrumental){lyricsStatus.Text=L.T("Instrumental · kein Liedtext");lyricsStatus.Visibility=Visibility.Visible;return;}
            if(lyricsData.Lines.Count==0&&string.IsNullOrWhiteSpace(lyricsData.Plain)){lyricsStatus.Text=L.T("Kein Liedtext vorhanden.");lyricsStatus.Visibility=Visibility.Visible;return;}
            lyricsStatus.Visibility=Visibility.Collapsed;
            if(lyricsData.Lines.Count==0){var plain=Text(lyricsData.Plain??L.T("Kein Liedtext vorhanden."),15,ink);plain.TextWrapping=TextWrapping.Wrap;lyricsLines.Children.Add(plain);return;}
            foreach(var line in lyricsData.Lines){var lyric=line;var t=Text(line.Text,17,ink);t.TextWrapping=TextWrapping.Wrap;t.RenderTransformOrigin=new Point(0,.5);t.RenderTransform=new ScaleTransform(.91,.91);t.Effect=new BlurEffect{Radius=.7};var b=Btn(t,delegate{if(mediaSeek.IsEnabled)Seek(lyric.Seconds);});b.MinHeight=38;b.Padding=new Thickness(0,7,0,7);b.Background=Brushes.Transparent;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.HorizontalAlignment=HorizontalAlignment.Stretch;b.ToolTip=line.Text;lyricsLines.Children.Add(b);}
            lyricsScroll.UpdateLayout();UpdateLyricsPosition(CurrentMediaPosition());
        }
        string CurrentLyricsKey(){return mediaTitle+"|"+mediaArtist+"|"+mediaAlbum;}
        void UpdateLyricsPosition(double position){
            if(!lyricsVisible||lyricsData==null||lyricsData.Lines.Count==0)return;
            int active=LyricsService.ActiveLine(lyricsData.Lines,position);if(active==lyricIndex)return;lyricIndex=active;
            AnimateLyricRows(active);
        }
        double ExpandedDepth(){return OpenDepth+(quickExpanded?36:0);}
        void LayoutExpanded(){
            bool revealed=quickExpanded||quickReveal>.002;
            expanded.Height=184;navigationRow.Height=new GridLength(62);navigation.Height=revealed?64:14;navigation.Width=revealed?Math.Max(150,nav.Width+30):72;
            navigation.Margin=new Thickness(0,revealed?0:17,0,0);pageDots.Margin=new Thickness(0,revealed?23:6,0,0);if(navigationTrigger!=null)navigationTrigger.Margin=pageDots.Margin;
            double x=(SceneWidth-438)/2-(lyricsVisible?172:0),current=Canvas.GetLeft(expanded);
            if(IsLoaded&&isOpen&&MotionEnabled()&&Math.Abs(current-x)>.1)expanded.BeginAnimation(Canvas.LeftProperty,new DoubleAnimation(current,x,TimeSpan.FromMilliseconds(360)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
            else{expanded.BeginAnimation(Canvas.LeftProperty,null);Canvas.SetLeft(expanded,x);}
            Canvas.SetLeft(lyricsPanel,x+458);lyricsPanel.Height=148+(quickExpanded?36:0);lyricsPanel.IsHitTestVisible=lyricsVisible&&isOpen;
            if(sizeMotion!=null)PositionNotch();
        }
        void PaintQuickBar(){
            double q=quickReveal;quickBar.Opacity=Math.Max(0,Math.Min(1,q*2));pageDots.Opacity=Math.Max(0,1-q*3);
            quickScale.ScaleX=.4+.6*q;quickScale.ScaleY=.15+.85*q;quickShift.Y=-8*(1-q);
            quickBar.Visibility=q>.002||quickExpanded?Visibility.Visible:Visibility.Collapsed;
            bool revealed=quickExpanded||q>.002;navigation.Height=revealed?64:14;navigation.Width=revealed?Math.Max(150,nav.Width+30):72;navigation.Margin=new Thickness(0,revealed?0:17,0,0);pageDots.Margin=new Thickness(0,revealed?23:6,0,0);if(navigationTrigger!=null)navigationTrigger.Margin=pageDots.Margin;
        }
        void SetQuickBar(bool show){
            if(quickExpanded==show)return;quickExpanded=show;quickBar.IsHitTestVisible=show;pageDots.IsHitTestVisible=!show;
            if(!MotionEnabled()){quickReveal=show?1:0;quickVelocity=0;PaintQuickBar();}else StartMorph();
            LayoutExpanded();if(isOpen)AnimateSurface(lyricsVisible?OpenSpan+344:OpenSpan,ExpandedDepth(),true);
        }

        void MediaKey(byte key) {keybd_event(key,0,0,UIntPtr.Zero);keybd_event(key,0,2,UIntPtr.Zero);}

        Grid BuildShelf() {
            var g=new Grid {Width=438,Height=148};var body=new Grid {Margin=new Thickness(34,38,30,19)};g.Children.Add(body);
            fileList=new StackPanel {Orientation=Orientation.Horizontal};
            var scroll=new ScrollViewer {HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=fileList};shelfScroll=scroll;StyleShelfScroll(scroll);body.Children.Add(scroll);
            emptyShelf=Text(L.T("Dateien hier ablegen"),13,muted);emptyShelf.TextAlignment=TextAlignment.Center;emptyShelf.HorizontalAlignment=HorizontalAlignment.Center;emptyShelf.Cursor=Cursors.Hand;emptyShelf.ToolTip=L.T("Dateien auswählen oder hierher ziehen");emptyShelf.MouseLeftButtonUp+=delegate{ChooseFiles();};body.Children.Add(emptyShelf);
            var add=Btn("+",ChooseFiles);shelfAddButton=add;add.RenderTransform=shelfAddShift;add.Width=28;add.Height=22;add.Padding=new Thickness(0);add.FontSize=15;add.HorizontalAlignment=HorizontalAlignment.Left;add.VerticalAlignment=VerticalAlignment.Top;add.Margin=new Thickness(73,3,0,0);add.ToolTip=L.T("Dateien auswählen");g.Children.Add(add);
            BuildShelfCount(g);RefreshShelf();return g;
        }

        void ChooseFiles() {if(prefs.Files.Count>=ShelfCapacity.Limit){SwitchPage("Ablage",false);Open();ShelfNotice(L.T("Ablage voll · maximal 4 Einträge"));return;}using(var f=new Forms.OpenFileDialog {Multiselect=true,Title=L.T("Dateien zur Iris-Ablage hinzufügen")}) {collapse.Stop();editorFocused=true;modalOpen=true;try{if(f.ShowDialog()==Forms.DialogResult.OK)AddFiles(f.FileNames);}finally{modalOpen=false;editorFocused=false;}if(!PointerInsideNotch()&&!prefs.Pinned)collapse.Start();}}
        void OnDrop(object sender,DragEventArgs e) {if(prefs.Shelf&&surface.ContainsPoint(e.GetPosition(canvas))&&e.Data.GetDataPresent(DataFormats.FileDrop)){int accepted=AddFiles((string[])e.Data.GetData(DataFormats.FileDrop));fileDragActive=false;if(accepted>0)shelfFeedback.Receive();else shelfFeedback.Hover(false);e.Effects=accepted>0?DragDropEffects.Link:DragDropEffects.None;e.Handled=true;}}
        int AddFiles(IEnumerable<string> paths) {return AdmitShelfFiles(paths);}
        void RefreshShelf() {
            if(fileList==null||emptyShelf==null)return;
            if(shelfMotion==null)shelfMotion=new ShelfMotion(uiMotion,fileList,emptyShelf,CreateShelfItem);
            shelfMotion.SetPaths(prefs.Files);UpdateShelfCount();
        }
        Button CreateShelfItem(string file){
            var item=new StackPanel {Width=78};
            var symbol=new Image {Source=NativeIcons.Get(file),Width=32,Height=32,HorizontalAlignment=HorizontalAlignment.Center};item.Children.Add(symbol);
            var name=Text(System.IO.Path.GetFileName(file),12,ink);name.TextTrimming=TextTrimming.CharacterEllipsis;name.TextAlignment=TextAlignment.Center;name.Margin=new Thickness(0,4,0,0);item.Children.Add(name);
            bool exists=File.Exists(file)||Directory.Exists(file);
            var b=Btn(item,delegate{if(File.Exists(file)||Directory.Exists(file)){try{Process.Start(new ProcessStartInfo(file){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message,L.T("Datei öffnen"));}}});b.Width=86;b.Height=78;b.Background=Brushes.Transparent;b.Margin=new Thickness(0,0,7,0);b.ToolTip=file+(exists?"":L.T("\nDatei nicht gefunden"));b.Opacity=exists?1:.45;
            Point down=new Point();b.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){down=e.GetPosition(b);};
            b.PreviewMouseMove+=delegate(object s,MouseEventArgs e){var p=e.GetPosition(b);if(e.LeftButton==MouseButtonState.Pressed&&(Math.Abs(p.X-down.X)>8||Math.Abs(p.Y-down.Y)>8)&&exists){try{DragDrop.DoDragDrop(b,new DataObject(DataFormats.FileDrop,new string[]{file}),DragDropEffects.Copy);}finally{fileDragActive=false;shelfFeedback.Hover(false);if(!PointerInsideNotch())collapse.Start();}e.Handled=true;}};
            var menu=new ContextMenu();UiMenus.Style(menu);var remove=new MenuItem {Header=L.T("Aus Ablage entfernen")};remove.Click+=delegate{prefs.Files.Remove(file);Save();RefreshShelf();};menu.Items.Add(remove);b.ContextMenu=menu;new UiMenuMotion(remove,uiMotion);menu.Opened+=delegate{var motion=new UiSurfaceMotion(uiMotion,menu,null,true);motion.Show(true);};
            return b;
        }

        void SwitchPage(string target,bool animate) { SwitchPage(target,animate,0); }
        void SwitchPage(string target,bool animate,int direction) {
            if(pageHost==null)return;var pages=Pages();if(!pages.Contains(target))target="Timer";
            if(target!="Add-ons")CancelAddonTileDrag();string previous=page;bool rebuilt=!builtStripPages.SequenceEqual(pages);
            if(rebuilt){
                pageStrip.Children.Clear();tabMotions.Clear();
                foreach(var name in pages){var view=name=="Musik"?musicPage:name=="Ablage"?shelfPage:name=="Mixer"?mixerPage:name=="Add-ons"?addonPage:timerPage;var motion=new TabMotion {View=view,Name=name};view.RenderTransform=motion.Shift;Canvas.SetLeft(view,0);pageStrip.Children.Add(view);tabMotions.Add(motion);}
                builtStripPages=pages.ToList();
            }
            page=target;prefs.Page=page;pageLabel.Text=L.T(target);pageLabel.Visibility=target=="Musik"?Visibility.Collapsed:Visibility.Visible;
            if(target!="Musik"&&lyricsVisible){lyricsVisible=false;lyricsPanel.Visibility=Visibility.Collapsed;if(lyricsRequest!=null)lyricsRequest.Cancel();LayoutExpanded();if(isOpen)AnimateSurface(OpenSpan,ExpandedDepth(),true);}
            if(rebuilt||!animate||!MotionEnabled())SettleTabs();
            else if(previous!=target){
                if(direction==0){int delta=pages.IndexOf(target)-pages.IndexOf(previous);if(pages.Count>2&&Math.Abs(delta)>pages.Count/2)delta=-Math.Sign(delta);direction=delta>=0?1:-1;}
                double now=tabClock.Elapsed.TotalSeconds;
                foreach(var motion in tabMotions){
                    bool selected=motion.Name==target;
                    double left=(SceneWidth-surface.Span)/2-Canvas.GetLeft(expanded),right=(SceneWidth+surface.Span)/2-Canvas.GetLeft(expanded);
                    bool visible=motion.Alpha>0&&motion.X+438>left&&motion.X<right;
                    // Reuse presentation values on reversal; only invisible views may be repositioned.
                    if(selected&&!visible){motion.X=direction*TabTravel;motion.Velocity=0;motion.Alpha=1;motion.BlurRadius=motion.BlurVelocity=0;}
                    if(!selected&&!visible){motion.Alpha=0;motion.Velocity=0;motion.BlurRadius=motion.BlurVelocity=0;}
                    motion.TargetX=selected?0:-direction*TabTravel;
                    motion.FromAlpha=motion.Alpha;motion.TargetAlpha=selected?1:0;motion.Started=now;motion.Delay=selected&&motion.Alpha<.001?.02:0;
                    motion.View.IsHitTestVisible=selected;PaintTab(motion);
                }
                UpdateTabFade();StartMorph();
            }
            RefreshNav();SyncTimerProgress();Save();
        }
        void SettleTabs(){
            foreach(var motion in tabMotions){bool selected=motion.Name==page;motion.X=motion.TargetX=0;motion.Velocity=0;motion.BlurRadius=motion.BlurVelocity=0;motion.Alpha=motion.FromAlpha=motion.TargetAlpha=selected?1:0;motion.View.IsHitTestVisible=selected;PaintTab(motion);}
            UpdateTabFade();
        }
        void PaintTab(TabMotion motion){
            motion.ExitBlur.Radius=Math.Max(0,Math.Min(6,motion.BlurRadius));
            motion.View.Effect=motion.BlurRadius>.015?motion.ExitBlur:null;
            motion.Shift.X=motion.X;motion.View.Opacity=motion.Alpha;motion.View.Visibility=motion.Alpha>0||motion.TargetAlpha>0?Visibility.Visible:Visibility.Hidden;
            double headerAlpha=Math.Max(0,1-Math.Abs(motion.X)/TabTravel);
            if(motion.Name=="Ablage"){shelfAddShift.X=-motion.X;shelfAddButton.Opacity=headerAlpha;}
            if(motion.Name==page){pageLabel.Opacity=headerAlpha;pageLabelShift.X=0;}
        }
        void AdvanceTabs(double dt,double now){
            double left=(SceneWidth-surface.Span)/2-Canvas.GetLeft(expanded),right=(SceneWidth+surface.Span)/2-Canvas.GetLeft(expanded);
            foreach(var motion in tabMotions){
                if(motion.Alpha<=0&&motion.TargetAlpha<=0){motion.X=motion.TargetX;motion.Velocity=0;continue;}
                IntegrateSpring(ref motion.X,ref motion.Velocity,motion.TargetX,dt,1,TabResponse);
                // Keep the live page visible until it has actually crossed the outer
                // notch edge. Only the perimeter mask fades it, never an inner gutter.
                motion.Alpha=motion.TargetAlpha>0||motion.X+438>left&&motion.X<right?1:0;
                if(Math.Abs(motion.X-motion.TargetX)<.05&&Math.Abs(motion.Velocity)<.3){motion.X=motion.TargetX;motion.Velocity=0;}
                double blurProgress=Math.Max(0,Math.Min(1,Math.Abs(motion.X)/140));
                double blurTarget=motion.TargetAlpha==0&&motion.Alpha>0?6*blurProgress*blurProgress*(3-2*blurProgress):0;
                IntegrateSpring(ref motion.BlurRadius,ref motion.BlurVelocity,blurTarget,dt,1,.14);
                PaintTab(motion);
            }
            UpdateTabFade();
        }
        void UpdateTabFade(){
            if(tabEdgeMask==null)return;
            double shoulder=13+5*Math.Max(0,Math.Min(1,(surface.Span-198)/322));
            double left=(SceneWidth-surface.Span)/2+shoulder,right=(SceneWidth+surface.Span)/2-shoulder;
            for(int i=0;i<=10;i++){double distance=i*tabFeatherWidth/10;tabLeftFade[i].Offset=(left+distance)/SceneWidth;tabRightFade[i].Offset=(right-distance)/SceneWidth;}
        }
        bool TabsSettled(){return tabMotions.All(m=>Math.Abs(m.Alpha-m.TargetAlpha)<.0001&&(m.TargetAlpha==0||(Math.Abs(m.X-m.TargetX)<.05&&Math.Abs(m.Velocity)<.3)));}
        bool SelectedTabReady(){var selected=tabMotions.FirstOrDefault(m=>m.Name==page);return selected!=null&&selected.Alpha>.999&&Math.Abs(selected.X)<1;}

        List<string> Pages() {var p=new List<string>();if(prefs.Music)p.Add("Musik");p.Add("Timer");if(prefs.Shelf)p.Add("Ablage");p.Add("Mixer");p.Add("Add-ons");return p;}
        void Cycle(int direction) {var p=Pages();int i=p.IndexOf(page);SwitchPage(p[(i+direction+p.Count)%p.Count],true,direction);}
        void RefreshNav() {
            if(nav==null)return;var pages=Pages();
            nav.Width=(pages.Count+1)*34;navBackdrop.Width=nav.Width;
            if(!builtNavPages.SequenceEqual(pages)){
                nav.Children.Clear();navLabels.Clear();
                foreach(var p in pages) {var target=p;var label=new DesignSymbol(p=="Musik"?"music":p=="Timer"?"timer":p=="Mixer"?"mixer":p=="Add-ons"?"addons":"shelf",muted){Width=18,Height=18};navLabels[p]=label;var b=Btn(label,delegate{SwitchPage(target,true);});b.Width=32;b.Padding=new Thickness(0);b.Margin=new Thickness(1,0,1,0);b.Height=28;b.Background=Brushes.Transparent;b.ToolTip=L.T(p);nav.Children.Add(b);}
                quickSettingsButton=Btn(Glyph("\uE713",18,muted),ShowSettings);quickSettingsButton.Width=32;quickSettingsButton.Height=28;quickSettingsButton.Padding=new Thickness(0);quickSettingsButton.Margin=new Thickness(1,0,1,0);quickSettingsButton.Background=Brushes.Transparent;quickSettingsButton.ToolTip=L.T("Einstellungen öffnen");System.Windows.Automation.AutomationProperties.SetName(quickSettingsButton,L.T("Einstellungen öffnen"));nav.Children.Add(quickSettingsButton);
                builtNavPages=pages.ToList();
            }
            foreach(var p in pages)navLabels[p].Ink=p==page?(p=="Musik"?new SolidColorBrush(Color.FromRgb(249,105,162)):p=="Timer"?new SolidColorBrush(Color.FromRgb(255,87,110)):ink):muted;
            pageDots.Children.Clear();foreach(var p in pages)pageDots.Children.Add(new Border{Width=p==page?14:4,Height=4,CornerRadius=new CornerRadius(2),Background=p==page?ink:muted,Margin=new Thickness(3,0,3,0),VerticalAlignment=VerticalAlignment.Bottom});
            double x=Math.Max(0,pages.IndexOf(page))*34+3;
            targetNav=x;
            if(IsLoaded && MotionEnabled())StartMorph();
            else{navSlide.BeginAnimation(TranslateTransform.XProperty,null);navSlide.X=x;navVelocity=0;}

        }

        void Open() {
            CancelCursorAvoidance();if(isOpen)return;isOpen=true;mascotTimer.Stop();ShowEyes(false);if(volumeHud!=null){volumeHud.Show(false);volumeHud.Visibility=Visibility.Collapsed;hudDismiss.Stop();}
            if(!uiTesting)hoverWatch.Start();
            expanded.Visibility=Visibility.Visible;expanded.IsHitTestVisible=true;compact.IsHitTestVisible=false;
            LayoutExpanded();if(lyricsVisible){lyricsPanel.Visibility=Visibility.Visible;}
            AnimateSurface(lyricsVisible?OpenSpan+344:OpenSpan,ExpandedDepth(),true);
            if(!MotionEnabled()){compact.BeginAnimation(OpacityProperty,null);compact.Opacity=0;expanded.BeginAnimation(OpacityProperty,null);expanded.Opacity=1;((TranslateTransform)expanded.RenderTransform).BeginAnimation(TranslateTransform.YProperty,null);((TranslateTransform)expanded.RenderTransform).Y=0;return;}
            compact.BeginAnimation(OpacityProperty,new DoubleAnimation(compact.Opacity,0,TimeSpan.FromMilliseconds(90)));
            expanded.BeginAnimation(OpacityProperty,new DoubleAnimation(expanded.Opacity,1,TimeSpan.FromMilliseconds(160)){BeginTime=TimeSpan.FromMilliseconds(expanded.Opacity<.01?60:0)});
            var openShift=(TranslateTransform)expanded.RenderTransform;
            openShift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(openShift.Y,0,TimeSpan.FromMilliseconds(300)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
        }
        void CloseNotch(bool force) {
            if(!isOpen || (!force&&(prefs.Pinned||timerFinished||(preview&&!uiTesting))))return;isOpen=false;
            lastCloseStartedUtc=DateTime.UtcNow;collapse.Stop();hoverWatch.Stop();
            if(editorFocused&&!modalOpen){Keyboard.ClearFocus();editorFocused=false;}
            expanded.IsHitTestVisible=false;compact.IsHitTestVisible=true;
            volumeOpen=false;
            quickExpanded=false;quickReveal=0;quickVelocity=0;PaintQuickBar();LayoutExpanded();
            if(durationPopup!=null)durationPopup.IsOpen=false;
            lyricsPanel.IsHitTestVisible=false;
            if(!MotionEnabled()){expanded.BeginAnimation(OpacityProperty,null);expanded.Opacity=0;expanded.Visibility=Visibility.Collapsed;compact.BeginAnimation(OpacityProperty,null);compact.Opacity=1;AnimateSurface(RestingSpan(),RestingDepth(),false);mascotTimer.Start();return;}
            var fade=new DoubleAnimation(expanded.Opacity,0,TimeSpan.FromMilliseconds(190));fade.Completed+=delegate{if(!isOpen)expanded.Visibility=Visibility.Collapsed;};expanded.BeginAnimation(OpacityProperty,fade);
            var closeShift=(TranslateTransform)expanded.RenderTransform;
            closeShift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(closeShift.Y,-7,TimeSpan.FromMilliseconds(220)){EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
            compact.BeginAnimation(OpacityProperty,new DoubleAnimation(compact.Opacity,1,TimeSpan.FromMilliseconds(180)){BeginTime=TimeSpan.FromMilliseconds(compact.Opacity<.01?110:0)});
            AnimateSurface(RestingSpan(),RestingDepth(),false);mascotTimer.Start();
        }
        void AnimateSurface(double width,double height,bool spring) {
            targetSpan=width;targetDepth=height;SyncTimerProgress();
            if(!MotionEnabled()) {StopMorph();surface.Span=width;surface.Depth=height;SettleTabs();navSlide.X=targetNav;quickReveal=quickExpanded?1:0;PaintQuickBar();UpdateContentClip();return;}
            StartMorph();
        }
        void StartMorph(){if(!morphing){morphing=true;morphClock.Restart();CompositionTarget.Rendering+=MorphFrame;}}
        bool MotionEnabled() {return !prefs.ReduceMotion && SystemParameters.ClientAreaAnimation;}
        void MorphFrame(object sender,EventArgs e) {
            double dt=Math.Min(.1,morphClock.Elapsed.TotalSeconds);morphClock.Restart();
            double span=surface.Span,depth=surface.Depth;
            IntegrateSpring(ref span,ref spanVelocity,targetSpan,dt,isOpen ? .639 : .449,isOpen ? .416 : .493);
            IntegrateSpring(ref depth,ref depthVelocity,targetDepth,dt,isOpen ? .67 : .58,isOpen ? .42 : .43);
            double navX=navSlide.X;IntegrateSpring(ref navX,ref navVelocity,targetNav,dt,1.0,.30);navSlide.X=navX;
            AdvanceTabs(dt,tabClock.Elapsed.TotalSeconds);
            IntegrateSpring(ref quickReveal,ref quickVelocity,quickExpanded?1:0,dt,.72,.36);PaintQuickBar();
            surface.Span=span;surface.Depth=depth;UpdateContentClip();
            if(Math.Abs(span-targetSpan)<.05&&Math.Abs(depth-targetDepth)<.05&&Math.Abs(spanVelocity)<.3&&Math.Abs(depthVelocity)<.3&&Math.Abs(navX-targetNav)<.05&&Math.Abs(navVelocity)<.3&&TabsSettled()&&Math.Abs(quickReveal-(quickExpanded?1:0))<.002&&Math.Abs(quickVelocity)<.02){surface.Span=targetSpan;surface.Depth=targetDepth;navSlide.X=targetNav;SettleTabs();quickReveal=quickExpanded?1:0;PaintQuickBar();StopMorph();UpdateContentClip();}
        }
        static void IntegrateSpring(ref double value,ref double velocity,double target,double dt,double damping,double response) {
            double omega=2*Math.PI/response,decay=damping*omega,frequency=omega*Math.Sqrt(1-damping*damping),offset=value-target;
            if(damping>=.9999){double c=velocity+omega*offset,exp=Math.Exp(-omega*dt);value=target+(offset+c*dt)*exp;velocity=(velocity-omega*c*dt)*exp;return;}
            double sine=Math.Sin(frequency*dt),cosine=Math.Cos(frequency*dt),exponential=Math.Exp(-decay*dt);
            value=target+exponential*(offset*cosine+(velocity+decay*offset)/frequency*sine);
            velocity=exponential*(velocity*cosine-(decay*velocity+omega*omega*offset)/frequency*sine);
        }
        void StopMorph(){if(morphing){CompositionTarget.Rendering-=MorphFrame;morphing=false;}morphClock.Stop();spanVelocity=0;depthVelocity=0;navVelocity=0;quickVelocity=0;}
        void UpdateContentClip(){
            if(expanded==null)return;
            SyncGlassBackdrop();SyncTimerProgress();if(compactLyrics!=null)ClipToSurface(compactLyrics);if(volumeHud!=null)ClipToSurface(volumeHud);if(shelfFeedback!=null)shelfFeedback.InvalidateVisual();
            if(pageHost!=null){pageHost.Clip=surface.GetOutline();Canvas.SetLeft(pageStrip,Canvas.GetLeft(expanded));Canvas.SetTop(pageStrip,Canvas.GetTop(expanded));UpdateTabFade();}
            ClipToSurface(expanded);if(lyricsPanel!=null)ClipToSurface(lyricsPanel);
            double compactWidth=Math.Min(compact.Width,Math.Max(0,surface.Span-26)),compactHeight=Math.Min(34,Math.Max(0,surface.Depth-1));compact.Clip=new RectangleGeometry(new Rect((compact.Width-compactWidth)/2,0,compactWidth,compactHeight));
            if(previousPageButton!=null){double x=Canvas.GetLeft(expanded);Canvas.SetLeft(previousPageButton,x);Canvas.SetLeft(nextPageButton,x+420);Canvas.SetTop(previousPageButton,70);Canvas.SetTop(nextPageButton,70);previousPageButton.Visibility=nextPageButton.Visibility=isOpen&&surface.Span>=OpenSpan-6?Visibility.Visible:Visibility.Collapsed;}
        }
        void UpdateMusicDate(){musicDate.Inlines.Clear();musicDate.Inlines.Add(new System.Windows.Documents.Run(DateTime.Now.ToString("ddd d MMM yyyy  ",L.Culture)));musicDate.Inlines.Add(new System.Windows.Documents.Run(DateTime.Now.ToString("HH:mm")){FontWeight=FontWeights.Bold});}

        void ClipToSurface(FrameworkElement panel){double left=(SceneWidth-surface.Span)/2+13,right=(SceneWidth+surface.Span)/2-13,x=Canvas.GetLeft(panel),l=Math.Max(0,left-x),r=Math.Min(panel.Width,right-x),height=Math.Min(panel.Height,Math.Max(0,surface.Depth-Canvas.GetTop(panel)-6));panel.Clip=new RectangleGeometry(new Rect(l,0,Math.Max(0,r-l),height),6,6);}
        bool PointerInsideNotch(){
            if(uiTesting&&qaPointerOutside)return false;
            POINT cursor;if(!GetCursorPos(out cursor))return IsMouseOver;
            try{return surface.ContainsPoint(surface.PointFromScreen(new Point(cursor.X,cursor.Y)));}catch{return IsMouseOver;}
        }
        void ShowEyes(bool show) {
            if(show&&(!MascotEligible()||IdleSeconds()<7))show=false;
            eyes.Visibility=show?Visibility.Visible:Visibility.Collapsed;eyes.Opacity=show?1:0;compactText.Visibility=show?Visibility.Collapsed:Visibility.Visible;
            if(idleEyesWatch!=null){if(show)idleEyesWatch.Start();else idleEyesWatch.Stop();}
        }
        void Blink() {if(eyes.Visibility!=Visibility.Visible||!MotionEnabled())return;EyeScale(.13,.075);blinkEnd.Start();}
        void EyeScale(double value,double seconds) {foreach(var e in new Border[]{eyeLeft,eyeRight})((ScaleTransform)e.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(value,TimeSpan.FromSeconds(seconds)){EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}});}
        void OnTick(object sender,EventArgs e) {
            var now=DateTime.UtcNow;
            if(!preview&&!uiTesting)RefreshFullscreenVisibility();
            if(countdown.Update(now))Finish();
            RefreshTimer();
            RefreshIdleEyes();
            if(mediaAvailable)RefreshMediaProgress();
            if(prefs.BatteryEnabled&&!uiTesting&&(DateTime.UtcNow-lastBatteryPoll).TotalSeconds>=15)RefreshBattery();
            if(++refreshTicks%4==0) {
                clock.Text=DateTime.Now.ToString("HH:mm");
                if(musicDate!=null)UpdateMusicDate();

            }
        }
        void Place() {
            var screens=Forms.Screen.AllScreens;if(screens.Length==0)return;
            int index=Math.Max(0,Math.Min(prefs.Monitor,screens.Length-1));
            var bounds=screens[index].Bounds;
            double scaleX=1,scaleY=1;
            try {var source=PresentationSource.FromVisual(this);if(source!=null){scaleX=source.CompositionTarget.TransformToDevice.M11;scaleY=source.CompositionTarget.TransformToDevice.M22;}}catch{}
            SetNotchScale(FitNotchScale(prefs.NotchSize,bounds.Width/scaleX));PositionNotch();
        }
        void PositionNotch(){
            var screens=Forms.Screen.AllScreens;if(screens.Length==0)return;var bounds=screens[Math.Max(0,Math.Min(prefs.Monitor,screens.Length-1))].Bounds;
            double scaleX=1,scaleY=1;var source=PresentationSource.FromVisual(this);if(source!=null){scaleX=source.CompositionTarget.TransformToDevice.M11;scaleY=source.CompositionTarget.TransformToDevice.M22;}
            double span=bounds.Width/scaleX,visibleWidth=(lyricsVisible?OpenSpan+344:OpenSpan)*requestedScale,limit=Math.Max(0,(span-visibleWidth)/2-12);
            Left=bounds.Left/scaleX+(span-base.Width)/2+Math.Max(-limit,Math.Min(limit,prefs.Offset));Top=bounds.Top/scaleY;
        }
        void OnDisplayChanged(object sender,EventArgs e) {Dispatcher.BeginInvoke(new Action(Place));}
        bool IsFullscreen() {
            IntPtr foreground=GetForegroundWindow();if(foreground==IntPtr.Zero)return false;if(foreground==new WindowInteropHelper(this).Handle)return designFullscreen;
            uint pid;GetWindowThreadProcessId(foreground,out pid);if(pid==Process.GetCurrentProcess().Id)return designFullscreen;
            var cls=new StringBuilder(128);GetClassName(foreground,cls,128);if(cls.ToString()=="Progman"||cls.ToString()=="WorkerW"||cls.ToString()=="Shell_TrayWnd")return false;
            if((GetWindowLong(foreground,-16)&0x00C00000)!=0)return false;
            RECT r;if(!GetWindowRect(foreground,out r))return false;
            var monitor=Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).Bounds;
            return r.Left<=monitor.Left && r.Top<=monitor.Top && r.Right>=monitor.Right && r.Bottom>=monitor.Bottom;
        }
        void SetupTray() {
            System.Drawing.Icon trayIcon=System.Drawing.SystemIcons.Application;
            try {trayIcon=new System.Drawing.Icon(System.IO.Path.Combine(root,"Iris.ico"));}catch{}
            tray=new Forms.NotifyIcon {Text="Iris",Icon=trayIcon,Visible=true};
            var menu=new Forms.ContextMenuStrip();menu.Items.Add(L.T("Notch öffnen"),null,delegate{Dispatcher.BeginInvoke(new Action(delegate{Open();}));});menu.Items.Add(L.T("Einstellungen"),null,delegate{Dispatcher.BeginInvoke(new Action(ShowSettings));});menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add(L.T("Beenden"),null,delegate{Dispatcher.BeginInvoke(new Action(Close));});tray.ContextMenuStrip=menu;
            tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)Dispatcher.BeginInvoke(new Action(ShowSettings));};
        }
        Button ArrowButton(bool right,Action action){var b=Btn(new DesignSymbol(right?"right":"left",Brushes.White){Width=16,Height=16},action);b.Width=19;b.Height=19;b.Padding=new Thickness(0);b.Background=new SolidColorBrush(Color.FromRgb(76,76,78));b.ToolTip=right?L.T("Nächste Ansicht"):L.T("Vorherige Ansicht");return b;}
        Button SymbolButton(string symbol,string tip,Action action){var b=Btn(new DesignSymbol(symbol,ink){Width=24,Height=24},action);b.Width=32;b.Height=30;b.Padding=new Thickness(0);b.Background=Brushes.Transparent;b.ToolTip=tip;if(symbol=="reset"){var spin=new UiSpinMotion((FrameworkElement)b.Content);uiMotion.Register(spin);b.Click+=delegate{spin.Turn();uiMotion.Wake(spin);};}return b;}
        void ShowSettings() {
            if(settings!=null){if(settingsWindowMotion!=null)settingsWindowMotion.Show(false);settings.Activate();return;}collapse.Stop();settingsSection="Allgemein";settingsTabs.Clear();
            settings=new Window {Title=L.T("Iris · Einstellungen"),Width=654,Height=506,WindowStartupLocation=WindowStartupLocation.CenterScreen,WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,Foreground=ink,FontFamily=new FontFamily("Segoe UI"),ResizeMode=ResizeMode.NoResize,Topmost=true,UseLayoutRounding=true};settings.Resources=Resources;
            var shell=new Border {Background=new SolidColorBrush(Color.FromRgb(28,28,30)),BorderBrush=new SolidColorBrush(Color.FromRgb(55,55,57)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),ClipToBounds=true};settings.Content=shell;
            var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(190)});layout.ColumnDefinitions.Add(new ColumnDefinition());shell.Child=layout;
            var side=new Border {Background=new SolidColorBrush(Color.FromRgb(21,21,23)),CornerRadius=new CornerRadius(10,0,0,10)};layout.Children.Add(side);
            var sideGrid=new Grid();side.Child=sideGrid;
            settingsSidebar=new StackPanel {Margin=new Thickness(10,43,10,0),VerticalAlignment=VerticalAlignment.Top};sideGrid.Children.Add(settingsSidebar);
            string[] titles={"Allgemein","Anzeige","Design","Musik & Liedtexte","Timer","Ablage","Add-ons","Gaming","Downloads"};string[] glyphs={"\uE713","\uE7F4","\uE793","\uE8D6","\uE916","\uE8B7","\uE80A","\uE7FC","\uE896"};string[] colors={"#88888B","#6654E8","#77777B","#F52968","#FF8237","#EEB32C","#8768C1","#447ECA","#3197BE"};
            for(int i=0;i<titles.Length;i++)AddSettingsTab(settingsSidebar,titles[i],glyphs[i],colors[i]);
            var about=new StackPanel {Margin=new Thickness(10,0,10,13),VerticalAlignment=VerticalAlignment.Bottom};sideGrid.Children.Add(about);AddSettingsTab(about,"Über Iris","\uE946","#8C8C90");
            settingsBody=new Border {Margin=new Thickness(22,21,22,18)};Grid.SetColumn(settingsBody,1);layout.Children.Add(settingsBody);
            var dragArea=new Border {Height=35,VerticalAlignment=VerticalAlignment.Top,Background=Brushes.Transparent};Grid.SetColumnSpan(dragArea,2);Panel.SetZIndex(dragArea,5);layout.Children.Add(dragArea);dragArea.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ButtonState==MouseButtonState.Pressed)settings.DragMove();};
            var close=Btn(new Ellipse {Width=12,Height=12,Fill=new SolidColorBrush(Color.FromRgb(255,85,94))},delegate{settings.Close();});close.Width=28;close.Height=28;close.Padding=new Thickness(0);close.Background=Brushes.Transparent;close.HorizontalAlignment=HorizontalAlignment.Left;close.VerticalAlignment=VerticalAlignment.Top;close.Margin=new Thickness(6,5,0,0);close.ToolTip=L.T("Einstellungen schließen");Panel.SetZIndex(close,6);layout.Children.Add(close);
            settings.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape)settings.Close();};
            settings.Closed+=delegate{settings=null;Save();if(!PointerInsideNotch())collapse.Start();};SetupSettingsMotion(settings,shell,sideGrid);RenderSettingsSection();settings.Show();
        }
        void AddSettingsTab(StackPanel parent,string title,string glyph,string color){
            var row=new StackPanel {Orientation=Orientation.Horizontal};row.Children.Add(new Border {Width=22,Height=22,CornerRadius=new CornerRadius(6),Background=(Brush)new BrushConverter().ConvertFromString(color),Child=Glyph(glyph,14,Brushes.White)});((TextBlock)((Border)row.Children[0]).Child).HorizontalAlignment=HorizontalAlignment.Center;
            var label=Text(L.T(title),13,ink);label.Margin=new Thickness(10,0,0,0);row.Children.Add(label);
            var b=Btn(row,delegate{settingsSection=title;RenderSettingsSection();});b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Height=32;b.Padding=new Thickness(7,3,7,3);b.Margin=new Thickness(0,0,0,0);b.Background=Brushes.Transparent;settingsTabs[title]=b;parent.Children.Add(b);
        }
        void RenderSettingsSection(){
            var panel=new StackPanel();
            var heading=Text(L.T(settingsSection),18,ink);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,14);
            if(settingsSection=="Allgemein"){
                BuildLanguageSettings(panel);BuildGeneralBehavior(panel);
            }else if(settingsSection=="Anzeige"){
                BuildNotchAppearance(panel);
                SettingsLabel(panel,L.T("Bildschirm"));
                var screens=Forms.Screen.AllScreens;var screenRows=new StackPanel();screenSelections.Clear();screenButtons.Clear();
                for(int i=0;i<screens.Length;i++){int index=i;var b=Btn(L.T("Bildschirm ")+(i+1)+(screens[i].Primary?L.T(" · Hauptbildschirm"):"")+"   "+screens[i].Bounds.Width+" × "+screens[i].Bounds.Height,delegate{prefs.Monitor=index;Place();Save();UpdateScreenSelection();});b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Height=40;b.Padding=new Thickness(12,0,12,0);AddScreenSelection(b,i);screenRows.Children.Add(b);}SettingsGroup(panel,screenRows);
                SettingsLabel(panel,"Position");var pos=new StackPanel {Margin=new Thickness(13,10,13,12)};var label=Text(L.T("Horizontaler Versatz: ")+(int)prefs.Offset+" px",13,ink);pos.Children.Add(label);
                var offset=new MotionSlider {Minimum=-650,Maximum=650,Value=prefs.Offset,TickFrequency=10,IsSnapToTickEnabled=true,SmallChange=10.0/1300,Margin=new Thickness(0,14,0,12)};settingsOffset=offset;new UiSliderMotion(offset,uiMotion);offset.ValueChanged+=delegate{prefs.Offset=offset.Value;label.Text=L.T("Horizontaler Versatz: ")+(int)prefs.Offset+" px";Place();};offset.PreviewMouseUp+=delegate{Save();};pos.Children.Add(offset);var center=Btn(L.T("Zentrieren"),delegate{offset.Value=0;Save();});center.Height=28;center.Background=new SolidColorBrush(Color.FromRgb(64,64,67));pos.Children.Add(center);SettingsGroup(panel,pos);
                SettingsLabel(panel,L.T("Oberer Bildschirmrand"));SettingsGroup(panel,SettingRow(L.T("Nach 0,5 Sekunden in der Nähe ausweichen"),L.T("Nur bei geschlossener Notch · Klicks durchlassen"),prefs.CursorAvoidance,SetCursorAvoidance));
            }else if(settingsSection=="Design"){
                BuildDesignSettings(panel);
            }else if(settingsSection=="Musik & Liedtexte"){
                SettingsLabel(panel,"Player");SettingsGroup(panel,SettingRow(L.T("Musiksteuerung anzeigen"),L.T("Verwendet die aktive Windows-Mediensitzung"),prefs.Music,SetMusicEnabled),SettingsInfo(L.T("Cover & Videovorschau"),L.T("Werden angezeigt, sobald der Player sie an Windows übermittelt.")));
                SettingsLabel(panel,L.T("Liedtexte"));SettingsGroup(panel,SettingsInfo(L.T("Synchronisierte Liedtexte"),L.T("Die Sprechblase im Player öffnet verfügbare Liedtexte von LRCLIB. Eine Zeile anklicken, um zu dieser Stelle zu springen.")),SettingsInfo(L.T("Verfügbarkeit"),L.T("Hängt vom Titel und den Metadaten des Players ab.")));
            }else if(settingsSection=="Timer"){
                SettingsLabel(panel,L.T("Dauer"));var box=new StackPanel {Margin=new Thickness(13)};box.Children.Add(Text(L.T("Frei einstellbarer Timer"),14,ink));var note=Text(L.T("Minuten und Sekunden in der Notch über die Uhrzeit oder „Dauer“ einstellen."),12,muted);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,5,0,13);box.Children.Add(note);var edit=Btn(L.T("Timerdauer einstellen"),delegate{settings.Close();SwitchPage("Timer",false);Open();durationPopup.IsOpen=true;});edit.Background=new SolidColorBrush(Color.FromRgb(64,64,67));box.Children.Add(edit);SettingsGroup(panel,box);
                SettingsLabel(panel,L.T("Verhalten"));SettingsGroup(panel,SettingsInfo(L.T("Läuft im Hintergrund weiter"),L.T("Der Timer bleibt beim Schließen der Notch aktiv. Bei Ablauf gibt es einen Ton und eine Benachrichtigung.")));
            }else if(settingsSection=="Ablage"){
                SettingsLabel(panel,L.T("Dateien"));SettingsGroup(panel,SettingRow(L.T("Ablage anzeigen"),L.T("Dateien per Drag & Drop oder Plus hinzufügen"),prefs.Shelf,SetShelfEnabled),SettingsInfo(L.T("Originaldateien bleiben erhalten"),L.T("Einträge sind Verweise. Das Entfernen aus der Ablage löscht keine Dateien.")));var open=Btn(L.T("Ablage öffnen"),delegate{SetShelfEnabled(true);settings.Close();SwitchPage("Ablage",false);Open();});open.Margin=new Thickness(0,14,0,0);panel.Children.Add(open);
            }else if(settingsSection=="Downloads"){BuildDownloadSettings(panel);
            }else if(settingsSection=="Gaming"){
                BuildGamingSettings(panel);
            }else if(settingsSection=="Add-ons"){
                BuildAddonSettings(panel);
            }else{
                SettingsLabel(panel,"Iris "+AppVersion());SettingsGroup(panel,SettingsInfo(L.T("Deine Windows-Notch"),L.T("Musik, Timer, Ablage, Lautstärkemixer und Add-ons. Eigenständige WPF-Implementierung mit rein schwarzer Notch.")),SettingsInfo("Navigation",L.T("Mausrad, Pfeile oder die Schnellleiste am unteren Rand. Einstellungen über den rechten Button der Schnellleiste oder das Iris-Symbol im Windows-Infobereich.")));BuildUpdateSettings(panel);BuildDataSettings(panel);
            }
            var layout=new Grid();
            layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            layout.RowDefinitions.Add(new RowDefinition());
            layout.Children.Add(heading);
            var scroll=new MixerScroll(uiMotion){Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            Grid.SetRow(scroll,1);layout.Children.Add(scroll);
            ShowSettingsPage(layout);
        }
        void SetReducedMotion(bool value){prefs.ReduceMotion=value;if(value){uiMotion.SnapAll();SettleTabs();quickReveal=quickExpanded?1:0;PaintQuickBar();AnimateSurface(isOpen?(lyricsVisible?OpenSpan+344:OpenSpan):RestingSpan(),isOpen?ExpandedDepth():RestingDepth(),true);EyeScale(1,0);}RefreshNav();Save();}
        void SetMusicEnabled(bool value){bool changed=prefs.Music!=value;prefs.Music=value;if(!value&&page=="Musik")SwitchPage("Timer",true);else SwitchPage(page,false);if(changed){if(value)SetupMedia();else StopMedia();}RefreshNav();Save();}
        void SetShelfEnabled(bool value){prefs.Shelf=value;if(!value&&page=="Ablage")SwitchPage("Timer",true);else SwitchPage(page,false);RefreshNav();Save();}
        void SettingsLabel(StackPanel panel,string title){var t=Text(title,11,new SolidColorBrush(Color.FromRgb(143,143,147)));t.Margin=new Thickness(5,0,0,5);panel.Children.Add(t);}
        void SettingsGroup(StackPanel panel,params UIElement[] rows){var group=new StackPanel();for(int i=0;i<rows.Length;i++){if(i>0)group.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromRgb(56,56,58)),Margin=new Thickness(13,0,13,0)});group.Children.Add(rows[i]);}panel.Children.Add(new Border {Background=new SolidColorBrush(Color.FromRgb(39,39,41)),CornerRadius=new CornerRadius(13),Margin=new Thickness(0,0,0,12),Child=group});}
        UIElement SettingsInfo(string title,string detail){var row=new StackPanel {Margin=new Thickness(13,11,13,11)};row.Children.Add(Text(title,13,ink));var note=Text(detail,11,new SolidColorBrush(Color.FromRgb(151,151,155)));note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,3,0,0);row.Children.Add(note);return row;}
        UIElement SettingRow(string title,string subtitle,bool value,Action<bool> action){
            var row=new Grid {MinHeight=47,Margin=new Thickness(13,5,13,5)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(48)});
            var labels=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};labels.Children.Add(Text(title,13,ink));if(!string.IsNullOrEmpty(subtitle)){var note=Text(subtitle,11,new SolidColorBrush(Color.FromRgb(145,145,149)));note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,2,0,0);labels.Children.Add(note);}row.Children.Add(labels);
            var toggle=new FluidToggle(uiMotion) {IsChecked=value,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right,ToolTip=title};System.Windows.Automation.AutomationProperties.SetName(toggle,title);Grid.SetColumn(toggle,1);row.Children.Add(toggle);
            if(action!=null){toggle.Checked+=delegate{action(true);Save();};toggle.Unchecked+=delegate{action(false);Save();};}return row;
        }
        void CaptureWindow(Window window,string name){window.UpdateLayout();InvalidateTree(window);var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(window);SaveBitmap(bitmap,name);}

        void Save() {
            if(prefs==null||countdown==null||settingsReadOnly)return;
            prefs.TimerRunning=countdown.Running;prefs.TimerEndUtc=countdown.Ends;prefs.Remaining=countdown.Remaining;
            try {
                PreferencesStore.Save(System.IO.Path.Combine(root,dataFolder,"settings.json"),prefs);settingsStorageNote="";
            } catch(Exception ex) {settingsStorageNote=L.T("Einstellungen konnten nicht gespeichert werden: ")+ex.Message;}

        }
        void StopMedia() {if(!shuttingDown)ReadMedia("{\"available\":false}");try{if(media!=null&&!media.HasExited)media.Kill();}catch{}if(media!=null)media.Dispose();media=null;if(bridgeDirectory!=null){try{foreach(var file in Directory.GetFiles(bridgeDirectory))File.Delete(file);Directory.Delete(bridgeDirectory);}catch{}}}
        void RunQa() {
            // QA demos persist their material in Data-qa; begin color/layout assertions from a known baseline.
            qaDesignFullscreen=false;SetNotchDesign("black");SetFullscreenDesign("inherit");materialMotion.Snap();SetReducedMotion(false);
            prefs.Minutes=5;prefs.Seconds=0;countdown.Reset(300);timerFinished=false;
            minutesBox.Text="05";secondsBox.Text="00";RefreshTimer();SwitchPage("Timer",false);Open();
            var results=new List<string>();
            int step=0,lyricsSettleWaits=0;DateTime hoverStarted=DateTime.MinValue;var qa=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(800)};
            qa.Tick+=delegate {
                try {
                    if(step==0) {
                        if(Math.Abs(surface.Depth-OpenDepth)>1)throw new Exception("Open animation did not reach target");
                        if((page!="Timer"||!SelectedTabReady()))throw new Exception("Timer page is not visible");
                        results.Add("PASS: Expanded notch reaches target geometry.");
                        VerifyPageLayout(results);
                        Capture("preview-timer.png");
                        minutesBox.Text="0";secondsBox.Text="2";
                        if(!ApplyDuration(true))throw new Exception("Valid custom duration rejected");
                        startButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(!countdown.Running)throw new Exception("Start button did not start countdown");
                        results.Add("PASS: Custom 2-second timer starts through button event.");
                    } else if(step==1) {
                        startButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if(countdown.Running||countdown.Remaining<=0)throw new Exception("Pause button failed");
                        results.Add("PASS: Pause button preserves remaining time.");
                        startButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    } else if(step==4) {
                        if(countdown.Running||countdown.Remaining!=0||!timerFinished)throw new Exception("Timer completion UI did not trigger");
                        results.Add("PASS: Timer finishes and opens notification state.");
                        VerifyTimerNotice(results);
                        timerFinished=false;finishedTimer.Stop();
                        secondsBox.Text="75";if(ApplyDuration(true))throw new Exception("Invalid duration accepted");
                        results.Add("PASS: Invalid seconds rejected.");
                        minutesBox.Text="5";secondsBox.Text="0";ApplyDuration(true);countdown.Reset(300);RefreshTimer();
                        SwitchPage("Ablage",true);Open();
                        var sample=System.IO.Path.Combine(root,dataFolder,"Beispiel.txt");File.WriteAllText(sample,"Iris test file");AddFiles(new string[]{sample});
                        if(!prefs.Files.Contains(sample)||File.ReadAllText(sample)!="Iris test file")throw new Exception("Shelf did not preserve original file");
                        prefs.Files.Remove(sample);Save();RefreshShelf();if(!File.Exists(sample))throw new Exception("Removing shelf reference deleted original");
                        results.Add("PASS: Shelf adds and removes references without changing original files.");
                    } else if(step==5) {
                        Capture("preview-shelf.png");
                        SwitchPage("Musik",true);
                    } else if(step==6) {
                        Capture("preview-music.png");
                        prefs.Pinned=false;CloseNotch(true);
                    } else if(step==7) {
                        if(Math.Abs(surface.Depth-ClosedDepth)>1)throw new Exception("Close animation did not reach target");
                        results.Add("PASS: Closed notch returns to compact geometry.");
                        Capture("preview-closed.png");
                        ShowEyes(true);Blink();
                    } else if(step==8) {
                        Capture("preview-eyes.png");results.Add("PASS: Mascot renders in compact state.");
                        ShowSettings();
                    } else if(step==9) {
                        if(settings==null||!settings.IsVisible)throw new Exception("Settings did not open");
                        CaptureWindow(settings,"preview-settings.png");
                        foreach(var section in settingsTabs.Keys.ToArray()){settingsSection=section;RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();if(settingsPages.Current.RenderSize.Height>settingsBody.ActualHeight+.5)throw new Exception("Settings section exceeds window: "+section);if(section=="Anzeige")CaptureWindow(settings,"preview-settings-display.png");}
                        bool originalMusic=prefs.Music,originalShelf=prefs.Shelf;SetMusicEnabled(false);if(Pages().Contains("Musik")||pageStrip.Children.Contains(musicPage))throw new Exception("Disabled music remains in page strip");SetMusicEnabled(originalMusic);SetShelfEnabled(false);if(Pages().Contains("Ablage")||pageStrip.Children.Contains(shelfPage))throw new Exception("Disabled shelf remains in page strip");SetShelfEnabled(originalShelf);
                        results.Add("PASS: All eight settings sections fit the window; category settings update the live strip.");results.Add("PASS: Settings window opens with custom sidebar and no native title bar.");settings.Close();
                        Open();SwitchPage("Timer",true);SwitchPage("Musik",true);SwitchPage("Ablage",true);SwitchPage("Timer",true);
                    } else if(step==10) {
                        if((page!="Timer"||!SelectedTabReady()))throw new Exception("Rapid page retarget lost the selected page");
                        if(Math.Abs(navSlide.X-targetNav)>.5)throw new Exception("Navigation indicator did not settle");
                        results.Add("PASS: Rapid page switches preserve the selected page and settle the shared indicator.");
                        CloseNotch(true);
                        int changes=0;var interruption=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(75)};
                        interruption.Tick+=delegate{changes++;if(changes==1||changes==3)Open();else CloseNotch(true);if(changes==3)interruption.Stop();};interruption.Start();
                    } else if(step==12) {
                        if(!isOpen||Math.Abs(surface.Depth-OpenDepth)>.1||morphing)throw new Exception("Interrupted spring did not settle in the final state: open="+isOpen+", depth="+surface.Depth+", target="+targetDepth+", morphing="+morphing+", spanVelocity="+spanVelocity+", depthVelocity="+depthVelocity);
                        results.Add("PASS: Opening and closing can be repeatedly interrupted and settle without a stuck render loop.");
                        prefs.ReduceMotion=true;CloseNotch(true);if(surface.Depth!=ClosedDepth)throw new Exception("Reduced motion did not close immediately");Open();if(surface.Depth!=OpenDepth)throw new Exception("Reduced motion did not open immediately");
                        SwitchPage("Musik",true);if((page!="Musik"||!SelectedTabReady()))throw new Exception("Reduced motion did not switch page immediately");
                        results.Add("PASS: Reduced motion uses immediate geometry and page changes.");
                        prefs.ReduceMotion=false;SwitchPage("Timer",false);Save();
                        editorFocused=true;qaPointerOutside=true;hoverStarted=DateTime.UtcNow;collapse.Start();
                    } else if(step==13) {
                        if(isOpen)throw new Exception("Mouse leave did not close while timer input was focused");
                        double leaveMs=(lastCloseStartedUtc-hoverStarted).TotalMilliseconds;
                        if(leaveMs<40||leaveMs>180)throw new Exception("Mouse leave timing out of budget: "+leaveMs+" ms");
                        if(surface.ContainsPoint(new Point(75,85)))throw new Exception("Invisible window area incorrectly captures hover");
                        if(!surface.ContainsPoint(new Point(SceneWidth/2,10)))throw new Exception("Visible compact notch rejects hover");
                        results.Add("PASS: Hover exit starts closing in "+Math.Round(leaveMs)+" ms even with focused timer input; invisible window bounds do not count as hover.");
                        editorFocused=false;qaPointerOutside=false;
                        ReadMediaFixture();SwitchPage("Musik",false);Open();SetQuickBar(true);
                        if(artwork.Source==null||artworkPlaceholder.Visibility!=Visibility.Collapsed)throw new Exception("Media artwork did not decode");
                        results.Add("PASS: Media metadata and encoded artwork render through the normal input pipeline (test fixture).");
                    } else if(step==14) {
                        if(quickBar.Visibility!=Visibility.Visible||quickBar.Opacity<.95||Math.Abs(surface.Depth-ExpandedDepth())>1)throw new Exception("Quick category bar did not pop open with the notch");
                        var artBefore=artwork.Source;ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"available",true},{"title","Beispielvideo · Medientest"},{"artist","Iris Testquelle"},{"album","Eigene Vorschau"},{"source","Iris QA"},{"playing",false},{"position",42.0},{"duration",181.0},{"canSeek",true},{"artChanged",false}}));
                        if(!object.ReferenceEquals(artBefore,artwork.Source))throw new Exception("A duration update incorrectly cleared the current artwork");ReadMediaFixture();
                        Capture("preview-music-cover.png");
                        ((Button)nav.Children[Pages().IndexOf("Timer")]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(page!="Timer")throw new Exception("Quick category button did not select its page");SwitchPage("Musik",false);
                        results.Add("PASS: Quick category bar grows the notch and selects categories through real button events.");
                        var data=new LyricsData {Lines=LyricsService.Parse("[00:00.00]Eigene Testzeile eins\n[00:30.00]Eigene Testzeile zwei\n[01:00.00]Eigene Testzeile drei")};
                        lyricCache[CurrentLyricsKey()]=data;ToggleLyrics();
                    } else if(step==15) {
                        // Software-rendered QA captures can miss presentation frames; wait for the existing spring, never retarget it.
                        if(lyricsVisible&&lyricsData!=null&&lyricIndex==1&&Math.Abs(targetSpan-(OpenSpan+344))<.1&&Math.Abs(surface.Span-(OpenSpan+344))>1&&lyricsSettleWaits++<2)return;
                        if(!lyricsVisible||lyricsData==null||lyricIndex!=1||Math.Abs(surface.Span-(OpenSpan+344))>1)throw new Exception("Synced lyrics did not open/highlight at the media position: visible="+lyricsVisible+", index="+lyricIndex+", span="+surface.Span+", target="+targetSpan);
                        Capture("preview-lyrics.png");
                        ((Button)lyricsLines.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var commandFile=Directory.GetFiles(bridgeDirectory,"*.json").Last();var command=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(commandFile));
                        if(Convert.ToString(command["action"])!="seek"||Math.Abs(Convert.ToDouble(command["seconds"])-60)>.001)throw new Exception("Lyric click did not queue the correct native seek command");
                        results.Add("PASS: Timed lyrics highlight the active line; clicking a line queues a seek to its exact timestamp (test fixture).");
                        ReadMedia("{\"available\":false}");if(artwork.Source!=null||mediaSeek.IsEnabled)throw new Exception("Missing session did not clear artwork and disable seeking");
                        ReadMediaFixture();if(lyricsData==null||lyricIndex!=1)throw new Exception("Restoring the same media session did not restore its lyrics");ReadMedia("{\"available\":false}");
                        ToggleLyrics();SetQuickBar(false);
                    } else if(step==16) {
                        if(quickExpanded||quickBar.Opacity>.01||lyricsVisible)throw new Exception("Quick bar or lyrics remained open");
                        Capture("preview-player-empty.png");results.Add("PASS: Missing media clears the artwork and keeps unavailable actions disabled.");
                        SetQuickBar(true);CloseNotch(true);Open();
                    } else if(step==17) {
                        if(quickExpanded||quickBar.Visibility!=Visibility.Collapsed||pageDots.Opacity<.95)throw new Exception("Closing while the quick bar was visible lost the collapsed page dots");
                        results.Add("PASS: Closing an expanded category bar restores page dots on the next open.");
                        VerifyPlayerAnimations(results);VerifyAppMotion(results);Verify014(results);Verify0141(results);VerifyMixerScroll(results);VerifyMixerEdges(results);VerifyIris(results);VerifyAddons(results);VerifyAddonModules021(results);VerifyActionIcons0211(results);VerifyQuickSettings(results);VerifyCompactTimer(results);VerifyTimerProgress(results);VerifyAppearance(results);VerifyCursorAvoidance(results);VerifyGaming(results);VerifyDesign020(results);VerifyStartup(results);results.Add("ALL UI CHECKS PASSED");File.WriteAllLines(System.IO.Path.Combine(root,"ui-test.txt"),results);qa.Stop();Close();
                    }
                    step++;
                } catch(Exception ex) {Environment.ExitCode=1;results.Add("FAILED: "+ex);File.WriteAllLines(System.IO.Path.Combine(root,"ui-test.txt"),results);qa.Stop();Close();}
            };qa.Start();
        }
        void VerifyPageLayout(List<string> results){
            UpdateLayout();Point anchor=musicDate.TranslatePoint(new Point(),canvas);
            foreach(var selected in Pages()){SwitchPage(selected,false);UpdateLayout();Point current=musicDate.TranslatePoint(new Point(),canvas);if((current-anchor).Length>.01)throw new Exception("Date/time moved when selecting "+selected);if(VisualTreeHelper.GetParent(pageHost)!=canvas||pageStrip.Children.Count!=Pages().Count)throw new Exception("Page strip lost a live view");}
            if(nav.Children.Count!=Pages().Count+1||nav.Children[nav.Children.Count-1]!=quickSettingsButton)throw new Exception("Quick bar settings action is missing or not last");
            if(pageHost.Clip==null||pageHost.Width!=SceneWidth||pageHost.OpacityMask!=tabEdgeMask)throw new Exception("The motion viewport is not the full notch outline");
            SwitchPage("Musik",false);UpdateLayout();
            Point title=trackTitle.TranslatePoint(new Point(),musicPage);if(title.X<30||title.X+trackTitle.ActualWidth>410)throw new Exception("Music text intrudes into arrow gutters");
            if(timerHint.Width+166>416)throw new Exception("Timer text intrudes into arrow gutter");
            SwitchPage("Timer",false);UpdateContentClip();
            if(previousPageButton.Visibility!=Visibility.Visible||nextPageButton.Visibility!=Visibility.Visible)throw new Exception("Navigation arrows missing in expanded notch");
            results.Add("PASS: Date/time stays fixed across every category; live pages use the full notch outline and pass behind the arrows; settings is the final quick bar action and does not add a page.");
            VerifyTabTransition(results);
        }
        void VerifyTabTransition(List<string> results){
            SwitchPage("Musik",false);SwitchPage("Timer",true);double start=tabMotions.First(m=>m.Name=="Timer").Started;
            for(int i=1;i<=6;i++)AdvanceTabs(1.0/60,start+i/60.0);
            var incoming=tabMotions.First(m=>m.Name=="Timer");var outgoing=tabMotions.First(m=>m.Name=="Musik");
            if(incoming.X<300||incoming.X>400||incoming.Alpha!=1||outgoing.Alpha!=1)throw new Exception("The full-width swipe fades a page before it crosses the notch");
            if(outgoing.BlurRadius<1||incoming.View.Effect!=null)throw new Exception("Exit blur is missing or also blurs the incoming page");
            Capture("preview-tab-mid.png");
            CaptureBlurComparison(outgoing);
            double preservedBlur=outgoing.BlurRadius,preservedBlurVelocity=outgoing.BlurVelocity;
            var xs=tabMotions.Select(m=>m.X).ToArray();var velocities=tabMotions.Select(m=>m.Velocity).ToArray();var alphas=tabMotions.Select(m=>m.Alpha).ToArray();
            SwitchPage("Musik",true);for(int i=0;i<tabMotions.Count;i++)if(alphas[i]>0&&(Math.Abs(tabMotions[i].X-xs[i])>.001||Math.Abs(tabMotions[i].Velocity-velocities[i])>.001||Math.Abs(tabMotions[i].Alpha-alphas[i])>.001))throw new Exception("Reversal jumped");
            if(Math.Abs(outgoing.BlurRadius-preservedBlur)>.001||Math.Abs(outgoing.BlurVelocity-preservedBlurVelocity)>.001)throw new Exception("Reversal jumps the current blur");
            start=tabMotions.First(m=>m.Name=="Musik").Started;for(int i=1;i<=85;i++)AdvanceTabs(1.0/60,start+i/60.0);if(!TabsSettled()||!SelectedTabReady())throw new Exception("Reversed swipe did not settle");
            SwitchPage("Musik",false);SwitchPage("Timer",true);SwitchPage("Ablage",true);SwitchPage("Musik",true);if(tabMotions.Any(m=>m.Name!="Musik"&&m.Alpha>0))throw new Exception("Rapid input sends an invisible category through the notch");
            SwitchPage("Musik",false);Cycle(-1);if(page!="Add-ons"||Math.Abs(tabMotions.First(m=>m.Name=="Add-ons").X+TabTravel)>.001||tabMotions.First(m=>m.Name=="Timer").TargetAlpha!=0)throw new Exception("Wrap shows an unrelated category");
            UpdateLayout();if(Math.Abs(shelfAddButton.TranslatePoint(new Point(),pageStrip).X-73)>.01||pageLabelShift.X!=0)throw new Exception("Header shifted");
            SwitchPage("Timer",false);var timer=tabMotions.First(m=>m.Name=="Timer");timer.X=-89;PaintTab(timer);UpdateContentClip();Capture("preview-pass-left.png");
            var leftImage=RenderCanvas();if(!IsRedPixel(leftImage,294,121))throw new Exception("The timer ring is cut off before passing the left arrow");
            timer.X=366;PaintTab(timer);Capture("preview-pass-right.png");var rightImage=RenderCanvas();if(!IsRedPixel(rightImage,746,121))throw new Exception("The timer ring is cut off before passing the right arrow");
            var probe=new Border {Width=SceneWidth,Height=SceneHeight,Background=Brushes.White,OpacityMask=tabEdgeMask,Clip=pageHost.Clip};probe.Measure(new Size(SceneWidth,SceneHeight));probe.Arrange(new Rect(0,0,SceneWidth,SceneHeight));
            var bitmap=new RenderTargetBitmap((int)SceneWidth,(int)SceneHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(probe);byte[] pixels=new byte[(int)(SceneWidth*SceneHeight*4)];bitmap.CopyPixels(pixels,(int)SceneWidth*4,0);
            if(pixels[(80*(int)SceneWidth+295)*4+3]!=255||pixels[(80*(int)SceneWidth+747)*4+3]!=255)throw new Exception("Content still fades at the arrow gutter");
            if(pixels[(80*(int)SceneWidth+276)*4+3]!=0||pixels[(80*(int)SceneWidth+764)*4+3]!=0)throw new Exception("Content escapes the actual notch outline");
            int last=-1;foreach(int x in new int[]{278,280,282,284,286,288,290}){int alpha=pixels[(80*(int)SceneWidth+x)*4+3];if(alpha<=last)throw new Exception("Outer-edge fade is not smooth");last=alpha;}
            SwitchPage("Timer",false);UpdateContentClip();
            if(tabMotions.Any(m=>m.View.Effect!=null||m.BlurRadius!=0))throw new Exception("Stationary content keeps a blur effect");
            results.Add("PASS: Gaussian blur grows only on the outgoing page, survives reversal continuously and is fully removed at rest.");
            results.Add("PASS: Full-width live pages remain visible through the notch and preserve position/velocity on reversal; wrap skips unrelated categories.");
            results.Add("PASS: Actual timer pixels render beyond both arrow positions; only the physical notch edge fades, and no pixels escape its outline.");
        }
        void CaptureBlurComparison(TabMotion outgoing){
            var effect=outgoing.View.Effect;outgoing.View.Effect=null;var before=RenderCanvas();SaveBitmap(before,"preview-without-blur.png");outgoing.View.Effect=effect;var after=RenderCanvas();SaveBitmap(after,"preview-exit-blur.png");
            byte[] a=new byte[(int)(SceneWidth*SceneHeight*4)],b=new byte[a.Length];before.CopyPixels(a,(int)SceneWidth*4,0);after.CopyPixels(b,(int)SceneWidth*4,0);
            int changed=0;
            for(int y=35;y<146;y++)for(int x=291;x<750;x++){int offset=(y*(int)SceneWidth+x)*4;if(a[offset]!=b[offset]||a[offset+1]!=b[offset+1]||a[offset+2]!=b[offset+2])changed++;}
            if(changed<40)throw new Exception("The rendered blur has no visible effect");
        }
        void SaveBitmap(RenderTargetBitmap bitmap,string name){if(!Environment.GetCommandLineArgs().Any(a=>a=="--capture-qa"||a.EndsWith("-demo",StringComparison.Ordinal)))return;var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(System.IO.Path.Combine(root,name)))encoder.Save(file);}
        RenderTargetBitmap RenderCanvas(){InvalidateTree(canvas);canvas.UpdateLayout();var bitmap=new RenderTargetBitmap((int)SceneWidth,(int)SceneHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);return bitmap;}
        bool IsRedPixel(RenderTargetBitmap bitmap,int x,int y){byte[] bytes=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);return bytes[2]>150&&bytes[2]>bytes[1]*2;}
        void Capture(string name) {
            UpdateContentClip();
            InvalidateTree(canvas);
            canvas.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)SceneWidth,(int)SceneHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);
            SaveBitmap(bitmap,name);
        }
        void InvalidateTree(DependencyObject element){var ui=element as UIElement;if(ui!=null)ui.InvalidateVisual();for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)InvalidateTree(VisualTreeHelper.GetChild(element,i));}
        void ReadMediaFixture(){string art;using(var source=File.OpenRead(System.IO.Path.Combine(root,"Iris.ico")))using(var stream=new MemoryStream()){var decoder=BitmapDecoder.Create(source,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoder.Frames.OrderByDescending(f=>f.PixelWidth).First();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));encoder.Save(stream);art=Convert.ToBase64String(stream.ToArray());}ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"available",true},{"title","Beispielvideo · Medientest"},{"artist","Iris Testquelle"},{"album","Eigene Vorschau"},{"source","Iris QA"},{"playing",false},{"position",42.0},{"duration",180.0},{"canSeek",true},{"artChanged",true},{"thumbnail",art}}));}
        void RunTabDemo(){
            prefs.ReduceMotion=false;timerFinished=false;countdown.Reset(300);prefs.Minutes=5;prefs.Seconds=0;minutesBox.Text="05";secondsBox.Text="00";RefreshTimer();ReadMediaFixture();SwitchPage("Musik",false);Open();SetQuickBar(false);
            Directory.CreateDirectory(System.IO.Path.Combine(root,"tab-frames"));
            int frame=0,eventIndex=0;var trace=new List<string>{"frame,time,page,music_x,music_alpha,timer_x,timer_alpha,shelf_x,shelf_alpha"};var demoClock=Stopwatch.StartNew();
            double[] times={1.0,2.1,3.2,4.3,4.40,5.3,6.4};Action[] actions={delegate{Cycle(1);},delegate{Cycle(1);},delegate{Cycle(1);},delegate{Cycle(1);},delegate{Cycle(-1);},delegate{Cycle(-1);},delegate{Cycle(-1);}};
            var recorder=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(15)};
            recorder.Tick+=delegate{
                double time=demoClock.Elapsed.TotalSeconds;while(eventIndex<times.Length&&time>=times[eventIndex]){actions[eventIndex]();eventIndex++;}
                Capture("tab-frames/frame-"+frame.ToString("0000")+".png");var row=frame+","+time.ToString("F4",CultureInfo.InvariantCulture)+","+page;
                foreach(var name in new string[]{"Musik","Timer","Ablage"}){var m=tabMotions.First(t=>t.Name==name);row+=","+m.X.ToString("F3",CultureInfo.InvariantCulture)+","+m.Alpha.ToString("F4",CultureInfo.InvariantCulture);}trace.Add(row);frame++;
                if(time>=7.5){recorder.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"tab-trace.csv"),trace);Close();}
            };recorder.Start();
        }
        void RunMotionDemo() {
            prefs.ReduceMotion=false;prefs.Pinned=false;timerFinished=false;
            countdown.Reset(300);prefs.Minutes=5;prefs.Seconds=0;minutesBox.Text="05";secondsBox.Text="00";SwitchPage("Timer",false);RefreshTimer();
            ReadMediaFixture();SwitchPage("Musik",false);CloseNotch(true);StopMorph();surface.Span=ClosedSpan;surface.Depth=ClosedDepth;UpdateContentClip();
            Directory.CreateDirectory(System.IO.Path.Combine(root,"demo-frames"));
            int frame=0,eventIndex=0;var trace=new List<string>{"frame,time,span,depth,open"};var clock=Stopwatch.StartNew();
            double[] times={.849,1.973,3.866,5.25,6.833,9.0,10.0,10.7,11.4,13.5};
            Action[] actions={delegate{Open();},delegate{CloseNotch(true);},delegate{Open();},delegate{CloseNotch(true);},delegate{Open();},delegate{SetQuickBar(true);},delegate{SwitchPage("Timer",true);},delegate{SwitchPage("Musik",true);},delegate{lyricCache[CurrentLyricsKey()]=new LyricsData {Lines=LyricsService.Parse("[00:00.00]Eigene Testzeile eins\n[00:30.00]Eigene Testzeile zwei\n[01:00.00]Eigene Testzeile drei")};ToggleLyrics();},delegate{CloseNotch(true);}};
            var recorder=new DispatcherTimer(DispatcherPriority.Render) {Interval=TimeSpan.FromMilliseconds(15)};
            recorder.Tick+=delegate{
                double time=clock.Elapsed.TotalSeconds;while(eventIndex<times.Length&&time>=times[eventIndex]){actions[eventIndex]();eventIndex++;}
                Capture("demo-frames/frame-"+frame.ToString("0000")+".png");
                trace.Add(frame+","+time.ToString("F4",CultureInfo.InvariantCulture)+","+surface.Span.ToString("F3",CultureInfo.InvariantCulture)+","+surface.Depth.ToString("F3",CultureInfo.InvariantCulture)+","+(isOpen?"1":"0"));frame++;
                if(time>=15.2){recorder.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"motion-trace.csv"),trace);Close();}
            };recorder.Start();
        }
        void OnClosed(object sender,EventArgs e) {shuttingDown=true;CancelAddonTileDrag();if(shelfNoticeTimer!=null)shelfNoticeTimer.Stop();DisposeDownloads();DisposeAddonActions();Microsoft.Win32.SystemEvents.UserPreferenceChanged-=OnMaterialPreferenceChanged;if(glassBackdrop!=null)glassBackdrop.Dispose();if(gamingPoll!=null)gamingPoll.Stop();if(gamingReader!=null)gamingReader.Dispose();if(avoidPoll!=null)avoidPoll.Stop();CancelCursorAvoidance();if(idleEyesWatch!=null)idleEyesWatch.Stop();Microsoft.Win32.SystemEvents.PowerModeChanged-=OnPowerChanged;foreach(var addonTimer in addonTimers.Values)addonTimer.Stop();if(addonWindow!=null)addonWindow.Close();if(systemVolumeObserver!=null)systemVolumeObserver.Dispose();if(audioPoll!=null)audioPoll.Stop();if(hudDismiss!=null)hudDismiss.Stop();if(audioSessions!=null)audioSessions.Dispose();if(uiMotion!=null)uiMotion.Dispose();if(timerNoticeTimer!=null)timerNoticeTimer.Stop();if(playerTimer!=null)playerTimer.Stop();if(lyricsRequest!=null)lyricsRequest.Cancel();StopMorph();countdown.Update(DateTime.UtcNow);Save();tick.Stop();collapse.Stop();hoverWatch.Stop();blinkTimer.Stop();blinkEnd.Stop();mascotTimer.Stop();finishedTimer.Stop();StopMedia();if(tray!=null){tray.Visible=false;tray.Dispose();}if(settings!=null)settings.Close();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=OnDisplayChanged;}
        [StructLayout(LayoutKind.Sequential)]struct POINT {public int X,Y;}
        [DllImport("user32.dll")]static extern bool GetCursorPos(out POINT point);
        [StructLayout(LayoutKind.Sequential)]struct RECT {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out RECT r);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder b,int n);
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr h,int n);
        [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr h,int n,int v);
        [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    }
}
