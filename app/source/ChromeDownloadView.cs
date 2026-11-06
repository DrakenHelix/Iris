using System;
using System.Linq;
using System.Windows.Automation;

namespace Iris {
    sealed class ChromeReadTrace {
        public int ChromeWindows, UiaPages, UiaBars, UiaLines, MsaaPages, MsaaLines;
        public string Route;
    }

    // Invoked by the existing isolated, time-bounded progress host.
    static class ChromeDownloadView {
        public static SteamCounterValue Read(AutomationElement window, ChromeReadTrace trace) {
            var documents = window.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var document = documents.Cast<AutomationElement>().FirstOrDefault(node =>
                !node.Current.IsOffscreen && ChromeDownloadCounter.IsDownloadsPage(ReadValue(node)));
            if (document == null && HasDownloadsAddress(window)) {
                document = documents.Cast<AutomationElement>().FirstOrDefault(node =>
                    !node.Current.IsOffscreen
                    && new[] { "Downloads", "Download history", "Downloadverlauf" }
                        .Contains(node.Current.Name, StringComparer.OrdinalIgnoreCase));
            }
            if (document == null) return null;
            if (trace != null) trace.UiaPages++;

            var list = document.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "downloadsList")) ?? document;
            var bars = list.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ProgressBar))
                .Cast<AutomationElement>().Where(node => !node.Current.IsOffscreen).Take(2).ToArray();
            if (trace != null) trace.UiaBars += bars.Length;

            double? fraction = null;
            object pattern;
            if (bars.Length == 1 && bars[0].TryGetCurrentPattern(RangeValuePattern.Pattern, out pattern)) {
                var range = ((RangeValuePattern)pattern).Current;
                if (range.Maximum > range.Minimum) fraction = (range.Value - range.Minimum) / (range.Maximum - range.Minimum);
            }
            var lines = list.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                .Cast<AutomationElement>().Take(180).Where(node => !node.Current.IsOffscreen)
                .Select(node => node.Current.Name).Distinct().Select(text => ChromeDownloadCounter.Parse(text, DateTime.UtcNow, fraction))
                .Where(value => value != null).Take(2).ToArray();
            if (trace != null) trace.UiaLines += lines.Length;
            return lines.Length == 1 ? lines[0] : null;
        }

        static string ReadValue(AutomationElement element) {
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern)) return ((ValuePattern)pattern).Current.Value;
            return element.GetCurrentPropertyValue(ValuePattern.ValueProperty, true) as string;
        }

        static bool HasDownloadsAddress(AutomationElement window) {
            var candidates = window.FindAll(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new OrCondition(new PropertyCondition(ValuePattern.ValueProperty, "chrome://downloads/"),
                    new PropertyCondition(ValuePattern.ValueProperty, "chrome://downloads"))));
            foreach (AutomationElement candidate in candidates) {
                if (candidate.Current.IsOffscreen || candidate.Current.HasKeyboardFocus) continue;
                if (candidate.Current.ClassName != "OmniboxViewViews" && !new[] {
                    "Address and search bar", "Adress- und Suchleiste", "Adress- und Suchfeld", "Adressleiste und Suchfeld"
                }.Contains(candidate.Current.Name, StringComparer.OrdinalIgnoreCase)) continue;
                var parent = TreeWalker.ControlViewWalker.GetParent(candidate);
                for (int level = 0; parent != null && level < 12; level++) {
                    if (parent.Current.ControlType == ControlType.Document) break;
                    if (parent.Equals(window)) return true;
                    parent = TreeWalker.ControlViewWalker.GetParent(parent);
                }
            }
            return false;
        }
    }
}
