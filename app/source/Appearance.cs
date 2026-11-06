using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public static class InputIdle {
        public static double Seconds(){var info=new LASTINPUT{Size=(uint)Marshal.SizeOf(typeof(LASTINPUT))};if(!GetLastInputInfo(ref info))return 0;uint elapsed=unchecked((uint)Environment.TickCount-info.Time);return elapsed>int.MaxValue?0:elapsed/1000.0;}
        [StructLayout(LayoutKind.Sequential)]struct LASTINPUT{public uint Size,Time;}
        [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LASTINPUT info);
    }
    public sealed class BatteryState {
        public bool Present,Charging,Plugged;public int Percent;
        public static BatteryState FromNative(bool success,byte flag,byte percent,byte ac){return new BatteryState{Present=success&&flag!=255&&(flag&128)==0&&percent<=100,Percent=percent<=100?percent:0,Charging=(flag&8)!=0,Plugged=ac==1};}
        public static BatteryState Read(){POWER status;bool ok=GetSystemPowerStatus(out status);return FromNative(ok,status.Flags,status.Percent,status.Ac);}
        [StructLayout(LayoutKind.Sequential)]struct POWER{public byte Ac,Flags,Percent,Saver;public uint Remaining,Full;}
        [DllImport("kernel32.dll")]static extern bool GetSystemPowerStatus(out POWER status);
    }
    public sealed class BatteryGlyph : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;double value,velocity,charge,chargeVelocity;BatteryState state=new BatteryState();
        public BatteryGlyph(UiMotionClock clock){this.clock=clock;clock.Register(this);Width=24;Height=18;IsHitTestVisible=false;}
        public void Set(BatteryState next){state=next;clock.Wake(this);}
        public bool Step(double dt,bool animate){double target=state.Percent/100.0,c=state.Charging?1:0;if(animate){PlayerSpring.Step(ref value,ref velocity,target,dt,.28,1);PlayerSpring.Step(ref charge,ref chargeVelocity,c,dt,.23,1);}else{value=target;charge=c;velocity=chargeVelocity=0;}InvalidateVisual();return Math.Abs(value-target)>.001||Math.Abs(velocity)>.01||Math.Abs(charge-c)>.001||Math.Abs(chargeVelocity)>.01;}
        public void Snap(){Step(0,false);}
        protected override void OnRender(DrawingContext dc){var brush=state.Percent<=20&&!state.Plugged?(Brush)new SolidColorBrush(Color.FromRgb(255,91,109)):Brushes.White;dc.DrawRoundedRectangle(null,new Pen(brush,1.2),new Rect(1,3.5,19,11),2.2,2.2);dc.DrawRoundedRectangle(brush,null,new Rect(21,7,1.5,4),.6,.6);dc.DrawRoundedRectangle(brush,null,new Rect(3,5.5,15*Math.Max(0,Math.Min(1,value)),7),.8,.8);if(charge>.001){dc.PushOpacity(Math.Max(0,Math.Min(1,charge)));dc.DrawGeometry(Brushes.Black,new Pen(Brushes.White,.5),Geometry.Parse("M 11.5,4 L 7.5,9.6 L 10.3,9.6 L 9,14 L 14.3,7.8 L 11.5,7.8 Z"));dc.Pop();}}
    }
    public sealed class NotchScaleMotion : IUiMotion {
        readonly UiMotionClock clock;readonly Action<double,bool> paint;double value=1,velocity,target=1;
        public double Value{get{return value;}}public double Velocity{get{return velocity;}}
        public NotchScaleMotion(UiMotionClock clock,Action<double,bool> paint){this.clock=clock;this.paint=paint;clock.Register(this);}
        public void Set(double next){target=next;clock.Wake(this);}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref value,ref velocity,target,dt,.32,1);else{value=target;velocity=0;}bool moving=Math.Abs(value-target)>.0001||Math.Abs(velocity)>.001;if(!moving){value=target;velocity=0;}paint(value,!moving);return moving;}
        public void Snap(){Step(0,false);}
    }
    public sealed class NotchSizePicker : Grid,IUiMotion {
        readonly UiMotionClock clock;readonly TranslateTransform move=new TranslateTransform();readonly Border highlight;readonly List<Button> buttons=new List<Button>();readonly Action<int> changed;readonly int[] values={0,100,115,130,150};double position,velocity;int selected;
        public double Position{get{return position;}}public double Velocity{get{return velocity;}}
        public NotchSizePicker(UiMotionClock clock,int current,Action<int> changed,Func<object,Action,Button> make){this.clock=clock;this.changed=changed;clock.Register(this);Height=27;Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(54,54,57)),CornerRadius=new CornerRadius(6),IsHitTestVisible=false});var backdrop=new Canvas{IsHitTestVisible=false};Children.Add(backdrop);highlight=new Border{Height=23,CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromRgb(98,98,102)),RenderTransform=move};Canvas.SetTop(highlight,2);backdrop.Children.Add(highlight);var row=new Grid();Children.Add(row);selected=Math.Max(0,Array.IndexOf(values,current));for(int i=0;i<values.Length;i++){int index=i;row.ColumnDefinitions.Add(new ColumnDefinition());var button=make(i==0?"Auto":values[i]+" %",delegate{Select(index);this.changed(values[index]);});button.FontSize=11;button.Padding=new Thickness(0);button.Background=Brushes.Transparent;button.ToolTip=i==0?L.T("Windows-Skalierung übernehmen"):values[i]+L.T(" % relativ zur Windows-Skalierung");Grid.SetColumn(button,i);row.Children.Add(button);buttons.Add(button);System.Windows.Automation.AutomationProperties.SetName(button,i==0?L.T("Notch-Größe Auto"):L.T("Notch-Größe ")+values[i]+L.T(" Prozent"));}SizeChanged+=delegate{Snap();};Loaded+=delegate{Snap();};Unloaded+=delegate{clock.Cancel(this);};}
        public void Select(int index){selected=index;foreach(var button in buttons)button.FontWeight=buttons.IndexOf(button)==index?FontWeights.SemiBold:FontWeights.Normal;clock.Wake(this);}
        public bool Step(double dt,bool animate){double width=ActualWidth/5,target=selected*width;if(animate)PlayerSpring.Step(ref position,ref velocity,target,dt,.28,1);else{position=target;velocity=0;}highlight.Width=Math.Max(0,width-4);move.X=position+2;return Math.Abs(position-target)>.02||Math.Abs(velocity)>.05;}
        public void Snap(){Step(0,false);}
    }
    public sealed partial class NotchWindow {
        const double SceneWidth=1040,SceneHeight=320,MinimalSpan=67.5,MinimalDepth=3.5;
        Grid sceneHost;ScaleTransform sceneScale=new ScaleTransform(1,1);NotchScaleMotion sizeMotion;double requestedScale=1,layoutScale=1;
        NotchSizePicker sizePicker;TextBlock sizeNote;BatteryState battery=new BatteryState();BatteryGlyph compactBattery,headerBattery;Border compactBatteryHost,headerBatteryHost;DateTime lastBatteryPoll;
        DispatcherTimer idleEyesWatch;double qaIdleSeconds=30;
        double IdleSeconds(){return uiTesting?qaIdleSeconds:InputIdle.Seconds();}
        bool MascotEligible(){return prefs.Mascot&&!HasDownloads()&&!gamingVisible&&!isOpen&&!countdown.Running&&!timerFinished&&!MinimalIdle()&&!(mediaAvailable&&isPlaying);}
        void RefreshIdleEyes(){if(!MascotEligible()||IdleSeconds()<7){if(eyes.Visibility==Visibility.Visible)ShowEyes(false);}else if(eyes.Visibility!=Visibility.Visible)ShowEyes(true);}
        void SetupAppearance(){
            sizeMotion=new NotchScaleMotion(uiMotion,PaintNotchScale);canvas.RenderTransform=sceneScale;canvas.RenderTransformOrigin=new Point(.5,0);
            compactBattery=new BatteryGlyph(uiMotion);compactBatteryHost=new Border{Child=compactBattery,Width=26,Height=28,Visibility=Visibility.Collapsed,Background=Brushes.Transparent};Grid.SetColumn(compactBatteryHost,0);compact.Children.Add(compactBatteryHost);
            headerBattery=new BatteryGlyph(uiMotion);headerBatteryHost=new Border{Child=headerBattery,Width=26,Height=26,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0),Visibility=Visibility.Collapsed,Background=Brushes.Transparent};headerChrome.Children.Add(headerBatteryHost);
            Microsoft.Win32.SystemEvents.PowerModeChanged+=OnPowerChanged;if(!uiTesting)RefreshBattery();UpdateCompactLyrics();
            idleEyesWatch=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};idleEyesWatch.Tick+=delegate{RefreshIdleEyes();if(eyes.Visibility!=Visibility.Visible)idleEyesWatch.Stop();};
            DpiChanged+=delegate{Place();};
        }
        static int ValidNotchSize(int value){return new[]{0,100,115,130,150}.Contains(value)?value:0;}
        static double FitNotchScale(int percent,double availableWidth){double wanted=percent==0?1:percent/100.0;return Math.Max(.5,Math.Min(wanted,Math.Max(.5,(availableWidth-24)/(OpenSpan+344))));}
        void SetNotchSize(int value){prefs.NotchSize=ValidNotchSize(value);Save();Place();}
        void SetNotchScale(double scale){if(sizeMotion==null)return;requestedScale=scale;layoutScale=Math.Max(sizeMotion.Value,scale);ResizeNotchHost();sizeMotion.Set(scale);if(sizeNote!=null)sizeNote.Text=SizeDescription();}
        string SizeDescription(){return Math.Abs(requestedScale-(prefs.NotchSize==0?1:prefs.NotchSize/100.0))>.01?L.T("Für diesen Bildschirm angepasst: ")+Math.Round(requestedScale*100)+" %":prefs.NotchSize==0?L.T("Folgt der Windows-Skalierung"):L.T("Sofort wirksam · relativ zur Windows-Skalierung");}
        void ResizeNotchHost(){base.Width=SceneWidth*layoutScale;base.Height=SceneHeight*layoutScale;}
        void PaintNotchScale(double value,bool settled){sceneScale.ScaleX=sceneScale.ScaleY=value;if(settled&&Math.Abs(layoutScale-requestedScale)>.0001){layoutScale=requestedScale;ResizeNotchHost();PositionNotch();}SyncGlassBackdrop();}
        void SetMinimalNotch(bool value){prefs.MinimalNotch=value;ShowEyes(false);UpdateCompactLyrics();Save();}
        bool BatteryVisible(){return prefs.BatteryEnabled&&battery.Present;}
        bool MinimalIdle(){return cursorAvoiding||prefs.MinimalNotch&&!HasDownloads()&&!countdown.Running&&!timerFinished&&!gamingVisible&&!(mediaAvailable&&isPlaying)&&!VolumeHudVisible()&&!HasCompactLyrics()&&!BatteryVisible();}
        double RestingDepth(){return MinimalIdle()?MinimalDepth:ClosedDepth;}
        void SetBatteryEnabled(bool value){prefs.BatteryEnabled=value;RefreshBattery();Save();}
        void RefreshBattery(){lastBatteryPoll=DateTime.UtcNow;ApplyBattery(prefs.BatteryEnabled?(uiTesting?battery:BatteryState.Read()):new BatteryState());}
        void ApplyBattery(BatteryState state){battery=state;if(compactBatteryHost==null)return;bool show=BatteryVisible();compactBatteryHost.Visibility=headerBatteryHost.Visibility=show?Visibility.Visible:Visibility.Collapsed;if(show){compactBattery.Set(state);headerBattery.Set(state);string text=L.T("Akku: ")+state.Percent+" %"+(state.Charging?L.T(" · lädt"):state.Plugged?L.T(" · am Netz"):"");compactBatteryHost.ToolTip=headerBatteryHost.ToolTip=text;System.Windows.Automation.AutomationProperties.SetName(compactBatteryHost,text);System.Windows.Automation.AutomationProperties.SetName(headerBatteryHost,text);}RefreshGamingBattery();UpdateCompactLyrics();}
        void OnPowerChanged(object sender,Microsoft.Win32.PowerModeChangedEventArgs e){Dispatcher.BeginInvoke(new Action(delegate{if(!shuttingDown&&prefs.BatteryEnabled)RefreshBattery();}));}
        UIElement CompactAppearanceToggle(string title,string detail,bool value,Action<bool> change){var row=(Grid)SettingRow(title,detail,value,change);row.MinHeight=36;row.Margin=new Thickness(13,3,13,3);return row;}
        void BuildNotchAppearance(StackPanel panel){
            SettingsLabel(panel,"Notch");var size=new StackPanel{Margin=new Thickness(13,8,13,9)};size.Children.Add(Text(L.T("Größe"),13,ink));sizeNote=Text(SizeDescription(),11,muted);sizeNote.Margin=new Thickness(0,2,0,7);size.Children.Add(sizeNote);sizePicker=new NotchSizePicker(uiMotion,prefs.NotchSize,SetNotchSize,Btn);size.Children.Add(sizePicker);
            SettingsGroup(panel,size,CompactAppearanceToggle(L.T("Minimale Notch"),L.T("Kleine Leiste, wenn keine Anzeige aktiv ist"),prefs.MinimalNotch,SetMinimalNotch),CompactAppearanceToggle(L.T("Akkuanzeige"),L.T("Nur bei vorhandenem Akku · standardmäßig aus"),prefs.BatteryEnabled,SetBatteryEnabled),CompactAppearanceToggle(L.T("Maskottchen"),L.T("Nach 7 s ohne Eingabe · Bewegung zeigt die Uhr"),prefs.Mascot,delegate(bool v){prefs.Mascot=v;if(!v)ShowEyes(false);else if(!isOpen&&!MinimalIdle())mascotTimer.Start();}));
        }
        void BuildGeneralBehavior(StackPanel panel){
            SettingsLabel(panel,L.T("Start & Bedienung"));
            SettingsGroup(panel,StartupSettingRow(),CompactAppearanceToggle(L.T("Animationen reduzieren"),L.T("Folgt zusätzlich den Windows-Einstellungen"),prefs.ReduceMotion,SetReducedMotion));
            ((Border)panel.Children[panel.Children.Count-1]).Margin=new Thickness(0);
        }
    }
}
