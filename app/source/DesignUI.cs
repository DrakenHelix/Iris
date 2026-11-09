using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Iris {
    public sealed partial class NotchSurface {
        public double BlackMaterial=1,GlassMaterial;
        static readonly Brush upperGlassReflection=Reflection(new Point(.035,.12),.59,.82,60);
        static readonly Brush lowerGlassReflection=Reflection(new Point(.68,1.03),.82,.72,74);
        static readonly double[] edgeWidths={6.0,3.6,1.8,.8},edgeWeights={.055,.10,.18,.30};
        static Brush Reflection(Point origin,double radiusX,double radiusY,byte peak){
            var brush=new RadialGradientBrush{Center=origin,GradientOrigin=origin,RadiusX=radiusX,RadiusY=radiusY};
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(peak,242,247,255),0));brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak*.62),242,247,255),.34));brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak*.16),242,247,255),.72));brush.GradientStops.Add(new GradientStop(Colors.Transparent,1));brush.Freeze();return brush;
        }
        void PaintGlassEdge(DrawingContext dc,Geometry shape){
            // A shallow inner bevel with separate reflection lobes, rather than a bright one-pixel outline.
            var fade=new LinearGradientBrush(Colors.Transparent,Colors.White,new Point(0,1),new Point(0,1+Math.Min(6,Math.Max(1,Depth*.18)))){MappingMode=BrushMappingMode.Absolute};fade.Freeze();
            dc.PushClip(new RectangleGeometry(new Rect(0,1,RenderSize.Width,Math.Max(0,RenderSize.Height-1))));dc.PushOpacityMask(fade);
            for(int i=0;i<edgeWidths.Length;i++){dc.PushOpacity(edgeWeights[i]);dc.DrawGeometry(null,new Pen(upperGlassReflection,edgeWidths[i]),shape);dc.DrawGeometry(null,new Pen(lowerGlassReflection,edgeWidths[i]),shape);dc.Pop();}
            dc.Pop();dc.Pop();
        }
        void PaintSurface(DrawingContext dc){
            var shape=GetOutline();if(BlackMaterial>=.9995&&GlassMaterial<.0005){dc.DrawGeometry(Brushes.Black,null,shape);return;}
            // A visually negligible alpha retains Windows hover/click hit testing in transparent mode.
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(1,0,0,0)),null,shape);
            if(GlassMaterial>.0001){dc.PushOpacity(GlassMaterial);dc.PushClip(shape);dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(85,16,20,29)),null,shape);PaintGlassEdge(dc,shape);dc.Pop();dc.Pop();}
            if(BlackMaterial>.0001){dc.PushOpacity(BlackMaterial);dc.DrawGeometry(Brushes.Black,null,shape);dc.Pop();}
        }
    }
    public sealed class MaterialMotion : IUiMotion {
        readonly UiMotionClock clock;readonly Action<double,double> paint;double black=1,glass,vb,vg,tb=1,tg;
        public double Black{get{return black;}}public double Glass{get{return glass;}}public double GlassVelocity{get{return vg;}}
        public MaterialMotion(UiMotionClock clock,Action<double,double> paint){this.clock=clock;this.paint=paint;clock.Register(this);}
        public void Set(string style){double nextBlack=style=="black"?1:0,nextGlass=style=="glass"?1:0;if(tb==nextBlack&&tg==nextGlass)return;tb=nextBlack;tg=nextGlass;clock.Wake(this);}
        public bool Step(double dt,bool animate){if(animate){PlayerSpring.Step(ref black,ref vb,tb,dt,.28,1);PlayerSpring.Step(ref glass,ref vg,tg,dt,.28,1);}else{black=tb;glass=tg;vb=vg=0;}bool active=Math.Abs(black-tb)>.0005||Math.Abs(glass-tg)>.0005||Math.Abs(vb)>.005||Math.Abs(vg)>.005;if(!active){black=tb;glass=tg;vb=vg=0;}paint(black,glass);return active;}
        public void Snap(){Step(0,false);}
    }
    public sealed class MaterialSwatch : FrameworkElement {
        readonly string style;public MaterialSwatch(string style){this.style=style;Width=104;Height=29;IsHitTestVisible=false;}
        protected override void OnRender(DrawingContext dc){var bounds=new Rect(0,0,104,29);dc.PushClip(new RectangleGeometry(bounds,6,6));for(int i=0;i<8;i++)dc.DrawRectangle(new SolidColorBrush(i%2==0?Color.FromRgb(62,112,147):Color.FromRgb(115,87,137)),null,new Rect(i*14,0,14,29));var pill=new Rect(12,2,80,24);if(style=="black")dc.DrawRoundedRectangle(Brushes.Black,null,pill,8,8);else if(style=="glass"){dc.DrawRoundedRectangle(new LinearGradientBrush(Color.FromArgb(210,117,133,157),Color.FromArgb(210,76,91,119),90),new Pen(new SolidColorBrush(Color.FromArgb(110,255,255,255)),.7),pill,8,8);}dc.DrawRoundedRectangle(Brushes.White,null,new Rect(44,12,16,3),1.5,1.5);dc.Pop();}
    }
    public sealed partial class NotchWindow {
        MaterialMotion materialMotion;NativeGlassBackdrop glassBackdrop;bool glassFailed,firstLaunchDesign,designFullscreen,materialEffectsAllowed=true;bool? qaDesignFullscreen;
        readonly SolidColorBrush materialContentMask=new SolidColorBrush(Colors.Black);
        string effectiveDesign="black",materialStatus="";TextBlock designStatus;readonly Dictionary<string,UiSelectionMotion> designSelections=new Dictionary<string,UiSelectionMotion>();
        static string ValidDesign(string value,bool full){return value=="black"||value=="glass"||value=="transparent"||full&&(value=="inherit"||value=="hide")?value:full?"inherit":"black";}
        static string ResolveDesign(string normal,string full,bool fullscreen){return fullscreen&&full!="inherit"?full:normal;}
        static void MigrateDesign(Preferences value){value.NotchDesign=ValidDesign(value.NotchDesign,false);if(value.DesignSchema==0){value.FullscreenDesign=value.HideFullscreen?"hide":"inherit";value.DesignSchema=1;}value.FullscreenDesign=ValidDesign(value.FullscreenDesign,true);value.HideFullscreen=value.FullscreenDesign=="hide";}
        void SetupDesign(){
            materialEffectsAllowed=WindowsGlassAllowed();materialMotion=new MaterialMotion(uiMotion,PaintMaterial);LocationChanged+=delegate{SyncGlassBackdrop();};SizeChanged+=delegate{SyncGlassBackdrop();};IsVisibleChanged+=delegate{SyncGlassBackdrop();};
            Microsoft.Win32.SystemEvents.UserPreferenceChanged+=OnMaterialPreferenceChanged;ApplyDesignContext(false);
        }
        void MaintainNotchWindowOrder(){
            if(shuttingDown||!IsLoaded||!IsVisible)return;
            IntPtr foreground=GetForegroundWindow();
            uint pid;
            GetWindowThreadProcessId(foreground,out pid);
            // Iris settings, editors and menus keep their existing order and focus.
            if(pid==System.Diagnostics.Process.GetCurrentProcess().Id)return;
            RestoreNotchWindowOrder(foreground);
        }
        bool RestoreNotchWindowOrder(IntPtr foreground){
            if(!IsLoaded||!IsVisible)return false;
            IntPtr hwnd=new WindowInteropHelper(this).Handle;
            if(hwnd==IntPtr.Zero)return false;
            bool missingTopmost=(GetWindowLong(hwnd,-20)&0x8)==0;
            if(!missingTopmost){
                RECT other;
                if(foreground==IntPtr.Zero||!OrderIsWindowVisible(foreground)||!GetWindowRect(foreground,out other))return false;
                Rect bounds=surface.GetOutline().Bounds;
                Point first=canvas.PointToScreen(bounds.TopLeft),last=canvas.PointToScreen(bounds.BottomRight);
                if(other.Right<=first.X||other.Bottom<=first.Y||other.Left>=last.X||other.Top>=last.Y)return false;
                if(!WindowIsAbove(foreground,hwnd))return false;
            }
            // Reassert native topmost without moving, resizing or activating Iris.
            if(!OrderSetWindowPos(hwnd,new IntPtr(-1),0,0,0,0,0x0010|0x0001|0x0002|0x0200))return false;
            SyncGlassBackdrop();
            return true;
        }
        static bool WindowIsAbove(IntPtr candidate,IntPtr window){
            IntPtr current=OrderGetWindow(window,3); // GW_HWNDPREV
            for(int count=0;current!=IntPtr.Zero&&count<128;count++){
                if(current==candidate)return true;
                current=OrderGetWindow(current,3);
            }
            return false;
        }
        [DllImport("user32.dll",EntryPoint="GetWindow")]static extern IntPtr OrderGetWindow(IntPtr window,uint command);
        [DllImport("user32.dll",EntryPoint="IsWindowVisible")]static extern bool OrderIsWindowVisible(IntPtr window);
        [DllImport("user32.dll",EntryPoint="SetWindowPos")]static extern bool OrderSetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        void OnMaterialPreferenceChanged(object sender,Microsoft.Win32.UserPreferenceChangedEventArgs args){Dispatcher.BeginInvoke(new Action(delegate{if(shuttingDown)return;materialEffectsAllowed=WindowsGlassAllowed();glassFailed=false;if(glassBackdrop!=null){glassBackdrop.Dispose();glassBackdrop=null;}ApplyDesignContext(designFullscreen);SyncGlassBackdrop();}));}
        static bool WindowsGlassAllowed(){try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))return !SystemParameters.HighContrast&&(key==null||Convert.ToInt32(key.GetValue("EnableTransparency",1))!=0);}catch{return !SystemParameters.HighContrast;}}
        void SetNotchDesign(string value){prefs.NotchDesign=ValidDesign(value,false);ApplyDesignContext(designFullscreen);UpdateDesignSelections();Save();}
        void SetFullscreenDesign(string value){prefs.FullscreenDesign=ValidDesign(value,true);prefs.HideFullscreen=prefs.FullscreenDesign=="hide";ApplyDesignContext(designFullscreen);UpdateDesignSelections();Save();}
        void ApplyDesignContext(bool fullscreen){
            designFullscreen=fullscreen;string chosen=ResolveDesign(prefs.NotchDesign,prefs.FullscreenDesign,fullscreen);bool hidden=chosen=="hide";
            string next=hidden?prefs.NotchDesign:chosen;if(next=="glass"&&!materialEffectsAllowed){next="black";materialStatus=L.T("Windows reduziert Transparenz · Schwarz als Ersatz");}else materialStatus="";
            effectiveDesign=next;if(materialMotion!=null)materialMotion.Set(next);Visibility=hidden?Visibility.Hidden:Visibility.Visible;SyncGlassBackdrop();RefreshDesignStatus();
        }
        void PaintMaterial(double black,double glass){surface.BlackMaterial=Math.Max(0,Math.Min(1,black));surface.GlassMaterial=Math.Max(0,Math.Min(1,glass));materialContentMask.Color=Color.FromArgb((byte)Math.Round(surface.BlackMaterial*255),0,0,0);surface.InvalidateVisual();SyncGlassBackdrop();}
        void SyncGlassBackdrop(){
            if(materialMotion==null||surface==null||!IsLoaded||!IsVisible||cursorAvoiding||!materialEffectsAllowed||materialMotion.Glass<.0005){if(glassBackdrop!=null)glassBackdrop.Hide();return;}
            if(glassFailed)return;
            try{
                if(glassBackdrop==null)glassBackdrop=new NativeGlassBackdrop();var hwnd=new WindowInteropHelper(this).Handle;RECT rect;if(!GetWindowRect(hwnd,out rect))return;
                Point origin=canvas.PointToScreen(new Point()),unitX=canvas.PointToScreen(new Point(1,0)),unitY=canvas.PointToScreen(new Point(0,1));var matrix=new Matrix(unitX.X-origin.X,unitX.Y-origin.Y,unitY.X-origin.X,unitY.Y-origin.Y,origin.X-rect.Left,origin.Y-rect.Top);
                string key=surface.Span.ToString("R",CultureInfo.InvariantCulture)+":"+surface.Depth.ToString("R",CultureInfo.InvariantCulture)+":"+matrix.ToString(CultureInfo.InvariantCulture);
                glassBackdrop.Update(hwnd,rect.Left,rect.Top,Math.Max(1,rect.Right-rect.Left),Math.Max(1,rect.Bottom-rect.Top),surface.GetOutline(),matrix,key,materialMotion.Glass);materialStatus=L.T("Echter Windows-Hintergrundblur aktiv");
            }catch{glassFailed=true;if(glassBackdrop!=null){glassBackdrop.Dispose();glassBackdrop=null;}materialStatus=L.T("Glas-Blur hier nicht verfügbar · transparente Tönung als Ersatz");}
            RefreshDesignStatus();
        }
        void ShowDesignSettings(){ShowSettings();settingsSection="Design";RenderSettingsSection();}
        void CompleteDesignSetup(){firstLaunchDesign=false;Save();if(settings!=null)settings.Close();}
        void RefreshDesignStatus(){if(designStatus!=null)designStatus.Text=(designFullscreen?L.T("Aktuell: Vollbild · "):L.T("Aktuell: Normalbetrieb · "))+(effectiveDesign=="glass"?materialStatus.Length>0?materialStatus:"Liquid Glass":effectiveDesign=="transparent"?L.T("Transparente Fläche · Bedienelemente bleiben sichtbar"):materialStatus.StartsWith(L.T("Windows reduziert"),StringComparison.Ordinal)?materialStatus:L.T("Schwarz"))+(Visibility==Visibility.Hidden?" · ausgeblendet":"");}
        void UpdateDesignSelections(){foreach(var pair in designSelections){bool full=pair.Key.StartsWith("full:",StringComparison.Ordinal);pair.Value.Select(pair.Key.Substring(full?5:7)==(full?prefs.FullscreenDesign:prefs.NotchDesign));uiMotion.Wake(pair.Value);}RefreshDesignStatus();}
        void AddDesignChoice(Panel parent,string key,string label,bool full,int column){
            Button button;if(full){button=Btn(label,delegate{SetFullscreenDesign(key);});button.Height=30;button.FontSize=11;button.Padding=new Thickness(5,0,5,0);}
            else{var content=new StackPanel();content.Children.Add(new MaterialSwatch(key));var title=Text(label,12,ink);title.TextAlignment=TextAlignment.Center;title.Margin=new Thickness(0,6,0,0);content.Children.Add(title);button=Btn(content,delegate{SetNotchDesign(key);});button.Height=69;button.Padding=new Thickness(5);}
            button.Margin=new Thickness(3);Grid.SetColumn(button,column);Grid.SetRow(button,full?column/3:0);Grid.SetColumn(button,full?column%3:column);var selected=new UiSelectionMotion(button,key==(full?prefs.FullscreenDesign:prefs.NotchDesign));uiMotion.Register(selected);designSelections[(full?"full:":"normal:")+key]=selected;System.Windows.Automation.AutomationProperties.SetName(button,(full?L.T("Bei Vollbild: "):L.T("Standarddesign: "))+label);parent.Children.Add(button);
        }
        void BuildDesignSettings(StackPanel panel){
            designSelections.Clear();if(firstLaunchDesign){var welcome=Text(L.T("Willkommen bei Iris. Wähle dein Design und das Verhalten bei Vollbild. Die Notch bleibt zunächst sichtbar."),12,muted);welcome.TextWrapping=TextWrapping.Wrap;welcome.Margin=new Thickness(0,-9,0,12);panel.Children.Add(welcome);}
            SettingsLabel(panel,L.T("Standarddesign"));var choices=new Grid{Margin=new Thickness(0,0,0,12)};for(int i=0;i<3;i++)choices.ColumnDefinitions.Add(new ColumnDefinition());AddDesignChoice(choices,"black",L.T("Schwarz"),false,0);AddDesignChoice(choices,"glass","Liquid Glass",false,1);AddDesignChoice(choices,"transparent","Transparent",false,2);panel.Children.Add(choices);
            SettingsLabel(panel,L.T("Bei Vollbild · Spiele, Videos und Apps"));var full=new Grid{Margin=new Thickness(0,0,0,9)};for(int i=0;i<3;i++)full.ColumnDefinitions.Add(new ColumnDefinition());full.RowDefinitions.Add(new RowDefinition());full.RowDefinitions.Add(new RowDefinition());AddDesignChoice(full,"inherit",L.T("Beibehalten"),true,0);AddDesignChoice(full,"black",L.T("Schwarz"),true,1);AddDesignChoice(full,"glass","Liquid Glass",true,2);AddDesignChoice(full,"transparent","Transparent",true,3);AddDesignChoice(full,"hide",L.T("Ausblenden"),true,4);panel.Children.Add(full);
            var note=Text(L.T("Wechselt automatisch zurück, sobald Vollbild endet. Maximierte Fenster zählen nicht als Vollbild. Transparent entfernt die Fläche; Texte und Buttons bleiben sichtbar."),11,muted);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,0,0,12);panel.Children.Add(note);
            designStatus=Text("",11,muted);designStatus.TextWrapping=TextWrapping.Wrap;designStatus.Margin=new Thickness(12,9,12,9);SettingsGroup(panel,designStatus);RefreshDesignStatus();
            var detail=Text(L.T("Liquid Glass nutzt Windows-Hintergrundblur mit Milchglas-Tönung und Lichtkante. Windows-Transparenzeffekte und hoher Kontrast werden respektiert."),11,muted);detail.TextWrapping=TextWrapping.Wrap;detail.Margin=new Thickness(0,10,0,0);panel.Children.Add(detail);
            if(firstLaunchDesign){var done=Btn(L.T("Auswahl übernehmen"),CompleteDesignSetup);done.Height=31;done.Margin=new Thickness(0,12,0,0);panel.Children.Add(done);}
        }
    }
}
