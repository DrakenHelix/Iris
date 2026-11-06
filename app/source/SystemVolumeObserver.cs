using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Iris {
    public sealed class AudioLevel {
        public readonly double Value;public readonly bool Muted;
        public AudioLevel(double value,bool muted){Value=Math.Max(0,Math.Min(1,value));Muted=muted;}
    }
    // Read-only endpoint notifications. OnNotify only copies its payload and posts UI work.
    public sealed class SystemVolumeObserver : IDisposable {
        readonly ManualResetEvent stop=new ManualResetEvent(false);
        readonly Action<AudioLevel> changed;
        readonly Thread worker;
        AudioLevel latest;
        volatile bool stopping,connected;
        string error="";
        public bool Connected {get{return connected;}}
        public AudioLevel Latest {get{return Interlocked.CompareExchange(ref latest,null,null);}}
        public string Error {get{return Interlocked.CompareExchange(ref error,null,null);}}
        public SystemVolumeObserver(Action<AudioLevel> changed){this.changed=changed;worker=new Thread(Run){IsBackground=true,Name="Iris Volume Notifications"};worker.SetApartmentState(ApartmentState.MTA);worker.Start();}
        void Publish(AudioLevel level,bool notify){Interlocked.Exchange(ref latest,level);if(notify&&!stopping){try{changed(level);}catch{}}}
        void Run(){
            IEndpointVolume volume=null;Callback callback=null;string selected=null;
            try{while(!stopping){
                IDeviceEnumerator enumerator=null;IDevice device=null;object activated=null;
                try{
                    enumerator=(IDeviceEnumerator)new DeviceEnumerator();Marshal.ThrowExceptionForHR(enumerator.Default(0,1,out device));string id;Marshal.ThrowExceptionForHR(device.GetId(out id));
                    if(selected!=id||volume==null){
                        if(volume!=null){connected=false;callback.Active=false;volume.Unregister(callback);Release(volume);volume=null;}
                        Guid iid=typeof(IEndpointVolume).GUID;Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out activated));volume=(IEndpointVolume)activated;activated=null;
                        callback=new Callback(this);Marshal.ThrowExceptionForHR(volume.Register(callback));float scalar;bool muted;Marshal.ThrowExceptionForHR(volume.GetScalar(out scalar));Marshal.ThrowExceptionForHR(volume.GetMute(out muted));Publish(new AudioLevel(scalar,muted),false);callback.Active=true;selected=id;connected=true;
                    }
                    Interlocked.Exchange(ref error,"");
                }catch(Exception ex){connected=false;Interlocked.Exchange(ref error,ex.Message);if(callback!=null)callback.Active=false;if(volume!=null){try{volume.Unregister(callback);}catch{}Release(volume);volume=null;}selected=null;}
                finally{Release(activated);Release(device);Release(enumerator);}
                stop.WaitOne(2000);
            }}finally{connected=false;if(callback!=null)callback.Active=false;if(volume!=null){try{volume.Unregister(callback);}catch{}Release(volume);}stop.Dispose();}
        }
        public void Dispose(){if(stopping)return;stopping=true;stop.Set();}
        static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
        [StructLayout(LayoutKind.Sequential)]struct NotificationData {public Guid Context;[MarshalAs(UnmanagedType.Bool)]public bool Muted;public float Volume;public uint Channels;}
        internal static AudioLevel ReadNotification(IntPtr pointer){var data=(NotificationData)Marshal.PtrToStructure(pointer,typeof(NotificationData));return new AudioLevel(data.Volume,data.Muted);}
        [ComVisible(true),ClassInterface(ClassInterfaceType.None)]sealed class Callback : ICallback {
            readonly SystemVolumeObserver owner;public volatile bool Active;
            public Callback(SystemVolumeObserver owner){this.owner=owner;}
            public int OnNotify(IntPtr pointer){if(pointer==IntPtr.Zero)return unchecked((int)0x80070057);try{owner.Publish(ReadNotification(pointer),Active);}catch{}return 0;}
        }
        [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class DeviceEnumerator {}
        [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDeviceEnumerator {[PreserveSig]int Enum(int flow,uint mask,out IntPtr collection);[PreserveSig]int Default(int flow,int role,out IDevice device);}
        [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDevice {[PreserveSig]int Activate(ref Guid iid,int context,IntPtr parameters,[MarshalAs(UnmanagedType.IUnknown)]out object value);[PreserveSig]int OpenPropertyStore(int access,out IntPtr store);[PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);}
        [ComVisible(true),Guid("657804FA-D6AD-4496-8A60-352752AF4F89"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ICallback {[PreserveSig]int OnNotify(IntPtr data);}
        [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IEndpointVolume {
            [PreserveSig]int Register(ICallback callback);[PreserveSig]int Unregister(ICallback callback);[PreserveSig]int ChannelCount(out uint n);[PreserveSig]int SetDb(float value,ref Guid context);[PreserveSig]int SetScalar(float value,ref Guid context);[PreserveSig]int GetDb(out float value);[PreserveSig]int GetScalar(out float value);
            [PreserveSig]int SetChannelDb(uint channel,float value,ref Guid context);[PreserveSig]int SetChannelScalar(uint channel,float value,ref Guid context);[PreserveSig]int GetChannelDb(uint channel,out float value);[PreserveSig]int GetChannelScalar(uint channel,out float value);[PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool value,ref Guid context);[PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool value);
        }
    }
}
