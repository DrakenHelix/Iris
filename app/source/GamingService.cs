using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Iris {
    public sealed class GamingReader : IDisposable {
        readonly object gate=new object();readonly FpsFrames frames=new FpsFrames();readonly Action<string,bool> state;
        NamedPipeServerStream pipe;StreamWriter writer;Process host;bool disposed,ready;int target,revision;long started;string status=L.T("Messung startet …");bool needsPermission;
        public bool TargetAcknowledged{get{lock(gate)return revision>0;}}public bool Elevated{get;private set;}public bool Ready{get{lock(gate)return ready;}}public string Status{get{lock(gate)return status;}}public bool NeedsPermission{get{lock(gate)return needsPermission;}}
        public GamingReader(Action<string,bool> state){this.state=state;}
        public static bool ToolsValid(){try{var root=AppDomain.CurrentDomain.BaseDirectory;if(!File.Exists(Path.Combine(root,"Iris.FpsHost.exe")))return false;using(var hash=System.Security.Cryptography.SHA256.Create())using(var stream=File.OpenRead(Path.Combine(root,"Tools","PresentMon","PresentMon.exe")))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant()=="b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";}catch{return false;}}
        void State(string text,bool permission){lock(gate){if(disposed)return;status=text;needsPermission=permission;}if(state!=null)state(text,permission);}
        public void Start(bool elevated){
            if(!ToolsValid()){State(L.T("FPS-Helfer fehlt oder wurde verändert. Iris vollständig neu entpacken."),false);Dispose();return;}
            try{string name="IrisFps-"+Guid.NewGuid().ToString("N");pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,null,HandleInheritability.None,PipeAccessRights.ChangePermissions);
            // Preserve the creating token's application capability rules as well as the user/admin rules.
            var security=pipe.GetAccessControl();security.SetAccessRuleProtection(true,true);security.PurgeAccessRules(new SecurityIdentifier(WellKnownSidType.WorldSid,null));security.PurgeAccessRules(new SecurityIdentifier(WellKnownSidType.AnonymousSid,null));security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));pipe.SetAccessControl(security);
            new Thread(ReadPipe){IsBackground=true,Name="Iris FPS pipe"}.Start();
            try{using(var self=Process.GetCurrentProcess()){var start=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Iris.FpsHost.exe"),name+" "+self.Id+" "+self.StartTime.ToUniversalTime().Ticks){WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,UseShellExecute=elevated,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};if(elevated)start.Verb="runas";host=Process.Start(start);}
                Task.Factory.StartNew(delegate{try{if(host.WaitForExit(6500)){if(!disposed){State(L.T("FPS-Helfer wurde beendet. Bei fehlenden Messrechten Windows-Freigabe verwenden."),true);Dispose();}}else if(!Ready&&!disposed){State(L.T("FPS-Helfer antwortet nicht."),false);Dispose();}}catch{}});
            }catch(System.ComponentModel.Win32Exception ex){State(ex.NativeErrorCode==1223?L.T("Windows-Freigabe abgebrochen. FPS bleibt aus."):L.T("FPS-Helfer konnte nicht starten: ")+ex.Message,false);Dispose();}catch(Exception ex){State(L.T("FPS-Helfer konnte nicht starten: ")+ex.Message,false);Dispose();}
            }catch(Exception ex){State(L.T("FPS-Verbindung konnte nicht starten: ")+ex.Message,false);Dispose();}
        }
        void ReadPipe(){
            try{State(L.T("FPS-Helfer verbindet …"),false);pipe.WaitForConnection();using(var reader=new StreamReader(pipe,Encoding.UTF8,false,65536,true)){lock(gate){if(disposed)return;writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};}string line;while(!disposed&&(line=reader.ReadLine())!=null){if(line.Length>20000)continue;int split=line.IndexOf('\t');string kind=split<0?line:line.Substring(0,split),body=split<0?"":line.Substring(split+1);
                    if(kind=="READY"){lock(gate){ready=true;Elevated=body=="True";SendTarget();}State(L.T("Bereit · wartet auf ein Spiel."),false);}
                    else if(kind=="TARGET"){var parts=body.Split('\t');int pid,rev;if(parts.Length==2&&int.TryParse(parts[0],out pid)&&int.TryParse(parts[1],out rev)){lock(gate){if(pid==target){revision=rev;frames.Target(target);}}}}
                    else if(kind=="DATA"){int tab=body.IndexOf('\t'),rev;if(tab>0&&int.TryParse(body.Substring(0,tab),out rev)){lock(gate){if(rev==revision)frames.Read(body.Substring(tab+1),DateTime.UtcNow);}}}
                    else if(kind=="STATE")State(L.T(body),false);
                    else if(kind=="ERROR"){if(!body.StartsWith("error:",StringComparison.OrdinalIgnoreCase)&&body.IndexOf("access denied",StringComparison.OrdinalIgnoreCase)<0)continue;bool denied=NeedsPermission||body.IndexOf("denied",StringComparison.OrdinalIgnoreCase)>=0;State(denied?L.T("Windows-Messrechte fehlen. Messung über Windows-Freigabe starten."):L.T("Messung: ")+body,denied);}
                    else if(kind=="ENDED"){lock(gate){frames.Target(target);}if(!NeedsPermission)State(L.T("Keine aktuellen FPS-Daten. Prüfe Messrechte und Spielunterstützung."),false);}
                }}}
            catch(Exception ex){if(!disposed)State(L.T("FPS-Verbindung beendet: ")+ex.Message,false);}finally{lock(gate){ready=false;frames.Target(0);}}
        }
        public void Target(GameTarget game){lock(gate){int pid=game==null?0:game.Pid;long time=game==null?0:game.Started;if(pid==target&&time==started)return;target=pid;started=time;revision=0;frames.Target(target);SendTarget();}}
        void SendTarget(){if(writer==null||!ready||disposed)return;try{writer.WriteLine("TARGET\t"+target+"\t"+started);}catch{}}
        public FpsSample Latest(){return frames.Latest(DateTime.UtcNow);}
        public bool WaitForExit(int milliseconds){try{return host==null||host.WaitForExit(milliseconds);}catch{return true;}}
        public int HostExitCode{get{try{return host==null?-1:host.ExitCode;}catch{return -2;}}}
        public void Dispose(){lock(gate){if(disposed)return;try{if(writer!=null)writer.WriteLine("STOP");}catch{}disposed=true;ready=false;frames.Target(0);try{if(pipe!=null)pipe.Dispose();}catch{}}}
    }
}
