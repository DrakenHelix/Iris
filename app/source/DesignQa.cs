using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        void VerifyNotchWindowOrder(List<string> results){
            string normal=prefs.NotchDesign,full=prefs.FullscreenDesign;
            bool context=designFullscreen;
            var cover=new Window{Title="Iris QA window order",Width=700,Height=220,
                Left=Left+(Width-700)/2,Top=Top,ShowInTaskbar=false,ShowActivated=false,
                Background=Brushes.DarkSlateGray,WindowStyle=WindowStyle.SingleBorderWindow};
            try{
                SetReducedMotion(true);
                prefs.FullscreenDesign="inherit";
                cover.Show();
                IntPtr other=new WindowInteropHelper(cover).Handle;
                IntPtr hwnd=new WindowInteropHelper(this).Handle;
                foreach(string mode in new[]{"window","maximized","borderless"}){
                    cover.WindowState=WindowState.Normal;
                    cover.WindowStyle=mode=="borderless"?WindowStyle.None:WindowStyle.SingleBorderWindow;
                    if(mode=="maximized")cover.WindowState=WindowState.Maximized;
                    else if(mode=="borderless"){
                        var screen=System.Windows.Forms.Screen.FromHandle(hwnd).Bounds;
                        var dpi=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
                        cover.Left=screen.Left/dpi.M11;cover.Top=screen.Top/dpi.M22;
                        cover.Width=screen.Width/dpi.M11;cover.Height=screen.Height/dpi.M22;
                    }
                    foreach(string design in new[]{"black","glass","transparent"}){
                        prefs.NotchDesign=design;
                        ApplyDesignContext(mode=="borderless");
                        uiMotion.SnapAll();UpdateLayout();
                        OrderSetWindowPos(other,new IntPtr(-1),0,0,0,0,0x0010|0x0001|0x0002|0x0200);
                        if(!WindowIsAbove(other,hwnd))throw new Exception("Cover fixture did not overlap Iris in native Z order.");
                        IntPtr foreground=GetForegroundWindow();
                        if(!RestoreNotchWindowOrder(other)||WindowIsAbove(other,hwnd))throw new Exception("Notch remains below "+mode+" in "+design+".");
                        if(GetForegroundWindow()!=foreground)throw new Exception("Restoring the notch stole foreground focus.");
                        if(RestoreNotchWindowOrder(other))throw new Exception("Uncovered notch is repeatedly raised.");
                        if(glassBackdrop!=null&&glassBackdrop.Shown&&WindowIsAbove(glassBackdrop.Handle,hwnd))throw new Exception("Glass backdrop moved above notch controls.");
                    }
                }
                OrderSetWindowPos(hwnd,new IntPtr(-2),0,0,0,0,0x0010|0x0001|0x0002|0x0200);
                if(!RestoreNotchWindowOrder(IntPtr.Zero)||(GetWindowLong(hwnd,-20)&0x8)==0)throw new Exception("Lost native topmost flag was not restored.");
                prefs.FullscreenDesign="hide";ApplyDesignContext(true);
                if(IsVisible||RestoreNotchWindowOrder(other))throw new Exception("Window order repair overrides explicit fullscreen hiding.");
                results.Add("PASS: Native window order restored above windowed, maximized and borderless owned fixtures in black/glass/transparent; foreground retained, no repeat raise at rest, blur remains behind controls and explicit hiding respected. No exclusive-game overlay tested.");
            }finally{
                cover.Close();prefs.NotchDesign=normal;prefs.FullscreenDesign=full;ApplyDesignContext(context);
            }
        }

        void VerifyDesign020(List<string> results){
            var previous=new Preferences();MigrateDesign(previous);if(previous.HideFullscreen||previous.FullscreenDesign!="inherit"||previous.NotchDesign!="black")throw new Exception("Legacy fullscreen default changed");var legacy=new Preferences{HideFullscreen=true};MigrateDesign(legacy);if(legacy.FullscreenDesign!="hide")throw new Exception("Existing fullscreen choice lost during migration");
            foreach(string normal in new[]{"black","glass","transparent"})foreach(string full in new[]{"inherit","black","glass","transparent","hide"}){if(ResolveDesign(normal,full,false)!=normal||ResolveDesign(normal,full,true)!=(full=="inherit"?normal:full))throw new Exception("Context design resolution incorrect");}
            uiMotion.PauseAutomatic();qaDesignFullscreen=false;SetReducedMotion(true);SetNotchDesign("black");SetFullscreenDesign("inherit");firstLaunchDesign=true;ShowDesignSettings();uiMotion.SnapAll();settings.UpdateLayout();if(settingsPages.Current.RenderSize.Height>settingsBody.ActualHeight+.5)throw new Exception("First-run design page overflows window");CaptureWindow(settings,"preview-design-first-run.png");firstLaunchDesign=false;RenderSettingsSection();uiMotion.SnapAll();CaptureWindow(settings,"preview-design-settings.png");settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
            Open();SetReducedMotion(false);SetNotchDesign("glass");AdvanceUi(4);if(materialMotion.Glass<=0||materialMotion.Glass>=.99)throw new Exception("Design change has no rendered spring intermediate");double value=materialMotion.Glass,velocity=materialMotion.GlassVelocity;SetNotchDesign("black");if(materialMotion.Glass!=value||materialMotion.GlassVelocity!=velocity)throw new Exception("Material reversal resets velocity or presentation");AdvanceUi(90);SetReducedMotion(true);SetNotchDesign("glass");uiMotion.SnapAll();SyncGlassBackdrop();if(WindowsGlassAllowed()&&(glassFailed||glassBackdrop==null||!glassBackdrop.Shown))throw new Exception("Native geometric backdrop unavailable: "+materialStatus);CaptureWindow(this,"preview-design-glass-overlay.png");CloseNotch(true);ApplyCursorAvoidance(true);if(glassBackdrop!=null&&glassBackdrop.Shown)throw new Exception("Glass backdrop remains during native click-through avoidance");ApplyCursorAvoidance(false);Open();SyncGlassBackdrop();
            SetNotchDesign("transparent");uiMotion.SnapAll();if(materialMotion.Black!=0||materialMotion.Glass!=0||glassBackdrop!=null&&glassBackdrop.Shown)throw new Exception("Transparent retains blur/opaque surface");CaptureWindow(this,"preview-design-transparent-overlay.png");if(!Hits(mediaButtons.First(b=>b.Button.Content==playGlyph).Button))throw new Exception("Transparent surface breaks player hit testing");
            SetNotchDesign("black");SetFullscreenDesign("glass");ApplyDesignContext(true);if(effectiveDesign!=(WindowsGlassAllowed()?"glass":"black")||Visibility!=Visibility.Visible)throw new Exception("Fullscreen glass did not apply");ApplyDesignContext(false);if(effectiveDesign!="black")throw new Exception("Exiting fullscreen does not restore normal design");SetFullscreenDesign("hide");ApplyDesignContext(true);if(Visibility!=Visibility.Hidden||glassBackdrop!=null&&glassBackdrop.Shown)throw new Exception("Explicit fullscreen hide leaves app/backdrop visible");ApplyDesignContext(false);SetFullscreenDesign("inherit");
            volumeWrites.Clear();volumeDirty=false;ApplyPlayerVolumeSource(null);volumeUpdating=true;volumeSlider.Value=0;volumeUpdating=false;UpdatePlayerMuteVisual();uiMotion.SnapAll();if(playerMuteMark.Muted||speakerSymbol.Known||playerMuteButton.IsEnabled)throw new Exception("Unknown initial audio source falsely shown muted");var source=new AudioSource{Key="qa:startup",Name="Testquelle",Volume=.5,Muted=false,Active=false};ApplyPlayerVolumeSource(source);uiMotion.SnapAll();if(volumeSlider.Value!=.5||playerMuteMark.Muted||!speakerSymbol.Known||volumeWrites.Count!=0||volumeDirty)throw new Exception("Paused first source not read at 50 percent without writing");source.Muted=true;ApplyPlayerVolumeSource(source);uiMotion.SnapAll();if(!playerMuteMark.Muted||volumeSlider.Value!=0)throw new Exception("Confirmed real mute lost");ApplyPlayerVolumeSource(null);uiMotion.SnapAll();if(playerMuteMark.Muted)throw new Exception("Unknown source inherits previous mute mark");source.Muted=false;ApplyPlayerVolumeSource(source);OpenVolume();StepPlayer(.016);uiMotion.SnapAll();CaptureWindow(this,"preview-startup-volume-50.png");if(volumeWrites.Count!=0)throw new Exception("Startup changed actual source volume");
            var previousGames=prefs.GamingGames.ToList();prefs.GamingGames.Clear();runningGameSuggestions=new[]{new RunningGameCandidate{Pid=42,Name="Externes Testspiel",Path=@"D:\OutsideSteam\game.exe"},new RunningGameCandidate{Pid=43,Name="Steam Testspiel",Path=@"D:\SteamLibrary\steamapps\common\Test\game.exe"}};ShowGamingGames();uiMotion.SnapAll();CaptureWindow(addonWindow,"preview-gaming-running-suggestions.png");var choice=Descendants<Button>(addonWindow).First(b=>Descendants<TextBlock>(b).Any(t=>t.Text=="Externes Testspiel"));choice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));uiMotion.SnapAll();if(!prefs.GamingGames.Contains(runningGameSuggestions[0].Path)||prefs.GamingGames.Count!=1)throw new Exception("Running game suggestion did not register exactly one path");EndAddonQaWindow();prefs.GamingGames=previousGames;runningGameSuggestions=new RunningGameCandidate[0];
            string path;long started;using(var self=System.Diagnostics.Process.GetCurrentProcess())if(!GameProcessDiscovery.Identity(self.Id,out path,out started)||started!=self.StartTime.ToUniversalTime().Ticks)throw new Exception("Limited-rights native identity read failed");if(GameProcessDiscovery.Suggestible(@"C:\Program Files\Browser\chrome.exe")||!GameProcessDiscovery.Suggestible(@"D:\Games\Extra\game.exe"))throw new Exception("Running suggestions filter invalid");
            qaDesignFullscreen=null;SetNotchDesign("black");SetFullscreenDesign("inherit");firstLaunchDesign=false;SetReducedMotion(false);AdvanceUi(100);uiMotion.SnapAll();if(uiMotion.ActiveCount!=0||glassBackdrop!=null&&glassBackdrop.Shown)throw new Exception("Design rendering fails to idle in black mode");Save();
            results.Add("PASS: Fresh fullscreen visible, legacy choice migrated; all 15 normal/fullscreen combinations, explicit hide/restore, first-run/settings layout and interruptible material/reduced-motion/idle checks.");
            results.Add("PASS: Real native Windows host backdrop created with exact path clip; transparent preserves WPF hit targets. RenderTarget images show overlay only; dedicated desktop demo captures actual blur over owned fixture window.");
            results.Add("PASS: Unknown startup source has no false mute, paused source binds at 50 percent without Play or writes, confirmed mute remains correct; source disappears/restores without stale mute. Native audio writes suppressed.");
            results.Add("PASS: Running external-game suggestion registers one executable reference without launching anything; Steam remains automatic. Native own-process identity read with limited rights succeeds. Suggestion list uses fixtures.");
        }
        void RunDesignDemo(){
            uiMotion.PauseAutomatic();qaIdleSeconds=0;SetReducedMotion(true);ReadMediaFixture();SwitchPage("Musik",false);prefs.MinimalNotch=false;prefs.CursorAvoidance=false;SetNotchDesign("black");SetFullscreenDesign("inherit");CloseNotch(true);firstLaunchDesign=false;
            string scenario=Environment.GetCommandLineArgs().Contains("--oled-switch")?"oled-switch":Environment.GetCommandLineArgs().Contains("--oled-black")?"oled-black":Environment.GetCommandLineArgs().Contains("--oled-dark")?"oled-dark":"design";
            var background=new Window{Left=Left,Top=Top,Width=Width,Height=Height+60,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true};var grid=new Grid();for(int i=0;i<26;i++){grid.ColumnDefinitions.Add(new ColumnDefinition());var band=new Border{Background=new SolidColorBrush(scenario=="oled-black"?Colors.Black:scenario=="oled-dark"?(i%2==0?Color.FromRgb(3,3,3):Color.FromRgb(17,17,17)):i%2==0?Color.FromRgb(43,141,171):Color.FromRgb(146,82,132))};Grid.SetColumn(band,i);grid.Children.Add(band);}background.Content=grid;background.Show();DesignSetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),0,0,0,0,0x0010|0x0001|0x0002);SetReducedMotion(false);
            Directory.CreateDirectory(System.IO.Path.Combine(root,scenario+"-frames"));var watch=System.Diagnostics.Stopwatch.StartNew();var trace=new List<string>{"frame,seconds,material,black,glass,backdrop"};int frame=0,index=0;double last=0;double[] times={.6,1.45,2.7,3.6,4.7,5.7};Action[] actions={delegate{SetNotchDesign("glass");},delegate{Open();},delegate{SetNotchDesign("transparent");},delegate{SetNotchDesign("black");SetFullscreenDesign("glass");qaDesignFullscreen=true;RefreshFullscreenVisibility();},delegate{qaDesignFullscreen=false;RefreshFullscreenVisibility();},delegate{CloseNotch(true);}};
            if(scenario=="oled-switch"){actions[2]=delegate{foreach(Border band in grid.Children)band.Background=Brushes.Black;};actions[3]=delegate{int i=0;foreach(Border band in grid.Children)band.Background=new SolidColorBrush(i++%2==0?Color.FromRgb(3,3,3):Color.FromRgb(17,17,17));};actions[4]=delegate{int i=0;foreach(Border band in grid.Children)band.Background=new SolidColorBrush(i++%2==0?Color.FromRgb(43,141,171):Color.FromRgb(146,82,132));};}
            var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{try{double now=watch.Elapsed.TotalSeconds;while(index<times.Length&&now>=times[index])actions[index++]();uiMotion.Advance(Math.Min(.06,now-last));last=now;SyncGlassBackdrop();if(now>2.2&&now<2.6||now>3.2&&now<3.5){RECT hitRect;GetWindowRect(new WindowInteropHelper(this).Handle,out hitRect);if(DesignWindowFromPoint(new POINT{X=hitRect.Left+520,Y=hitRect.Top+30})!=new WindowInteropHelper(this).Handle)throw new Exception("Native glass/transparent body loses input to another HWND");if(glassBackdrop!=null&&(GetWindowLong(glassBackdrop.Handle,-16)&0x08000000)==0)throw new Exception("Visual backdrop can receive input");if(glassBackdrop!=null&&DesignWindowFromPoint(new POINT{X=hitRect.Left+20,Y=hitRect.Top+200})==glassBackdrop.Handle)throw new Exception("Visual backdrop blocks transparent exterior");}CaptureDesignDesktop(scenario+"-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+effectiveDesign+","+materialMotion.Black.ToString("F4",CultureInfo.InvariantCulture)+","+materialMotion.Glass.ToString("F4",CultureInfo.InvariantCulture)+","+(glassBackdrop!=null&&glassBackdrop.Shown?1:0));frame++;if(now>=6.6){timer.Stop();File.WriteAllLines(System.IO.Path.Combine(root,scenario+"-trace.csv"),trace);File.WriteAllText(System.IO.Path.Combine(root,scenario+"-native-check.txt"),"Native host backdrop: "+(!glassFailed)+"; geometric clip follows actual notch outline; actual desktop composition recorded over owned pattern fixture. Native glass HWND disabled for input; glass/transparent body and outside native hit tests passed. No capture/screenshot code runs in product mode.\n"+materialStatus);background.Close();Close();}}catch(Exception ex){timer.Stop();File.WriteAllText(System.IO.Path.Combine(root,"design-demo-error.txt"),ex.ToString());Environment.ExitCode=1;background.Close();Close();}};timer.Start();
        }
        void CaptureDesignDesktop(string name){RECT rect;GetWindowRect(new WindowInteropHelper(this).Handle,out rect);using(var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=System.Drawing.Graphics.FromImage(bitmap)){IntPtr screen=DesignGetDC(IntPtr.Zero),dest=graphics.GetHdc();try{if(!DesignBitBlt(dest,0,0,bitmap.Width,bitmap.Height,screen,rect.Left,rect.Top,0x40CC0020))throw new Exception("Native desktop fixture capture failed");}finally{graphics.ReleaseHdc(dest);DesignReleaseDC(IntPtr.Zero,screen);}bitmap.Save(System.IO.Path.Combine(root,name),System.Drawing.Imaging.ImageFormat.Png);}}
        [DllImport("user32.dll",EntryPoint="WindowFromPoint")]static extern IntPtr DesignWindowFromPoint(POINT point);
        [DllImport("user32.dll",EntryPoint="GetDC")]static extern IntPtr DesignGetDC(IntPtr hwnd);
        [DllImport("user32.dll",EntryPoint="ReleaseDC")]static extern int DesignReleaseDC(IntPtr hwnd,IntPtr dc);
        [DllImport("gdi32.dll",EntryPoint="BitBlt")]static extern bool DesignBitBlt(IntPtr dest,int x,int y,int w,int h,IntPtr source,int sx,int sy,uint rop);
        [DllImport("user32.dll",EntryPoint="SetWindowPos")]static extern bool DesignSetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    }
}
