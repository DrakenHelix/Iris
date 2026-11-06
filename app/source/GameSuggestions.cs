using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Iris {
    public sealed class RunningGameCandidate {public int Pid;public string Name,Path;}
    public static class GameProcessDiscovery {
        public static bool Identity(int pid,out string path,out long started){
            path="";started=0;IntPtr handle=OpenProcess(0x1000,false,pid);if(handle==IntPtr.Zero)return false;
            try{var name=new StringBuilder(32768);int size=name.Capacity;long created,exited,kernel,user;if(!QueryFullProcessImageNameW(handle,0,name,ref size)||!GetProcessTimes(handle,out created,out exited,out kernel,out user))return false;path=name.ToString();started=DateTime.FromFileTimeUtc(created).Ticks;return true;}catch{return false;}finally{CloseHandle(handle);}
        }
        public static bool Suggestible(string path){
            if(string.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathRooted(path)||!string.Equals(System.IO.Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase))return false;
            string[] names={"iris","iris.fpshost","presentmon","chrome","msedge","firefox","explorer","dwm","steam","steamwebhelper","gameoverlayui","discord","vlc","mpv","medal","obs64","applicationframehost","searchhost","shellexperiencehost","startmenuexperiencehost","systemsettings","runtimebroker","textinputhost"};
            if(names.Contains(System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant()))return false;string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\')+"\\";return !path.StartsWith(windows,StringComparison.OrdinalIgnoreCase);
        }
        public static RunningGameCandidate[] Snapshot(){
            var ids=new HashSet<int>();int self=Process.GetCurrentProcess().Id;EnumWindows(delegate(IntPtr hwnd,IntPtr state){uint pid;if(IsWindowVisible(hwnd)&&GetWindow(hwnd,4)==IntPtr.Zero){GetWindowThreadProcessId(hwnd,out pid);if(pid>0&&pid!=self)ids.Add((int)pid);}return true;},IntPtr.Zero);
            var result=new List<RunningGameCandidate>();foreach(int pid in ids.Take(128)){string path;long started;if(Identity(pid,out path,out started)&&Suggestible(path))result.Add(new RunningGameCandidate{Pid=pid,Path=path,Name=System.IO.Path.GetFileNameWithoutExtension(path)});}return result.GroupBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).Take(32).ToArray();
        }
        delegate bool EnumProc(IntPtr hwnd,IntPtr state);
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr state);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr hwnd,uint command);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageNameW(IntPtr process,uint flags,StringBuilder path,ref int size);
        [DllImport("kernel32.dll")]static extern bool GetProcessTimes(IntPtr process,out long created,out long exited,out long kernel,out long user);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
    }
    public sealed partial class NotchWindow {
        RunningGameCandidate[] runningGameSuggestions=new RunningGameCandidate[0];bool suggestionsLoading;int suggestionRevision;
        bool AddGamePath(string path){path=System.IO.Path.GetFullPath(path);if(!string.Equals(System.IO.Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase)||prefs.GamingGames.Any(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase))||prefs.GamingGames.Count>=80)return false;prefs.GamingGames.Add(path);gamingResolvedAt=DateTime.MinValue;Save();return true;}
        void RefreshGameSuggestions(){
            if(uiTesting){suggestionsLoading=false;RenderGamingGames();return;}int revision=++suggestionRevision;var window=addonWindow;suggestionsLoading=true;RenderGamingGames();
            Task.Factory.StartNew<RunningGameCandidate[]>(GameProcessDiscovery.Snapshot).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(delegate{if(shuttingDown||revision!=suggestionRevision||addonWindow!=window||window==null||!window.IsVisible)return;suggestionsLoading=false;runningGameSuggestions=task.IsFaulted?new RunningGameCandidate[0]:task.Result;RenderGamingGames();})));
        }
        void RenderGameSuggestions(System.Windows.Controls.StackPanel panel){
            panel.Children.Add(AddonNote(L.T("Laufende Programme · Vorschläge für weitere Spiele")));panel.Children.Add(AddonChoice(L.T("Liste aktualisieren"),suggestionsLoading?L.T("Laufende Fenster werden gelesen …"):L.T("Nur Zuordnungen · es wird kein Programm gestartet"),RefreshGameSuggestions));
            var choices=runningGameSuggestions.Where(s=>!GameCatalog.IsGame(s.Path,prefs.GamingGames)).ToArray();
            foreach(var candidate in choices){string path=candidate.Path;panel.Children.Add(AddonChoice(candidate.Name,path+L.T("\nAls Spiel hinzufügen"),delegate{AddGamePath(path);RenderGamingGames();}));}
            if(!suggestionsLoading&&choices.Length==0)panel.Children.Add(AddonNote(L.T("Keine weiteren Vorschläge. Starte dein Spiel und aktualisiere die Liste, oder wähle seine .exe manuell. Steam-Spiele werden bereits automatisch erkannt.")));
        }
    }
}
