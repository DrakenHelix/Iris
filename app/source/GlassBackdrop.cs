using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Windows.Media;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Iris {
    // Windows owns the backdrop pixels. No desktop screenshots, capture loop or extra runtime package.
    public sealed class NativeGlassBackdrop : IDisposable {
        static object queue;static bool registered;static readonly WindowProc callback=WndProc;
        IntPtr window,factory;Compositor compositor;DesktopWindowTarget target;SpriteVisual visual;
        CompositionBackdropBrush brush;CompositionPathGeometry geometry;CompositionGeometricClip clip;GeometrySource source;
        int left,top,width,height;bool shown;float alpha=-1;string shapeKey="";
        public IntPtr Handle{get{return window;}}
        public bool Shown{get{return shown;}}
        public NativeGlassBackdrop(){
            try{
                if(Windows.System.DispatcherQueue.GetForCurrentThread()==null){var options=new QueueOptions{Size=12,Thread=2,Apartment=2};Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options,out queue));}
                if(!registered){var wc=new WindowClass{Class="Iris.Material.Backdrop.v1",Instance=GetModuleHandleW(null),Procedure=callback};if(RegisterClassW(ref wc)==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());registered=true;}
                // This HWND is visual only; disabling it also excludes its transparent exterior from native hit testing.
                window=CreateWindowExW(0x082000a8,"Iris.Material.Backdrop.v1",L.T("Iris · Glasfläche"),unchecked((int)0x88000000),0,0,1,1,IntPtr.Zero,IntPtr.Zero,GetModuleHandleW(null),IntPtr.Zero);
                if(window==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                int enabled=1;Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(window,17,ref enabled,4));
                compositor=new Compositor();IntPtr raw;((DesktopInterop)(object)compositor).CreateDesktopWindowTarget(window,true,out raw);
                try{target=(DesktopWindowTarget)Marshal.GetObjectForIUnknown(raw);}finally{Marshal.Release(raw);}
                visual=compositor.CreateSpriteVisual();visual.BorderMode=CompositionBorderMode.Soft;brush=compositor.CreateHostBackdropBrush();visual.Brush=brush;
                geometry=compositor.CreatePathGeometry();clip=compositor.CreateGeometricClip(geometry);visual.Clip=clip;target.Root=visual;
                var iid=new Guid("06152247-6f50-465a-9245-118bfd3b6007");Marshal.ThrowExceptionForHR(D2D1CreateFactory(1,ref iid,IntPtr.Zero,out factory));
            }catch{Dispose();throw;}
        }
        public void Update(IntPtr above,int x,int y,int w,int h,Geometry outline,Matrix transform,string key,double opacity){
            if(window==IntPtr.Zero)return;
            if(key!=shapeKey){
                var path=outline.GetFlattenedPathGeometry(.035,ToleranceType.Absolute);var points=new List<PointF>();
                foreach(var figure in path.Figures){Add(points,transform.Transform(figure.StartPoint));foreach(var segment in figure.Segments){var lines=segment as PolyLineSegment;if(lines!=null)foreach(var point in lines.Points)Add(points,transform.Transform(point));else{var line=segment as LineSegment;if(line!=null)Add(points,transform.Transform(line.Point));}}}
                // Leave room for optical antialiasing inside the unchanged integer input region.
                float minX=float.MaxValue,maxX=float.MinValue,minY=float.MaxValue,maxY=float.MinValue;foreach(var p in points){minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minY=Math.Min(minY,p.Y);maxY=Math.Max(maxY,p.Y);}
                float span=maxX-minX,depth=maxY-minY,inset=Math.Min(1.25f,Math.Min(span*.02f,depth*.12f));var optical=new List<PointF>(points.Count);foreach(var p in points)optical.Add(new PointF{X=(minX+maxX)/2+(p.X-(minX+maxX)/2)*(span-2*inset)/span,Y=minY+(p.Y-minY)*(depth-inset)/depth});
                var next=new GeometrySource(factory,optical);try{geometry.Path=new CompositionPath(next);}catch{next.Dispose();throw;}source=next;
                var inputPoints=new PointI[points.Count];for(int i=0;i<points.Count;i++)inputPoints[i]=new PointI{X=(int)Math.Round(points[i].X),Y=(int)Math.Round(points[i].Y)};
                IntPtr region=CreatePolygonRgn(inputPoints,inputPoints.Length,2);if(region==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(SetWindowRgn(window,region,false)==0){DeleteObject(region);throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());}shapeKey=key;
            }
            bool resized=width!=w||height!=h;if(resized){width=w;height=h;visual.Size=new Vector2(w,h);}
            if(!shown||resized||left!=x||top!=y){left=x;top=y;SetWindowPos(window,above,x,y,w,h,0x0010|0x0040|0x0200);shown=true;}
            else if(width==w&&height==h)SetWindowPos(window,above,x,y,w,h,0x0010|0x0001|0x0002|0x0200);
            float nextAlpha=(float)Math.Max(0,Math.Min(1,opacity));if(Math.Abs(nextAlpha-alpha)>.0001){alpha=nextAlpha;visual.Opacity=alpha;}
        }
        static void Add(List<PointF> points,System.Windows.Point p){points.Add(new PointF{X=(float)p.X,Y=(float)p.Y});}
        public void Hide(){if(window!=IntPtr.Zero&&shown){ShowWindow(window,0);shown=false;}}
        public void Dispose(){Hide();if(target!=null){target.Root=null;target.Dispose();target=null;}if(visual!=null){visual.Dispose();visual=null;}if(clip!=null){clip.Dispose();clip=null;}if(geometry!=null){geometry.Dispose();geometry=null;}source=null;if(brush!=null){brush.Dispose();brush=null;}if(compositor!=null){compositor.Dispose();compositor=null;}if(factory!=IntPtr.Zero){Marshal.Release(factory);factory=IntPtr.Zero;}if(window!=IntPtr.Zero){DestroyWindow(window);window=IntPtr.Zero;}GC.KeepAlive(queue);}
        static IntPtr WndProc(IntPtr hwnd,uint message,IntPtr w,IntPtr l){if(message==0x14)return new IntPtr(1);if(message==0x84)return new IntPtr(-1);if(message==0x21)return new IntPtr(3);return DefWindowProcW(hwnd,message,w,l);}
        [StructLayout(LayoutKind.Sequential)]public struct PointF{public float X,Y;}
        [StructLayout(LayoutKind.Sequential)]struct PointI{public int X,Y;}
        [ComVisible(true),ClassInterface(ClassInterfaceType.None)]public sealed class GeometrySource : Windows.Graphics.IGeometrySource2D,GeometryInterop,IDisposable {
            IntPtr path;
            public GeometrySource(IntPtr factory,List<PointF> points){
                if(points.Count<3)throw new ArgumentException("Notch geometry is empty");
                Marshal.ThrowExceptionForHR(Method<CreateResource>(factory,10)(factory,out path));IntPtr sink=IntPtr.Zero,buffer=IntPtr.Zero;
                try{Marshal.ThrowExceptionForHR(Method<CreateResource>(path,17)(path,out sink));Method<BeginFigure>(sink,5)(sink,points[0],0);buffer=Marshal.AllocHGlobal((points.Count-1)*8);for(int i=1;i<points.Count;i++)Marshal.StructureToPtr(points[i],IntPtr.Add(buffer,(i-1)*8),false);Method<AddLines>(sink,6)(sink,buffer,(uint)(points.Count-1));Method<EndFigure>(sink,8)(sink,1);Marshal.ThrowExceptionForHR(Method<CloseSink>(sink,9)(sink));}
                catch{Dispose();throw;}finally{if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(sink!=IntPtr.Zero)Marshal.Release(sink);}
            }
            public int GetGeometry(out IntPtr result){result=path;if(result==IntPtr.Zero)return unchecked((int)0x80004005);Marshal.AddRef(result);return 0;}
            public int TryGetGeometryUsingFactory(IntPtr requested,out IntPtr result){result=IntPtr.Zero;return 0;}
            // CompositionPath owns the managed source through its CCW until the render thread releases it.
            ~GeometrySource(){Dispose();}
            public void Dispose(){if(path!=IntPtr.Zero){Marshal.Release(path);path=IntPtr.Zero;}}
        }
        static T Method<T>(IntPtr instance,int slot)where T:class{return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T)) as T;}
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateResource(IntPtr self,out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void BeginFigure(IntPtr self,PointF point,int begin);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void AddLines(IntPtr self,IntPtr points,uint count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void EndFigure(IntPtr self,int end);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CloseSink(IntPtr self);
        [ComImport,Guid("0657AF73-53FD-47CF-84FF-C8492D2A80A3"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]public interface GeometryInterop{[PreserveSig]int GetGeometry(out IntPtr value);[PreserveSig]int TryGetGeometryUsingFactory(IntPtr factory,out IntPtr value);}
        [ComImport,Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface DesktopInterop{void CreateDesktopWindowTarget(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)]bool topmost,out IntPtr target);}
        delegate IntPtr WindowProc(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass{public uint Style;public WindowProc Procedure;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;public string Menu,Class;}
        [StructLayout(LayoutKind.Sequential)]struct QueueOptions{public int Size,Thread,Apartment;}
        [DllImport("CoreMessaging.dll")]static extern int CreateDispatcherQueueController(QueueOptions options,[MarshalAs(UnmanagedType.IUnknown)]out object controller);
        [DllImport("d2d1.dll")]static extern int D2D1CreateFactory(int type,ref Guid iid,IntPtr options,out IntPtr factory);
        [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandleW(string name);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ushort RegisterClassW(ref WindowClass cls);
        [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowExW(int extended,string cls,string title,int style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
        [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("gdi32.dll",SetLastError=true)]static extern IntPtr CreatePolygonRgn([In]PointI[] points,int count,int fill);
        [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll",SetLastError=true)]static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]static extern IntPtr DefWindowProcW(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
    }
}
