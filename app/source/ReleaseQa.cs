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
                ShowSettings();
                if(settings.Title!=(english?"Iris · Settings":"Iris · Einstellungen"))
                    throw new Exception("Settings window did not use the saved language.");
                if(tray.ContextMenuStrip.Items[1].Text!=(english?"Settings":"Einstellungen"))
                    throw new Exception("Tray menu did not use the saved language.");
                if(settingsTabs.Count!=10||settingsTabs.ContainsKey("Sprachsteuerung")||tray.ContextMenuStrip.Items.Count!=4)
                    throw new Exception("Unexpected settings tab or tray entry.");
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
    }
}
