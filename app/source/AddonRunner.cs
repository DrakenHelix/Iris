using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Iris {
    public interface IAddonHost {
        bool FileExists(string path);bool DirectoryExists(string path);
        void Launch(string target,string arguments,string directory);
        void Folder(string path);void Shortcut(string process,ushort[] keys);void DiscordShare();
    }
    public static class AddonRunner {
        public static string Run(AddonAction action,IAddonHost host){
            return Run(action,host,System.Threading.CancellationToken.None);
        }
        public static string Run(AddonAction action,IAddonHost host,System.Threading.CancellationToken cancellation){
            cancellation.ThrowIfCancellationRequested();
            string error=AddonCatalog.Check(action,true);if(error!=null)throw new InvalidOperationException(error);
            string target=Environment.ExpandEnvironmentVariables(action.Target??"");
            switch(action.Kind){
                case "screenshot":host.Shortcut("",new ushort[]{0x5B,0x10,0x53});return L.T("Screenshot-Auswahl angefordert");
                case "folders":
                    var paths=action.Paths.Select(Environment.ExpandEnvironmentVariables).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach(var path in paths){cancellation.ThrowIfCancellationRequested();if(!host.DirectoryExists(path))throw new DirectoryNotFoundException(L.T("Ordner fehlt: ")+path);}
                    int opened=0;try{foreach(var path in paths){cancellation.ThrowIfCancellationRequested();host.Folder(path);opened++;}}catch(OperationCanceledException){throw;}catch(Exception ex){throw new InvalidOperationException(opened+" von "+paths.Length+L.T(" Ordnern geöffnet. ")+ex.Message);}return paths.Length+L.T(" Ordner geöffnet");
                case "program":if(!host.FileExists(target))throw new FileNotFoundException(L.T("Programm fehlt: ")+target);cancellation.ThrowIfCancellationRequested();host.Launch(target,action.Arguments??"",Path.GetDirectoryName(target));return L.T("Programmstart angefordert");
                case "website":host.Launch(target,"","");return L.T("Website geöffnet");
                case "steam":host.Launch("steam://rungameid/"+action.Target,"","");return L.T("Steam-Start angefordert");
                case "hotkey":ushort[] keys;TryChord(action.Shortcut,out keys);host.Shortcut(action.Process,keys);return L.T("Tastenkürzel gesendet");
                default:throw new InvalidOperationException(L.T("Aktion ist nicht verfügbar."));
            }
        }
        public static bool TryChord(string text,out ushort[] keys){
            keys=null;var parts=(text??"").Split('+').Select(p=>p.Trim()).ToArray();if(parts.Length<1||parts.Length>5||parts.Length==1&&!System.Text.RegularExpressions.Regex.IsMatch(parts[0],@"^F(?:[1-9]|1[0-9]|2[0-4])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return false;var list=new List<ushort>();
            for(int i=0;i<parts.Length;i++){
                string p=parts[i].ToUpperInvariant();ushort key=0;
                if(p=="CTRL"||p=="STRG"||p=="CONTROL")key=0x11;else if(p=="ALT")key=0x12;else if(p=="SHIFT"||p=="UMSCHALT")key=0x10;else if(p=="WIN")key=0x5B;
                else if(p.Length==1&&char.IsLetterOrDigit(p[0])&&p[0]<128)key=p[0];
                else if(p=="SPACE"||p=="LEERTASTE")key=0x20;else if(p=="ENTER")key=0x0D;else if(p=="ESC")key=0x1B;else if(p=="TAB")key=0x09;else if(p=="UP")key=0x26;else if(p=="DOWN")key=0x28;else if(p=="LEFT")key=0x25;else if(p=="RIGHT")key=0x27;
                else {int f;if(p.StartsWith("F")&&int.TryParse(p.Substring(1),out f)&&f>=1&&f<=24)key=(ushort)(0x70+f-1);}
                bool modifier=key==0x11||key==0x12||key==0x10||key==0x5B;if(key==0||list.Contains(key)||(i<parts.Length-1&&!modifier)||(i==parts.Length-1&&modifier))return false;list.Add(key);
            }
            keys=list.ToArray();return true;
        }
    }
    public sealed partial class NativeAddonHost : IAddonPlatformHost {
        public bool FileExists(string path){return File.Exists(path);}public bool DirectoryExists(string path){return Directory.Exists(path);}
        public void Launch(string target,string arguments,string directory){var start=new ProcessStartInfo(target){UseShellExecute=true,Arguments=arguments};if(!string.IsNullOrEmpty(directory))start.WorkingDirectory=directory;using(var process=Process.Start(start)){} }
        public void Folder(string path){Launch(path,"","");}
        static bool IsTarget(IntPtr window,string process){uint pid;GetWindowThreadProcessId(window,out pid);string name=process.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?process.Substring(0,process.Length-4):process;try{using(var p=Process.GetProcessById((int)pid))return string.Equals(p.ProcessName,name,StringComparison.OrdinalIgnoreCase);}catch{return false;}}
        static IntPtr Focus(string process){
            IntPtr found=IntPtr.Zero;EnumWindows(delegate(IntPtr hwnd,IntPtr arg){if(IsWindowVisible(hwnd)&&GetWindowTextLength(hwnd)>0&&IsTarget(hwnd,process)){found=hwnd;return false;}return true;},IntPtr.Zero);
            if(found==IntPtr.Zero)throw new InvalidOperationException(process+L.T(" hat kein geöffnetes Fenster. Öffne das Programm zuerst."));
            if(IsIconic(found))ShowWindowAsync(found,9);SetForegroundWindow(found);
            if(GetForegroundWindow()!=found||!IsTarget(found,process))throw new InvalidOperationException(process+L.T(" konnte nicht in den Vordergrund wechseln. Öffne das Fenster und versuche es erneut."));return found;
        }
        public void Shortcut(string process,ushort[] keys){
            foreach(int vk in new[]{0x10,0x11,0x12,0x5B,0x5C})if((GetAsyncKeyState(vk)&0x8000)!=0)throw new InvalidOperationException(L.T("Lass die gedrückten Zusatztasten kurz los."));
            IntPtr expected=string.IsNullOrEmpty(process)?IntPtr.Zero:Focus(process);
            if(expected!=IntPtr.Zero&&(GetForegroundWindow()!=expected||!IsTarget(expected,process)))throw new InvalidOperationException(L.T("Der Programmfokus hat sich geändert. Kein Tastenkürzel gesendet."));
            var input=new List<INPUT>();foreach(var key in keys)input.Add(Input(key,false));foreach(var key in keys.Reverse())input.Add(Input(key,true));
            uint sent=SendInput((uint)input.Count,input.ToArray(),Marshal.SizeOf(typeof(INPUT)));
            if(sent!=input.Count){var releases=keys.Reverse().Select(k=>Input(k,true)).ToArray();SendInput((uint)releases.Length,releases,Marshal.SizeOf(typeof(INPUT)));throw new InvalidOperationException(L.T("Windows hat das Tastenkürzel blockiert. Prüfe, ob beide Programme ohne Administratorrechte laufen."));}
        }
        public void DiscordShare(){throw new InvalidOperationException(L.T("Discord-Aktionen benötigen die zentrale Action-Engine."));}
        static INPUT Input(ushort key,bool up){return new INPUT{type=1,data=new INPUTUNION{keyboard=new KEYBDINPUT{vk=key,flags=up?2u:0u}}};}
        delegate bool EnumProc(IntPtr hwnd,IntPtr arg);
        [StructLayout(LayoutKind.Sequential)]struct INPUT{public uint type;public INPUTUNION data;}
        [StructLayout(LayoutKind.Explicit)]struct INPUTUNION{[FieldOffset(0)]public KEYBDINPUT keyboard;[FieldOffset(0)]public MOUSEINPUT mouse;}
        [StructLayout(LayoutKind.Sequential)]struct KEYBDINPUT{public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
        [StructLayout(LayoutKind.Sequential)]struct MOUSEINPUT{public int x,y;public uint data,flags,time;public UIntPtr extra;}
        [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc proc,IntPtr arg);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
        [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr hwnd,int command);
        [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,INPUT[] input,int size);
    }
}
