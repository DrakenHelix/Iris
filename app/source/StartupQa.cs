using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        string qaStartupCommand;
        bool qaStartupWriteFails;

        void VerifyFirstLaunchDefaults(List<string> results){
            SetReducedMotion(true);
            ShowDesignSettings();
            uiMotion.SnapAll();
            settings.UpdateLayout();
            if(designSelections["full:glass"].Value!=1||designSelections["normal:black"].Value!=1)
                throw new Exception("First-launch design choices are incorrect");
            foreach(var setting in new[]{
                new{Section="Gaming",Label="FPS anzeigen"},
                new{Section="Anzeige",Label="Nach 0,5 Sekunden in der Nähe ausweichen"}
            }){
                settingsSection=setting.Section;
                RenderSettingsSection();
                uiMotion.SnapAll();
                settings.UpdateLayout();
                var toggle=Descendants<FluidToggle>(settingsPages.Current)
                    .Single(t=>AutomationProperties.GetName(t)==setting.Label);
                if(toggle.IsChecked!=true)throw new Exception("First-launch switch should be on: "+setting.Label);
            }
            if(gamingReader!=null||gamingPoll.IsEnabled||avoidPoll.IsEnabled)
                throw new Exception("First-launch QA started live listeners");
            settings.Close();
            results.Add("PASS: Fresh settings select fullscreen Liquid Glass and enable FPS and 0.5 s cursor avoidance in the real settings UI. QA uses no live listeners or elevation.");
        }

        void WriteStartupFixture(string command) {
            if (qaStartupWriteFails) throw new IOException("Fixture write denied.");
            qaStartupCommand = command;
        }

        void VerifyStartup(List<string> results) {
            uiMotion.PauseAutomatic();
            SetReducedMotion(false);
            ShowSettings();
            settingsSection = "Allgemein";
            RenderSettingsSection();
            uiMotion.SnapAll();
            settings.UpdateLayout();
            Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            Func<FluidToggle> find = () => Descendants<FluidToggle>(settingsPages.Current)
                .Single(t => AutomationProperties.GetName(t) == "Mit Windows starten");
            var toggle = find();
            if (!toggle.IsLoaded) throw new Exception("Autostart toggle is not loaded.");
            if (toggle.IsChecked != false) throw new Exception("Autostart fixture should initially be off.");
            if (settingsPages.Current.RenderSize.Height > settingsBody.ActualHeight + .5)
                throw new Exception("Autostart row clips General settings.");
            toggle.IsChecked = true;
            if (qaStartupCommand != "\"" + typeof(NotchWindow).Assembly.Location + "\"")
                throw new Exception("Autostart must immediately register the exact quoted executable.");
            AdvanceUi(4);
            if (MotionEnabled() && (toggle.Phase <= 0 || toggle.Phase >= 1))
                throw new Exception("Autostart toggle has no rendered intermediate phase.");
            CaptureWindow(settings, "preview-startup-mid.png");
            double phase = toggle.Phase;
            toggle.IsChecked = false;
            if (qaStartupCommand != null || MotionEnabled() && toggle.Phase != phase)
                throw new Exception("Fast reversal must remove the entry without resetting the switch position.");
            AdvanceUi(90);
            if (Math.Abs(toggle.Phase) > .001) throw new Exception("Autostart toggle did not settle.");

            qaStartupWriteFails = true;
            toggle.IsChecked = true;
            if (toggle.IsChecked != false || qaStartupCommand != null)
                throw new Exception("Failed registration left the switch on.");
            qaStartupWriteFails = false;
            qaStartupCommand = "\"C:\\Fixture Iris\\Iris.exe\"";
            RenderSettingsSection();
            uiMotion.SnapAll();
            settings.UpdateLayout();
            toggle = find();
            if (toggle.IsChecked != true || !Descendants<TextBlock>(settingsPages.Current)
                .Any(t => t.Text.Contains("Andere Fassung")))
                throw new Exception("Existing registration was not shown as belonging to another version.");
            toggle.IsChecked = false;
            SetReducedMotion(true);
            toggle.IsChecked = true;
            if (toggle.Phase != 1 || qaStartupCommand != WindowsStartup.CommandFor(typeof(NotchWindow).Assembly.Location))
                throw new Exception("Explicit switch to this version failed with reduced motion.");
            CaptureWindow(settings, "preview-startup-on.png");
            toggle.IsChecked = false;
            settings.Close();
            Dispatcher.Invoke(DispatcherPriority.ContextIdle, new Action(delegate { }));
            results.Add("PASS: Native General autostart UI, quoted path, immediate enable/remove, rollback, version replacement, animation and reduced motion. Fixture registration only.");
        }
    }
}
