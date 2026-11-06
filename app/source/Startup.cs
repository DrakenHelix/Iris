using System;
using System.Linq;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Iris {
    static class WindowsStartup {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Entry = "IrisNotch";

        public static string Command {
            get { return "\"" + typeof(NotchWindow).Assembly.Location + "\""; }
        }

        public static string Read() {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) {
                return key == null ? null : key.GetValue(Entry, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }

        public static void Write(string command) {
            if (command == null) {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) {
                    if (key != null) key.DeleteValue(Entry, false);
                }
                return;
            }
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (key == null) throw new InvalidOperationException(L.T("Der Windows-Autostart ist nicht erreichbar."));
                key.SetValue(Entry, command, RegistryValueKind.String);
            }
        }
    }

    public sealed partial class NotchWindow {
        Grid StartupSettingRow() {
            var row = (Grid)CompactAppearanceToggle(L.T("Mit Windows starten"), L.T("Beim Anmelden in deinem Windows-Konto"), false, null);
            var toggle = row.Children.OfType<FluidToggle>().Single();
            var detail = ((StackPanel)row.Children[0]).Children.OfType<TextBlock>().Last();

            string registered;
            try {
                registered = uiTesting ? qaStartupCommand : WindowsStartup.Read();
            } catch (Exception ex) {
                toggle.IsEnabled = false;
                detail.Text = L.T("Windows-Autostart konnte nicht gelesen werden.");
                row.ToolTip = ex.Message;
                return row;
            }

            Action updateDetail = delegate {
                if (string.IsNullOrEmpty(registered)) {
                    detail.Text = L.T("Beim Anmelden in deinem Windows-Konto");
                } else if (string.Equals(registered, WindowsStartup.Command, StringComparison.OrdinalIgnoreCase)) {
                    detail.Text = L.T("Diese Fassung ist für die Anmeldung eingetragen.");
                } else {
                    detail.Text = L.T("Andere Fassung eingetragen. Aus- und einschalten.");
                }
            };
            toggle.IsChecked = !string.IsNullOrEmpty(registered);
            updateDetail();
            bool restoring = false;
            Action<bool> change = enabled => {
                if (restoring) return;
                try {
                    string command = enabled ? WindowsStartup.Command : null;
                    if (command != null && command.Length > 260)
                        throw new InvalidOperationException(L.T("Der Programmordner ist für den Windows-Autostart zu lang."));
                    if (uiTesting) WriteStartupFixture(command);
                    else WindowsStartup.Write(command);
                    registered = command;
                    row.ToolTip = null;
                    updateDetail();
                } catch (Exception ex) {
                    restoring = true;
                    toggle.IsChecked = !string.IsNullOrEmpty(registered);
                    restoring = false;
                    detail.Text = L.T("Autostart konnte nicht geändert werden.");
                    row.ToolTip = ex.Message;
                }
            };
            toggle.Checked += delegate { change(true); };
            toggle.Unchecked += delegate { change(false); };
            return row;
        }
    }
}
