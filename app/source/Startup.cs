using System;
using System.Linq;
using System.Windows.Controls;

namespace Iris {
    public sealed partial class NotchWindow {
        Grid StartupSettingRow() {
            string currentCommand = WindowsStartup.CommandFor(typeof(NotchWindow).Assembly.Location);
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
                } else if (string.Equals(registered, currentCommand, StringComparison.OrdinalIgnoreCase)) {
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
                    string command = enabled ? currentCommand : null;
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
