using System;using System.IO;using System.Linq;using System.Text.RegularExpressions;
namespace Iris {
    public static partial class AddonRegistry {
        static AddonDefinition Integration(AddonPackage p,string suffix,string title,string description,string input){var d=Add(p,suffix,p.Id.Substring(5)+"-"+suffix,title,description,AddonActionType.Integration,input==null?AddonConfiguration.None:AddonConfiguration.Text,AddonRoute.Platform,"\uE713");d.Operation=AddonPlatformOperation.Integration;d.InputLabel=input??"";if(d.Kind.StartsWith("obs-"))d.Dependency="process:obs64";if(d.Kind.StartsWith("spotify-")&&new[]{"api-shuffle","api-repeat","like","devices","play-context","device"}.Contains(suffix))d.Dependency="integration:spotify";return d;}
        static void RegisterPhase4(){
            foreach(string provider in new[]{"spotify","applemusic"}){
                string name=provider=="spotify"?"Spotify":"Apple Music";var p=Module("iris."+provider,name,"Musik","\uE189",L.T("Eigene Medienquelle steuern; kein Wechsel auf Windows-Gesamtlautstärke."));
                Integration(p,"open",name+L.T(" öffnen"),L.T("Öffnet die installierte App oder erklärt die fehlende Installation."),null);
                foreach(var item in new[]{new[]{"toggle","Play / Pause"},new[]{"next",L.T("Nächster Titel")},new[]{"previous",L.T("Vorheriger Titel")},new[]{"shuffle",L.T("Zufallswiedergabe umschalten")},new[]{"repeat",L.T("Wiederholung weiterschalten")},new[]{"volume-up",L.T("Quelle lauter (+5 %)")},new[]{"volume-down",L.T("Quelle leiser (−5 %)")},new[]{"mute",L.T("Quelle stumm / Ton an")}})Integration(p,item[0],item[1],L.T("Steuert ausschließlich ")+name+L.T(". Die App muss eine eindeutige Medien-/Audioquelle bereitstellen. Nicht angebotene Befehle werden erklärt."),null);
                Integration(p,"info",L.T("Titelinformationen"),L.T("Liest Titel, Interpret und Wiedergabezustand der passenden Windows-Mediensitzung."),null).ShowResult=true;
                Integration(p,"player",L.T("Player in Iris"),L.T("Öffnet den vorhandenen Iris-Player mit Cover, Zeitleiste und Liedtexten der aktuellen Quelle."),null);
                Integration(p,"search",L.T("Musik suchen"),L.T("Öffnet die Suche beim jeweiligen Anbieter; startet keine Wiedergabe automatisch."),L.T("Suchbegriff"));
                Integration(p,"playlist",L.T("Playlist öffnen"),L.T("Öffnet den konfigurierten Original-Link. Wiedergabe anschließend in der App starten."),"Playlist-/Album-Link · https://…");
                if(provider=="spotify"){
                    Integration(p,"connect",L.T("Spotify verbinden"),L.T("Einmalige Browser-Anmeldung mit PKCE. Client-ID unter Verbindungen eintragen; kein Client-Secret nötig."),null);
                    foreach(var item in new[]{new[]{"api-shuffle","Spotify Connect · Shuffle"},new[]{"api-repeat",L.T("Spotify Connect · Wiederholung")},new[]{"like",L.T("Aktuellen Titel speichern")},new[]{"devices",L.T("Connect-Geräte anzeigen")}})Integration(p,item[0],item[1],L.T("Benötigt eingerichtete Spotify-Web-API-Anmeldung; Playerfunktionen können Spotify Premium und ein aktives Gerät benötigen."),null).ShowResult=item[0]=="devices";
                    Integration(p,"play-context",L.T("Playlist direkt abspielen"),L.T("Spotify Connect: startet das konfigurierte Album/die Playlist auf dem aktiven Gerät. Anmeldung erforderlich."),L.T("Spotify-URI · spotify:playlist:… / spotify:album:…"));
                    Integration(p,"device",L.T("Connect-Gerät wählen"),L.T("Überträgt den Player auf die konfigurierte Geräte-ID, ohne die Wiedergabe automatisch zu starten."),L.T("Geräte-ID aus „Connect-Geräte anzeigen“"));
                }else{
                    foreach(var item in new[]{new[]{"miniplayer","MiniPlayer","Ctrl+Shift+M"},new[]{"equalizer","Equalizer","Ctrl+Shift+E"},new[]{"current",L.T("Aktuellen Titel in App zeigen"),"Ctrl+L"}}){var d=Add(p,item[0],"applemusic-"+item[0],item[1],L.T("Dokumentiertes Apple-Music-Kürzel. Nur an das überprüfte AppleMusic-Fenster; Zustand nicht ausgelesen."),AddonActionType.Shortcut,AddonConfiguration.AppShortcut,AddonRoute.Keys,"\uE189");d.DefaultProcess="AppleMusic";d.DefaultShortcut=item[2];p.Actions[p.Actions.Count-1]=d.Template();}
                }
            }
            var obs=Module("iris.obs","OBS Studio","Streaming","\uE714",L.T("OBS-WebSocket 5: authentifiziert, lokal und nur auf Kachel-Klick. Zugang unter Verbindungen einrichten."));
            foreach(var item in new[]{new[]{"record-start",L.T("Aufnahme starten")},new[]{"record-stop",L.T("Aufnahme stoppen")},new[]{"stream-start",L.T("Stream starten")},new[]{"stream-stop",L.T("Stream stoppen")},new[]{"replay-start",L.T("Replay-Puffer starten")},new[]{"replay-stop",L.T("Replay-Puffer stoppen")},new[]{"replay-save",L.T("Replay speichern")},new[]{"status",L.T("Aufnahme- und Streamstatus")},new[]{"scenes",L.T("Szenen anzeigen")},new[]{"inputs",L.T("Audio-/Videoquellen anzeigen")}}){var d=Integration(obs,item[0],item[1],L.T("OBS muss laufen und WebSocket 5 aktiviert sein. Zugang unter Verbindungen hinterlegen. Rückmeldung vom OBS-Server."),null);d.ShowResult=new[]{"status","scenes","inputs"}.Contains(item[0]);d.Confirm=item[0]=="stream-start";}
            Integration(obs,"scene",L.T("Szene wechseln"),L.T("Wechselt exakt zur konfigurierten OBS-Programmszene."),L.T("Exakter Szenenname"));
            Integration(obs,"input-mute",L.T("OBS-Mikrofon / Audio umschalten"),L.T("Liest und schaltet den Stummzustand des benannten OBS-Audioeingangs."),L.T("Exakter Audioeingangsname"));
            Integration(obs,"source",L.T("Kamera / Quelle ein-/ausblenden"),L.T("Liest und schaltet das Element innerhalb der benannten Szene."),L.T("Szene | Quellenname"));
            var browser=Module("iris.browser","Browser","Web","\uE774",L.T("Gezielte Browser-Kürzel und eigene Links. Zielprozess und Kürzel sind anpassbar."));
            foreach(var item in new[]{new[]{"new-tab",L.T("Neuer Tab"),"Ctrl+T"},new[]{"private",L.T("Privates Fenster · Edge/Chrome"),"Ctrl+Shift+N"},new[]{"private-firefox",L.T("Privates Fenster · Firefox"),"Ctrl+Shift+P"},new[]{"close",L.T("Tab schließen"),"Ctrl+W"},new[]{"next",L.T("Nächster Tab"),"Ctrl+Tab"},new[]{"previous",L.T("Vorheriger Tab"),"Ctrl+Shift+Tab"},new[]{"reload",L.T("Tab neu laden"),"Ctrl+R"},new[]{"favorites",L.T("Favoriten"),"Ctrl+Shift+O"},new[]{"downloads","Downloads","Ctrl+J"},new[]{"history",L.T("Verlauf"),"Ctrl+H"}}){var d=Add(browser,item[0],"browser-"+item[0],item[1],L.T("Fokus wird vor dem Senden geprüft. Standard: ")+(item[0]=="private-firefox"?"Firefox":"Microsoft Edge")+L.T("; Prozess und Kürzel lassen sich ändern."),AddonActionType.Shortcut,AddonConfiguration.AppShortcut,AddonRoute.Keys,"\uE774");d.DefaultProcess=item[0]=="private-firefox"?"firefox":"msedge";d.DefaultShortcut=item[2];browser.Actions[browser.Actions.Count-1]=d.Template();}
            Integration(browser,"open",L.T("Browser / Favorit öffnen"),L.T("Öffnet eine gespeicherte Webadresse im Standardbrowser."),"Webadresse · https://…");
            UriAction(browser,"browser-youtube",L.T("YouTube öffnen"),L.T("Öffnet YouTube im Standardbrowser."),"https://www.youtube.com","");
            UriAction(browser,"browser-translate",L.T("Übersetzer öffnen"),L.T("Öffnet die Übersetzungsseite; übernimmt keinen Text automatisch."),"https://translate.google.com","");
            var code=Module("iris.coding","Coding","Entwicklung","\uE943",L.T("Projektordner, VS Code, Terminal, Git und .NET. Build/Test und Git-Schreibaktionen fragen vorher nach."));
            foreach(var item in new[]{new[]{"project",L.T("Projekt in VS Code öffnen")},new[]{"folder",L.T("Projektordner öffnen")},new[]{"terminal",L.T("Terminal im Projekt")},new[]{"git-status",L.T("Git-Status")},new[]{"git-pull",L.T("Git pull · nur Fast-forward")},new[]{"git-push","Git push"},new[]{"build",L.T(".NET-Projekt bauen")},new[]{"test",L.T(".NET-Tests ausführen")},new[]{"logs",L.T("Letztes Aktionsprotokoll")}}){var d=Integration(code,item[0],item[1],L.T("Verwendet den gewählten Projektordner. Keine Shell-Befehlskette; bestehende Dateien werden nicht zurückgesetzt. Git pull verwendet --ff-only."),L.T("Vollständiger Projektordner"));d.Confirm=new[]{"git-pull","git-push","build","test"}.Contains(item[0]);d.ShowResult=new[]{"git-status","git-pull","git-push","build","test","logs"}.Contains(item[0]);}
            UriAction(code,"coding-github",L.T("GitHub öffnen"),L.T("Öffnet GitHub im Standardbrowser."),"https://github.com","");
            UriAction(code,"coding-codex",L.T("Neuer Codex-Chat"),L.T("Öffnet einen neuen lokalen Chat, ohne Text zu senden."),"codex://threads/new","codex");
            var hardware=Module("iris.hardware",L.T("Hardware & Leistung"),"System","\uE9D9",L.T("Messwerte auf Anforderung; keine Dauerabfragen, kein erfundener Temperatur- oder FPS-Wert."));
            Integration(hardware,"snapshot",L.T("CPU, RAM, Netzwerk & Laufwerke"),L.T("Liest zwei CPU-/Netzwerk-Zeitpunkte, RAM und freien Laufwerksplatz. Keine zusätzliche Hardwarebibliothek."),null).ShowResult=true;
            Integration(hardware,"mixer",L.T("Lautstärkemixer"),L.T("Öffnet den vorhandenen Iris-Mixer mit getrennten Quellen."),null);
            Integration(hardware,"gaming",L.T("Gaming-Einstellungen"),L.T("Öffnet die vorhandenen Gaming-Einstellungen. FPS benötigen die dortige ausdrückliche Freigabe."),null);
            UriAction(hardware,"hardware-brightness",L.T("Helligkeit & Anzeige"),L.T("Öffnet Windows-Anzeigeeinstellungen; Desktopmonitore können eigene Regler benötigen."),"ms-settings:display","ms-settings");
            var productivity=Module("iris.productivity",L.T("Produktivität"),"Werkzeuge","\uE916",L.T("Vorhandenen Iris-Timer und Windows-Werkzeuge nutzen. Keine To-do- oder Wassererinnerungen."));
            Integration(productivity,"timer",L.T("Timer in Iris öffnen"),L.T("Öffnet den frei einstellbaren vorhandenen Timer."),null);
            Integration(productivity,"timer-start",L.T("Timer starten"),L.T("Startet den vorhandenen Iris-Timer mit der gewählten Dauer. Er läuft auch bei geschlossener Notch weiter."),L.T("Dauer in Minuten · 1 bis 999"));
            Integration(productivity,"timer-toggle",L.T("Timer pausieren / fortsetzen"),L.T("Steuert den vorhandenen Iris-Timer."),null);
            Integration(productivity,"timer-reset",L.T("Timer zurücksetzen"),L.T("Setzt den vorhandenen Iris-Timer auf seine eingestellte Dauer zurück."),null);
            Integration(productivity,"note",L.T("Notizdatei öffnen"),L.T("Öffnet eine vorhandene .txt- oder .md-Datei mit der zugeordneten App."),L.T("Vollständiger Pfad · .txt / .md"));
            UriAction(productivity,"productivity-calendar",L.T("Kalender öffnen"),L.T("Öffnet Outlook Kalender im Browser; Anmeldung dort."),"https://outlook.live.com/calendar/","");
            UriAction(productivity,"productivity-focus",L.T("Windows-Fokus"),L.T("Öffnet die Windows-Fokuseinstellungen."),"ms-settings:quiethours","ms-settings");
        }
        public static string Phase4Validation(AddonAction a){
            var d=Find(a.Kind);if(d.Configuration!=AddonConfiguration.Text)return null;
            string value=(a.Target??"").Trim();if(value.Length==0)return L.T("Bitte „")+d.InputLabel+L.T("“ angeben.");
            Uri uri;
            if(a.Kind=="browser-open"&&(!Uri.TryCreate(value,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https")))return L.T("Vollständige HTTP-/HTTPS-Adresse angeben.");
            if(a.Kind=="spotify-playlist"||a.Kind=="applemusic-playlist"){if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Port!=443||uri.UserInfo.Length>0||uri.Host!=(a.Kind.StartsWith("spotify")?"open.spotify.com":"music.apple.com"))return L.T("Originalen HTTPS-Link von open.spotify.com bzw. music.apple.com verwenden.");}
            if(a.Kind=="spotify-play-context"&&!Regex.IsMatch(value,@"^spotify:(?:playlist|album):[A-Za-z0-9]{22}$"))return L.T("Spotify-Playlist-/Album-URI angeben.");
            if(a.Kind=="spotify-device"&&!Regex.IsMatch(value,@"^[A-Za-z0-9_-]{1,128}$"))return L.T("Geräte-ID aus der Geräteanzeige übernehmen.");
            if(a.Kind.StartsWith("coding-")&&(!Path.IsPathRooted(value)||value.Contains("\"")||value.IndexOfAny(new[]{'\r','\n','\0'})>=0))return L.T("Vollständigen lokalen Projektordner angeben.");
            if(a.Kind=="productivity-timer-start"){int minutes;if(!int.TryParse(value,out minutes)||minutes<1||minutes>999)return L.T("Dauer: 1 bis 999 ganze Minuten.");}
            if(a.Kind=="productivity-note"&&(!Path.IsPathRooted(value)||!new[]{".txt",".md"}.Contains(Path.GetExtension(value).ToLowerInvariant())))return L.T("Vollständige .txt- oder .md-Datei angeben.");
            if(a.Kind.StartsWith("obs-")&&(value.Length>255||value.Any(char.IsControl)))return L.T("OBS-Namen dürfen höchstens 255 Zeichen ohne Steuerzeichen enthalten.");
            if(a.Kind=="obs-source"&&(value.Split('|').Length!=2||value.Split('|').Any(string.IsNullOrWhiteSpace)))return L.T("Szene | Quellenname angeben.");
            return null;
        }
    }
}
