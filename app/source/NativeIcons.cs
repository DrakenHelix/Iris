using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Iris {
    public static class NativeIcons {
        static readonly Dictionary<string,ImageSource> cache=new Dictionary<string,ImageSource>(StringComparer.OrdinalIgnoreCase);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ShellInfo {public IntPtr Icon;public int Index;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string DisplayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string TypeName;}
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out ShellInfo info,uint size,uint flags);
        [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
        public static ImageSource Get(string path){
            if(string.IsNullOrWhiteSpace(path))return null;ImageSource cached;if(cache.TryGetValue(path,out cached))return cached;
            ShellInfo info;bool exists=File.Exists(path)||Directory.Exists(path);uint flags=0x100u|(exists?0u:0x10u);
            if(SHGetFileInfo(path,0x80,out info,(uint)Marshal.SizeOf(typeof(ShellInfo)),flags)==IntPtr.Zero||info.Icon==IntPtr.Zero)return null;
            try{var image=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(32,32));image.Freeze();if(cache.Count>256)cache.Clear();cache[path]=image;return image;}finally{DestroyIcon(info.Icon);}
        }
    }
}
