using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Iris {
    public sealed class AudioSource {
        public string Key,Name,Path,Package,ProcessName;
        public double Volume;public bool Muted,Active;
        public AudioSource Copy(){return (AudioSource)MemberwiseClone();}
    }
    // All Core Audio objects stay on one MTA worker. UI receives immutable copies only.
    public sealed class AudioSessions : IDisposable {
        readonly object gate=new object();
        readonly Dictionary<string,double> pending=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        readonly List<Session> sessions=new List<Session>();
        readonly Dictionary<string,Endpoint> endpoints=new Dictionary<string,Endpoint>();
        readonly List<Arrival> arrivals=new List<Arrival>();
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly Thread worker;
        AudioSource[] snapshot=new AudioSource[0];
        volatile bool stopping;double master=double.NaN;string error="";
        sealed class Session {public string Id,Device;public IControl Control;public ISimpleVolume Volume;public AudioSource Info;}
        sealed class Endpoint {public IDevice Device;public IManager Manager;public Notification Notification;}
        sealed class Arrival {public string Device;public IControl Control;}
        public AudioSessions(){worker=new Thread(Run){IsBackground=true,Name="Iris Audio Sessions"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();}
        public AudioSource[] Snapshot(){lock(gate)return snapshot.Select(s=>s.Copy()).ToArray();}
        public double Master {get{lock(gate)return master;}}
        public string Error {get{lock(gate)return error;}}
        public void Set(string key,double value){if(string.IsNullOrEmpty(key))return;lock(gate)pending[key]=Math.Max(0,Math.Min(1,value));wake.Set();}
        public static AudioSource FindSource(IEnumerable<AudioSource> sources,string source){
            if(string.IsNullOrWhiteSpace(source))return null;
            int split=source.IndexOf('!');string package=split>0?source.Substring(0,split):"";
            string process=MediaSourceLauncher.ProcessName(source);
            if(package.Length>0){var exact=sources.FirstOrDefault(s=>string.Equals(s.Package,package,StringComparison.OrdinalIgnoreCase));if(exact!=null)return exact;}
            // SMTC identifiers for these packaged players are stable even when package lookup is denied.
            if(package.StartsWith("AppleInc.AppleMusicWin_",StringComparison.OrdinalIgnoreCase))process="AppleMusic";
            else if(package.StartsWith("SpotifyAB.SpotifyMusic_",StringComparison.OrdinalIgnoreCase))process="Spotify";
            return process.Length==0?null:sources.FirstOrDefault(s=>string.Equals(s.ProcessName,process,StringComparison.OrdinalIgnoreCase));
        }
        void Run(){
            DateTime discover=DateTime.MinValue;
            try{while(!stopping){try{
                if(DateTime.UtcNow>=discover){Discover();discover=DateTime.UtcNow.AddSeconds(2);}
                Arrival[] added;lock(arrivals){added=arrivals.ToArray();arrivals.Clear();}foreach(var arrival in added)AddSession(arrival.Device,arrival.Control);
                Dictionary<string,double> writes;lock(gate){writes=new Dictionary<string,double>(pending,StringComparer.OrdinalIgnoreCase);pending.Clear();}
                foreach(var session in sessions.ToArray()){try{
                    int state;if(session.Control.GetState(out state)<0||state==2){Remove(session);continue;}
                    double requested;if(writes.TryGetValue(session.Info.Key,out requested)){Guid context=typeof(AudioSessions).GUID;Marshal.ThrowExceptionForHR(session.Volume.SetMasterVolume((float)requested,ref context));Marshal.ThrowExceptionForHR(session.Volume.SetMute(false,ref context));}
                    float volume;bool muted;Marshal.ThrowExceptionForHR(session.Volume.GetMasterVolume(out volume));Marshal.ThrowExceptionForHR(session.Volume.GetMute(out muted));session.Info.Volume=volume;session.Info.Muted=muted;session.Info.Active=state==1;
                }catch{Remove(session);discover=DateTime.MinValue;}}
                var values=sessions.GroupBy(s=>s.Info.Key,StringComparer.OrdinalIgnoreCase).Select(g=>{var info=g.First().Info.Copy();info.Volume=g.Max(s=>s.Info.Volume);info.Muted=g.All(s=>s.Info.Muted);info.Active=g.Any(s=>s.Info.Active);return info;}).OrderByDescending(s=>s.Active).ThenBy(s=>s.Name,StringComparer.CurrentCultureIgnoreCase).ToArray();
                double endpoint=double.NaN;try{endpoint=NativeVolume.Get();}catch{}
                lock(gate){snapshot=values;master=endpoint;error="";}
            }catch(Exception ex){lock(gate)error=ex.Message;discover=DateTime.MinValue;}wake.WaitOne(100);}}
            finally{foreach(var session in sessions.ToArray())Remove(session);foreach(var endpoint in endpoints.Values)ReleaseEndpoint(endpoint);lock(arrivals){foreach(var arrival in arrivals)Release(arrival.Control);arrivals.Clear();}wake.Dispose();}
        }
        void Discover(){
            var enumerator=(IDeviceEnumerator)new DeviceEnumerator();IDeviceCollection collection=null;
            try{Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0,1,out collection));uint count;Marshal.ThrowExceptionForHR(collection.GetCount(out count));var found=new HashSet<string>();
                for(uint i=0;i<count;i++){IDevice device=null;collection.Item(i,out device);if(device==null)continue;string id;device.GetId(out id);found.Add(id);Endpoint endpoint;
                    object activated=null;Guid iid=typeof(IManager).GUID;
                    try{
                        if(!endpoints.TryGetValue(id,out endpoint)){Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out activated));var manager=(IManager)activated;var notification=new Notification(this,id);Marshal.ThrowExceptionForHR(manager.RegisterSessionNotification(notification));endpoint=new Endpoint{Device=device,Manager=manager,Notification=notification};endpoints.Add(id,endpoint);device=null;activated=null;}
                        ISessionEnumerator list=null;try{Marshal.ThrowExceptionForHR(endpoint.Manager.GetSessionEnumerator(out list));int n;list.GetCount(out n);for(int j=0;j<n;j++){IControl control;if(list.GetSession(j,out control)>=0)AddSession(id,control);}}finally{Release(list);}
                    }catch{if(endpoints.TryGetValue(id,out endpoint)){foreach(var session in sessions.Where(s=>s.Device==id).ToArray())Remove(session);ReleaseEndpoint(endpoint);endpoints.Remove(id);}}
                    finally{Release(activated);Release(device);}
                }
                foreach(var id in endpoints.Keys.Where(id=>!found.Contains(id)).ToArray()){foreach(var session in sessions.Where(s=>s.Device==id).ToArray())Remove(session);ReleaseEndpoint(endpoints[id]);endpoints.Remove(id);}
            }finally{Release(collection);Release(enumerator);}
        }
        void AddSession(string device,IControl control){
            bool retained=false;try{var extended=(IControl2)control;string id;Marshal.ThrowExceptionForHR(extended.GetSessionInstanceIdentifier(out id));id=device+"|"+id;if(sessions.Any(s=>s.Id==id))return;
                uint pid;Marshal.ThrowExceptionForHR(extended.GetProcessId(out pid));string display;control.GetDisplayName(out display);var info=Describe(pid,display);var volume=(ISimpleVolume)control;sessions.Add(new Session{Id=id,Device=device,Control=control,Volume=volume,Info=info});retained=true;
            }catch{}finally{if(!retained)Release(control);}
        }
        static AudioSource Describe(uint pid,string display){
            var info=new AudioSource{Key="pid:"+pid,Name=L.T("Audioquelle"),Path="",Package="",ProcessName=""};
            if(pid==0){info.Key="system";info.Name=L.T("Systemsounds");return info;}
            IntPtr handle=OpenProcess(0x1000,false,pid);if(handle!=IntPtr.Zero){try{uint length=1024;var path=new StringBuilder((int)length);if(QueryFullProcessImageName(handle,0,path,ref length))info.Path=path.ToString();uint size=0;GetPackageFamilyName(handle,ref size,null);if(size>0&&size<1024){var package=new StringBuilder((int)size);if(GetPackageFamilyName(handle,ref size,package)==0)info.Package=package.ToString();}}finally{CloseHandle(handle);}}
            try{using(var p=Process.GetProcessById((int)pid))info.ProcessName=p.ProcessName;}catch{}
            info.Name=info.ProcessName.Length>0?info.ProcessName:L.T("Audioquelle ")+pid;
            if(info.Path.Length>0){info.Key="exe:"+info.Path.ToLowerInvariant();try{string product=FileVersionInfo.GetVersionInfo(info.Path).ProductName;if(!string.IsNullOrWhiteSpace(product))info.Name=product;}catch{}}
            if(info.Package.Length>0)info.Key="package:"+info.Package;
            if(info.Package.StartsWith("AppleInc.AppleMusicWin_",StringComparison.OrdinalIgnoreCase)||info.ProcessName.Equals("AppleMusic",StringComparison.OrdinalIgnoreCase))info.Name="Apple Music";
            else if(info.Package.StartsWith("SpotifyAB.SpotifyMusic_",StringComparison.OrdinalIgnoreCase))info.Name="Spotify";
            else if(info.ProcessName.Equals("chrome",StringComparison.OrdinalIgnoreCase))info.Name="Google Chrome";
            else if(info.ProcessName.Equals("msedge",StringComparison.OrdinalIgnoreCase))info.Name="Microsoft Edge";
            else if(!string.IsNullOrWhiteSpace(display)&&!display.StartsWith("@")&&!display.Contains("\\"))info.Name=display;
            return info;
        }
        void Remove(Session session){sessions.Remove(session);Release(session.Control);}
        static void ReleaseEndpoint(Endpoint endpoint){try{endpoint.Manager.UnregisterSessionNotification(endpoint.Notification);}catch{}Release(endpoint.Manager);Release(endpoint.Device);}
        static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
        public void Dispose(){stopping=true;wake.Set();}
        [ComVisible(true),ClassInterface(ClassInterfaceType.None)]sealed class Notification : INotification {
            readonly AudioSessions owner;readonly string device;public Notification(AudioSessions owner,string device){this.owner=owner;this.device=device;}
            public int OnSessionCreated(IControl control){lock(owner.arrivals)owner.arrivals.Add(new Arrival{Device=device,Control=control});if(!owner.stopping)owner.wake.Set();return 0;}
        }
        [DllImport("kernel32.dll")]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageName(IntPtr handle,uint flags,StringBuilder path,ref uint length);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackageFamilyName(IntPtr process,ref uint length,StringBuilder name);
        [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class DeviceEnumerator {}
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDeviceEnumerator {[PreserveSig]int EnumAudioEndpoints(int flow,uint mask,out IDeviceCollection collection);}
        [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDeviceCollection {[PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out IDevice device);}
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDevice {[PreserveSig]int Activate(ref Guid iid,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object value);[PreserveSig]int OpenPropertyStore(int access,out IntPtr store);[PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);}
        [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IManager {
            [PreserveSig]int GetAudioSessionControl(IntPtr guid,uint flags,out IControl control);[PreserveSig]int GetSimpleAudioVolume(IntPtr guid,uint flags,out ISimpleVolume volume);[PreserveSig]int GetSessionEnumerator(out ISessionEnumerator list);[PreserveSig]int RegisterSessionNotification(INotification notification);[PreserveSig]int UnregisterSessionNotification(INotification notification);
        }
        [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ISessionEnumerator {[PreserveSig]int GetCount(out int count);[PreserveSig]int GetSession(int index,out IControl control);}
        [ComImport,Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IControl {
            [PreserveSig]int GetState(out int state);[PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);[PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);[PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);[PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,ref Guid context);[PreserveSig]int GetGroupingParam(out Guid grouping);[PreserveSig]int SetGroupingParam(ref Guid grouping,ref Guid context);[PreserveSig]int RegisterAudioSessionNotification(IntPtr notification);[PreserveSig]int UnregisterAudioSessionNotification(IntPtr notification);
        }
        [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IControl2 {
            [PreserveSig]int GetState(out int state);[PreserveSig]int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)]out string name);[PreserveSig]int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)]string name,ref Guid context);[PreserveSig]int GetIconPath([MarshalAs(UnmanagedType.LPWStr)]out string path);[PreserveSig]int SetIconPath([MarshalAs(UnmanagedType.LPWStr)]string path,ref Guid context);[PreserveSig]int GetGroupingParam(out Guid grouping);[PreserveSig]int SetGroupingParam(ref Guid grouping,ref Guid context);[PreserveSig]int RegisterAudioSessionNotification(IntPtr notification);[PreserveSig]int UnregisterAudioSessionNotification(IntPtr notification);[PreserveSig]int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)]out string id);[PreserveSig]int GetProcessId(out uint pid);
        }
        [ComImport,Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ISimpleVolume {[PreserveSig]int SetMasterVolume(float volume,ref Guid context);[PreserveSig]int GetMasterVolume(out float volume);[PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool mute,ref Guid context);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool mute);}
        [ComVisible(true),Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface INotification {[PreserveSig]int OnSessionCreated(IControl control);}
    }
}
