using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Iris {
    public sealed partial class NativeAddonHost {
        public Action OpenIrisMixer;
        static readonly object discoveryGate=new object();static DateTime discoveryUtc;static string discordApp="",chatApp="",discoveryNote="";
        static readonly int ownPid=Process.GetCurrentProcess().Id;
        static object ComCall(object value,string name,BindingFlags flags,params object[] args){return value.GetType().InvokeMember(name,flags,null,value,args);}
        static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
        static void ReadApps(CancellationToken token){lock(discoveryGate){
            if((DateTime.UtcNow-discoveryUtc).TotalMinutes<2)return;
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);string discord=Path.Combine(local,"Discord","Update.exe");string foundDiscord=File.Exists(discord)?discord:"",foundChat="",note="";
            object shell=null,folder=null,items=null;
            try{var type=Type.GetTypeFromProgID("Shell.Application");if(type==null)throw new NotSupportedException();shell=Activator.CreateInstance(type);folder=ComCall(shell,"NameSpace",BindingFlags.InvokeMethod,"shell:AppsFolder");if(folder==null)throw new NotSupportedException();items=ComCall(folder,"Items",BindingFlags.InvokeMethod);int count=Math.Min(1024,Convert.ToInt32(ComCall(items,"Count",BindingFlags.GetProperty)));
                for(int i=0;i<count;i++){token.ThrowIfCancellationRequested();object item=null;try{item=ComCall(items,"Item",BindingFlags.InvokeMethod,i);string path=Convert.ToString(ComCall(item,"Path",BindingFlags.GetProperty));if(Regex.IsMatch(path??"",@"^OpenAI\.(?:Codex|ChatGPT)_[a-zA-Z0-9._-]+!App$"))foundChat=path;if(string.Equals(path,"com.squirrel.Discord.Discord",StringComparison.OrdinalIgnoreCase))foundDiscord="aumid:"+path;if(foundChat.Length>0&&foundDiscord.Length>0)break;}finally{Release(item);}}
            }catch(OperationCanceledException){throw;}catch{note=L.T("Windows-Appkatalog konnte nicht vollständig gelesen werden.");}finally{Release(items);Release(folder);Release(shell);}
            token.ThrowIfCancellationRequested();discordApp=foundDiscord;chatApp=foundChat;discoveryNote=note;discoveryUtc=DateTime.UtcNow;
        }}
        public AddonEnvironmentSnapshot Inspect(CancellationToken token){return Inspect(token,false);}
        public AddonEnvironmentSnapshot Inspect(CancellationToken token,bool forceAppRefresh){
            var env=new AddonEnvironmentSnapshot();token.ThrowIfCancellationRequested();
            foreach(var protocol in new[]{"steam","codex","ms-settings","spotify","music","vscode"}){token.ThrowIfCancellationRequested();bool registered=false;try{using(var key=Registry.ClassesRoot.OpenSubKey(protocol+@"\shell\open\command"))registered=key!=null;if(!registered){var operation=Windows.System.Launcher.QueryUriSupportAsync(new Uri(protocol+":"),Windows.System.LaunchQuerySupportType.Uri);var completion=new System.Threading.Tasks.TaskCompletionSource<Windows.System.LaunchQuerySupportStatus>();operation.Completed=delegate{try{completion.TrySetResult(operation.GetResults());}catch(Exception ex){completion.TrySetException(ex);}};try{if(completion.Task.Wait(1500,token))registered=completion.Task.Result==Windows.System.LaunchQuerySupportStatus.Available;}finally{if(operation.Status==Windows.Foundation.AsyncStatus.Started)operation.Cancel();}}}catch(OperationCanceledException){throw;}catch{}if(registered)env.Protocols.Add(protocol);}
            foreach(var process in Process.GetProcesses()){using(process){token.ThrowIfCancellationRequested();try{env.Processes.Add(process.ProcessName);}catch{}}}
            if(!string.IsNullOrEmpty(IntegrationDirectory)){try{env.SpotifyConnected=!string.IsNullOrEmpty(AddonConnectionStore.Read(IntegrationDirectory).SpotifyRefresh);}catch{env.Note=L.T("Gespeicherte Integrationsdaten konnten nicht gelesen werden.");}}
            if(forceAppRefresh)lock(discoveryGate)discoveryUtc=DateTime.MinValue;ReadApps(token);lock(discoveryGate){env.DiscordTarget=discordApp;env.ChatGptTarget=chatApp;env.Note=discoveryNote;}return env;
        }
        public static bool IsOwnWindow(IntPtr window){uint pid;GetWindowThreadProcessId(window,out pid);return pid==ownPid;}
        public static bool IsExternalWindow(IntPtr window){if(window==IntPtr.Zero||window==GetDesktopWindow()||window==GetShellWindow()||!IsWindow(window)||!IsWindowVisible(window))return false;var name=new System.Text.StringBuilder(128);GetClassName(window,name,name.Capacity);if(Array.Exists(new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd"},s=>s==name.ToString()))return false;uint pid;GetWindowThreadProcessId(window,out pid);return pid!=0&&pid!=ownPid;}
        public static IntPtr ExternalWindow(IntPtr previous){var current=GetForegroundWindow();return IsExternalWindow(current)?current:IsOwnWindow(current)&&IsExternalWindow(previous)?previous:IntPtr.Zero;}
        static void FocusExternal(IntPtr window){if(!IsExternalWindow(window))throw new InvalidOperationException(L.T("Kein aktives fremdes Zielfenster bekannt. Aktiviere zuerst das gewünschte Fenster."));if(IsIconic(window))ShowWindowAsync(window,9);SetForegroundWindow(window);if(GetForegroundWindow()!=window)throw new InvalidOperationException(L.T("Das Zielfenster konnte nicht aktiviert werden. Es wurde kein Fensterkürzel gesendet."));}
        static void NativeCommand(string name,string arguments){using(var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),name),arguments){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(process==null)throw new InvalidOperationException(L.T("Windows-Befehl konnte nicht gestartet werden."));if(!process.WaitForExit(3000))throw new InvalidOperationException(L.T("Windows hat noch keine Bestätigung geliefert; der Befehl kann bereits übergeben sein."));if(process.ExitCode!=0)throw new InvalidOperationException(L.T("Windows hat den Befehl abgelehnt (")+process.ExitCode+").");}}
        static string ClipboardText(){string value=null;Exception failure=null;var thread=new Thread(delegate(){try{if(System.Windows.Forms.Clipboard.ContainsText())value=System.Windows.Forms.Clipboard.GetText();}catch(Exception ex){failure=ex;}}){IsBackground=true,Name=L.T("Iris Zwischenablage lesen")};thread.SetApartmentState(ApartmentState.STA);thread.Start();if(!thread.Join(1800))throw new InvalidOperationException(L.T("Die Zwischenablage antwortet nicht. Es wurde kein Entwurf geöffnet."));if(failure!=null)throw new InvalidOperationException(L.T("Zwischenablage konnte nicht gelesen werden."),failure);if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException(L.T("Kopiere zuerst den gewünschten Text. Die Zwischenablage wurde nicht verändert."));if(value.Length>2000)throw new InvalidOperationException(L.T("Der Text ist für diesen lokalen Entwurfsweg zu lang (höchstens 2000 Zeichen). Öffne ChatGPT und füge ihn selbst ein."));return value;}
        public AddonResult ExecutePlatform(AddonDefinition definition,AddonAction action,AddonExecutionContext context,CancellationToken token){
            token.ThrowIfCancellationRequested();if(definition.Confirm&&!context.Confirmed)return AddonResult.Of(AddonResultCode.ConfirmationRequired,definition.Description);
            if(definition.Operation==AddonPlatformOperation.Integration)return ExecuteIntegration(definition,action,context,token);
            switch(definition.Operation){
                case AddonPlatformOperation.Discord:return RunDiscord(action,token);
                case AddonPlatformOperation.OpenIrisMixer:if(OpenIrisMixer==null)throw new InvalidOperationException(L.T("Iris-Mixer ist nicht verfügbar."));OpenIrisMixer();return AddonResult.Of(AddonResultCode.Succeeded,L.T("Iris-Mixer geöffnet"));
                case AddonPlatformOperation.OpenDiscord:ReadApps(token);string discord;lock(discoveryGate)discord=discordApp;if(discord.StartsWith("aumid:",StringComparison.Ordinal)&&string.Equals(discord.Substring(6),"com.squirrel.Discord.Discord",StringComparison.OrdinalIgnoreCase))Launch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),"shell:AppsFolder\\"+discord.Substring(6),"");else{if(!File.Exists(discord))throw new FileNotFoundException(L.T("Discord nicht gefunden."));Launch(discord,"--processStart Discord.exe",Path.GetDirectoryName(discord));}return AddonResult.Of(AddonResultCode.Succeeded,L.T("Discord-Start angefordert"));
                case AddonPlatformOperation.OpenChatGpt:ReadApps(token);string app;lock(discoveryGate)app=chatApp;if(!Regex.IsMatch(app??"",@"^OpenAI\.(?:Codex|ChatGPT)_[a-zA-Z0-9._-]+!App$"))throw new InvalidOperationException(L.T("ChatGPT-Desktop-App nicht registriert."));Launch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),"shell:AppsFolder\\"+app,"");return AddonResult.Of(AddonResultCode.Succeeded,L.T("Desktop-App geöffnet; Anmeldung und Funktionen hängen von der App ab"));
                case AddonPlatformOperation.PrepareChatDraft:string text=ClipboardText();token.ThrowIfCancellationRequested();Launch(AddonPromptBuilder.CreateUri(definition.Preset,text),"","");return AddonResult.Of(AddonResultCode.Succeeded,L.T("Entwurf vorbereitet – in ChatGPT prüfen und selbst senden"));
                case AddonPlatformOperation.VolumeUp:case AddonPlatformOperation.VolumeDown:double level=NativeVolume.Get()+(definition.Operation==AddonPlatformOperation.VolumeUp?.05:-.05);NativeVolume.Set(level);return AddonResult.State(L.T("Windows-Lautstärke ")+Math.Round(NativeVolume.Get()*100)+" %","volume");
                case AddonPlatformOperation.VolumeMute:case AddonPlatformOperation.MicrophoneMute:bool input=definition.Operation==AddonPlatformOperation.MicrophoneMute;bool muted=NativeVolume.ToggleMute(input);return AddonResult.State((input?L.T("Standardmikrofon"):L.T("Systemton"))+(muted?L.T(" stumm"):L.T(" an")),muted?"muted":"unmuted");
                case AddonPlatformOperation.Minimize:case AddonPlatformOperation.Maximize:case AddonPlatformOperation.SnapLeft:case AddonPlatformOperation.SnapRight:case AddonPlatformOperation.Topmost:
                    var window=context.TargetWindow;FocusExternal(window);token.ThrowIfCancellationRequested();
                    if(definition.Operation==AddonPlatformOperation.Minimize||definition.Operation==AddonPlatformOperation.Maximize){if(!ShowWindowAsync(window,definition.Operation==AddonPlatformOperation.Minimize?6:3))throw new InvalidOperationException(L.T("Fensteraktion von Windows abgelehnt."));return AddonResult.Of(AddonResultCode.Succeeded,L.T("Fensteraktion angefordert"));}
                    if(definition.Operation==AddonPlatformOperation.Topmost){bool top=(GetWindowLong(window,-20)&8)!=0;if(!SetWindowPos(window,new IntPtr(top?-2:-1),0,0,0,0,0x13))throw new InvalidOperationException(L.T("Vordergrundzustand nicht geändert."));bool actual=(GetWindowLong(window,-20)&8)!=0;return AddonResult.State(actual?L.T("Immer im Vordergrund an"):L.T("Immer im Vordergrund aus"),actual?"topmost":"normal");}
                    Shortcut("",new ushort[]{0x5B,definition.Operation==AddonPlatformOperation.SnapLeft?(ushort)0x25:(ushort)0x27});return AddonResult.Of(AddonResultCode.Succeeded,L.T("Fensteranordnung angefordert"));
                case AddonPlatformOperation.Lock:if(!LockWorkStation())throw new InvalidOperationException(L.T("Windows konnte nicht gesperrt werden."));return AddonResult.Of(AddonResultCode.Succeeded,L.T("Windows-Sperre angefordert"));
                case AddonPlatformOperation.Shutdown:case AddonPlatformOperation.Restart:NativeCommand("shutdown.exe",definition.Operation==AddonPlatformOperation.Shutdown?"/s /t 0":"/r /t 0");return AddonResult.Of(AddonResultCode.Succeeded,L.T("Windows-Vorgang angefordert"));
                case AddonPlatformOperation.CancelShutdown:NativeCommand("shutdown.exe","/a");return AddonResult.Of(AddonResultCode.Succeeded,L.T("Ausstehender Windows-Vorgang abgebrochen"));
                case AddonPlatformOperation.Sleep:if(!SetSuspendState(false,false,false))throw new InvalidOperationException(L.T("Windows hat Standby abgelehnt."));return AddonResult.Of(AddonResultCode.Succeeded,L.T("Standby angefordert"));
                case AddonPlatformOperation.CloseProcess:
                    string name=action.Process.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?action.Process.Substring(0,action.Process.Length-4):action.Process;
                    if(Array.Exists(new[]{"iris","system","registry","csrss","wininit","winlogon","services","lsass","smss","svchost","dwm","explorer"},p=>string.Equals(p,name,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException(L.T("Windows- und Iris-Prozesse werden hier nicht beendet."));
                    int count=0;foreach(var process in Process.GetProcessesByName(name)){using(process){token.ThrowIfCancellationRequested();try{if(process.Id!=ownPid&&process.MainWindowHandle!=IntPtr.Zero&&process.CloseMainWindow())count++;}catch{}}}if(count==0)throw new InvalidOperationException(L.T("Kein zugängliches Programmfenster dieses Prozesses gefunden."));return AddonResult.Of(AddonResultCode.Succeeded,count+L.T(" Fenster um Schließen gebeten. Speicherdialoge selbst bestätigen; kein erzwungenes Beenden."));
                default:return AddonResult.Of(AddonResultCode.Unavailable,L.T("Diese Systemaktion ist nicht implementiert."));
            }
        }
        [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int length);
        [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll",SetLastError=true)]static extern bool LockWorkStation();
        [DllImport("powrprof.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)]bool hibernate,[MarshalAs(UnmanagedType.U1)]bool force,[MarshalAs(UnmanagedType.U1)]bool disableWake);
    }
}
