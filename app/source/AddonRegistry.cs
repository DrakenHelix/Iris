using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Iris {
    public enum AddonActionType {Program,Shortcut,System,Integration,Url,Macro,Script,Profile,Submenu,Conditional}
    public enum AddonConfiguration {None,Program,Folders,Website,Shortcut,SteamId,Process,AppShortcut,DiscordChannel,Text}
    public enum AddonRoute {Legacy,Keys,Uri,Platform,Unsupported}
    public enum AddonPlatformOperation {None,OpenDiscord,OpenChatGpt,VolumeUp,VolumeDown,VolumeMute,MicrophoneMute,Minimize,Maximize,SnapLeft,SnapRight,Topmost,Lock,Shutdown,Restart,CancelShutdown,Sleep,CloseProcess,PrepareChatDraft,OpenIrisMixer,Integration,Discord}
    public sealed class AddonDefinition {
        public string Kind,PackageId,Id,Title,Description,Category,Icon,Dependency="",Preset="",UnsupportedReason="";
        public string DefaultProcess="",DefaultShortcut="";public ushort[] Keys;
        public AddonActionType Type;public AddonConfiguration Configuration;public AddonRoute Route;public AddonPlatformOperation Operation;
        public string InputLabel="";public bool ShowResult;public bool Confirm;public bool Extended {get{return Route!=AddonRoute.Legacy;}}
        public AddonAction Template(){return new AddonAction{Id=Id,Title=Title,Kind=Kind,Process=DefaultProcess,Shortcut=DefaultShortcut};}
    }
    public sealed class AddonAvailability {
        public bool CanConfigure=true,CanExecute;public string Message;
        public AddonAvailability(bool available,string message){CanExecute=available;Message=message;}
    }
    public sealed class AddonEnvironmentSnapshot {
        public readonly HashSet<string> Protocols=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Processes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool SpotifyConnected;public string DiscordTarget="",ChatGptTarget="",Note="";
    }
    public static partial class AddonRegistry {
        static readonly Dictionary<string,AddonDefinition> definitions=new Dictionary<string,AddonDefinition>(StringComparer.Ordinal);
        static readonly List<AddonPackage> modules=new List<AddonPackage>();
        public static IEnumerable<AddonDefinition> Definitions {get{return definitions.Values.ToArray();}}
        public static AddonDefinition Find(string kind){AddonDefinition value;return kind!=null&&definitions.TryGetValue(kind,out value)?value:null;}
        public static AddonPackage BuiltinPackage(string id){return modules.FirstOrDefault(package=>package.Id==id);}
        static AddonPackage Module(string id,string name,string category,string icon,string description){var p=new AddonPackage{Id=id,Name=name,Category=category,Icon=icon,Description=description,Version="2.0"};modules.Add(p);return p;}
        static AddonDefinition Add(AddonPackage p,string id,string kind,string title,string description,AddonActionType type,AddonConfiguration configuration,AddonRoute route,string icon){
            if(definitions.ContainsKey(kind))throw new InvalidOperationException(L.T("Doppelter Aktionstyp: ")+kind);
            var d=new AddonDefinition{PackageId=p.Id,Id=id,Kind=kind,Title=title,Description=description,Category=p.Category,Type=type,Configuration=configuration,Route=route,Icon=icon};definitions.Add(kind,d);p.Actions.Add(d.Template());return d;
        }
        static void Keys(AddonPackage p,string kind,string title,string description,params ushort[] keys){Add(p,kind,kind,title,description,AddonActionType.System,AddonConfiguration.None,AddonRoute.Keys,"\uE765").Keys=keys;}
        static void UriAction(AddonPackage p,string kind,string title,string description,string uri,string dependency){var d=Add(p,kind,kind,title,description,AddonActionType.Url,AddonConfiguration.None,AddonRoute.Uri,"\uE774");d.Preset=uri;d.Dependency=dependency;}
        static void SystemAction(AddonPackage p,string kind,string title,string description,AddonPlatformOperation op,bool confirm){var d=Add(p,kind,kind,title,description,AddonActionType.System,op==AddonPlatformOperation.CloseProcess?AddonConfiguration.Process:AddonConfiguration.None,AddonRoute.Platform,"\uE713");d.Operation=op;d.Confirm=confirm;}
        static void Unavailable(AddonPackage p,string kind,string title,string reason){var d=Add(p,kind,kind,title,reason,AddonActionType.Integration,AddonConfiguration.None,AddonRoute.Unsupported,"\uE711");d.UnsupportedReason=reason;}
        static AddonRegistry(){
            var windows=Module("iris.windows","Windows","System","\uE770",L.T("Audio, Fenster, Systemeinstellungen, Screenshot und Ordner. Riskante Aktionen fragen vorher nach."));
            Add(windows,"screenshot","screenshot","Screenshot",L.T("Windows-Bildschirmausschnitt auswählen; den Bereich bestimmst du selbst."),AddonActionType.System,AddonConfiguration.None,AddonRoute.Legacy,"\uE722");
            Add(windows,"folders","folders",L.T("Meine Ordner"),L.T("Bis zu acht Ordner öffnen."),AddonActionType.Program,AddonConfiguration.Folders,AddonRoute.Legacy,"\uE8B7");
            Keys(windows,"windows-desktop",L.T("Desktop anzeigen"),"Windows + D",0x5B,0x44);
            Keys(windows,"windows-clipboard",L.T("Zwischenablageverlauf"),"Windows + V",0x5B,0x56);
            Keys(windows,"windows-desktop-left",L.T("Vorheriger Desktop"),L.T("Windows + Ctrl + Links"),0x5B,0x11,0x25);
            Keys(windows,"windows-desktop-right",L.T("Nächster Desktop"),L.T("Windows + Ctrl + Rechts"),0x5B,0x11,0x27);
            Keys(windows,"windows-task-manager",L.T("Task-Manager"),"Ctrl + Shift + Esc",0x11,0x10,0x1B);
            Keys(windows,"windows-project",L.T("Anzeigemodus wählen"),L.T("Windows + P öffnet die Auswahl, ohne den Modus selbst zu wechseln."),0x5B,0x50);
            Keys(windows,"windows-recording",L.T("Aufnahme-Steuerung"),L.T("Windows + G öffnet die Xbox Game Bar, sofern installiert. Aufnahme dort selbst starten."),0x5B,0x47);
            SystemAction(windows,"windows-volume-up",L.T("System lauter (+5 %)"),L.T("Ändert ausdrücklich die Windows-Gesamtlautstärke, nicht den Playerregler."),AddonPlatformOperation.VolumeUp,false);
            SystemAction(windows,"windows-volume-down",L.T("System leiser (−5 %)"),L.T("Ändert ausdrücklich die Windows-Gesamtlautstärke."),AddonPlatformOperation.VolumeDown,false);
            SystemAction(windows,"windows-volume-mute",L.T("Systemton umschalten"),L.T("Liest und schaltet den tatsächlichen Stummzustand des Standardausgangs."),AddonPlatformOperation.VolumeMute,false);
            SystemAction(windows,"windows-app-volume",L.T("Apps im Iris-Mixer"),L.T("Öffnet den vorhandenen Iris-Mixer für die getrennte Lautstärke jeder Audioquelle."),AddonPlatformOperation.OpenIrisMixer,false);
            SystemAction(windows,"windows-microphone-mute",L.T("Standardmikrofon umschalten"),L.T("Schaltet den Windows-Standard-Kommunikationseingang. Apps mit eigenem Eingabegerät können abweichen."),AddonPlatformOperation.MicrophoneMute,false);
            SystemAction(windows,"windows-minimize",L.T("Fenster minimieren"),L.T("Das zuletzt aktive fremde Fenster; Iris wird nicht minimiert."),AddonPlatformOperation.Minimize,false);
            SystemAction(windows,"windows-maximize",L.T("Fenster maximieren"),L.T("Das zuletzt aktive fremde Fenster."),AddonPlatformOperation.Maximize,false);
            SystemAction(windows,"windows-snap-left",L.T("Fenster links"),L.T("Ordnet das überprüfte Zielfenster links an."),AddonPlatformOperation.SnapLeft,false);
            SystemAction(windows,"windows-snap-right",L.T("Fenster rechts"),L.T("Ordnet das überprüfte Zielfenster rechts an."),AddonPlatformOperation.SnapRight,false);
            SystemAction(windows,"windows-topmost",L.T("Fenster im Vordergrund"),L.T("Schaltet Immer im Vordergrund für das überprüfte Zielfenster."),AddonPlatformOperation.Topmost,false);
            SystemAction(windows,"windows-lock",L.T("Windows sperren"),L.T("Sperrt die aktuelle Windows-Sitzung."),AddonPlatformOperation.Lock,false);
            SystemAction(windows,"windows-shutdown",L.T("Herunterfahren"),L.T("Nur nach Bestätigung: Windows jetzt herunterfahren. Vorher alle Arbeiten speichern. Programme werden nicht zwangsweise geschlossen."),AddonPlatformOperation.Shutdown,true);
            SystemAction(windows,"windows-restart",L.T("Neu starten"),L.T("Nur nach Bestätigung: Windows jetzt neu starten. Vorher alle Arbeiten speichern. Programme werden nicht zwangsweise geschlossen."),AddonPlatformOperation.Restart,true);
            SystemAction(windows,"windows-cancel-shutdown",L.T("Herunterfahren abbrechen"),L.T("Bricht einen noch ausstehenden Windows-Neustart oder Shutdown ab."),AddonPlatformOperation.CancelShutdown,false);
            SystemAction(windows,"windows-sleep",L.T("Standby"),L.T("Nur nach Bestätigung: Windows in den Energiesparmodus versetzen."),AddonPlatformOperation.Sleep,true);
            SystemAction(windows,"windows-close-process",L.T("Programm kontrolliert schließen"),L.T("Nur nach Bestätigung: sichtbare Fenster dieses Prozesses um Schließen bitten. Kein erzwungenes Beenden."),AddonPlatformOperation.CloseProcess,true);
            foreach(var item in new[]{new[]{"settings",L.T("Einstellungen"),"ms-settings:"},new[]{"sound",L.T("Audiogeräte wählen"),"ms-settings:sound"},new[]{"wifi",L.T("WLAN-Einstellungen"),"ms-settings:network-wifi"},new[]{"bluetooth",L.T("Bluetooth-Einstellungen"),"ms-settings:bluetooth"},new[]{"energy",L.T("Energieeinstellungen"),"ms-settings:powersleep"},new[]{"nightlight",L.T("Nachtlicht-Einstellungen"),"ms-settings:nightlight"},new[]{"notifications",L.T("Benachrichtigungen"),"ms-settings:notifications"},new[]{"display",L.T("Anzeige und HDR"),"ms-settings:display"}})UriAction(windows,"windows-"+item[0],item[1],L.T("Öffnet die Windows-Auswahl. Die Einstellung wird dort von dir geändert."),item[2],"ms-settings");
            UriAction(windows,"windows-recycle-bin",L.T("Papierkorb öffnen"),L.T("Öffnet den Papierkorb; löscht keine Dateien."),"shell:RecycleBinFolder","");
            var discord=Module("iris.discord","Discord","Kommunikation","\uE720",L.T("Gezielte Desktop-Schalter mit Zustandsprüfung und Kanalauswahl aus Discord. Keine Tokens oder API-Schlüssel."));
            var open=Add(discord,"open","discord-open",L.T("Discord öffnen"),L.T("Startet die installierte Discord-Desktop-App."),AddonActionType.Program,AddonConfiguration.None,AddonRoute.Platform,"\uE8A5");open.Operation=AddonPlatformOperation.OpenDiscord;open.Dependency="app:discord";
            foreach(var item in new[]{new[]{"mute","discord-mute",L.T("Mikrofon umschalten"),L.T("Schaltet gezielt das Discord-Mikrofon. Erfolg erst nach ausgelesenem Zustandswechsel; kein Tastenkürzel.")},new[]{"deafen","discord-deafen",L.T("Ton umschalten"),L.T("Schaltet Discord-Ein- und Ausgabe. Erfolg erst nach ausgelesenem Zustandswechsel; kein Windows-Mute.")},new[]{"share","discord-share",L.T("Freigabe-Auswahl"),L.T("Öffnet die zugängliche Discord-Auswahl; startet keine Übertragung ohne deine Auswahl.")}}){var d=Add(discord,item[0],item[1],item[2],item[3],AddonActionType.Integration,AddonConfiguration.None,AddonRoute.Platform,"\uE720");d.Operation=AddonPlatformOperation.Discord;}
            var channel=Add(discord,"channel","discord-channel",L.T("Server oder Kanal öffnen"),L.T("Kanal aus dem Discord-Hauptfenster auswählen; später direkt in der Desktop-App öffnen."),AddonActionType.Integration,AddonConfiguration.DiscordChannel,AddonRoute.Platform,"\uE774");channel.Operation=AddonPlatformOperation.Discord;
            Unavailable(discord,"discord-status",L.T("Status / Kamera / Soundboard"),L.T("Keine freigegebene Integration eingerichtet. Eigene in Discord konfigurierte Kürzel sind über Eigene Aktionen möglich; kein bestätigter Zustand."));
            var steam=Module("iris.steam","Steam","Gaming","\uE7FC",L.T("Steam-Client und Spiele über das registrierte Steam-Protokoll öffnen."));
            var game=Add(steam,"game","steam",L.T("Spiel starten"),L.T("Installiertes Spiel nach Namen auswählen. Steam übernimmt Start und Anmeldung."),AddonActionType.Integration,AddonConfiguration.SteamId,AddonRoute.Legacy,"\uE7FC");game.Dependency="steam";
            foreach(var item in new[]{new[]{"main",L.T("Steam öffnen"),"main"},new[]{"library",L.T("Bibliothek öffnen"),"games"},new[]{"friends",L.T("Freundesliste öffnen"),"friends"},new[]{"downloads",L.T("Downloads öffnen"),"downloads"},new[]{"bigpicture",L.T("Big Picture öffnen"),"bigpicture"}})UriAction(steam,"steam-"+item[0],item[1],L.T("Öffnung im registrierten Steam-Client anfordern. Kein Spielstart ohne gewählte Spielaktion."),"steam://open/"+item[2],"steam");
            var custom=Module("iris.custom",L.T("Eigene Aktionen"),"Werkzeuge","\uE765",L.T("Programme, Websites und gezielte Tastenkürzel. Import und Bearbeiten führen nichts aus."));
            Add(custom,"program","program",L.T("Programm"),L.T("EXE mit optionalen Argumenten starten. Skript- und Befehlshosts sind nicht freigegeben."),AddonActionType.Program,AddonConfiguration.Program,AddonRoute.Legacy,"\uE8A5");
            Add(custom,"website","website","Website",L.T("HTTP- oder HTTPS-Adresse im Standardbrowser öffnen."),AddonActionType.Url,AddonConfiguration.Website,AddonRoute.Legacy,"\uE774");
            Add(custom,"hotkey","hotkey",L.T("Tastenkürzel"),L.T("Zielprogramm nach vorn holen, Fokus prüfen und erst dann das Kürzel senden."),AddonActionType.Shortcut,AddonConfiguration.Shortcut,AddonRoute.Legacy,"\uE765");
            var ai=Module("iris.chatgpt",L.T("ChatGPT & KI"),"KI","\uE8F2",L.T("Desktop und Browser, lokaler Chat und vorbereitete Entwürfe. Keine automatische Textübermittlung."));
            open=Add(ai,"desktop","chatgpt-open",L.T("Desktop-App öffnen"),L.T("Erkennt die registrierte ChatGPT-/Codex-App aus dem Windows-Appkatalog."),AddonActionType.Program,AddonConfiguration.None,AddonRoute.Platform,"\uE8F2");open.Dependency="app:chatgpt";open.Operation=AddonPlatformOperation.OpenChatGpt;
            UriAction(ai,"chatgpt-browser",L.T("ChatGPT im Browser"),L.T("Öffnet ChatGPT. Anmeldung erfolgt direkt dort."),"https://chatgpt.com","");
            UriAction(ai,"chatgpt-new",L.T("Neuer lokaler Chat"),L.T("Öffnet über den dokumentierten Desktop-Link einen neuen lokalen Chat; sendet keinen Text."),"codex://threads/new","codex");
            foreach(var item in new[]{new[]{"voice",L.T("Sprachmodus-Kürzel")},new[]{"dictation",L.T("Diktier-Kürzel")}}){var d=Add(ai,item[0],"chatgpt-"+item[0],item[1],L.T("Übernimm das in deiner App eingerichtete Kürzel unter Einstellungen → Tastenkürzel. Nur das überprüfte Zielprogramm erhält es. Verfügbarkeit abhängig von App und Konto."),AddonActionType.Shortcut,AddonConfiguration.AppShortcut,AddonRoute.Keys,"\uE720");}
            foreach(var item in new[]{new[]{"summary",L.T("Zwischenablage zusammenfassen"),L.T("Fasse den folgenden Text zusammen:")},new[]{"translate",L.T("Zwischenablage übersetzen"),L.T("Übersetze den folgenden Text ins Deutsche:")},new[]{"improve",L.T("Zwischenablage verbessern"),L.T("Verbessere den folgenden Text, ohne seinen Sinn zu ändern:")},new[]{"analyze",L.T("Zwischenablage analysieren"),L.T("Analysiere den folgenden Text:")}}){var d=Add(ai,item[0],"chatgpt-"+item[0],item[1],L.T("Bereitet den kopierten Text als Entwurf in einem neuen lokalen Chat vor. Du prüfst und sendest selbst. Keine automatische Auswahl-/Bildschirmübernahme."),AddonActionType.Integration,AddonConfiguration.None,AddonRoute.Platform,"\uE8F2");d.Operation=AddonPlatformOperation.PrepareChatDraft;d.Preset=item[2];d.Dependency="codex";}
            Unavailable(ai,"chatgpt-selection",L.T("Markierten Text automatisch übernehmen"),L.T("Noch nicht unterstützt. Text selbst kopieren und eine Zwischenablage-Aktion verwenden; kein automatisches Kopieren oder Senden."));
            RegisterPhase4();
        }
        public static IEnumerable<AddonPackage> Packages(){foreach(var p in modules)yield return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<AddonPackage>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(p));}
        public static bool IsScriptHost(string path){var name=Path.GetFileNameWithoutExtension(path??"");return new[]{"cmd","powershell","pwsh","wscript","cscript","mshta","rundll32","regsvr32","msbuild","shutdown","taskkill","logoff","reg","sc","diskpart","format","bcdedit","wmic","net","netsh","wsl","bash","sh","ssh","python","python3","pythonw","node","csi","dotnet","java","ruby","perl","schtasks","runas"}.Contains(name,StringComparer.OrdinalIgnoreCase);}
        public static AddonAvailability Availability(AddonAction action,AddonEnvironmentSnapshot env){
            var d=action==null?null:Find(action.Kind);if(d==null)return new AddonAvailability(false,L.T("Unbekannter Aktionstyp")){CanConfigure=false};
            if(d.Route==AddonRoute.Unsupported)return new AddonAvailability(false,d.UnsupportedReason){CanConfigure=false};
            string validation=AddonCatalog.Check(action,true);if(validation!=null)return new AddonAvailability(false,L.T("Konfigurieren: ")+validation);
            if(env==null)return new AddonAvailability(false,L.T("Abhängigkeiten werden geprüft …"));
            if(d.Dependency=="integration:spotify"&&!env.SpotifyConnected)return new AddonAvailability(false,L.T("Spotify noch nicht verbunden · Add-ons → Verbindungen"));
            if(d.Dependency=="app:discord"&&string.IsNullOrEmpty(env.DiscordTarget))return new AddonAvailability(false,L.T("Discord nicht gefunden. Desktop-App installieren oder eigenen Programmstarter verwenden."));
            if(d.Dependency=="app:chatgpt"&&string.IsNullOrEmpty(env.ChatGptTarget))return new AddonAvailability(false,string.IsNullOrEmpty(env.Note)?L.T("Keine registrierte ChatGPT-/Codex-Desktop-App gefunden."):env.Note);
            if(d.Dependency.StartsWith("process:")&&!env.Processes.Contains(d.Dependency.Substring(8)))return new AddonAvailability(false,d.Dependency.Substring(8)+L.T(" muss geöffnet sein."));
            if(d.Dependency.Length>0&&!d.Dependency.Contains(":")&&!env.Protocols.Contains(d.Dependency))return new AddonAvailability(false,L.T("Das Protokoll „")+d.Dependency+L.T("“ ist nicht registriert. Die zugehörige App installieren."));
            string process=string.IsNullOrEmpty(action.Process)?"":action.Process.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?action.Process.Substring(0,action.Process.Length-4):action.Process;
            if((action.Kind=="hotkey"||d.Configuration==AddonConfiguration.AppShortcut)&&!env.Processes.Contains(process))return new AddonAvailability(false,action.Process+L.T(" muss geöffnet sein; das Tastenkürzel wird vor dem Senden auf den Fokus geprüft."));
            if(d.Operation==AddonPlatformOperation.Discord)return new AddonAvailability(true,L.T("Discord-Ziel beim Ausführen prüfen · keine Anmeldung in Iris nötig"));
            if(d.Operation==AddonPlatformOperation.Integration&&!d.Confirm)return new AddonAvailability(true,d.Dependency=="integration:spotify"?L.T("Anmeldung gespeichert · Konto/Gerät beim Ausführen prüfen"):L.T("Ziel und Verfügbarkeit beim Ausführen prüfen"));
            return new AddonAvailability(true,d.Confirm?L.T("Bestätigung bei jedem Ausführen erforderlich"):action.Kind=="program"||action.Kind=="folders"?L.T("Dateien werden beim Ausführen geprüft"):L.T("Bereit"));
        }
    }
}
