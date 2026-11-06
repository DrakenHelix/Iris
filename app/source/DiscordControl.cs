using System;using System.Collections.Generic;using System.Threading;using System.Text.RegularExpressions;
namespace Iris {
    public sealed class DiscordRequest {public int Version=1;public string Operation="inspect";}
    public sealed class DiscordChannelChoice {public string Name="",Url="",ServerId="",ServerName="",Category="",Kind="";}
    public sealed class DiscordServerInfo {public string Id="",Name="";}
    public sealed class DiscordReply {
        public int Version=1;public string Code="unavailable",Message="",ObservedState="";public bool Sent;
        public List<DiscordChannelChoice> Channels=new List<DiscordChannelChoice>();
        public List<DiscordServerInfo> Servers=new List<DiscordServerInfo>();
        public static DiscordReply Of(string code,string message){return new DiscordReply{Code=code,Message=message};}
    }
    public sealed class DiscordSwitch {public string Scope,Identity;public bool? State;public bool Enabled;public object Native;}
    public interface IDiscordSurface {
        DiscordSwitch Read(string operation,string scope);
        void Toggle(string operation,DiscordSwitch control,CancellationToken token,Action sent);
        DiscordReply Inspect(CancellationToken token);
        void Share(CancellationToken token,Action sent);
    }
    public static class DiscordControl {
        public static bool ValidOperation(string value){return value=="inspect"||value=="mute"||value=="deafen"||value=="share";}
        public static bool ChannelUrl(string value){Uri url;return value!=null&&value.Length<=2048&&Uri.TryCreate(value,UriKind.Absolute,out url)&&url.Scheme=="https"&&url.Host=="discord.com"&&url.IsDefaultPort&&url.UserInfo.Length==0&&url.Query.Length==0&&url.Fragment.Length==0&&Regex.IsMatch(url.AbsolutePath,@"^/channels/(?:[0-9]{1,20}|@me)(?:/[0-9]{1,20})?/?$");}
        // Loading an older deck must retain formerly accepted references. The
        // stricter rule above still applies to saving and every execution.
        public static bool StoredChannelUrl(string value){Uri url;return value!=null&&Uri.TryCreate(value,UriKind.Absolute,out url)&&url.Scheme=="https"&&url.Host=="discord.com"&&Regex.IsMatch(url.AbsolutePath,@"^/channels/(?:[0-9]+|@me)(?:/[0-9]+)?/?$");}
        public static string ChannelProtocol(string value){if(!ChannelUrl(value))throw new InvalidOperationException(L.T("Wähle einen Discord-Kanal aus."));return "discord://-"+new Uri(value).AbsolutePath.TrimEnd('/');}
        public static DiscordReply Execute(DiscordRequest request,IDiscordSurface surface,CancellationToken token,Action sent){
            if(request==null||request.Version!=1||!ValidOperation(request.Operation))return DiscordReply.Of("unavailable",L.T("Ungültiger Discord-Auftrag."));
            bool invoked=false;Action mark=delegate{invoked=true;if(sent!=null)sent();};
            try{token.ThrowIfCancellationRequested();string op=request.Operation;if(op=="inspect")return surface.Inspect(token);
                if(op=="share"){surface.Share(token,mark);var share=DiscordReply.Of("ok",L.T("Discord-Freigabeauswahl angefordert. Bildschirm und Freigabe bestätigst du selbst in Discord."));share.Sent=invoked;return share;}
                var before=surface.Read(op,null);if(before==null||!before.State.HasValue||!before.Enabled)return DiscordReply.Of("unavailable",L.T("Discord-Schalter nicht eindeutig lesbar. Öffne das Discord-Hauptfenster; beide Apps müssen ohne Administratorrechte laufen."));
                token.ThrowIfCancellationRequested();surface.Toggle(op,before,token,mark);
                var until=DateTime.UtcNow.AddMilliseconds(1600);while(true){token.ThrowIfCancellationRequested();var after=surface.Read(op,before.Scope);if(after!=null&&after.State.HasValue&&after.State.Value!=before.State.Value){var result=DiscordReply.Of("ok",op=="mute"?(after.State.Value?L.T("Discord-Mikrofon stumm"):L.T("Discord-Mikrofon an")):(after.State.Value?L.T("Discord-Ein- und Ausgabe stumm"):L.T("Discord-Ein- und Ausgabe an")));result.ObservedState=op=="mute"?(after.State.Value?"muted":"unmuted"):(after.State.Value?"deafened":"undeafened");result.Sent=invoked;return result;}if(DateTime.UtcNow>=until)break;if(token.WaitHandle.WaitOne(40))token.ThrowIfCancellationRequested();}
                return new DiscordReply{Code="unconfirmed",Sent=invoked,Message=L.T("Discord-Schalter betätigt, aber der neue Zustand wurde nicht bestätigt. In Discord prüfen; Iris schaltet nicht automatisch erneut.")};
            }catch(OperationCanceledException){return new DiscordReply{Code="cancelled",Sent=invoked,Message=invoked?L.T("Abgebrochen. Der bereits übergebene Discord-Schaltbefehl kann wirksam sein; in Discord prüfen."):L.T("Abgebrochen; kein Discord-Schaltbefehl übergeben.")};}
            catch(Exception ex){return new DiscordReply{Code=invoked?"unconfirmed":"unavailable",Sent=invoked,Message=invoked?L.T("Discord hat nach dem Schaltauftrag keine sichere Rückmeldung geliefert. In Discord prüfen; kein automatischer zweiter Versuch."):ex.Message};}
        }
    }
    // Used only by explicit QA hosts. No native target or keyboard fallback.
    public sealed class DiscordFixtureSurface : IDiscordSurface {
        public bool? Mute=false,Deafen=false;public bool Enabled=true,Confirm=true,ChangeBeforeInvoke,ThrowAfterInvoke,HangBeforeInvoke,HangAfterInvoke;public int Calls;public string Scope="fixture";
        public DiscordSwitch Read(string op,string scope){if(HangBeforeInvoke)Thread.Sleep(Timeout.Infinite);return scope!=null&&scope!=Scope?null:new DiscordSwitch{Scope=Scope,Identity=op,State=op=="mute"?Mute:Deafen,Enabled=Enabled};}
        public void Toggle(string op,DiscordSwitch control,CancellationToken token,Action sent){token.ThrowIfCancellationRequested();if(ChangeBeforeInvoke)throw new InvalidOperationException(L.T("Discord-Zustand hat sich geändert; erneut prüfen."));sent();Calls++;if(HangAfterInvoke)Thread.Sleep(Timeout.Infinite);if(ThrowAfterInvoke)throw new InvalidOperationException("Fixture interruption");if(Confirm){if(op=="mute")Mute=!Mute;else Deafen=!Deafen;}}
        public DiscordReply Inspect(CancellationToken token){token.ThrowIfCancellationRequested();var r=DiscordReply.Of("ok",L.T("Discord-Prüfdaten; keine echte Discord-Schaltung."));r.Channels.Add(new DiscordChannelChoice{Name=L.T("Beispielkanal"),Url="https://discord.com/channels/100/200"});return r;}
        public void Share(CancellationToken token,Action sent){token.ThrowIfCancellationRequested();sent();Calls++;}
    }
}
