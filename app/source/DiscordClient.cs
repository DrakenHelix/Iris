using System;using System.Diagnostics;using System.IO;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
namespace Iris {
    public static class DiscordClient {
        public static DiscordReply Request(string operation,CancellationToken token){return Request(operation,token,false);}
        public static DiscordReply Request(string operation,CancellationToken token,bool qa){
            if(!DiscordControl.ValidOperation(operation))return DiscordReply.Of("unavailable",L.T("Ungültige Discord-Aktion."));token.ThrowIfCancellationRequested();
            string binary=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Iris.DiscordHost.exe");if(!File.Exists(binary))return DiscordReply.Of("unavailable",L.T("Iris.DiscordHost.exe fehlt. Entpacke bitte die vollständige Iris-Ausgabe."));
            var start=new ProcessStartInfo(binary,(qa?"--qa ":"")+(L.Language=="en"?"--english":"")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory};
            using(var process=Process.Start(start)){if(process==null)return DiscordReply.Of("unavailable",L.T("Discord-Helfer konnte nicht gestartet werden."));var json=new JavaScriptSerializer{MaxJsonLength=512*1024};var writeGate=new object();process.StandardInput.WriteLine(json.Serialize(new DiscordRequest{Operation=operation}));process.StandardInput.Flush();
                Action cancel=delegate{lock(writeGate)try{process.StandardInput.WriteLine("cancel");process.StandardInput.Flush();}catch{}};
                var output=Task.Factory.StartNew(delegate{var buffer=new StringBuilder();int value;while((value=process.StandardOutput.Read())>=0){if(buffer.Length>=512*1024)throw new InvalidDataException(L.T("Discord-Rückmeldung ist zu groß."));buffer.Append((char)value);}return buffer.ToString();});var errors=process.StandardError.ReadToEndAsync();
                using(token.Register(cancel)){if(!process.WaitForExit(9500)){cancel();output.ContinueWith(t=>{var ignored=t.Exception;});errors.ContinueWith(t=>{var ignored=t.Exception;});return DiscordReply.Of("unconfirmed",L.T("Discord-Helfer antwortet nicht. Ein Schaltauftrag könnte bereits übergeben sein; in Discord prüfen."));}
                    try{if(!output.Wait(500)||process.ExitCode!=0)return DiscordReply.Of("unconfirmed",L.T("Discord-Rückmeldung unterbrochen. Zustand in Discord prüfen."));var ignored=errors.Exception;var result=json.Deserialize<DiscordReply>(output.Result);if(result==null||result.Version!=1||result.Channels==null||result.Channels.Count>512)throw new InvalidDataException();return result;}catch{return DiscordReply.Of("unconfirmed",L.T("Ungültige Discord-Rückmeldung. Zustand in Discord prüfen."));}}
            }
        }
        public static AddonResult Result(DiscordReply reply){if(reply.Code=="ok")return string.IsNullOrEmpty(reply.ObservedState)?AddonResult.Of(AddonResultCode.Succeeded,reply.Message):AddonResult.State(reply.Message,reply.ObservedState);return AddonResult.Of(reply.Code=="busy"?AddonResultCode.Busy:reply.Code=="unconfirmed"||reply.Code=="cancelled"&&reply.Sent?AddonResultCode.Unconfirmed:reply.Code=="cancelled"?AddonResultCode.Cancelled:AddonResultCode.Unavailable,reply.Message);}
    }
    public sealed partial class NativeAddonHost {
        AddonResult RunDiscord(AddonAction action,CancellationToken token){
            if(action.Kind=="discord-channel"){string uri=DiscordControl.ChannelProtocol(action.Target);using(var key=Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"discord\shell\open\command")){if(key==null)return AddonResult.Of(AddonResultCode.Unavailable,L.T("Discord-Desktopprotokoll fehlt. Installiere bzw. repariere die Discord-Desktop-App."));}token.ThrowIfCancellationRequested();Launch(uri,"","");return AddonResult.Of(AddonResultCode.Succeeded,L.T("Kanalöffnung in der Discord-Desktop-App angefordert"));}
            string operation=action.Kind=="discord-mute"?"mute":action.Kind=="discord-deafen"?"deafen":action.Kind=="discord-share"?"share":"";
            return DiscordClient.Result(DiscordClient.Request(operation,token));
        }
    }
}
