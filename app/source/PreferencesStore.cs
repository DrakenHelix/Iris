using System;using System.IO;using System.Text;using System.Web.Script.Serialization;
namespace Iris {
    public static class PreferencesStore {
        static Preferences Parse(string path){var info=new FileInfo(path);if(!info.Exists||info.Length>2*1024*1024)throw new InvalidDataException(L.T("Einstellungsdatei fehlt oder ist zu groß."));var value=new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=32}.Deserialize<Preferences>(File.ReadAllText(path));if(value==null)throw new InvalidDataException(L.T("Leere Einstellungsdatei."));if(double.IsNaN(value.Offset)||double.IsInfinity(value.Offset))value.Offset=0;if(double.IsNaN(value.Remaining)||double.IsInfinity(value.Remaining))value.Remaining=300;return value;}
        public static Preferences Read(string path,out string note,out bool readOnly){
            note="";readOnly=false;
            if(!File.Exists(path)&&!File.Exists(path+".bak")){
                // Field initializers also apply when reading older files with missing settings.
                return new Preferences{GamingEnabled=true,FullscreenDesign="glass",DesignSchema=1,CursorAvoidance=true};
            }
            try{return Parse(path);}catch{}
            try{var backup=Parse(path+".bak");note=L.T("Einstellungen aus der letzten Sicherung wiederhergestellt.");return backup;}catch{}
            note=L.T("Einstellungsdatei und Sicherung sind nicht lesbar. Vorhandene Dateien bleiben erhalten; Einstellungen werden vorläufig nicht gespeichert.");
            readOnly=true;
            return new Preferences();
        }
        public static bool Readable(string path){try{Parse(path);return true;}catch{return false;}}
        public static void Save(string path,Preferences value,Action beforeCommit=null){Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(value),new UTF8Encoding(false));if(beforeCommit!=null)beforeCommit();if(File.Exists(path)){bool valid=Readable(path);if(!valid)File.Copy(path,path+".unreadable-"+Guid.NewGuid().ToString("N"));File.Replace(temp,path,valid?path+".bak":null);}else File.Move(temp,path);}finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}}
    }
}
