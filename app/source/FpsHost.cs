using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Separate, narrowly scoped process: elevation never changes the WPF app's privileges.
namespace IrisFpsHost {
    static class Program {
        const string Expected="b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";
        static readonly object outputGate=new object();static StreamWriter writer;static volatile bool stop;static volatile int wantedPid;static long wantedStarted;static int generation;static Process capture;static string session="";static int parentId;static long parentStarted;static string helper;
        static readonly Stopwatch flushTimer=new Stopwatch();
        static bool canFlush;
        [StructLayout(LayoutKind.Sequential)]
        struct TraceHeader {
            public uint BufferSize,ProviderId;
            public ulong HistoricalContext;
            public long Timestamp;
            public Guid Guid;
            public uint ClientContext,Flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct TraceProperties {
            public TraceHeader Header;
            public uint BufferSize,MinimumBuffers,MaximumBuffers,MaximumFileSize,LogFileMode,FlushTimer,EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers,FreeBuffers,EventsLost,BuffersWritten,LogBuffersLost,RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset,LoggerNameOffset;
        }
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern uint ControlTraceW(ulong sessionId,string name,IntPtr properties,uint control);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafePipeHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool WaitNamedPipeW(string path,uint timeout);
        static NamedPipeClientStream Connect(string name){
            // Overlapped I/O allows simultaneous command reads and metric writes on this local pipe.
            string path=@"\\.\pipe\"+name;var elapsed=Stopwatch.StartNew();
            while(elapsed.ElapsedMilliseconds<5000){var handle=CreateFileW(path,0xc0000000,0,IntPtr.Zero,3,0x40110000,IntPtr.Zero);if(!handle.IsInvalid)return new NamedPipeClientStream(PipeDirection.InOut,true,true,handle);int error=Marshal.GetLastWin32Error();handle.Dispose();if(error!=2&&error!=231)throw new System.ComponentModel.Win32Exception(error);if(error==231)WaitNamedPipeW(path,100);else Thread.Sleep(25);}
            throw new TimeoutException("Iris FPS pipe connection timed out.");
        }
        static void Send(string line){lock(outputGate){try{writer.WriteLine(line);writer.Flush();}catch{stop=true;}}}
        static bool ParentAlive(){try{using(var p=Process.GetProcessById(parentId))return p.StartTime.ToUniversalTime().Ticks==parentStarted&&string.Equals(p.MainModule.FileName,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Iris.exe"),StringComparison.OrdinalIgnoreCase);}catch{return false;}}
        static bool TargetAlive(int pid,long started){try{using(var p=Process.GetProcessById(pid))return p.StartTime.ToUniversalTime().Ticks==started;}catch{return false;}}
        static bool VerifyHelper(){try{using(var sha=SHA256.Create())using(var stream=File.OpenRead(helper))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()==Expected;}catch{return false;}}
        static uint FlushTrace(string name){
            const int NameBytes=1024*2;
            const uint FlushControl=3;
            int size=Marshal.SizeOf(typeof(TraceProperties));
            var properties=new TraceProperties{
                Header=new TraceHeader{BufferSize=(uint)(size+2*NameBytes)},
                LoggerNameOffset=(uint)size,LogFileNameOffset=(uint)(size+NameBytes)
            };
            IntPtr buffer=Marshal.AllocHGlobal((int)properties.Header.BufferSize);
            try{
                Marshal.StructureToPtr(properties,buffer,false);
                return ControlTraceW(0,name,buffer,FlushControl);
            }finally{Marshal.FreeHGlobal(buffer);}
        }
        static void FlushCaptureIfDue(){
            if(!canFlush||flushTimer.ElapsedMilliseconds<300)return;
            flushTimer.Restart();
            // ETW normally flushes a real-time buffer once a second. Ask Windows
            // to deliver our own live session sooner so recent game frames do not expire in transit.
            uint status=FlushTrace(session);
            const uint SessionNotFound=4201;
            if(status!=0&&status!=SessionNotFound)canFlush=false;
        }
        static void StopCapture(){
            var old=capture;capture=null;if(old==null)return;Interlocked.Increment(ref generation);
            try{if(!old.HasExited&&!string.IsNullOrEmpty(session)){var info=new ProcessStartInfo(helper,"--session_name "+session+" --terminate_existing_session --no_console_stats"){UseShellExecute=false,CreateNoWindow=true};using(var cleanup=Process.Start(info)){if(!cleanup.WaitForExit(2500))cleanup.Kill();}if(!old.WaitForExit(1500))old.Kill();}}catch{}finally{old.Dispose();session="";}
        }
        static void StartCapture(int pid,long started){
            StopCapture();if(pid<=0)return;if(!TargetAlive(pid,started)){Send("STATE\tSpiel beendet.");return;}int rev=Interlocked.Increment(ref generation);session="IrisFps-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N");
            var info=new ProcessStartInfo(helper,"--process_id "+pid+" --session_name "+session+" --output_stdout --v1_metrics --qpc_time --no_console_stats --no_track_input --no_track_gpu --terminate_on_proc_exit"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.GetDirectoryName(helper)};
            var child=new Process{StartInfo=info};capture=child;child.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null&&generation==rev)Send("DATA\t"+rev+"\t"+e.Data);};child.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null&&generation==rev){string text=e.Data.Length>1500?e.Data.Substring(0,1500):e.Data;Send("ERROR\t"+text);}};
            try{child.Start();canFlush=true;flushTimer.Restart();Send("TARGET\t"+pid+"\t"+rev);Send("STATE\tMessung läuft.");child.BeginOutputReadLine();child.BeginErrorReadLine();}catch(Exception ex){Send("ERROR\terror: "+ex.Message);StopCapture();}
        }
        static int Main(string[] args){
            if(args.Length!=3||!Regex.IsMatch(args[0],"^IrisFps-[a-f0-9]{32}$")||!int.TryParse(args[1],out parentId)||!long.TryParse(args[2],out parentStarted))return 2;
            helper=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Tools","PresentMon","PresentMon.exe");if(!ParentAlive()||!VerifyHelper())return 3;
            try{using(var pipe=Connect(args[0])){writer=new StreamWriter(pipe,new UTF8Encoding(false)){AutoFlush=true};var reader=new StreamReader(pipe,Encoding.UTF8);Send("READY\t"+new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator));
                var input=new Thread(delegate(){try{string line;while(!stop&&(line=reader.ReadLine())!=null){if(line=="STOP")break;var parts=line.Split('\t');int pid;long started;if(parts.Length==3&&parts[0]=="TARGET"&&int.TryParse(parts[1],out pid)&&pid>=0&&long.TryParse(parts[2],out started)){Interlocked.Exchange(ref wantedStarted,started);wantedPid=pid;}}}catch{}finally{stop=true;}}){IsBackground=true};input.Start();int active=-1;long activeStarted=0;
                while(!stop&&ParentAlive()){int pid=wantedPid;long started=Interlocked.Read(ref wantedStarted);if(pid!=active||started!=activeStarted){active=pid;activeStarted=started;StartCapture(pid,started);}if(capture!=null){bool exited=false;try{exited=capture.HasExited;}catch{exited=true;}if(exited){Send("ENDED\t"+capture.ExitCode);StopCapture();}else FlushCaptureIfDue();}Thread.Sleep(100);}StopCapture();}}
            catch(Exception ex){try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fps-host-error.log"),ex.ToString());}catch{}return 4;}finally{stop=true;StopCapture();}return 0;
        }
    }
}
