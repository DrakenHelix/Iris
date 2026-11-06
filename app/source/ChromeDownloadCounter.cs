using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Iris {
    public static class ChromeDownloadCounter {
        public const string TargetId = "chromium-file-activity";
        const string Number = @"[0-9]+(?:[.,][0-9]+)?";
        const string Unit = @"B|KB|MB|GB|TB";
        static readonly Regex ProgressLine = new Regex(
            @"^\s*(?<speed>" + Number + @")\s*(?:" + Unit + @")/s\s*[-–—]\s*"
            + @"(?<received>" + Number + @")\s*(?<receivedUnit>" + Unit + @")\s+(?:von|of)\s+"
            + @"(?<total>" + Number + @")\s*(?<totalUnit>" + Unit + @")(?=\s*(?:[,·]|$))",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Stopped = new Regex(@"\b(Pausiert|Paused|Fortsetzen|Resume|Abgebrochen|Canceled|Cancelled|Interrupted|Unterbrochen)\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsDownloadsPage(string address) {
            return string.Equals(address, "chrome://downloads/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(address, "chrome://downloads", StringComparison.OrdinalIgnoreCase);
        }

        public static SteamCounterValue Parse(string text, DateTime now, double? reportedFraction = null) {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 240 || Stopped.IsMatch(text)) return null;
            var match = ProgressLine.Match(text);
            if (!match.Success || Amount(match.Groups["speed"].Value) <= 0) return null;
            double received = Bytes(match.Groups["received"].Value, match.Groups["receivedUnit"].Value);
            double total = Bytes(match.Groups["total"].Value, match.Groups["totalUnit"].Value);
            if (received < 0 || total <= received || total > 1e16) return null;
            if (reportedFraction.HasValue && (double.IsNaN(reportedFraction.Value) || reportedFraction.Value < 0
                || reportedFraction.Value >= 1 || Math.Abs(reportedFraction.Value - received / total) > .03))
                reportedFraction = null;
            return new SteamCounterValue {
                Id = TargetId, At = now.ToString("o"), Received = received, Total = total,
                Rounded = true, Fraction = reportedFraction
            };
        }

        static double Amount(string text) {
            double value;
            return double.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
                && !double.IsInfinity(value) ? value : -1;
        }

        static double Bytes(string number, string unit) {
            int power = Array.IndexOf(new[] { "B", "KB", "MB", "GB", "TB" }, unit.ToUpperInvariant());
            return Amount(number) * Math.Pow(1024, power);
        }
    }
}
