using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        void RunReleaseUiQa(){
            var results=new List<string>();
            try{
                bool english=L.Language=="en";
                SetReducedMotion(true);
                VerifyMonitorPriority(results);
                VerifyMaximizedMini(results,english);
                VerifyNotchWindowOrder(results);
                VerifyQuickBarHover(results);
                VerifyKeepOpenAndUpdates(results,english);
                ShowSettings();
                if(settings.Title!=(english?"Iris · Settings":"Iris · Einstellungen"))
                    throw new Exception("Settings window did not use the saved language.");
                if(tray.ContextMenuStrip.Items[1].Text!=(english?"Settings":"Einstellungen"))
                    throw new Exception("Tray menu did not use the saved language.");
                if(settingsTabs.Count!=10||settingsTabs.ContainsKey("Sprachsteuerung")||tray.ContextMenuStrip.Items.Count!=4)
                    throw new Exception("Unexpected settings tab or tray entry.");
                settingsSection="Allgemein";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();
                var startup=Descendants<FluidToggle>(settingsPages.Current).Single(t=>AutomationProperties.GetName(t)==(english?"Start with Windows":"Mit Windows starten"));
                if(startup.IsChecked!=false||qaStartupCommand!=null)throw new Exception("Fresh startup switch must remain off until explicitly enabled.");
                foreach(var tab in settingsTabs.ToArray()){
                    tab.Value.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    uiMotion.SnapAll();settings.UpdateLayout();
                    Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
                    var view=settingsPages.Current;
                    var scroll=Descendants<MixerScroll>(view).Single();
                    if(view.ActualHeight>settingsBody.ActualHeight+.5||scroll.ViewportHeight<=0)
                        throw new Exception("Settings page does not fit: "+tab.Key);
                    scroll.ScrollToVerticalOffset(scroll.ScrollableHeight);
                    settings.UpdateLayout();
                    if(Math.Abs(scroll.DisplayOffset-scroll.ScrollableHeight)>.5)
                        throw new Exception("Bottom of settings page is unreachable: "+tab.Key);
                    if(tab.Key=="Über Iris"&&!Descendants<Button>(view).Any(b=>Convert.ToString(b.Content)==(english?"Check for updates":"Nach Updates suchen")))
                        throw new Exception("The localized update button is missing from About Iris.");
                }
                settingsSection="Anzeige";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();
                string avoidance=english?"Move aside after 0.5 seconds nearby":"Nach 0,5 Sekunden in der Nähe ausweichen";
                if(sizePicker==null||!Descendants<FluidToggle>(settingsPages.Current).Any(t=>AutomationProperties.GetName(t)==avoidance))
                    throw new Exception("Display settings are missing notch size or avoidance.");
                if(english){
                    var action=new AddonAction{Kind="folders",Title="Meine Ordner"};
                    if(AddonCatalog.DisplayTitle(action)!="My folders")throw new Exception("A built-in tile name did not localize.");
                    action.Title="Persönliche Ordner";
                    if(AddonCatalog.DisplayTitle(action)!=action.Title)throw new Exception("Custom tile label was translated.");
                }
                SwitchPage("Ablage",false);
                if(pageLabel.Text!=(english?"Shelf":"Ablage")||prefs.Page!="Ablage")
                    throw new Exception("Changing the displayed tab name changed its saved ID.");
                SwitchPage("Timer",false);
                minutesBox.Text="05";secondsBox.Text="00";
                ToggleTimer();
                if(!countdown.Running||prefs.Minutes!=5||compactTimer.Text!="05:00")
                    throw new Exception("Manual timer did not start.");
                ToggleTimer();
                if(countdown.Running)throw new Exception("Manual timer did not pause.");
                settingsSection="Allgemein";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();
                string other=english?"Deutsch":"English";
                Descendants<Button>(settingsPages.Current).Single(b=>Convert.ToString(b.Content)==other).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string note;bool readOnly;
                var saved=PreferencesStore.Read(Path.Combine(root,dataFolder,"settings.json"),out note,out readOnly);
                if(readOnly||saved.Language!=(english?"de":"en"))throw new Exception("Language choice was not saved.");
                results.Add("PASS: "+L.Language+" settings, tray, manual timer, language choice, translated tab with stable ID, scrollable sections and built-in/custom tile labels.");
            }catch(Exception ex){Environment.ExitCode=1;results.Add("FAILED: "+ex);}
            finally{File.WriteAllLines(Path.Combine(root,"release-"+L.Language+"-test.txt"),results);Close();}
        }

        void VerifyMaximizedMini(List<string> results,bool english){
            bool previousOption=prefs.MinimizeMaximized,previousContext=maximizedWindow,previousFullscreen=designFullscreen;
            IntPtr previousWindow=maximizedContextWindow;
            var bounds=SelectedMonitorBounds();
            var dpi=PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice;
            var cover=new Window{Title="Iris QA maximize",Width=600,Height=260,Left=(bounds.Left+60)/dpi.M11,Top=(bounds.Top+60)/dpi.M22,
                ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.SingleBorderWindow};
            try{
                SetReducedMotion(true);CloseNotch(true);SetMinimalNotch(false);ReadMedia("{\"available\":false}");
                countdown.Reset(300);RefreshTimer();ApplyDesignContext(false);
                cover.Show();var handle=new System.Windows.Interop.WindowInteropHelper(cover).Handle;
                if(IsMaximizedOnNotchMonitor(handle))throw new Exception("Normal fixture window is treated as maximized.");
                cover.WindowState=WindowState.Maximized;cover.UpdateLayout();
                if(!IsMaximizedOnNotchMonitor(handle))throw new Exception("Native Windows maximize state is not recognized.");
                maximizedContextWindow=handle;RefreshMaximizedWindow(handle);
                if(!maximizedWindow||MinimizeForMaximized())throw new Exception("Disabled setting still minimizes the notch.");
                ShowSettings();settingsSection="Anzeige";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();
                var toggle=Descendants<FluidToggle>(settingsPages.Current).Single(t=>AutomationProperties.GetName(t)==
                    (english?"Minimize for maximized windows":"Bei maximierten Fenstern minimieren"));
                toggle.IsChecked=true;
                if(!prefs.MinimizeMaximized||RestingSpan()!=MinimalSpan||RestingDepth()!=MinimalDepth)
                    throw new Exception("Maximize option does not apply immediately from settings.");
                string note;bool readOnly;
                var saved=PreferencesStore.Read(Path.Combine(root,dataFolder,"settings.json"),out note,out readOnly);
                if(readOnly||!saved.MinimizeMaximized)throw new Exception("Maximized-window choice was not persisted.");
                toggle.IsChecked=false;
                SetReducedMotion(false);
                toggle.IsChecked=true;
                if(MotionEnabled()){
                    System.Threading.Thread.Sleep(60);MorphFrame(this,EventArgs.Empty);
                    if(surface.Span<=MinimalSpan||surface.Span>=ClosedSpan)throw new Exception("Mini transition has no intermediate frame.");
                }
                var frame=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width,(int)Height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                frame.Render(this);
                SetReducedMotion(true);RefreshMaximizedWindow(new System.Windows.Interop.WindowInteropHelper(settings).Handle);
                if(!maximizedWindow)throw new Exception("Iris settings loses the external maximized context.");
                settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
                ReadMediaFixture();isPlaying=true;lyricsVisible=true;countdown.Start(DateTime.UtcNow);RefreshTimer();UpdateCompactLyrics();
                ShowVolumeHud(.5,"QA");
                if(RestingSpan()!=MinimalSpan||RestingDepth()!=MinimalDepth||compactLyrics.Visibility==Visibility.Visible||VolumeHudVisible())
                    throw new Exception("Playback, timer, lyrics or volume popups cover the maximized window's tab bar.");
                Open();SetKeepOpen(true);ApplyMaximizedWindow(true);
                if(!isOpen||!keepOpen||surface.Span<=MinimalSpan)throw new Exception("Maximize rule prevents opening or keeping the notch open.");
                CloseNotch(true);
                ApplyDesignContext(true);
                if(MinimizeForMaximized())throw new Exception("True fullscreen uses the maximized-window rule.");
                ApplyDesignContext(false);
                cover.WindowState=WindowState.Normal;ApplyMaximizedWindow(IsMaximizedOnNotchMonitor(handle));
                if(maximizedWindow||MinimizeForMaximized())throw new Exception("Restoring the window leaves the notch minimized.");
                var screens=System.Windows.Forms.Screen.AllScreens;
                var other=screens.FirstOrDefault(s=>s.Bounds!=bounds);
                if(other!=null){
                    cover.Left=(other.Bounds.Left+60)/dpi.M11;cover.Top=(other.Bounds.Top+60)/dpi.M22;cover.WindowState=WindowState.Maximized;
                    if(IsMaximizedOnNotchMonitor(handle))throw new Exception("A maximized window on another monitor minimizes Iris.");
                }
                results.Add("PASS: Native owned windows test maximize/restore, other-monitor exclusion, localized saved toggle, Iris focus, media/timer suppression and opening/pin access. Fullscreen design remains independent. No live Chrome window was changed.");
            }finally{
                cover.Close();SetReducedMotion(true);lyricsVisible=false;lyricsPanel.Visibility=Visibility.Collapsed;
                countdown.Pause(DateTime.UtcNow);countdown.Reset(300);RefreshTimer();ReadMedia("{\"available\":false}");
                prefs.MinimizeMaximized=previousOption;maximizedWindow=previousContext;maximizedContextWindow=previousWindow;
                ApplyDesignContext(previousFullscreen);CloseNotch(true);RefreshMaximizedPresentation();Save();
            }
        }

        void VerifyMonitorPriority(List<string> results){
            var bounds=System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            Func<string,bool,IrisMonitor> make=(id,primary)=>new IrisMonitor{Id=id,Name="QA "+id,Device=id,Stable=true,Primary=primary,Bounds=bounds};
            var a=make("monitor-a",true);var b=make("monitor-b",false);var c=make("monitor-c",false);
            var order=MonitorTopology.Remember(new[]{a,b,c},null,1);
            if(order[0].Id!=b.Id)throw new Exception("Migration loses the existing selected monitor.");
            var preferred=new List<MonitorPreference>{new MonitorPreference{Id=a.Id,Name=a.Name},new MonitorPreference{Id=b.Id,Name=b.Name},new MonitorPreference{Id=c.Id,Name=c.Name}};
            if(MonitorTopology.Choose(new[]{c,a,b},preferred,2)!=a||MonitorTopology.Choose(new[]{c,b},preferred,0)!=b||
                MonitorTopology.Choose(new[]{c},preferred,0)!=c||MonitorTopology.Choose(new[]{b,a,c},preferred,0)!=a)
                throw new Exception("Preferred display / fallback / return depends on enumeration order.");
            var remembered=MonitorTopology.Remember(new[]{c},preferred,0);
            if(string.Join(",",remembered.Select(p=>p.Id))!="monitor-a,monitor-b,monitor-c")throw new Exception("Disconnected monitors were removed or reprioritized.");
            if(MonitorTopology.Choose(new IrisMonitor[0],preferred,0)!=null)throw new Exception("No-monitor state returns an unavailable device.");
            var savedOrder=serializer.Deserialize<Preferences>(serializer.Serialize(new Preferences{MonitorPriority=preferred}));
            if(MonitorTopology.Choose(new[]{b,a,c},savedOrder.MonitorPriority,0)!=a)throw new Exception("Restart forgets the preferred monitor identity.");
            var temporary=make("temporary",true);
            if(MonitorTopology.Choose(new[]{temporary},preferred,0)!=temporary)throw new Exception("An unranked temporary screen cannot host Iris.");

            var previousScreens=monitorScreens;var previousPriority=prefs.MonitorPriority;
            int previousMonitor=prefs.Monitor;
            try{
                prefs.MonitorPriority=preferred;ApplyMonitorTopology(new[]{c,b,a});
                ShowSettings();settingsSection="Anzeige";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();
                if(monitorChoices.Count!=3)throw new Exception("Monitor priority controls are incomplete.");
                var page=settingsPages.Current;
                SetReducedMotion(false);
                monitorChoices[b.Id].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));AdvanceUi(4);settings.UpdateLayout();
                if(prefs.MonitorPriority[0].Id!=b.Id||settingsPages.Current!=page||monitorSelections[b.Id].Value<=0||monitorSelections[b.Id].Value>=1)
                    throw new Exception("Monitor selection has no animated state or rebuilds the settings page.");
                SetReducedMotion(true);MoveMonitor(a.Id,0);
                ApplyMonitorTopology(new[]{c,b});
                if(SelectedMonitor()!=b||prefs.MonitorPriority[0].Id!=a.Id||monitorRows.Children.Count!=3)
                    throw new Exception("Fallback changes the saved first choice or hides disconnected rows.");
                ApplyMonitorTopology(new[]{c});
                if(SelectedMonitor()!=c)throw new Exception("Third monitor fallback failed.");
                ApplyMonitorTopology(new[]{b,c,a});
                if(SelectedMonitor()!=a)throw new Exception("Returning preferred monitor was ignored.");
                MoveMonitor(c.Id,1);MoveMonitor(b.Id,2);
                ApplyMonitorTopology(new[]{b,c});
                if(SelectedMonitor()!=c)throw new Exception("Changed backup order does not determine the fallback.");
                bool automaticUpdates=prefs.AutomaticUpdates;
                string note;bool blocked;
                var saved=PreferencesStore.Read(Path.Combine(root,dataFolder,"settings.json"),out note,out blocked);
                if(blocked||saved.MonitorPriority[0].Id!=a.Id||saved.MonitorPriority[1].Id!=c.Id||saved.AutomaticUpdates!=automaticUpdates)
                    throw new Exception("Monitor order did not persist with existing settings.");
                var render=new System.Windows.Media.Imaging.RenderTargetBitmap((int)settings.Width,(int)settings.Height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                render.Render(settings);
                settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
            }finally{
                monitorScreens=previousScreens;prefs.MonitorPriority=previousPriority;prefs.Monitor=previousMonitor;Place();Save();
            }
            results.Add("PASS: Saved device identities survive reordered enumeration, first/second/third choices, temporary displays, disconnects, reconnects and restart. Native settings reorder existing rows, animate selection and preserve offline entries. Topology events simulated; no physical monitor was switched off.");
        }

        async void RunMonitorUiQa(){
            var results=new List<string>();
            try{
                SetReducedMotion(true);VerifyMonitorPriority(results);
                var screens=await System.Threading.Tasks.Task.Run(()=>MonitorTopology.Read());
                ApplyMonitorTopology(screens);
                ShowSettings();settingsSection="Anzeige";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();
                Descendants<MixerScroll>(settingsPages.Current).Single().ScrollToVerticalOffset(260);
                settings.UpdateLayout();
                var finish=new DispatcherTimer{Interval=TimeSpan.FromSeconds(35)};
                finish.Tick+=delegate{finish.Stop();File.WriteAllLines(Path.Combine(root,"monitor-ui-test.txt"),results);Close();};
                finish.Start();
            }catch(Exception error){Environment.ExitCode=1;results.Add("FAILED: "+error);File.WriteAllLines(Path.Combine(root,"monitor-ui-test.txt"),results);Close();}
        }

        void VerifyQuickBarHover(List<string> results){
            SwitchPage("Musik",false);Open();SetQuickBar(false);UpdateLayout();
            double center=expanded.Width/2;
            long now=System.Diagnostics.Stopwatch.Frequency;
            long tenth=now/10;
            var playButton=Descendants<Button>(musicPage).Single(b=>b.Content==playGlyph);
            if(!Hits(playButton))throw new Exception("The collapsed quick bar covers Play/Pause.");
            UpdateQuickBarHover(new Point(center,124),now);
            if(quickExpanded)throw new Exception("Quick bar opens over the playback row.");
            UpdateQuickBarHover(new Point(center+53,151),now);
            UpdateLayout();
            if(!quickExpanded||!Hits(playButton)||!Hits(keepOpenButton)||!Hits(quickSettingsButton))
                throw new Exception("The wider trigger fails or interrupts playback / quick actions.");
            UpdateQuickBarHover(new Point(center+nav.Width/2+19,167),now+tenth);
            if(quickBarExit.HasValue)throw new Exception("The bar starts closing over its side margin.");
            UpdateQuickBarHover(new Point(center+170,167),now+2*tenth);
            UpdateQuickBarHover(new Point(center+170,167),now+3*tenth);
            if(!quickExpanded)throw new Exception("The bar vanishes on a brief exit.");
            UpdateQuickBarHover(new Point(center+95,175),now+3*tenth);
            if(!quickExpanded||quickBarExit.HasValue)throw new Exception("Re-entering does not cancel the queued close.");
            UpdateQuickBarHover(new Point(center+170,167),now+4*tenth);
            UpdateQuickBarHover(new Point(center+170,167),now+6*tenth);
            if(quickExpanded)throw new Exception("The bar stays open after leaving its retention area.");
            CloseNotch(true);
            results.Add("PASS: Quick-bar pointer fixtures exercise the wider hover area, peripheral retention, exit grace, re-entry and hit tests for Play/Pause, the pin and Settings in the native layout.");
        }

        void VerifyKeepOpenAndUpdates(List<string> results,bool english){
            if(updateTimer!=null)throw new Exception("QA started live update checks.");
            qaPointerOutside=true;
            qaIdleSeconds=130;
            CloseNotch(true);
            SetMinimalNotch(true);
            var previousRelease=IrisUpdate.Parse("{\"tag_name\":\"v1.0.3\"}",false,IrisUpdate.Current);
            if(previousRelease!=null)throw new Exception("An older GitHub release could trigger an update.");
            availableUpdate=new IrisUpdate{Version=new Version(1,0,5),Notes="Fixture release"};
            RefreshUpdateIndicators();UpdateLayout();
            string label=(english?"New version available: ":"Neue Version verfügbar: ")+availableUpdate.Version;
            if(MinimalIdle()||compactUpdate.Visibility!=Visibility.Visible||AutomationProperties.GetName(compactUpdate)!=label||!CanApplyAutomaticUpdate())
                throw new Exception("The closed notch does not announce the available version or idle installation cannot proceed.");
            countdown.Start(DateTime.UtcNow);
            if(CanApplyAutomaticUpdate())throw new Exception("Automatic installation would interrupt the timer.");
            countdown.Pause(DateTime.UtcNow);countdown.Reset(300);

            SwitchPage("Musik",false);Open();SetQuickBar(true);UpdateLayout();
            if(!Hits(keepOpenButton)||!Hits(headerUpdate)||nav.Children[nav.Children.Count-1]!=quickSettingsButton)
                throw new Exception("Keep-open, update or settings action cannot be reached.");
            SetReducedMotion(false);
            keepOpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CloseNotch(false);
            if(!isOpen||page!="Musik"||CanApplyAutomaticUpdate()||AutomationProperties.GetName(keepOpenButton)!=(english?"Release notch":"Offenhalten lösen"))
                throw new Exception("Keep-open did not respond immediately or protect the player.");
            AdvanceUi(4);UpdateLayout();
            if(MotionEnabled()&&(keepOpenSelection.Value<=0||keepOpenSelection.Value>=1))throw new Exception("No intermediate keep-open state.");
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Width,(int)Height,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(this);
            keepOpenButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CloseNotch(false);
            if(isOpen||keepOpen)throw new Exception("Releasing the notch did not restore automatic closing.");

            SetReducedMotion(true);Open();headerUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(settingsSection!="Über Iris"||!Descendants<FluidToggle>(settingsPages.Current).Any(t=>AutomationProperties.GetName(t)==(english?"Automatic updates":"Automatische Updates"))||
                !Descendants<Button>(settingsPages.Current).Any(b=>Convert.ToString(b.Content)==(english?"Download and install":"Herunterladen und installieren")))
                throw new Exception("Update notice did not navigate to the localized update controls.");
            settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
            availableUpdate=null;RefreshUpdateIndicators();SetMinimalNotch(false);
            CloseNotch(true);qaPointerOutside=false;qaIdleSeconds=30;
            results.Add("PASS: Keep-open button responds, marks selection, survives hover close, releases and renders in memory. Update icon and settings verified with a fixture; no GitHub request, install or UAC.");
        }
    }
}
