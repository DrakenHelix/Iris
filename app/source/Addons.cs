using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Iris {
    public sealed class AddonAction {
        public string Id="",Title="",Kind="",Target="",Arguments="",Shortcut="",Process="",Icon="";
        public List<string> Paths=new List<string>();
        public AddonAction Copy(){return new JavaScriptSerializer().Deserialize<AddonAction>(new JavaScriptSerializer().Serialize(this));}
    }
    public sealed class AddonPackage {
        public int Format=1;
        public string Id="",Name="",Version="1.0",Description="",Category="",Icon="";
        public bool Enabled=true;
        public List<AddonAction> Actions=new List<AddonAction>();
    }
    public sealed class DeckSlot {
        public int Position;
        public string BindingId=Guid.NewGuid().ToString("N"),ProfileId="default";
        public string PackageId="";
        public AddonAction Action;
    }
    public sealed class AddonDeck {
        public int Format=1;
        public List<AddonPackage> Packages=new List<AddonPackage>();
        public List<DeckSlot> Slots=new List<DeckSlot>();
        public DeckSlot At(int position){return Slots.FirstOrDefault(s=>s.Position==position);}
        public bool Enabled(DeckSlot slot){return slot!=null&&Packages.Any(p=>p.Id==slot.PackageId&&p.Enabled);}
        public void Put(DeckSlot slot){Slots.RemoveAll(s=>s.Position==slot.Position);Slots.Add(slot);}
    }
    public static class AddonCatalog {
        public static string[] Kinds {get{return AddonRegistry.Definitions.Select(d=>d.Kind).ToArray();}}
        public static IEnumerable<AddonPackage> Builtins(){return AddonRegistry.Packages();}
        public static string DisplayTitle(AddonAction action){
            var definition=AddonRegistry.Find(action.Kind);
            return definition==null?action.Title:L.DisplayDefault(action.Title,definition.Title);
        }
        public static string DisplayName(AddonPackage package){
            var builtin=AddonRegistry.BuiltinPackage(package.Id);
            return builtin==null?package.Name:L.DisplayDefault(package.Name,builtin.Name);
        }
        public static string DisplayDescription(AddonPackage package){
            var builtin=AddonRegistry.BuiltinPackage(package.Id);
            return builtin==null?package.Description:L.DisplayDefault(package.Description,builtin.Description);
        }
        public static string Check(AddonAction a,bool configured){return Check(a,configured,true);}
        static string Check(AddonAction a,bool configured,bool executionPolicy){
            if(a==null||AddonRegistry.Find(a.Kind)==null)return L.T("Dieser Aktionstyp wird nicht unterstützt.");
            if(string.IsNullOrWhiteSpace(a.Id)||a.Id.Length>80||string.IsNullOrWhiteSpace(a.Title)||a.Title.Length>48)return L.T("Aktionen benötigen eine ID und einen Namen mit höchstens 48 Zeichen.");
            if((a.Target??"").Length>2048||(a.Arguments??"").Length>4096||(a.Process??"").Length>100||(a.Shortcut??"").Length>100)return L.T("Ein Eingabewert ist zu lang.");
            if(a.Paths==null||a.Paths.Count>8||a.Paths.Any(p=>string.IsNullOrWhiteSpace(p)||p.Length>2048))return L.T("Bis zu acht gültige Ordner sind möglich.");
            if(!string.IsNullOrEmpty(a.Icon)&&(a.Icon.Length!=1||a.Icon[0]<'\uE000'||a.Icon[0]>'\uF8FF'))return L.T("Wähle ein gültiges Kachelsymbol.");
            if(!configured)return null;
            if(a.Kind=="steam"&&!Regex.IsMatch(a.Target??"",@"^[1-9][0-9]{0,9}$"))return L.T("Wähle ein Steam-Spiel aus.");
            if(a.Kind=="folders"&&(a.Paths.Count==0||a.Paths.Any(p=>!Path.IsPathRooted(Environment.ExpandEnvironmentVariables(p)))))return L.T("Wähle mindestens einen vollständigen Ordnerpfad.");
            if(a.Kind=="program"&&(!Path.IsPathRooted(Environment.ExpandEnvironmentVariables(a.Target??""))||!string.Equals(Path.GetExtension(a.Target),".exe",StringComparison.OrdinalIgnoreCase)))return L.T("Wähle eine Programmdatei (.exe) mit vollständigem Pfad.");
            Uri url;if(a.Kind=="website"&&(!Uri.TryCreate(a.Target,UriKind.Absolute,out url)||(url.Scheme!="https"&&url.Scheme!="http")))return L.T("Trage eine vollständige http- oder https-Adresse ein.");
            ushort[] keys;if(a.Kind=="hotkey"&&(!Regex.IsMatch(a.Process??"",@"^[a-zA-Z0-9_. -]{1,80}$")||!AddonRunner.TryChord(a.Shortcut,out keys)))return L.T("Prozessname ohne .exe und Tastenkürzel angeben, z. B. Ctrl+Shift+M.");
            var definition=AddonRegistry.Find(a.Kind);
            if(definition.Configuration==AddonConfiguration.Process&&!Regex.IsMatch(a.Process??"",@"^[a-zA-Z0-9_. -]{1,80}$"))return L.T("Gib den Prozessnamen ohne .exe an.");
            if(definition.Configuration==AddonConfiguration.AppShortcut&&(!Regex.IsMatch(a.Process??"",@"^[a-zA-Z0-9_. -]{1,80}$")||!AddonRunner.TryChord(a.Shortcut,out keys)))return L.T("Übernimm Prozessname und Tastenkürzel aus der installierten App.");
            if(a.Kind=="discord-channel"&&!(executionPolicy?DiscordControl.ChannelUrl(a.Target):DiscordControl.StoredChannelUrl(a.Target)))return L.T("Wähle einen Kanal aus dem Discord-Hauptfenster aus.");
            if(executionPolicy&&a.Kind=="program"&&AddonRegistry.IsScriptHost(a.Target))return L.T("Skript-/Befehlshosts und administrative Werkzeuge sind für diesen Starter nicht freigegeben. Verwende eine passende Systemaktion; Skriptfreigaben folgen später.");
            return AddonRegistry.Phase4Validation(a);
        }
        public static string CheckPackage(AddonPackage p){
            if(p==null||p.Format!=1)return L.T("Dieses Add-on-Format wird nicht unterstützt.");
            if(!Regex.IsMatch(p.Id??"",@"^[a-zA-Z0-9][a-zA-Z0-9._-]{2,79}$")||string.IsNullOrWhiteSpace(p.Name)||p.Name.Length>48||(p.Description??"").Length>600||(p.Version??"").Length>32||(p.Category??"").Length>48||(p.Icon??"").Length>4)return L.T("Name oder Kennung des Add-ons ist ungültig.");
            if(p.Actions==null||p.Actions.Count<1||p.Actions.Count>64||p.Actions.Select(a=>a==null?"":a.Id).Distinct().Count()!=p.Actions.Count)return L.T("Ein Add-on enthält 1 bis 64 Aktionen mit eindeutigen IDs.");
            foreach(var action in p.Actions){var error=Check(action,false);if(error!=null)return error;}return null;
        }
        public static AddonPackage ReadPackage(string path){
            if(new FileInfo(path).Length>256*1024)throw new InvalidDataException(L.T("Die Add-on-Datei darf höchstens 256 KB groß sein."));
            var p=new JavaScriptSerializer{MaxJsonLength=256*1024,RecursionLimit=24}.Deserialize<AddonPackage>(File.ReadAllText(path));var error=CheckPackage(p);if(error!=null)throw new InvalidDataException(error);return p;
        }
        public static AddonPackage ForExport(AddonPackage package,AddonDeck deck){
            var copy=new JavaScriptSerializer().Deserialize<AddonPackage>(new JavaScriptSerializer().Serialize(package));
            var configured=deck.Slots.Where(s=>s.PackageId==package.Id).OrderBy(s=>s.Position).ToArray();
            if(configured.Length>0){copy.Actions=new List<AddonAction>();foreach(var slot in configured){var action=slot.Action.Copy();action.Id="tile-"+(slot.Position+1);copy.Actions.Add(action);}}
            return copy;
        }
        public static AddonDeck Load(string path){
            if(!File.Exists(path))return new AddonDeck();
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException(L.T("Add-on-Konfiguration ist zu groß."));
            var deck=new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=32}.Deserialize<AddonDeck>(File.ReadAllText(path));
            if(deck==null||deck.Format!=1||deck.Packages==null||deck.Slots==null||deck.Packages.Count>40||deck.Slots.Count>8)throw new InvalidDataException(L.T("Add-on-Konfiguration ist ungültig."));
            if(deck.Packages.Any(p=>CheckPackage(p)!=null)||deck.Packages.Select(p=>p.Id).Distinct().Count()!=deck.Packages.Count||deck.Slots.Any(s=>s==null)||deck.Slots.Select(s=>s.Position).Distinct().Count()!=deck.Slots.Count||deck.Slots.Any(s=>s.Position<0||s.Position>7||!deck.Packages.Any(p=>p.Id==s.PackageId)||Check(s.Action,true,false)!=null))throw new InvalidDataException(L.T("Ungültige Add-on-Belegung; Originaldatei bleibt erhalten."));
            foreach(var slot in deck.Slots){if(!Regex.IsMatch(slot.BindingId??"",@"^[a-zA-Z0-9-]{1,64}$"))slot.BindingId=Guid.NewGuid().ToString("N");if(slot.ProfileId!="default")throw new InvalidDataException(L.T("Dieses Profilformat wird noch nicht unterstützt; Originaldatei bleibt erhalten."));}
            if(deck.Slots.Select(slot=>slot.BindingId).Distinct().Count()!=deck.Slots.Count)throw new InvalidDataException(L.T("Doppelte Belegungskennungen; Originaldatei bleibt erhalten."));
            foreach(var built in Builtins()){var installed=deck.Packages.FirstOrDefault(pkg=>pkg.Id==built.Id);if(installed==null)continue;if(string.IsNullOrEmpty(installed.Category))installed.Category=built.Category;if(string.IsNullOrEmpty(installed.Icon))installed.Icon=built.Icon;foreach(var action in built.Actions)if(installed.Actions.Count<64&&!installed.Actions.Any(a=>a.Id==action.Id))installed.Actions.Add(action);}
            return deck;
        }
        public static void Save(string path,AddonDeck deck){
            var temp=path+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(deck),System.Text.Encoding.UTF8);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        public static string Detail(AddonAction a){
            var definition=AddonRegistry.Find(a.Kind);if(definition!=null&&definition.Extended)return definition.Description;
            if(a.Kind=="folders")return a.Paths.Count+L.T(" Ordner öffnen");
            if(a.Kind=="steam")return a.Title+L.T(" über Steam starten");
            if(a.Kind=="program")return a.Target+(string.IsNullOrEmpty(a.Arguments)?"":L.T("\nArgumente: ")+a.Arguments);
            if(a.Kind=="hotkey")return a.Process+" · "+a.Shortcut;
            if(a.Kind=="website")return a.Target;
            if(a.Kind=="screenshot")return L.T("Windows-Bildschirmausschnitt auswählen");
            if(a.Kind=="discord-share")return L.T("Discord: Freigabe-Auswahl öffnen");
            return "Discord: "+(a.Kind=="discord-mute"?L.T("Mikrofon umschalten"):L.T("Ton umschalten"));
        }
    }
}
