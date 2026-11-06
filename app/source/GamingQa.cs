using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        void RunGamingUiQa(){
            var results=new List<string>();
            try{if(firstLaunchDesign)VerifyFirstLaunchDefaults(results);VerifyGaming(results);results.Add("ALL GAMING CHECKS PASSED");}
            catch(Exception ex){Environment.ExitCode=1;results.Add("FAILED: "+ex);}
            finally{File.WriteAllLines(Path.Combine(root,"gaming-ui-test.txt"),results);Close();}
        }
        static void VerifyLiveFpsWindow(DateTime arrival){
            const string header="ProcessID,SwapChainAddress,PresentMode,Dropped,msBetweenPresents,TimeInSeconds,QPCTime";
            long frequency=System.Diagnostics.Stopwatch.Frequency,start=frequency*100;
            var frames=new FpsFrames();
            frames.Target(42);
            frames.Read(header,arrival);
            Action<string,int,int,bool> add=(chain,rate,tick,shown)=>{
                long time=start+tick*frequency/240;
                string row=string.Format(CultureInfo.InvariantCulture,"42,{0},Composed: Flip,{1},{2:R},{3:R},{4}",chain,shown?0:1,1000.0/rate,tick/240.0,time);
                frames.Read(row,arrival);
            };
            Action<int,int> expect=(fps,tick)=>{
                var sample=frames.Latest(arrival,start+tick*frequency/240);
                if(sample==null||Math.Abs(sample.Fps-fps)>.01)throw new Exception("Expected "+fps+" render FPS from recent visible game stream, got "+(sample==null?"none":sample.Fps.ToString(CultureInfo.InvariantCulture)));
            };
            for(int tick=0;tick<480;tick++){
                int fps=tick<240?120:30,step=240/fps;
                add("0xHidden",240,tick,false);
                if(tick%16==0)add("0xSlow",15,tick,true);
                if(tick%step==0){
                    add("0xGame",fps,tick,tick%(step*2)==0);
                    add("0xGame",15,tick,true); // Duplicate capture timestamp must not distort the measurement.
                }
                if(tick==239)expect(120,tick);
            }
            expect(30,479);
            if(frames.Latest(arrival,start+4*frequency)!=null)throw new Exception("Late pipe delivery made old QPC frames appear live");
            frames.Target(42);
            frames.Read(header,arrival);
            // The first present follows a long pause, then the game is already back at 120.
            add("0xGame",2,720,true);
            for(int tick=722;tick<=744;tick+=2)add("0xGame",120,tick,true);
            expect(120,744);
            frames.Target(99);
            if(frames.Latest(arrival,start+2*frequency)!=null)throw new Exception("Previous game survived reset");
        }
        void RunGamingProbe(){
            var log=new List<string>();bool connected=false;var reader=new GamingReader(delegate(string state,bool permission){lock(log)log.Add("STATE: "+state);});
            if(!GamingReader.ToolsValid())throw new Exception("Official FPS helper verification failed");
            reader.Start(false);using(var self=System.Diagnostics.Process.GetCurrentProcess())reader.Target(new GameTarget{Pid=self.Id,Started=self.StartTime.ToUniversalTime().Ticks});
            var watch=System.Diagnostics.Stopwatch.StartNew();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=delegate{connected|=reader.Ready;if(watch.Elapsed.TotalSeconds<5&&!reader.NeedsPermission)return;timer.Stop();bool acknowledged=reader.TargetAcknowledged,denied=reader.NeedsPermission,elevated=reader.Elevated;string status=reader.Status;bool fps=reader.Latest()!=null;reader.Dispose();
                System.Threading.Tasks.Task.Factory.StartNew(delegate{bool exited=reader.WaitForExit(6000);Dispatcher.BeginInvoke(new Action(delegate{lock(log){log.Add("Native pipe connected: "+connected+"; broker elevated: "+elevated+"; broker exited after disconnect: "+exited+"; exit code: "+reader.HostExitCode);log.Add("Target acknowledged: "+acknowledged+"; permission denied: "+denied+"; FPS data received: "+fps+"; final status: "+status);log.Add("LIMIT: Only Iris itself targeted, no game launched, no UAC, no microphone. This does not confirm live game FPS.");if(!connected||!acknowledged||elevated||!exited||reader.HostExitCode!=0){log.Add("FAILED: Broker connection/lifetime verification.");Environment.ExitCode=1;}else log.Add("BROKER CHECK PASSED");File.WriteAllLines(System.IO.Path.Combine(root,"gaming-native-check.txt"),log);}Close();}));});
            };timer.Start();
        }
        void VerifyGaming(List<string> results){
            VerifyLiveFpsWindow(DateTime.UtcNow);
            results.Add("PASS: A visible game reports 120 and 30 render FPS despite every second present being dropped, a separate 15 FPS stream, a hidden 240 FPS stream, duplicate events and batched pipe delivery. Old capture timestamps expire.");
            // Literal v2.6.0 WriteCsvHeader<FrameMetrics1> contract with our GPU/input flags.
            var contract=new FpsFrames();var contractNow=DateTime.UtcNow;contract.Target(42);
            const string actualHeader="Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents,AllowsTearing,PresentMode,msUntilRenderComplete,msUntilDisplayed,msBetweenDisplayChange,msFlipDelay";
            foreach(string variant in new[]{actualHeader,"\uFEFF"+actualHeader,actualHeader.ToUpperInvariant()," "+actualHeader.Replace(",",", ")+" "}){
                contract.Target(42);contract.Read(variant,contractNow);for(int i=0;i<90;i++)contract.Read("game.exe,42,0x0000000000000123,DXGI,0,0,0,"+(i/60.0).ToString("R",CultureInfo.InvariantCulture)+",0.1,16.6666667,1,Hardware: Independent Flip,1,1,16.6666667,0",contractNow.AddMilliseconds(i*10));var actual=contract.Latest(contractNow.AddSeconds(1));if(actual==null||Math.Abs(actual.Fps-60)>.01||actual.Pid!=42)throw new Exception("Official PresentMon v1 CSV contract rejected; original no-FPS regression");
            }
            results.Add("PASS: Actual PresentMon 2.6.0 v1 16-column CSV contract (msBetweenPresents) produces 60 FPS; BOM, case and header whitespace variants accepted. This is a contract fixture, not live game capture.");
            var legacy=serializer.Deserialize<Preferences>("{}");if(legacy.GamingEnabled||!legacy.GamingWindowed||!legacy.GamingFullscreen||legacy.GamingGames==null)throw new Exception("Legacy gaming defaults changed");
            var registered=new[]{@"D:\Games\Mein Spiel\game.exe"};if(!GameCatalog.IsGame(@"D:\SteamLibrary\steamapps\common\Test\game.exe",null)||!GameCatalog.IsGame(registered[0],registered)||GameCatalog.IsGame(@"C:\Program Files\Browser\chrome.exe",null)||GameCatalog.IsGame(@"D:\SteamLibrary\steamapps\common\Tools\steamwebhelper.exe",null))throw new Exception("Game matching includes arbitrary desktop/media apps or misses registered games");
            var stream=new FpsFrames();stream.Target(42);string header="Application,ProcessID,SwapChainAddress,PresentMode,MsBetweenPresents";stream.Read(header,DateTime.UtcNow);DateTime now=DateTime.UtcNow;for(int i=0;i<70;i++)stream.Read("\"Spiel, Test.exe\",42,0x123,Composed: Flip,16.6666667",now.AddMilliseconds(i));var sample=stream.Latest(now.AddMilliseconds(75));if(sample==null||Math.Abs(sample.Fps-60)>.01||sample.Pid!=42)throw new Exception("PresentMon CSV does not yield actual present interval FPS");
            for(int i=0;i<10;i++)stream.Read("other.exe,99,0x456,Hardware: Independent Flip,1",now);if(Math.Abs(stream.Latest(now).Fps-60)>.01)throw new Exception("FPS mixes a different process");foreach(string value in new[]{"0","-5","NaN","Infinity","n/a","9000"})stream.Read("test.exe,42,0x123,Composed: Flip,"+value,now);if(Math.Abs(stream.Latest(now).Fps-60)>.01||stream.Latest(now.AddSeconds(2))!=null)throw new Exception("Invalid or stale frames remain visible");stream.Target(99);if(stream.Latest(now)!=null)throw new Exception("FPS persists across game switches");if(FpsFrames.Csv("\"a,b\",\"c\"\"d\",x")[1]!="c\"d")throw new Exception("CSV quote handling corrupts values");
            uiMotion.PauseAutomatic();qaIdleSeconds=0;SetReducedMotion(true);ReadMedia("{\"available\":false}");countdown.Reset(300);RefreshTimer();volumeHud.Show(false);hudDismiss.Stop();prefs.MinimalNotch=true;prefs.GamingEnabled=true;prefs.GamingWindowed=true;prefs.GamingFullscreen=true;CloseNotch(true);
            var game=new GameTarget{Pid=42,Name="Testspiel",Path=registered[0],Fullscreen=false};sample=new FpsSample{Pid=42,Fps=144,At=now};gamingTarget=game;SetGamingSample(game,sample);UpdateLayout();if(!gamingVisible||!compactFps.Showing||surface.Span!=ClosedSpan||MinimalIdle())throw new Exception("FPS does not restore adequate space from minimal idle");var before=clock.TranslatePoint(new Point(clock.ActualWidth/2,clock.ActualHeight/2),canvas);
            SetGamingSample(game,null);
            if(!gamingVisible||!compactFps.Showing||displayedGamingFps.HasValue||compactFps.Reveal!=1)
                throw new Exception("Short sampling gap hid the slot or displayed an old FPS value");
            lastGamingMeasurement=DateTime.UtcNow.AddSeconds(-2);
            SetGamingSample(game,null);
            if(gamingVisible||compactFps.Showing)throw new Exception("An expired measurement still occupies the FPS slot");
            SetGamingSample(game,sample);
            results.Add("PASS: A brief gap in the same game keeps the FPS slot visible without a stale number; it expires if the stream stays silent.");
            prefs.BatteryEnabled=true;ApplyBattery(new BatteryState{Present=true,Percent=83});if(compactBatteryHost.Visibility==Visibility.Visible&&Grid.GetColumn(compactBatteryHost)==Grid.GetColumn(compactFps))throw new Exception("Battery overlaps FPS in the same slot");SetGamingSample(null,null);uiMotion.SnapAll();if(compactBatteryHost.Visibility!=Visibility.Visible||gamingVisible)throw new Exception("Leaving game does not restore battery/hide FPS");prefs.BatteryEnabled=false;ApplyBattery(new BatteryState());
            prefs.MinimalNotch=false;ReadMediaFixture();lyricsVisible=true;SetGamingSample(game,sample);UpdateLayout();if(!compactLyricMode||!lyricFps.Showing||lyricMusicNote.Visibility!=Visibility.Collapsed)throw new Exception("FPS disappears behind compact lyrics");lyricsVisible=false;ReadMedia("{\"available\":false}");SetGamingSample(game,sample);UpdateLayout();var after=clock.TranslatePoint(new Point(clock.ActualWidth/2,clock.ActualHeight/2),canvas);if((before-after).Length>.1)throw new Exception("FPS shifts centered clock");game.Fullscreen=true;prefs.GamingFullscreen=false;SetGamingSample(game,sample);if(gamingVisible)throw new Exception("Fullscreen switch does not gate FPS");prefs.GamingFullscreen=true;SetGamingSample(game,sample);if(!gamingVisible)throw new Exception("Fullscreen FPS cannot override generic hiding");game.Fullscreen=false;prefs.GamingWindowed=false;SetGamingSample(game,sample);if(gamingVisible)throw new Exception("Windowed switch does not gate FPS");prefs.GamingWindowed=true;sample.Pid=99;SetGamingSample(game,sample);if(gamingVisible)throw new Exception("FPS shows for wrong active game");sample.Pid=42;
            SetReducedMotion(false);SetGamingSample(game,sample);AdvanceUi(4);if(compactFps.Reveal<=0||compactFps.Reveal>=.99)throw new Exception("FPS presence has no animated intermediate state");AdvanceUi(80);SetReducedMotion(true);ReadMediaFixture();SwitchPage("Musik",false);Open();uiMotion.SnapAll();ShowSettings();settingsSection="Gaming";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();if(settingsPages.Current.RenderSize.Height>settingsBody.ActualHeight+.5)throw new Exception("Gaming settings overflow window");ShowGamingGames();uiMotion.SnapAll();EndAddonQaWindow();settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
            SetGamingEnabled(false);prefs.GamingWindowed=prefs.GamingFullscreen=true;prefs.MinimalNotch=false;SetReducedMotion(true);if(compactFps.Showing||gamingVisible)throw new Exception("Disabled FPS remains visible");SetReducedMotion(false);AdvanceUi(100);uiMotion.SnapAll();if(gamingReader!=null||gamingPoll.IsEnabled||uiMotion.ActiveCount!=0)throw new Exception("Disabled/QA gaming opens capture or keeps rendering active");Save();
            results.Add("PASS: Gaming CSV actual frame-interval aggregation, PID/swapchain separation, malformed/stale/reset data, recognized Steam/registered games and mode gates; no browser/video auto-detection.");
            results.Add("PASS: Native FPS slot preserves clock center and music slot, yields/restores optional battery, expands minimal idle, settings/game manager fit, presence animation/reduced motion/idle and disabled state. FPS readings are fixtures; no ETW capture or game launched by UI QA.");
        }
        void RunGamingDemo(){
            uiMotion.PauseAutomatic();qaIdleSeconds=0;SetReducedMotion(true);countdown.Reset(300);RefreshTimer();ReadMediaFixture();SwitchPage("Musik",false);prefs.MinimalNotch=false;prefs.GamingEnabled=true;prefs.GamingWindowed=prefs.GamingFullscreen=true;CloseNotch(true);SetReducedMotion(false);Directory.CreateDirectory(System.IO.Path.Combine(root,"gaming-frames"));var game=new GameTarget{Pid=42,Name="Testspiel"};var sample=new FpsSample{Pid=42,Fps=144,At=DateTime.UtcNow};double[] times={.5,1.5,2.2,2.8,3.5,5.1,5.7};Action[] actions={delegate{gamingTarget=game;SetGamingSample(game,sample);},delegate{sample.Fps=120;SetGamingSample(game,sample);},delegate{Open();},delegate{ShowSettings();settingsSection="Gaming";RenderSettingsSection();},delegate{settingsTabs["Gaming"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{settings.Close();},delegate{SetGamingSample(null,null);CloseNotch(true);}};
            var watch=System.Diagnostics.Stopwatch.StartNew();var trace=new List<string>{"frame,seconds,settings"};int frame=0,index=0;double previous=0;var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(index<times.Length&&now>=times[index])actions[index++]();uiMotion.Advance(Math.Min(.06,now-previous));previous=now;CaptureWindow(this,"gaming-frames/main-"+frame.ToString("0000")+".png");bool show=settings!=null&&settings.IsVisible;if(show)CaptureWindow(settings,"gaming-frames/settings-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+(show?1:0));frame++;if(now>=6.5){timer.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"gaming-trace.csv"),trace);Close();}};timer.Start();
        }
    }
}
