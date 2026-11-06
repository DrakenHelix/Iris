using System;using System.IO;using System.Security.Cryptography;using System.Text;using System.Text.RegularExpressions;using System.Web.Script.Serialization;
namespace Iris {
    public sealed class AddonConnections {
        public string ObsEndpoint="ws://127.0.0.1:4455",ObsPassword="",SpotifyClientId="",SpotifyAccess="",SpotifyRefresh="";
        public DateTime SpotifyExpires=DateTime.MinValue;
    }
    public static class AddonConnectionStore {
        static readonly object gate=new object();static readonly byte[] entropy=Encoding.UTF8.GetBytes("Iris.Integrations.v1");
        static string FileName(string directory){if(string.IsNullOrEmpty(directory))throw new InvalidOperationException(L.T("Lokales Datenverzeichnis fehlt."));return Path.Combine(directory,"integrations.protected");}
        public static AddonConnections Read(string directory){lock(gate){string file=FileName(directory);if(!File.Exists(file))return new AddonConnections();if(new FileInfo(file).Length>65536)throw new InvalidDataException(L.T("Verbindungskonfiguration ist zu groß."));try{return new JavaScriptSerializer().Deserialize<AddonConnections>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),entropy,DataProtectionScope.CurrentUser)))??new AddonConnections();}catch(Exception ex){throw new InvalidDataException(L.T("Gespeicherte Verbindungen sind für dieses Windows-Konto nicht lesbar. Einstellungen erneut hinterlegen."),ex);}}}
        public static void Update(string directory,Action<AddonConnections> update){lock(gate){var value=Read(directory);update(value);Validate(value);Directory.CreateDirectory(directory);var bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)),entropy,DataProtectionScope.CurrentUser);string file=FileName(directory),temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}}}
        public static void Validate(AddonConnections value){Uri endpoint;if(!Uri.TryCreate(value.ObsEndpoint,UriKind.Absolute,out endpoint)||endpoint.Scheme!="ws"||!endpoint.IsLoopback||endpoint.UserInfo.Length>0||endpoint.Query.Length>0||endpoint.Fragment.Length>0||endpoint.AbsolutePath!="/")throw new InvalidDataException(L.T("OBS: lokalen WebSocket-Zugang verwenden, z. B. ws://127.0.0.1:4455."));if((value.ObsPassword??"").Length>512)throw new InvalidDataException(L.T("OBS-Passwort ist zu lang."));if(!string.IsNullOrEmpty(value.SpotifyClientId)&&!Regex.IsMatch(value.SpotifyClientId,@"^[a-fA-F0-9]{32}$"))throw new InvalidDataException(L.T("Spotify: die 32-stellige Client-ID aus dem Dashboard angeben, kein Secret."));}
    }
    public static class IntegrationJson {
        public static object Get(object obj,string name){var map=obj as System.Collections.Generic.Dictionary<string,object>;object value;return map!=null&&map.TryGetValue(name,out value)?value:null;}
        public static string Text(object obj,string name){return Convert.ToString(Get(obj,name),System.Globalization.CultureInfo.InvariantCulture);}
        public static bool Bool(object obj,string name){return Get(obj,name) is bool&&(bool)Get(obj,name);}
        public static double Number(object obj,string name){object v=Get(obj,name);return v==null?0:Convert.ToDouble(v,System.Globalization.CultureInfo.InvariantCulture);}
        public static object[] Array(object obj,string name){var v=Get(obj,name);var a=v as object[];if(a!=null)return a;var list=v as System.Collections.ArrayList;return list==null?new object[0]:list.ToArray();}
        public static System.Collections.Generic.Dictionary<string,object> Parse(string json){return new JavaScriptSerializer{MaxJsonLength=1024*1024,RecursionLimit=40}.DeserializeObject(json) as System.Collections.Generic.Dictionary<string,object>??new System.Collections.Generic.Dictionary<string,object>();}
        public static string Stringify(object value){return new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(value);}
    }
}
