using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public sealed class NearNotchDwell {
        bool near;double elapsed,outside;public bool Avoiding{get;private set;}
        public double Elapsed{get{return elapsed;}}
        public void Reset(){near=false;elapsed=outside=0;Avoiding=false;}
        public bool Update(Point point,double dt,double halfWidth,bool eligible,bool buttonDown){
            dt=Math.Max(0,Math.Min(.2,dt));bool horizontal=Math.Abs(point.X)<=halfWidth+28;bool corridor=horizontal&&point.Y>=0&&point.Y<=84;
            if(!eligible){Reset();return false;}
            if(Avoiding){if(!corridor&&!buttonDown)outside+=dt;else outside=0;if(outside>=.22)Reset();return Avoiding;}
            bool inside=horizontal&&point.Y>=0&&point.Y<=52;
            if(!inside||buttonDown){near=false;elapsed=0;return false;}
            if(!near){near=true;elapsed=0;return false;}
            elapsed+=dt;if(elapsed>=.5-1e-9)Avoiding=true;return Avoiding;
        }
    }
    public sealed partial class NotchWindow {
        bool cursorAvoiding,avoidStyleApplied,avoidWasTransparent;double avoidHalfWidth=99;
        readonly NearNotchDwell nearDwell=new NearNotchDwell();readonly Stopwatch avoidClock=Stopwatch.StartNew();DispatcherTimer avoidPoll;
        void SetupCursorAvoidance(){avoidPoll=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(25)};avoidPoll.Tick+=delegate{PollCursorAvoidance();};if(prefs.CursorAvoidance&&!uiTesting)avoidPoll.Start();}
        void SetCursorAvoidance(bool value){prefs.CursorAvoidance=value;CancelCursorAvoidance();if(avoidPoll!=null){if(value&&!uiTesting){avoidClock.Restart();avoidPoll.Start();}else avoidPoll.Stop();}Save();}
        void PollCursorAvoidance(){
            double dt=avoidClock.Elapsed.TotalSeconds;avoidClock.Restart();if(shuttingDown)return;POINT mouse;if(!GetCursorPos(out mouse))return;Point point;try{point=canvas.PointFromScreen(new Point(mouse.X,mouse.Y));}catch{return;}point.X-=SceneWidth/2;
            bool down=(AvoidGetAsyncKeyState(1)&0x8000)!=0||(AvoidGetAsyncKeyState(2)&0x8000)!=0;
            bool eligible=prefs.CursorAvoidance&&!modalOpen&&!timerFinished&&!fileDragActive&&!PlayerDragging()&&settings==null&&addonWindow==null&&Visibility==Visibility.Visible;
            if(!cursorAvoiding&&!isOpen)avoidHalfWidth=Math.Max(ClosedSpan,RestingSpan())/2;
            UpdateCursorDwell(point,dt,eligible,down);
        }
        void UpdateCursorDwell(Point point,double dt,bool eligible,bool down){bool next=nearDwell.Update(point,dt,avoidHalfWidth,eligible&&!isOpen,down);if(next!=cursorAvoiding)ApplyCursorAvoidance(next);}
        bool SetMousePassThrough(bool value){
            var hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return false;int style=GetWindowLong(hwnd,-20);
            if(value){if(!avoidStyleApplied)avoidWasTransparent=(style&0x20)!=0;SetWindowLong(hwnd,-20,style|0x20);avoidStyleApplied=(GetWindowLong(hwnd,-20)&0x20)!=0;return avoidStyleApplied;}
            if(avoidStyleApplied&&!avoidWasTransparent)SetWindowLong(hwnd,-20,style&~0x20);avoidStyleApplied=false;return true;
        }
        void ApplyCursorAvoidance(bool value){
            if(value&&!SetMousePassThrough(true)){nearDwell.Reset();return;}if(!value)SetMousePassThrough(false);cursorAvoiding=value;if(compact==null)return;ShowEyes(false);UpdateCompactLyrics();SyncGlassBackdrop();
        }
        void CancelCursorAvoidance(){nearDwell.Reset();if(cursorAvoiding||avoidStyleApplied)ApplyCursorAvoidance(false);}
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetAsyncKeyState")]static extern short AvoidGetAsyncKeyState(int key);
        void VerifyCursorAvoidance(List<string> results){
            foreach(bool moving in new[]{false,true}){var model=new NearNotchDwell();model.Update(new Point(0,20),.05,99,true,false);for(int i=0;i<9;i++)model.Update(new Point(moving?(i%2==0?-110:110):0,20),.05,99,true,false);if(model.Avoiding)throw new Exception("Dwell triggers before 500 ms");model.Update(new Point(0,20),.05,99,true,false);if(!model.Avoiding)throw new Exception("Dwell must work at any speed including stationary");for(int i=0;i<20;i++)model.Update(new Point(0,16),.05,99,true,true);if(!model.Avoiding)throw new Exception("Browser click restores blocking notch too early");for(int i=0;i<5;i++)model.Update(new Point(220,120),.05,99,true,false);if(model.Avoiding)throw new Exception("Notch does not return after leaving");}
            var reset=new NearNotchDwell();reset.Update(new Point(0,20),.1,99,true,false);reset.Update(new Point(0,20),.05,99,true,false);reset.Update(new Point(0,120),.1,99,true,false);reset.Update(new Point(0,20),.1,99,true,false);for(int i=0;i<9;i++)reset.Update(new Point(0,20),.05,99,true,false);if(reset.Avoiding)throw new Exception("Dwell accumulates across departures");reset.Update(new Point(0,20),.05,99,true,false);if(!reset.Avoiding)throw new Exception("Dwell cannot restart");reset.Update(new Point(0,20),.1,99,false,false);if(reset.Avoiding)throw new Exception("Disabled dwell retains pass through");
            SetReducedMotion(true);prefs.MinimalNotch=false;prefs.CursorAvoidance=true;countdown.Reset(300);RefreshTimer();ReadMedia("{\"available\":false}");volumeHud.Show(false);hudDismiss.Stop();CloseNotch(true);
            var near=new Point(0,20);
            var hwnd=new WindowInteropHelper(this).Handle;
            UpdateCursorDwell(near,.05,true,false);
            for(int i=0;i<9;i++)UpdateCursorDwell(near,.05,true,false);
            Open();
            for(int i=0;i<20;i++)UpdateCursorDwell(near,.05,true,false);
            if(!isOpen||cursorAvoiding||nearDwell.Elapsed!=0||(GetWindowLong(hwnd,-20)&0x20)!=0)
                throw new Exception("An open notch must stay open and receive clicks while the pointer remains nearby");
            CloseNotch(true);
            UpdateCursorDwell(near,.05,true,false);
            for(int i=0;i<9;i++)UpdateCursorDwell(near,.05,true,false);
            if(cursorAvoiding)throw new Exception("Closing the notch must start a new 500 ms dwell");
            UpdateCursorDwell(near,.05,true,false);
            UpdateLayout();
            if(!cursorAvoiding||isOpen||(GetWindowLong(hwnd,-20)&0x20)==0||surface.Span!=MinimalSpan||surface.Depth!=MinimalDepth)throw new Exception("Dwell has no actual layered-window mouse pass-through or mini geometry");CancelCursorAvoidance();if(cursorAvoiding||(GetWindowLong(hwnd,-20)&0x20)!=0||surface.Span!=ClosedSpan)throw new Exception("Avoidance does not restore previous interaction and size");ApplyCursorAvoidance(true);Open();if(cursorAvoiding||!isOpen||(GetWindowLong(hwnd,-20)&0x20)!=0)throw new Exception("Explicit open cannot cancel avoidance");
            ShowSettings();settingsSection="Anzeige";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();if(settingsPages.Current.RenderSize.Height>settingsBody.ActualHeight+.5)throw new Exception("Cursor avoidance settings overflow");settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));SetCursorAvoidance(false);SetReducedMotion(false);AdvanceUi(100);uiMotion.SnapAll();
            double oldOffset=prefs.Offset;int oldMonitor=prefs.Monitor;var screens=System.Windows.Forms.Screen.AllScreens;var fresh=new Preferences();if(fresh.Offset!=0)throw new Exception("Fresh install not centered by default");prefs.Offset=0;prefs.Monitor=Math.Max(0,Array.FindIndex(screens,s=>s.Primary));SetReducedMotion(true);Place();var source=PresentationSource.FromVisual(this);double dpi=source==null?1:source.CompositionTarget.TransformToDevice.M11;var bounds=screens[prefs.Monitor].Bounds;if(Math.Abs((Left+base.Width/2)*dpi-(bounds.Left+bounds.Width/2.0))>1)throw new Exception("Fresh placement not centered on primary screen");if(serializer.Deserialize<Preferences>("{\"Offset\":360}").Offset!=360)throw new Exception("Saved offset is discarded");prefs.Offset=oldOffset;prefs.Monitor=oldMonitor;Place();SetReducedMotion(false);AdvanceUi(100);uiMotion.SnapAll();
            results.Add("PASS: 500 continuous milliseconds near notch trigger for stationary/fast paths while closed; opening resets dwell and keeps the notch interactive, leaving resets dwell, click hold/return/off cases preserve access. Own native WS_EX_TRANSPARENT is set/restored. Paths simulated, no real browser tab clicked.");
            results.Add("PASS: Fresh preferences use zero offset and native placement centers on primary screen; saved existing offset remains unchanged.");
        }
        void RunDwellDemo(){
            uiMotion.PauseAutomatic();qaIdleSeconds=0;SetReducedMotion(true);prefs.MinimalNotch=false;prefs.CursorAvoidance=true;countdown.Reset(300);RefreshTimer();ReadMedia("{\"available\":false}");CloseNotch(true);CancelCursorAvoidance();Open();SetReducedMotion(false);System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root,"dwell-frames"));var trace=new List<string>{"frame,seconds,avoiding,waiting"};var watch=Stopwatch.StartNew();int frame=0;double previous=0;
            var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds,dt=Math.Min(.2,now-previous);previous=now;if(now>=1&&isOpen)CloseNotch(true);UpdateCursorDwell(now<4?new Point(0,18):new Point(220,120),dt,true,false);uiMotion.Advance(Math.Min(.06,dt));CaptureWindow(this,"dwell-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+(cursorAvoiding?1:0)+","+nearDwell.Elapsed.ToString("F4",System.Globalization.CultureInfo.InvariantCulture));frame++;if(now>=5.2){timer.Stop();System.IO.File.WriteAllLines(System.IO.Path.Combine(root,"dwell-trace.csv"),trace);SetCursorAvoidance(false);Close();}};timer.Start();
        }
    }
}
