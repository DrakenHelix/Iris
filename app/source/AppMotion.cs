using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Iris {
    public interface IUiMotion {bool Step(double dt,bool animate);void Snap();}
    public sealed class UiMotionClock {
        readonly Func<bool> enabled;
        readonly HashSet<IUiMotion> active=new HashSet<IUiMotion>();
        readonly List<WeakReference> known=new List<WeakReference>();
        readonly Stopwatch watch=new Stopwatch();
        public bool Automatic=true;
        bool subscribed;
        public int ActiveCount {get{return active.Count;}}
        public bool Enabled {get{return enabled();}}
        public void PauseAutomatic(){Automatic=false;if(subscribed){CompositionTarget.Rendering-=Frame;subscribed=false;watch.Stop();}}
        public UiMotionClock(Func<bool> enabled){this.enabled=enabled;}
        public void Register(IUiMotion state){known.Add(new WeakReference(state));if(known.Count>128)known.RemoveAll(r=>!r.IsAlive);}
        public void Wake(IUiMotion state){if(!enabled()){state.Snap();active.Remove(state);StopIfIdle();return;}active.Add(state);if(Automatic&&!subscribed){watch.Restart();CompositionTarget.Rendering+=Frame;subscribed=true;}}
        public void Cancel(IUiMotion state){active.Remove(state);StopIfIdle();}
        void Frame(object sender,EventArgs e){double dt=Math.Min(.06,Math.Max(.001,watch.Elapsed.TotalSeconds));watch.Restart();Advance(dt);}
        public void Advance(double dt){foreach(var state in active.ToArray())if(!state.Step(dt,enabled()))active.Remove(state);StopIfIdle();}
        void StopIfIdle(){if(active.Count==0&&subscribed){CompositionTarget.Rendering-=Frame;subscribed=false;watch.Stop();}}
        public void SnapAll(){foreach(var reference in known.ToArray()){var state=reference.Target as IUiMotion;if(state!=null)state.Snap();}active.Clear();StopIfIdle();}
        public void Dispose(){active.Clear();known.Clear();StopIfIdle();}
    }
    public sealed class UiButtonMotion : IUiMotion {
        readonly WeakReference owner;
        readonly UiMotionClock clock;
        Border root;
        ScaleTransform scale;
        bool attached=true,hover,held,pressed;
        double age,value=1,velocity,alpha=1,alphaVelocity;
        public double Scale {get{return value;}}
        public UiButtonMotion(Button button,UiMotionClock clock){
            owner=new WeakReference(button);this.clock=clock;clock.Register(this);
            button.Loaded+=delegate{if(attached){EnsureRoot();Snap();}};button.Unloaded+=delegate{clock.Cancel(this);root=null;scale=null;};
            button.MouseEnter+=delegate{if(attached){hover=true;if(held){pressed=true;age=0;}clock.Wake(this);}};button.MouseLeave+=delegate{if(attached){hover=false;if(held)pressed=false;clock.Wake(this);}};
            button.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(delegate{if(attached){held=true;pressed=true;age=0;EnsureRoot();if(root!=null)root.Opacity=.78;clock.Wake(this);}}),true);
            button.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(delegate{if(attached){held=false;clock.Wake(this);}}),true);
            button.LostMouseCapture+=delegate{if(attached){held=false;clock.Wake(this);}};
            button.Click+=delegate{if(attached){if(!pressed){pressed=true;age=0;}held=false;clock.Wake(this);}};
            button.IsEnabledChanged+=delegate{if(attached)clock.Wake(this);};
            button.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(attached&&!e.IsRepeat&&(e.Key==Key.Space||e.Key==Key.Return)){held=pressed=true;age=0;clock.Wake(this);}};
            button.PreviewKeyUp+=delegate(object sender,KeyEventArgs e){if(attached&&(e.Key==Key.Space||e.Key==Key.Return)){held=false;clock.Wake(this);}};
            button.LostKeyboardFocus+=delegate{if(attached){held=false;clock.Wake(this);}};
        }
        bool EnsureRoot(){var button=owner.Target as Button;if(!attached||button==null)return false;if(root==null){button.ApplyTemplate();root=button.Template.FindName("b",button) as Border;if(root!=null){scale=root.RenderTransform as ScaleTransform;if(scale==null){scale=new ScaleTransform(1,1);root.RenderTransform=scale;}root.RenderTransformOrigin=new Point(.5,.5);}}return root!=null;}
        public void Detach(){attached=false;clock.Cancel(this);if(root!=null){scale.ScaleX=scale.ScaleY=1;root.ClearValue(UIElement.OpacityProperty);}root=null;scale=null;}
        public bool Step(double dt,bool animate){
            var button=owner.Target as Button;if(button==null||!button.IsLoaded||!EnsureRoot())return false;age+=dt;if(!held&&age>=.065)pressed=false;
            double width=button.ActualWidth,rest=width<40?1.065:width>120?1.008:1.025,down=width<40?.91:width>120?.985:.955;
            double target=!button.IsEnabled?1:pressed?down:hover?rest:1,opacity=!button.IsEnabled?.35:pressed?.78:hover?.96:1;
            if(animate){PlayerSpring.Step(ref value,ref velocity,target,dt,pressed?.11:.25,pressed?1:.82);PlayerSpring.Step(ref alpha,ref alphaVelocity,opacity,dt,.16,1);}else{value=1;velocity=0;alpha=opacity;alphaVelocity=0;}
            scale.ScaleX=scale.ScaleY=value;root.Opacity=alpha;
            return (!held&&pressed)||Math.Abs(value-target)>.0005||Math.Abs(velocity)>.005||Math.Abs(alpha-opacity)>.001||Math.Abs(alphaVelocity)>.01;
        }
        public void Snap(){if(!held){pressed=false;age=.1;}value=1;velocity=alphaVelocity=0;var button=owner.Target as Button;alpha=button!=null&&!button.IsEnabled?.35:pressed?.78:1;if(EnsureRoot()){scale.ScaleX=scale.ScaleY=1;root.Opacity=alpha;}}
    }
    public sealed class FluidToggle : ToggleButton,IUiMotion {
        readonly UiMotionClock clock;
        double phase,velocity,pressure,pressureVelocity;
        bool held;
        public double Phase {get{return phase;}}
        public FluidToggle(UiMotionClock clock){
            this.clock=clock;clock.Register(this);Width=38;Height=22;Cursor=Cursors.Hand;FocusVisualStyle=null;
            var area=new FrameworkElementFactory(typeof(Border));area.SetValue(Border.BackgroundProperty,Brushes.Transparent);Template=new ControlTemplate(typeof(ToggleButton)){VisualTree=area};
            Loaded+=delegate{Snap();};Unloaded+=delegate{clock.Cancel(this);};
            PreviewMouseLeftButtonDown+=delegate{held=true;clock.Wake(this);};PreviewMouseLeftButtonUp+=delegate{held=false;clock.Wake(this);};LostMouseCapture+=delegate{held=false;clock.Wake(this);};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Space||e.Key==Key.Return){held=true;clock.Wake(this);}};PreviewKeyUp+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Space||e.Key==Key.Return){held=false;clock.Wake(this);}};
        }
        protected override void OnChecked(RoutedEventArgs e){base.OnChecked(e);Changed();}
        protected override void OnUnchecked(RoutedEventArgs e){base.OnUnchecked(e);Changed();}
        void Changed(){if(clock==null)return;if(!IsLoaded)Snap();else clock.Wake(this);}
        public bool Step(double dt,bool animate){double target=IsChecked==true?1:0;if(animate){PlayerSpring.Step(ref phase,ref velocity,target,dt,.24,1);PlayerSpring.Step(ref pressure,ref pressureVelocity,held?1:0,dt,.14,1);}else{phase=target;velocity=0;pressure=held?1:0;pressureVelocity=0;}InvalidateVisual();return Math.Abs(phase-target)>.0005||Math.Abs(velocity)>.005||Math.Abs(pressure-(held?1:0))>.001||Math.Abs(pressureVelocity)>.01;}
        public void Snap(){phase=IsChecked==true?1:0;velocity=0;pressure=pressureVelocity=0;InvalidateVisual();}
        protected override void OnRender(DrawingContext dc){
            double p=Math.Max(0,Math.Min(1,phase));var color=Color.FromRgb((byte)(75+(50-75)*p),(byte)(75+(212-75)*p),(byte)(79+(91-79)*p));
            dc.PushOpacity(IsEnabled?1:.35);dc.DrawRoundedRectangle(new SolidColorBrush(color),null,new Rect(0,0,38,22),11,11);
            double width=18+1.5*pressure;dc.DrawRoundedRectangle(Brushes.White,null,new Rect(2+(34-width)*p,2,width,18),9,9);dc.Pop();
        }
    }
    public sealed class UiSurfaceMotion : IUiMotion {
        readonly UiMotionClock clock;
        readonly FrameworkElement surface;
        readonly ScaleTransform scale=new ScaleTransform(1,1);
        readonly TranslateTransform shift=new TranslateTransform();
        readonly Action closed;
        readonly bool scaleEnabled;
        double value=1,velocity,target=1;
        bool completed;
        public double Value {get{return value;}}
        public bool Closing {get{return target==0;}}
        public UiSurfaceMotion(UiMotionClock clock,FrameworkElement surface,Action closed,bool scaleEnabled){this.clock=clock;this.surface=surface;this.closed=closed;this.scaleEnabled=scaleEnabled;clock.Register(this);surface.RenderTransformOrigin=new Point(.5,.5);var group=new TransformGroup();group.Children.Add(scale);group.Children.Add(shift);surface.RenderTransform=group;}
        public void Show(bool fresh){completed=false;if(fresh)value=0;target=1;Paint();clock.Wake(this);}
        public void Close(){target=0;clock.Wake(this);}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref value,ref velocity,target,dt,target==1?.30:.22,1);else{value=target;velocity=0;}if(target==0&&value<.012&&Math.Abs(velocity)<.5){value=0;velocity=0;}Paint();bool active=Math.Abs(value-target)>.0005||Math.Abs(velocity)>.005;if(!active&&target==0)Complete();return active;}
        void Paint(){surface.Opacity=Math.Max(0,Math.Min(1,value));scale.ScaleX=scale.ScaleY=scaleEnabled?.98+.02*value:1;shift.Y=5*(1-value);}
        void Complete(){if(!completed){completed=true;if(closed!=null)closed();}}
        public void Snap(){value=target;velocity=0;Paint();if(target==0)Complete();}
        public void Dispose(){clock.Cancel(this);}
    }
    sealed class UiFocusMotion : IUiMotion {
        readonly TextBox box;readonly UiMotionClock clock;readonly SolidColorBrush brush=new SolidColorBrush();double value,velocity;
        public UiFocusMotion(TextBox box,UiMotionClock clock){this.box=box;this.clock=clock;clock.Register(this);box.BorderBrush=brush;box.GotKeyboardFocus+=delegate{clock.Wake(this);};box.LostKeyboardFocus+=delegate{clock.Wake(this);};box.Unloaded+=delegate{clock.Cancel(this);};Snap();}
        public bool Step(double dt,bool animate){double target=box.IsKeyboardFocusWithin?1:0;if(animate)PlayerSpring.Step(ref value,ref velocity,target,dt,.20,1);else{value=target;velocity=0;}brush.Color=Color.FromRgb((byte)(52+105*value),(byte)(58+186*value),(byte)(69+139*value));return Math.Abs(value-target)>.001||Math.Abs(velocity)>.01;}
        public void Snap(){Step(0,false);}
    }
    sealed class UiSliderMotion : IUiMotion {
        readonly MotionSlider slider;readonly UiMotionClock clock;
        public UiSliderMotion(MotionSlider slider,UiMotionClock clock){this.slider=slider;this.clock=clock;clock.Register(this);slider.ValueChanged+=delegate{clock.Wake(this);};slider.MouseEnter+=delegate{clock.Wake(this);};slider.MouseLeave+=delegate{clock.Wake(this);};slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(delegate{clock.Wake(this);}),true);slider.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(delegate{clock.Wake(this);}),true);slider.Unloaded+=delegate{clock.Cancel(this);};}
        public bool Step(double dt,bool animate){return slider.Step(dt,animate);}
        public void Snap(){slider.Step(0,false);}
    }
    sealed class UiGlyphMotion : IUiMotion {
        readonly TransportSymbol glyph;
        public UiGlyphMotion(TransportSymbol glyph){this.glyph=glyph;}
        public bool Step(double dt,bool animate){glyph.StepAction(dt,animate);return glyph.ActionRunning;}
        public void Snap(){glyph.StepAction(0,false);}
    }
    sealed class UiSpinMotion : IUiMotion {
        readonly RotateTransform rotate=new RotateTransform();double value,velocity,target;
        public UiSpinMotion(FrameworkElement icon){icon.RenderTransformOrigin=new Point(.5,.5);icon.RenderTransform=rotate;}
        public void Turn(){target-=360;}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref value,ref velocity,target,dt,.42,1);else{value=target;velocity=0;}rotate.Angle=value%360;return Math.Abs(value-target)>.02||Math.Abs(velocity)>.05;}
        public void Snap(){Step(0,false);}
    }
    sealed class UiTextMotion : IUiMotion {
        readonly TextBlock text;readonly TranslateTransform shift=new TranslateTransform();double value=1,velocity;
        public UiTextMotion(TextBlock text){this.text=text;text.RenderTransform=shift;}
        public void Change(){value=0;}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref value,ref velocity,1,dt,.18,1);else{value=1;velocity=0;}text.Opacity=.78+.22*value;shift.Y=1.5*(1-value);return Math.Abs(value-1)>.001||Math.Abs(velocity)>.01;}
        public void Snap(){Step(0,false);}
    }
    sealed class UiSelectionMotion : IUiMotion {
        readonly SolidColorBrush brush=new SolidColorBrush();double value,velocity,target;
        public double Value {get{return value;}}
        public UiSelectionMotion(Button button,bool selected){value=target=selected?1:0;button.Background=brush;Snap();}
        public void Select(bool selected){target=selected?1:0;}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref value,ref velocity,target,dt,.22,1);else{value=target;velocity=0;}brush.Color=Color.FromArgb((byte)(255*Math.Max(0,Math.Min(1,value))),65,65,68);return Math.Abs(value-target)>.001||Math.Abs(velocity)>.01;}
        public void Snap(){Step(0,false);}
    }
    static class UiMenus {
        // Own the entire popup surface: the system ContextMenu template otherwise
        // paints a separate light icon gutter even when Background is dark.
        static readonly ControlTemplate menuTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{x:Type ContextMenu}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='ContextSurface' Background='#1C1C1E' BorderBrush='#454548' BorderThickness='1' CornerRadius='10' Padding='4' SnapsToDevicePixels='True'>
    <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/>
  </Border>
</ControlTemplate>");
        static readonly ControlTemplate itemTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='{x:Type MenuItem}' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid>
    <Border x:Name='MenuSurface' CornerRadius='7' Padding='10,7'>
      <Grid>
        <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
        <ContentPresenter ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
        <Path x:Name='SubmenuArrow' Grid.Column='1' Visibility='Collapsed' Margin='14,0,2,0' Width='4' Height='8' Data='M 0,0 L 4,4 L 0,8' Stroke='#CBCBCD' StrokeThickness='1.4' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' VerticalAlignment='Center' IsHitTestVisible='False'/>
      </Grid>
    </Border>
    <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-5' IsOpen='{TemplateBinding IsSubmenuOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='None'>
      <Border x:Name='SubmenuSurface' Background='#1C1C1E' BorderBrush='#454548' BorderThickness='1' CornerRadius='10' Padding='4' SnapsToDevicePixels='True'>
        <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/>
      </Border>
    </Popup>
  </Grid>
  <ControlTemplate.Triggers><Trigger Property='HasItems' Value='True'><Setter TargetName='SubmenuArrow' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>");
        public static void Style(ContextMenu menu){menu.Template=menuTemplate;menu.Background=new SolidColorBrush(Color.FromRgb(28,28,30));menu.Foreground=Brushes.White;menu.FocusVisualStyle=null;menu.UseLayoutRounding=true;menu.SnapsToDevicePixels=true;}
        public static ControlTemplate ItemTemplate {get{return itemTemplate;}}
    }
    sealed class UiMenuMotion : IUiMotion {
        readonly MenuItem item;readonly UiMotionClock clock;readonly SolidColorBrush brush=new SolidColorBrush();double value,velocity,press,pressVelocity;bool held;Border root;ScaleTransform scale;
        public UiMenuMotion(MenuItem item,UiMotionClock clock){this.item=item;this.clock=clock;clock.Register(this);item.Foreground=Brushes.White;item.FocusVisualStyle=null;
            item.Template=UiMenus.ItemTemplate;
            item.Loaded+=delegate{item.ApplyTemplate();root=item.Template.FindName("MenuSurface",item) as Border;if(root!=null){root.Background=brush;scale=new ScaleTransform(1,1);root.RenderTransform=scale;root.RenderTransformOrigin=new Point(.5,.5);}Snap();};item.Unloaded+=delegate{clock.Cancel(this);};
            item.MouseEnter+=delegate{clock.Wake(this);};item.MouseLeave+=delegate{held=false;clock.Wake(this);};item.GotKeyboardFocus+=delegate{clock.Wake(this);};item.LostKeyboardFocus+=delegate{clock.Wake(this);};item.IsEnabledChanged+=delegate{clock.Wake(this);};item.PreviewMouseLeftButtonDown+=delegate{held=true;clock.Wake(this);};item.PreviewMouseLeftButtonUp+=delegate{held=false;clock.Wake(this);};
        }
        public bool Step(double dt,bool animate){double target=item.IsMouseOver||item.IsKeyboardFocusWithin?1:0;if(animate){PlayerSpring.Step(ref value,ref velocity,target,dt,.18,1);PlayerSpring.Step(ref press,ref pressVelocity,held?1:0,dt,.12,1);}else{value=target;velocity=0;press=0;pressVelocity=0;}brush.Color=Color.FromArgb((byte)(255*Math.Max(0,Math.Min(1,value))),59,59,63);if(root!=null){scale.ScaleX=scale.ScaleY=1-.015*press;root.Opacity=item.IsEnabled?1-.2*press:.35;}return Math.Abs(value-target)>.001||Math.Abs(velocity)>.01||Math.Abs(press-(held?1:0))>.001||Math.Abs(pressVelocity)>.01;}
        public void Snap(){Step(0,false);}
    }
    public sealed partial class NotchWindow {
        UiMotionClock uiMotion;
        readonly ConditionalWeakTable<Button,UiButtonMotion> buttonMotions=new ConditionalWeakTable<Button,UiButtonMotion>();
        UiGlyphMotion timerGlyphMotion;
        TransportSymbol timerPlayGlyph;
        UiTextMotion timerTextMotion;
        UiSurfaceMotion durationMotion;
        bool durationClosing;
        FrameworkElement durationAnchor;
        void SetupAppMotion(){uiMotion=new UiMotionClock(MotionEnabled);}
        void AttachButtonMotion(Button button){buttonMotions.Add(button,new UiButtonMotion(button,uiMotion));}
        void ExcludeButtonMotion(Button button){UiButtonMotion state;if(buttonMotions.TryGetValue(button,out state))state.Detach();}
        void UpdateTimerText(string value){if(timerText.Text!=value){timerText.Text=value;if(timerTextMotion==null){timerTextMotion=new UiTextMotion(timerText);uiMotion.Register(timerTextMotion);}timerTextMotion.Change();uiMotion.Wake(timerTextMotion);}}
        void UpdateTimerGlyph(){string mode=countdown.Running?"pause":"play";timerPlayGlyph.AnimateChanges=MotionEnabled();if(timerPlayGlyph.Mode!=mode){timerPlayGlyph.Mode=mode;uiMotion.Wake(timerGlyphMotion);}}
        void OpenDurationMotion(){durationClosing=false;if(durationMotion!=null)durationMotion.Dispose();durationMotion=new UiSurfaceMotion(uiMotion,(FrameworkElement)durationPopup.Child,delegate{if(durationClosing)durationPopup.IsOpen=false;},true);durationMotion.Show(true);}
        void CloseDurationEditor(){if(durationMotion==null||!durationPopup.IsOpen){durationPopup.IsOpen=false;return;}durationClosing=true;durationMotion.Close();}
        void OpenDurationEditor(FrameworkElement source){durationPopup.PlacementTarget=source??durationAnchor;durationPopup.IsOpen=true;}
    }
}
