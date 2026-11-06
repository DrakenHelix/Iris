using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Text.RegularExpressions;
namespace Iris {
    public sealed class SteamCounterTarget {public string Id,Name;}
    public sealed class SteamCounterRequest {public SteamCounterTarget[] Items=new SteamCounterTarget[0];public bool Stop;}
    public sealed class SteamCounterValue {public string Id,At;public double Received=-1,Total=-1;public double? Fraction;public bool Rounded;}
    public sealed class SteamCounterReply {public SteamCounterValue[] Items=new SteamCounterValue[0];}
    public static class SteamProgressCounter {
        static readonly Regex pair=new Regex(@"(?<a>[0-9]+(?:[.,][0-9]+)*)\s*(?<u>KiB|MiB|GiB|TiB|KB|MB|GB|TB|Bytes|Byte|B)\s*(?:/|von|of)\s*(?<b>[0-9]+(?:[.,][0-9]+)*)\s*(?<v>KiB|MiB|GiB|TiB|KB|MB|GB|TB|Bytes|Byte|B)(?![/a-z])",RegexOptions.Compiled|RegexOptions.IgnoreCase);
        static double Quantity(string text,string unit){text=Regex.Replace(text,@"\s","");bool bytes=unit.Equals("B",StringComparison.OrdinalIgnoreCase)||unit.StartsWith("Byte",StringComparison.OrdinalIgnoreCase);if(bytes&&Regex.IsMatch(text,@"^[0-9]{1,3}(?:[.,][0-9]{3})+$"))text=text.Replace(".","").Replace(",","");else if(text.Contains(",")&&text.Contains(".")){if(text.LastIndexOf(',')>text.LastIndexOf('.'))text=text.Replace(".","").Replace(',','.');else text=text.Replace(",","");}else text=text.Replace(',','.');double amount;if(!double.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out amount))return -1;string normalized=unit.ToUpperInvariant();int power=normalized.StartsWith("K")?1:normalized.StartsWith("M")?2:normalized.StartsWith("G")?3:normalized.StartsWith("T")?4:0;return amount*Math.Pow(normalized.Contains("I")?1024:1000,power);}
        public static SteamCounterValue Parse(string id,IEnumerable<string> names,double? nativeFraction,DateTime now){var labels=names.Where(n=>!string.IsNullOrWhiteSpace(n)&&n.Length<=240).ToArray();string text=string.Join(" ",labels);if(Regex.IsMatch(text,@"\b(Eingeplant|Scheduled|Pausiert|Paused|Fortsetzen|Resume|Jetzt herunterladen|Download now|Rabatt|Discount)\b",RegexOptions.IgnoreCase))return null;var matches=pair.Matches(text).Cast<Match>().ToArray();bool transfer=Regex.IsMatch(text,@"\b(Download|Downloading|Herunterladen|Heruntergeladen|Netzwerk|Network)\b",RegexOptions.IgnoreCase);if(matches.Length==1){var m=matches[0];double received=Quantity(m.Groups["a"].Value,m.Groups["u"].Value),total=Quantity(m.Groups["b"].Value,m.Groups["v"].Value);if(received<0||total<=0||received>=total||total>1e16)return null;return new SteamCounterValue{Id=id,Received=received,Total=total,Rounded=!Regex.IsMatch(m.Groups["u"].Value,@"^(?:B|Bytes?)$",RegexOptions.IgnoreCase),At=now.ToString("o")};}
            // A directly exposed provider percentage is kept separately. Never
            // manufacture byte totals of 100 from a percentage-only control.
            if(matches.Length>1)return null;
            if(transfer&&!nativeFraction.HasValue){var percents=Regex.Matches(text,@"(?<![0-9.,])([0-9]{1,2}(?:[.,][0-9]+)?)\s*%");double percent;if(percents.Count==1&&double.TryParse(percents[0].Groups[1].Value.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out percent))nativeFraction=percent/100;}
            if(transfer&&nativeFraction.HasValue&&nativeFraction>=0&&nativeFraction<1)return new SteamCounterValue{Id=id,Fraction=nativeFraction,At=now.ToString("o")};return null;
        }
    }
}
