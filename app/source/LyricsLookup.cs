using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Iris {
    public static partial class LyricsService {
        static readonly SemaphoreSlim requestGate=new SemaphoreSlim(1,1);
        static DateTime nextRequest,rateLimitUntil;
        public static Task<LyricsData> Get(string title,string artist,string album,double duration,CancellationToken token){return GetUsing(client,title,artist,album,duration,token);}
        internal static async Task<LyricsData> GetUsing(HttpClient http,string title,string artist,string album,double duration,CancellationToken token){
            var keys=Normalize(title,artist);if(string.IsNullOrWhiteSpace(keys[0])||string.IsNullOrWhiteSpace(keys[1]))return null;
            var exact=await Fetch(http,title,artist,album,duration,token).ConfigureAwait(false);if(HasContent(exact))return CheckTiming(exact,duration);
            string principal=PrincipalArtist(keys[1]);
            if(title!=keys[0]||artist!=principal||!string.IsNullOrWhiteSpace(album)||duration>0){var relaxed=await Fetch(http,keys[0],principal,"",0,token).ConfigureAwait(false);if(HasContent(relaxed))return CheckTiming(relaxed,duration);}
            string search="https://lrclib.net/api/search?track_name="+Uri.EscapeDataString(keys[0])+"&artist_name="+Uri.EscapeDataString(principal);
            string json=await Request(http,search,token).ConfigureAwait(false);var found=SelectSearchResult(json,keys[0],keys[1],album,duration);if(found!=null)return found;
            // Search by title as well; collaborations and edition labels can defeat the artist filter.
            json=await Request(http,"https://lrclib.net/api/search?track_name="+Uri.EscapeDataString(keys[0]),token).ConfigureAwait(false);
            return SelectSearchResult(json,keys[0],keys[1],album,duration);
        }
        public static string[] Normalize(string title,string artist){
            string track=CleanTrack(title);
            string name=Regex.Replace(artist??"",@"(?:VEVO|\s*-\s*Topic)$","",RegexOptions.IgnoreCase).Trim();
            int albumSeparator=name.IndexOf(" — ",StringComparison.Ordinal);if(albumSeparator>0)name=name.Substring(0,albumSeparator).Trim();
            int separator=track.IndexOf(" - ",StringComparison.Ordinal);
            if(separator>0&&(name.Length==0||Canonical(track.Substring(0,separator))==Canonical(name))){name=track.Substring(0,separator).Trim();track=track.Substring(separator+3).Trim();}
            return new[]{track,name};
        }
        static string CleanTrack(string title){string track=Regex.Replace(title??"",@"\s*[\[(](?:official|lyrics?|audio|music video|visuali[sz]er)[^)\]]*[)\]]","",RegexOptions.IgnoreCase);return Regex.Replace(track,@"\s*[\[(]?(?:feat\.?|ft\.?|featuring)\s+.*$","",RegexOptions.IgnoreCase).Trim();}
        static string PrincipalArtist(string artist){return Regex.Split(artist??"",@"\s+(?:feat\.?|ft\.?|featuring)\s+|,\s+|\s+&\s+|\s+and\s+",RegexOptions.IgnoreCase)[0].Trim();}
        static string Canonical(string text){
            var builder=new StringBuilder();foreach(char c in (text??"").Normalize(NormalizationForm.FormD))if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)builder.Append(char.IsLetterOrDigit(c)?char.ToLowerInvariant(c):' ');
            return Regex.Replace(builder.ToString(),@"\s+"," ").Trim();
        }
        static string TrackKey(string title){return Canonical(Regex.Replace(CleanTrack(title),@"\s*(?:[\[(](?:(?:\d{4}\s+)?remaster(?:ed)?|deluxe|anniversary)[^)\]]*[)\]]| - (?:\d{4} )?remaster(?:ed)?.*$)","",RegexOptions.IgnoreCase));}
        static bool HasContent(LyricsData data){return data!=null&&(data.Instrumental||data.Lines.Count>0||!string.IsNullOrWhiteSpace(data.Plain));}
        static LyricsData CheckTiming(LyricsData data,double duration){
            if(data!=null&&duration>0&&data.Duration>0&&Math.Abs(duration-data.Duration)>4){if(string.IsNullOrWhiteSpace(data.Plain))data.Plain=string.Join("\n",data.Lines.Select(l=>l.Text));data.Lines.Clear();}
            return data;
        }
        static LyricsData Decode(Dictionary<string,object> data){
            return new LyricsData {Track=data.ContainsKey("trackName")?Convert.ToString(data["trackName"]):"",Artist=data.ContainsKey("artistName")?Convert.ToString(data["artistName"]):"",Album=data.ContainsKey("albumName")?Convert.ToString(data["albumName"]):"",Duration=data.ContainsKey("duration")?Convert.ToDouble(data["duration"],CultureInfo.InvariantCulture):0,Instrumental=data.ContainsKey("instrumental")&&Convert.ToBoolean(data["instrumental"]),Plain=data.ContainsKey("plainLyrics")?Convert.ToString(data["plainLyrics"]):"",Lines=Parse(data.ContainsKey("syncedLyrics")?Convert.ToString(data["syncedLyrics"]):"")};
        }
        internal static LyricsData SelectSearchResult(string json,string title,string artist,string album,double duration){
            if(string.IsNullOrWhiteSpace(json))return null;var entries=new JavaScriptSerializer().Deserialize<List<Dictionary<string,object>>>(json);LyricsData best=null;double score=double.NegativeInfinity;
            string track=TrackKey(title),name=Canonical(artist),principal=Canonical(PrincipalArtist(artist));
            foreach(var entry in entries){var item=Decode(entry);if(!HasContent(item)||TrackKey(item.Track)!=track)continue;
                string candidate=Canonical(item.Artist),candidatePrincipal=Canonical(PrincipalArtist(item.Artist));if(name!=candidate&&principal!=candidate&&name!=candidatePrincipal&&principal!=candidatePrincipal)continue;
                double diff=duration>0&&item.Duration>0?Math.Abs(duration-item.Duration):0;
                double rank=(name==candidate?10:4)+(Canonical(album)==Canonical(item.Album)?4:0)+(item.Lines.Count>0?8:0)+(diff<=2?24:diff<=4?18:diff<=10?8:-Math.Min(40,diff/2));
                if(rank>score){score=rank;best=item;}
            }
            return CheckTiming(best,duration);
        }
        static async Task<LyricsData> Fetch(HttpClient http,string title,string artist,string album,double duration,CancellationToken token){
            if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(artist))return null;
            string url="https://lrclib.net/api/get?track_name="+Uri.EscapeDataString(title)+"&artist_name="+Uri.EscapeDataString(artist);
            if(!string.IsNullOrWhiteSpace(album))url+="&album_name="+Uri.EscapeDataString(album);
            if(duration>=1&&duration<=3600)url+="&duration="+duration.ToString("F2",CultureInfo.InvariantCulture);
            string json=await Request(http,url,token).ConfigureAwait(false);return json==null?null:Decode(new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json));
        }
        static async Task<string> Request(HttpClient http,string url,CancellationToken token){
            await requestGate.WaitAsync(token).ConfigureAwait(false);
            try{
                if(DateTime.UtcNow<rateLimitUntil)throw new HttpRequestException(L.T("Die Liedtextquelle begrenzt Anfragen. Bitte später erneut versuchen."));
                var delay=nextRequest-DateTime.UtcNow;if(delay>TimeSpan.Zero)await Task.Delay(delay,token).ConfigureAwait(false);
                using(var response=await http.GetAsync(url,token).ConfigureAwait(false)){
                    nextRequest=DateTime.UtcNow.AddMilliseconds(250);
                    if(response.StatusCode==HttpStatusCode.NotFound)return null;
                    if((int)response.StatusCode==429||(int)response.StatusCode==503){var retry=response.Headers.RetryAfter;rateLimitUntil=retry!=null&&retry.Date.HasValue?retry.Date.Value.UtcDateTime:DateTime.UtcNow.Add(retry!=null&&retry.Delta.HasValue?retry.Delta.Value:TimeSpan.FromSeconds(30));}
                    response.EnsureSuccessStatusCode();var json=await response.Content.ReadAsStringAsync().ConfigureAwait(false);token.ThrowIfCancellationRequested();return json;
                }
            }finally{requestGate.Release();}
        }
    }
}
