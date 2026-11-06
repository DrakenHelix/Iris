using System;using System.IO;using System.Text;using System.Web.Script.Serialization;
namespace Iris {
    public sealed class DataReference {public int Format=1;public string Directory;}
    public static class DataLocation {
        const string FileName="Iris.data-location.json";
        internal static string RegisteredInstallFolder(){
            try{
                using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Iris\Installer")){
                    var location=key==null?null:key.GetValue("InstallLocation") as string;
                    if(!string.IsNullOrEmpty(location))return location.TrimEnd('\\','/');
                }
            }catch(UnauthorizedAccessException){}catch(System.Security.SecurityException){}
            return null;
        }
        internal static bool IsInstalled(string root){
            return string.Equals(Path.GetFullPath(root).TrimEnd('\\','/'),RegisteredInstallFolder(),StringComparison.OrdinalIgnoreCase);
        }
        static string StorageRoot(string root){
            string programFiles=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string installed=RegisteredInstallFolder()??Path.Combine(programFiles,"Iris");
            return !string.IsNullOrEmpty(programFiles)&&string.Equals(Path.GetFullPath(root).TrimEnd('\\','/'),installed,StringComparison.OrdinalIgnoreCase)
                ?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Iris"):root;
        }
        public static string ErrorLog(string root){return Path.Combine(StorageRoot(root),"error.log");}
        public static string Resolve(string root,bool qa){if(qa)return Path.GetFullPath(Path.Combine(root,"Data-qa"));string storage=StorageRoot(root),pointer=Path.Combine(storage,FileName);if(!File.Exists(pointer))return Path.GetFullPath(Path.Combine(storage,"Data"));try{if(new FileInfo(pointer).Length>8192)throw new InvalidDataException();var reference=new JavaScriptSerializer{MaxJsonLength=8192}.Deserialize<DataReference>(File.ReadAllText(pointer));if(reference==null||reference.Format!=1)throw new InvalidDataException();return Validate(reference.Directory);}catch(Exception ex){throw new InvalidDataException(L.T("Der bisherige Iris-Datenordner ist nicht erreichbar. Prüfe Iris.data-location.json; die persönlichen Daten wurden nicht verschoben. ")+ex.Message);}}
        public static string Validate(string directory){if(string.IsNullOrWhiteSpace(directory)||!Path.IsPathRooted(directory)||directory.StartsWith(@"\\"))throw new InvalidDataException(L.T("Bitte einen vorhandenen lokalen Iris-Datenordner verwenden."));string full=Path.GetFullPath(directory);if(!System.IO.Directory.Exists(full))throw new DirectoryNotFoundException(L.T("Der bisherige Datenordner ist nicht mehr vorhanden."));string note;bool readOnly;PreferencesStore.Read(Path.Combine(full,"settings.json"),out note,out readOnly);if(readOnly||!File.Exists(Path.Combine(full,"settings.json"))&&!File.Exists(Path.Combine(full,"settings.json.bak")))throw new InvalidDataException(L.T("In diesem Ordner wurden keine lesbaren Iris-Einstellungen gefunden."));return full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);}
        public static void Use(string root,string directory,Action beforeCommit=null){string full=Validate(directory),storage=StorageRoot(root);Directory.CreateDirectory(storage);string path=Path.Combine(storage,FileName),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new DataReference{Directory=full}),new UTF8Encoding(false));if(beforeCommit!=null)beforeCommit();if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}}
    }
}
