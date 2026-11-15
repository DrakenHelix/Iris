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
