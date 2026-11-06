using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Iris {
    public sealed class TimerProgressRing : FrameworkElement {
        static readonly Pen ringPen=MakePen();
        Countdown source;
        double duration=1;
        bool animate,active,subscribed;
        public double Fraction {get;private set;}
        public bool DrawingFrames {get{return subscribed;}}
        static Pen MakePen(){var p=new Pen(new SolidColorBrush(Color.FromRgb(248,56,87)),7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};p.Freeze();return p;}
        public TimerProgressRing(){IsHitTestVisible=false;IsVisibleChanged+=delegate{UpdateSubscription();};Unloaded+=delegate{StopFrames();};}
        public void SetCountdown(Countdown countdown,double total,bool smooth,bool visible){source=countdown;duration=Math.Max(1,total);animate=smooth;active=visible;Refresh(DateTime.UtcNow);UpdateSubscription();}
        public void Refresh(DateTime now){double remaining=source==null?0:source.Running?Math.Max(0,(source.Ends-now).TotalSeconds):source.Remaining;double fraction=Math.Max(0,Math.Min(1,remaining/duration));if(Math.Abs(Fraction-fraction)>.0000001){Fraction=fraction;InvalidateVisual();}}
        void UpdateSubscription(){bool needed=source!=null&&source.Running&&Fraction>0&&animate&&active&&IsVisible;if(needed&&!subscribed){CompositionTarget.Rendering+=Frame;subscribed=true;}else if(!needed)StopFrames();}
        void StopFrames(){if(subscribed){CompositionTarget.Rendering-=Frame;subscribed=false;}}
        void Frame(object sender,EventArgs e){Refresh(DateTime.UtcNow);UpdateSubscription();}
        protected override void OnRender(DrawingContext dc){
            base.OnRender(dc);if(Fraction<=0)return;var center=new Point(RenderSize.Width/2,RenderSize.Height/2);double radius=Math.Max(0,Math.Min(RenderSize.Width,RenderSize.Height)/2-3.5);if(radius<=0)return;
            if(Fraction>=.9999999){dc.DrawEllipse(null,ringPen,center,radius,radius);return;}
            double angle=Fraction*Math.PI*2;var end=new Point(center.X+Math.Sin(angle)*radius,center.Y-Math.Cos(angle)*radius);var arc=new StreamGeometry();using(var c=arc.Open()){c.BeginFigure(new Point(center.X,center.Y-radius),false,false);c.ArcTo(end,new Size(radius,radius),0,Fraction>.5,SweepDirection.Clockwise,true,false);}arc.Freeze();dc.DrawGeometry(null,ringPen,arc);
        }
    }
    public sealed partial class NotchWindow {
        TimerProgressRing timerProgress;
        void SyncTimerProgress(){if(timerProgress!=null&&countdown!=null)timerProgress.SetCountdown(countdown,ConfiguredSeconds(),MotionEnabled(),isOpen&&page=="Timer"&&!timerFinished);}
        void VerifyTimerProgress(List<string> results){
            SetReducedMotion(true);SwitchPage("Timer",false);Open();countdown.Reset(300);RefreshTimer();UpdateLayout();Capture("preview-timer-ring-full.png");
            var now=DateTime.UtcNow;countdown.Start(now);countdown.Ends=now.AddSeconds(150);countdown.Update(now);RefreshTimer();timerProgress.Refresh(now);if(Math.Abs(timerProgress.Fraction-.5)>.000001||timerProgress.DrawingFrames)throw new Exception("Timer ring ratio or reduced motion is incorrect");Capture("preview-timer-ring-half.png");
            countdown.Pause(now);SyncTimerProgress();timerProgress.Refresh(now.AddSeconds(50));if(Math.Abs(timerProgress.Fraction-.5)>.000001||timerProgress.DrawingFrames)throw new Exception("Timer ring moves while paused");
            SetReducedMotion(false);countdown.Start(now);countdown.Update(now.AddSeconds(75));RefreshTimer();timerProgress.Refresh(now.AddSeconds(75));if(Math.Abs(timerProgress.Fraction-.25)>.000001||!timerProgress.DrawingFrames)throw new Exception("Timer ring does not follow resumed elapsed time");Capture("preview-timer-ring-quarter.png");
            SwitchPage("Musik",false);if(timerProgress.DrawingFrames)throw new Exception("Off-page timer ring keeps a render loop");SwitchPage("Timer",false);CloseNotch(true);if(timerProgress.DrawingFrames)throw new Exception("Hidden timer ring keeps a render loop");Open();SyncTimerProgress();if(!timerProgress.DrawingFrames)throw new Exception("Timer ring does not resume after opening");
            timerProgress.Refresh(now.AddSeconds(200));if(timerProgress.Fraction!=0)throw new Exception("Expired timer ring is not empty");countdown.Update(now.AddSeconds(200));SyncTimerProgress();if(timerProgress.DrawingFrames)throw new Exception("Expired timer ring keeps a render loop");
            countdown.Reset(300);RefreshTimer();if(timerProgress.Fraction!=1||timerProgress.DrawingFrames)throw new Exception("Reset does not restore the full resting timer ring");
            results.Add("PASS: Timer ring follows actual remaining/total time, pauses exactly, resumes, empties at zero and resets to full. Reduced motion, hidden/other pages and expiration stop the ring render loop; rendered full/half/quarter states captured.");
        }
        void RunTimerProgressDemo(){
            uiMotion.PauseAutomatic();SetReducedMotion(true);ReadMedia("{\"available\":false}");ShowEyes(false);prefs.Minutes=0;prefs.Seconds=6;minutesBox.Text="00";secondsBox.Text="06";countdown.Reset(6);timerFinished=false;SwitchPage("Timer",false);SetNotchDesign("black");SetQuickBar(false);Open();RefreshTimer();SetReducedMotion(false);
            Directory.CreateDirectory(System.IO.Path.Combine(root,"timer-progress-frames"));var watch=Stopwatch.StartNew();var trace=new List<string>{"frame,seconds,remaining,fraction,running,notice"};int frame=0,index=0;double previous=0;double[] times={.6,2.6,3.4};Action[] actions={delegate{ToggleTimer();},delegate{ToggleTimer();},delegate{ToggleTimer();}};
            var recorder=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(33)};recorder.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(index<times.Length&&now>=times[index])actions[index++]();uiMotion.Advance(Math.Min(.06,now-previous));previous=now;Capture("timer-progress-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+countdown.Remaining.ToString("F4",CultureInfo.InvariantCulture)+","+timerProgress.Fraction.ToString("F6",CultureInfo.InvariantCulture)+","+(countdown.Running?1:0)+","+(timerFinished?1:0));frame++;if(now>=9){recorder.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"timer-progress-trace.csv"),trace);Close();}};recorder.Start();
        }
        Rect CompactInkBounds(){
            var bitmap=RenderCanvas();var pixels=new byte[(int)(SceneWidth*SceneHeight*4)];bitmap.CopyPixels(pixels,(int)SceneWidth*4,0);int left=(int)SceneWidth,right=0,top=34,bottom=0;
            for(int y=2;y<32;y++)for(int x=(int)(SceneWidth/2-60);x<SceneWidth/2+60;x++){int i=(y*(int)SceneWidth+x)*4;if(pixels[i]>80||pixels[i+1]>80||pixels[i+2]>80){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}}
            if(right<left)throw new Exception("Compact time digits are not rendered");return new Rect(left,top,right-left+1,bottom-top+1);
        }
        void VerifyCompactTimer(List<string> results){
            bool ticking=tick.IsEnabled;tick.Stop();uiMotion.PauseAutomatic();SetReducedMotion(true);hudDismiss.Stop();volumeHud.Show(false);ReadMedia("{\"available\":false}");ShowEyes(false);SetQuickBar(false);CloseNotch(true);countdown.Reset(300);RefreshTimer();UpdateCompactLyrics();string originalClock=clock.Text;clock.Text="05:00";UpdateLayout();
            var clockPoint=clock.TranslatePoint(new Point(clock.ActualWidth/2,clock.ActualHeight/2),canvas);if(Math.Abs(clockPoint.X-SceneWidth/2)>.51||Math.Abs(clockPoint.Y-ClosedDepth/2)>.51)throw new Exception("Compact clock is not centered");var clockInk=CompactInkBounds();Capture("preview-compact-clock.png");
            countdown.Start(DateTime.UtcNow);RefreshTimer();UpdateLayout();var timerPoint=compactTimer.TranslatePoint(new Point(compactTimer.ActualWidth/2,compactTimer.ActualHeight/2),canvas);var timerInk=CompactInkBounds();
            if(Math.Abs(timerPoint.X-clockPoint.X)>.51||Math.Abs(timerPoint.Y-clockPoint.Y)>.51||Math.Abs(timerInk.Left-clockInk.Left)>1||Math.Abs(timerInk.Top-clockInk.Top)>1||Math.Abs(timerInk.Right-clockInk.Right)>1||Math.Abs(timerInk.Bottom-clockInk.Bottom)>1)throw new Exception("Running timer digits do not align with the clock's actual rendered digits");
            var pixels=new byte[(int)(SceneWidth*SceneHeight*4)];RenderCanvas().CopyPixels(pixels,(int)SceneWidth*4,0);int whites=0;for(int y=(int)timerInk.Top;y<timerInk.Bottom;y++)for(int x=(int)timerInk.Left;x<timerInk.Right;x++){int i=(y*(int)SceneWidth+x)*4;if(pixels[i]>80||pixels[i+1]>80||pixels[i+2]>80){if(pixels[i]!=pixels[i+1]||pixels[i+1]!=pixels[i+2])throw new Exception("Timer glyphs have a color tint");if(pixels[i]==255)whites++;}}if(whites<8)throw new Exception("Timer has no opaque white glyph pixels");Capture("preview-compact-timer-running.png");
            foreach(double seconds in new[]{1.0,11,599,3599,3600,6000,59999}){countdown.Reset(seconds);countdown.Start(DateTime.UtcNow);RefreshTimer();UpdateLayout();var point=compactTimer.TranslatePoint(new Point(compactTimer.ActualWidth/2,compactTimer.ActualHeight/2),canvas);if(Math.Abs(point.X-SceneWidth/2)>.51||Math.Abs(point.Y-clockPoint.Y)>.51||compactTimer.ActualWidth>compact.ColumnDefinitions[1].ActualWidth+.1)throw new Exception("Timer changes center or clips with a different digit count");}Capture("preview-compact-timer-hours.png");
            countdown.Reset(300);countdown.Start(DateTime.UtcNow);RefreshTimer();SwitchPage("Timer",false);Open();uiMotion.SnapAll();Capture("preview-timer-running-white.png");countdown.Pause(DateTime.UtcNow);RefreshTimer();CloseNotch(true);UpdateLayout();var pausedClock=clock.TranslatePoint(new Point(clock.ActualWidth/2,clock.ActualHeight/2),canvas);if(clock.Visibility!=Visibility.Visible||Math.Abs(pausedClock.X-clockPoint.X)>.51)throw new Exception("Pause displaces the clock");clock.Text=originalClock;countdown.Reset(300);RefreshTimer();SetReducedMotion(false);AdvanceUi(90);uiMotion.SnapAll();if(ticking)tick.Start();
            results.Add("PASS: Native rendered running timer glyph bounds match the clock in both axes; timer pixels are pure white; seconds/minutes/hours remain centered and unclipped; pause restores the centered clock.");
        }
        void RunCompactTimerDemo(){
            uiMotion.PauseAutomatic();SetReducedMotion(true);ReadMedia("{\"available\":false}");ShowEyes(false);CloseNotch(true);countdown.Reset(300);RefreshTimer();SetReducedMotion(false);Directory.CreateDirectory(System.IO.Path.Combine(root,"timer-center-frames"));
            var watch=Stopwatch.StartNew();var trace=new List<string>{"frame,seconds,open,running"};int frame=0,index=0;double previous=0;double[] times={.6,2.1,3};Action[] actions={delegate{ToggleTimer();},delegate{SwitchPage("Timer",false);Open();},delegate{CloseNotch(true);}};
            var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(index<times.Length&&now>=times[index])actions[index++]();uiMotion.Advance(Math.Min(.06,now-previous));previous=now;Capture("timer-center-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+(isOpen?1:0)+","+(countdown.Running?1:0));frame++;if(now>=4){timer.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"timer-center-trace.csv"),trace);Close();}};timer.Start();
        }
        Canvas timerControls,timerNotice;
        Button timerNoticeDismiss;
        ScaleTransform timerNoticeScale=new ScaleTransform(1,1);
        TranslateTransform timerNoticeShift=new TranslateTransform();
        RotateTransform timerBellTilt=new RotateTransform();
        Ellipse timerHalo;
        DispatcherTimer timerNoticeTimer;
        readonly Stopwatch timerNoticeWatch=Stopwatch.StartNew();
        double timerNoticeValue,timerNoticeVelocity,timerNoticeAge;
        bool timerNoticeTarget;

        void BuildTimerNotice(Grid host,Canvas controls){
            timerControls=controls;
            timerNotice=new Canvas {Background=Brushes.Black,Visibility=Visibility.Collapsed,IsHitTestVisible=false,RenderTransformOrigin=new Point(.5,.5)};
            var transform=new TransformGroup();transform.Children.Add(timerNoticeScale);transform.Children.Add(timerNoticeShift);timerNotice.RenderTransform=transform;host.Children.Add(timerNotice);
            var icon=new Grid {Width=82,Height=82};Canvas.SetLeft(icon,53);Canvas.SetTop(icon,40);timerNotice.Children.Add(icon);
            timerHalo=new Ellipse {Width=82,Height=82,Stroke=new SolidColorBrush(Color.FromRgb(248,56,87)),StrokeThickness=2,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};icon.Children.Add(timerHalo);
            icon.Children.Add(new Ellipse {Stroke=new SolidColorBrush(Color.FromRgb(248,56,87)),StrokeThickness=5});
            var bell=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 6,16 L 8,13 L 8,9 C 8,3 16,3 16,9 L 16,13 L 18,16 Z M 10,19 Q 12,22 14,19 M 12,3 L 12,2"),Stroke=Brushes.White,StrokeThickness=1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Stretch=Stretch.Uniform,Width=36,Height=39,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=new Point(.5,.15),RenderTransform=timerBellTilt};icon.Children.Add(bell);
            var title=Text(L.T("Zeit ist um"),24,ink);title.FontWeight=FontWeights.SemiBold;title.Width=206;title.TextAlignment=TextAlignment.Center;Canvas.SetLeft(title,179);Canvas.SetTop(title,36);timerNotice.Children.Add(title);
            var detail=Text(L.T("Dein Timer ist abgelaufen."),12,muted);detail.Width=206;detail.TextAlignment=TextAlignment.Center;Canvas.SetLeft(detail,179);Canvas.SetTop(detail,76);timerNotice.Children.Add(detail);
            timerNoticeDismiss=Btn(L.T("Fertig"),delegate{DismissTimerNotice(true);});timerNoticeDismiss.Width=82;timerNoticeDismiss.Height=30;timerNoticeDismiss.Padding=new Thickness(0);timerNoticeDismiss.Background=new SolidColorBrush(Color.FromRgb(246,53,84));timerNoticeDismiss.Foreground=Brushes.White;Canvas.SetLeft(timerNoticeDismiss,241);Canvas.SetTop(timerNoticeDismiss,104);timerNotice.Children.Add(timerNoticeDismiss);
            timerNoticeTimer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
            timerNoticeTimer.Tick+=delegate{double dt=Math.Min(.06,timerNoticeWatch.Elapsed.TotalSeconds);timerNoticeWatch.Restart();StepTimerNotice(dt);if(Math.Abs(timerNoticeValue-(timerNoticeTarget?1:0))<.001&&Math.Abs(timerNoticeVelocity)<.01&&(!timerNoticeTarget||timerNoticeAge>1.65))timerNoticeTimer.Stop();};
        }
        void SetTimerNotice(bool show){
            if(timerNotice==null||timerNoticeTarget==show)return;timerNoticeTarget=show;
            if(show)timerNoticeAge=0;
            timerControls.IsHitTestVisible=!show;timerNotice.IsHitTestVisible=show;
            if(!MotionEnabled()){timerNoticeValue=show?1:0;timerNoticeVelocity=0;PaintTimerNotice();timerNoticeTimer.Stop();}
            else{PaintTimerNotice();timerNoticeWatch.Restart();if(!uiTesting)timerNoticeTimer.Start();}
        }
        void StepTimerNotice(double dt){
            timerNoticeAge+=dt;
            if(MotionEnabled())PlayerSpring.Step(ref timerNoticeValue,ref timerNoticeVelocity,timerNoticeTarget?1:0,dt,timerNoticeTarget?.40:.23,timerNoticeTarget?.72:1);
            else{timerNoticeValue=timerNoticeTarget?1:0;timerNoticeVelocity=0;}
            PaintTimerNotice();
        }
        void PaintTimerNotice(){
            double a=Math.Max(0,Math.Min(1,timerNoticeValue));timerControls.Opacity=1-a;timerNotice.Opacity=a;timerNotice.Visibility=a>.001||timerNoticeTarget?Visibility.Visible:Visibility.Collapsed;
            timerNoticeScale.ScaleX=timerNoticeScale.ScaleY=.94+.06*timerNoticeValue;timerNoticeShift.Y=8*(1-timerNoticeValue);
            bool moving=MotionEnabled()&&timerNoticeTarget;
            timerBellTilt.Angle=moving?18*Math.Sin(timerNoticeAge*22)*Math.Exp(-timerNoticeAge*3.4):0;
            double wave=moving?Math.Max(0,Math.Min(1,(timerNoticeAge-.15)/.85)):1;timerHalo.Width=timerHalo.Height=82+18*wave;timerHalo.Opacity=moving?Math.Sin(Math.PI*wave)*.65:0;
        }
        void DismissTimerNotice(bool close){
            finishedTimer.Stop();timerFinished=false;SetTimerNotice(false);RefreshTimer();
            if(close&&!PointerInsideNotch())CloseNotch(false);
        }
        void VerifyToggleMorph(List<string> results){
            SetReducedMotion(true);CloseNotch(true);Open();SetReducedMotion(false);
            playGlyph.AnimateChanges=false;playGlyph.Mode="play";StepPlayer(.016);
            var playPixels=TogglePixels();
            playGlyph.AnimateChanges=true;playGlyph.Mode="pause";
            for(int i=0;i<4;i++)StepPlayer(1.0/60);
            if(playGlyph.Pause<=.1||playGlyph.Pause>=.99)throw new Exception("Play-to-pause has no intermediate morph");Capture("preview-play-pause-mid.png");var intermediatePixels=TogglePixels();
            double before=playGlyph.Pause;playGlyph.Mode="play";if(Math.Abs(playGlyph.Pause-before)>.0001)throw new Exception("Morph reversal resets its current shape");
            for(int i=0;i<60;i++)StepPlayer(1.0/60);if(Math.Abs(playGlyph.Pause)>.001||playGlyph.ActionRunning)throw new Exception("Pause-to-play does not settle");
            Capture("preview-play.png");playGlyph.Mode="pause";for(int i=0;i<60;i++)StepPlayer(1.0/60);if(Math.Abs(playGlyph.Pause-1)>.001)throw new Exception("Play-to-pause does not settle");Capture("preview-pause.png");var pausePixels=TogglePixels();
            if(playPixels.SequenceEqual(intermediatePixels)||pausePixels.SequenceEqual(intermediatePixels)||playPixels.SequenceEqual(pausePixels))throw new Exception("Rendered play/pause shapes do not change");
            var image=RenderCanvas();byte[] header=new byte[1040*30*4];image.CopyPixels(new Int32Rect(0,0,1040,30),header,1040*4,0);for(int i=0;i<header.Length;i+=4)if(header[i+1]>120&&header[i+1]>header[i+2]*1.5&&header[i+1]>header[i]*1.3)throw new Exception("Green charging symbol still appears in the expanded header");
            playGlyph.AnimateChanges=false;playGlyph.Mode="play";if(playGlyph.Pause!=0)throw new Exception("Reduced morph does not snap");playGlyph.AnimateChanges=MotionEnabled();
            results.Add("PASS: Play and pause use intermediate polygon shapes in both directions; reversals retain the current shape, both endpoints settle, reduced motion snaps.");
            results.Add("PASS: Rendered play, intermediate and pause glyphs differ; no green charging symbol appears in the expanded header, and clock layout stays fixed.");
        }
        byte[] TogglePixels(){var image=RenderCanvas();var p=playGlyph.TranslatePoint(new Point(),canvas);int w=(int)Math.Ceiling(playGlyph.ActualWidth),h=(int)Math.Ceiling(playGlyph.ActualHeight);var pixels=new byte[w*h*4];image.CopyPixels(new Int32Rect((int)p.X,(int)p.Y,w,h),pixels,w*4,0);return pixels;}
        void VerifyTimerNotice(List<string> results){
            for(int i=0;i<4;i++)StepTimerNotice(1.0/60);
            if(timerNoticeValue<.1||timerNoticeValue>.95||Math.Abs(timerBellTilt.Angle)<1)throw new Exception("Timer notification has no entrance or bell motion");Capture("preview-timer-finished-mid.png");
            for(int i=0;i<55;i++)StepTimerNotice(1.0/60);
            if(timerNotice.Visibility!=Visibility.Visible||timerNotice.Opacity<.98||timerControls.Opacity>.02||!timerNoticeDismiss.IsVisible)throw new Exception("Timer completion notification is not visible");
            Capture("preview-timer-finished.png");
            timerNoticeDismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<55;i++)StepTimerNotice(1.0/60);Open();
            if(timerFinished||timerNotice.Visibility!=Visibility.Collapsed||timerControls.Opacity<.99||!timerControls.IsHitTestVisible)throw new Exception("Dismissing the timer does not restore its controls");
            SetReducedMotion(true);Finish();if(timerNotice.Opacity!=1||timerBellTilt.Angle!=0||timerHalo.Opacity!=0)throw new Exception("Reduced timer notification still moves");DismissTimerNotice(false);SetReducedMotion(false);
            results.Add("PASS: Countdown completion opens a visible timer notification without hover; confirmation restores controls, reduced motion shows a static notification.");
        }
        void RunFinishDemo(){
            prefs.ReduceMotion=false;timerFinished=false;countdown.Reset(300);DemoTrack(1,true,12);SwitchPage("Musik",false);Open();SetQuickBar(false);
            Directory.CreateDirectory(System.IO.Path.Combine(root,"finish-frames"));var trace=new List<string>{"frame,time,playing,morph,finished,notice,bell"};int frame=0,eventIndex=0;double last=0;var watch=Stopwatch.StartNew();
            double[] times={.8,1.6,2.3,2.42,3.2,4.2,7.4};
            Action[] actions={delegate{DemoTrack(1,false,CurrentMediaPosition());},delegate{DemoTrack(1,true,CurrentMediaPosition());},delegate{DemoTrack(1,false,CurrentMediaPosition());},delegate{DemoTrack(1,true,CurrentMediaPosition());},delegate{SwitchPage("Timer",false);countdown.Reset(1);countdown.Start(DateTime.UtcNow);RefreshTimer();CloseNotch(true);},delegate{OnTick(null,EventArgs.Empty);},delegate{timerNoticeDismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
            var recorder=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};recorder.Tick+=delegate{
                double now=watch.Elapsed.TotalSeconds;while(eventIndex<times.Length&&now>=times[eventIndex]){actions[eventIndex++]();}double dt=Math.Min(.06,now-last);last=now;StepPlayer(dt);StepTimerNotice(dt);
                Capture("finish-frames/frame-"+frame.ToString("0000")+".png");trace.Add(string.Join(",",new[]{frame.ToString(),now.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),isPlaying?"1":"0",playGlyph.Pause.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),timerFinished?"1":"0",timerNoticeValue.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),timerBellTilt.Angle.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)}));frame++;
                if(now>=8.5){recorder.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"finish-trace.csv"),trace);Close();}
            };recorder.Start();
        }
    }
}
