using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Iris {
    public static class PlayerSpring {
        public static void Step(ref double value,ref double velocity,double target,double dt,double response,double damping){
            double w=2*Math.PI/response,offset=value-target;
            if(damping>=.999){double c=velocity+w*offset,e=Math.Exp(-w*dt);value=target+(offset+c*dt)*e;velocity=(velocity-w*c*dt)*e;}
            else{double decay=damping*w,f=w*Math.Sqrt(1-damping*damping),a=Math.Sin(f*dt),b=Math.Cos(f*dt),e=Math.Exp(-decay*dt);value=target+e*(offset*b+(velocity+decay*offset)/f*a);velocity=e*(velocity*b-(decay*velocity+w*w*offset)/f*a);}
            if(Math.Abs(value-target)<.0005&&Math.Abs(velocity)<.005){value=target;velocity=0;}
        }
    }
    // Playback status animation, not an audio-level meter.
    public sealed class PlaybackPulse : FrameworkElement {
        double time,activity,velocity;
        public double Activity {get{return activity;}}
        public PlaybackPulse(){Width=23;Height=15;IsHitTestVisible=false;}
        public void Step(double dt,bool playing,bool animate){time+=dt;if(animate)PlayerSpring.Step(ref activity,ref velocity,playing?1:0,dt,.30,1);else activity=playing?1:0;InvalidateVisual();}
        protected override void OnRender(DrawingContext dc){
            double sx=ActualWidth/23,sy=ActualHeight/15;
            for(int i=0;i<5;i++){double wave=.35+.65*(.5+.5*Math.Sin(time*(5.7+i*.37)+i*1.6));double h=(2+activity*(3+9*wave))*sy;dc.DrawRoundedRectangle(Brushes.White,null,new Rect((1+i*4.5)*sx,(ActualHeight-h)/2,2.4*sx,h),1.1,1.1);}
        }
    }
    public sealed class MotionSlider : Slider {
        double display,displayVelocity,hot,hotVelocity,pressure,pressureVelocity,contactAge,elastic,elasticVelocity,elasticTarget;
        bool contactHeld,contactActive;
        public bool CapsuleMode;
        public double Pressure {get{return pressure;}}
        public double TrackThickness {get{return 3+2.4*Math.Max(0,hot)+2.6*Math.Max(0,pressure);}}
        public double ElasticOffset {get{return elastic;}}
        public void BeginContact(){contactHeld=true;contactActive=true;contactAge=0;}
        public void EndContact(){contactHeld=false;elasticTarget=0;}
        public void SetPointerFraction(double fraction){double p=Math.Max(0,Math.Min(1,fraction));double value=Minimum+(Maximum-Minimum)*p;if(IsSnapToTickEnabled&&TickFrequency>0)value=Minimum+Math.Round((value-Minimum)/TickFrequency)*TickFrequency;Value=Math.Max(Minimum,Math.Min(Maximum,value));elasticTarget=Math.Max(-3,Math.Min(3,(fraction-p)*14));}
        bool initialized;
        public bool Dragging {get;private set;}
        public bool TestHover;
        public double DisplayedValue {get{return display;}}
        public double Highlight {get{return hot;}}
        public MotionSlider(){
            Height=22;Focusable=true;Cursor=Cursors.Hand;SmallChange=.01;LargeChange=.05;IsMoveToPointEnabled=true;
            var hitArea=new FrameworkElementFactory(typeof(Border));hitArea.SetValue(Border.BackgroundProperty,Brushes.Transparent);Template=new ControlTemplate(typeof(Slider)){VisualTree=hitArea};
        }
        protected override Size MeasureOverride(Size size){return new Size(double.IsInfinity(size.Width)?100:size.Width,Height);}
        protected override void OnValueChanged(double oldValue,double newValue){base.OnValueChanged(oldValue,newValue);if(!initialized||Dragging){display=newValue;displayVelocity=0;initialized=true;}InvalidateVisual();}
        public bool Step(double dt,bool animate){
            contactAge+=dt;if(!contactHeld&&contactAge>=.075)contactActive=false;
            double target=IsEnabled&&(IsMouseOver||Dragging||TestHover)?1:0;
            if(!animate){display=Value;displayVelocity=0;hot=target;hotVelocity=0;}
            else{if(Dragging){display=Value;displayVelocity=0;}else PlayerSpring.Step(ref display,ref displayVelocity,Value,dt,.14,1);PlayerSpring.Step(ref hot,ref hotVelocity,target,dt,.23,.85);}
            if(animate){PlayerSpring.Step(ref pressure,ref pressureVelocity,contactActive?1:0,dt,contactActive?.105:.24,contactActive?1:.65);PlayerSpring.Step(ref elastic,ref elasticVelocity,elasticTarget,dt,.24,.65);}else{pressure=contactActive?1:0;elastic=elasticTarget;}
            InvalidateVisual();return Math.Abs(display-Value)>.0005||Math.Abs(hot-target)>.001||Math.Abs(pressure-(contactActive?1:0))>.001||Math.Abs(pressureVelocity)>.01||Math.Abs(elastic-elasticTarget)>.001||Math.Abs(elasticVelocity)>.01||(!contactHeld&&contactAge<.075);
        }
        void ReadPointer(MouseEventArgs e){double width=Math.Max(1,ActualWidth-8);SetPointerFraction((e.GetPosition(this).X-4)/width);}
        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e){if(!IsEnabled)return;Focus();Dragging=true;BeginContact();CaptureMouse();ReadPointer(e);base.OnPreviewMouseLeftButtonDown(e);}
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){e.Handled=true;}
        protected override void OnPreviewMouseMove(MouseEventArgs e){if(Dragging)ReadPointer(e);base.OnPreviewMouseMove(e);}
        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e){if(Dragging){ReadPointer(e);Dragging=false;EndContact();ReleaseMouseCapture();RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});}base.OnPreviewMouseLeftButtonUp(e);}
        protected override void OnLostMouseCapture(MouseEventArgs e){if(Dragging){Dragging=false;EndContact();RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});}base.OnLostMouseCapture(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.Key==Key.Left||e.Key==Key.Down){Value=Math.Max(Minimum,Value-(Maximum-Minimum)*SmallChange);e.Handled=true;}else if(e.Key==Key.Right||e.Key==Key.Up){Value=Math.Min(Maximum,Value+(Maximum-Minimum)*SmallChange);e.Handled=true;}else if(e.Key==Key.Home){Value=Minimum;e.Handled=true;}else if(e.Key==Key.End){Value=Maximum;e.Handled=true;}else base.OnKeyDown(e);}
        protected override void OnRender(DrawingContext dc){
            double range=Maximum-Minimum,p=range>0?Math.Max(0,Math.Min(1,(display-Minimum)/range)):0,x=4+elastic,w=Math.Max(0,ActualWidth-8),h=TrackThickness,y=(ActualHeight-h)/2;
            dc.PushOpacity(IsEnabled?1:.35);dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(65,65,68)),null,new Rect(x,y,w,h),h/2,h/2);
            double fill=CapsuleMode&&IsEnabled?Math.Max(h,Math.Min(w,w*p)):w*p,fh=h+.5*Math.Max(0,pressure);
            if(fill>0)dc.DrawRoundedRectangle(Brushes.White,null,new Rect(x,(ActualHeight-fh)/2,fill,fh),fh/2,fh/2);
            if(!CapsuleMode){double r=3.5*Math.Max(0,hot)+1.1*Math.Max(0,pressure);if(r>.01)dc.DrawEllipse(Brushes.White,null,new Point(x+w*p,ActualHeight/2),r,r);}dc.Pop();

        }
    }
    public sealed class SpeakerSymbol : FrameworkElement {
        public bool Known=true;
        double level=.7,velocity,contact;
        public double Level {get{return level;}}
        public void Step(double dt,double value,double pressure,bool animate){if(animate)PlayerSpring.Step(ref level,ref velocity,value,dt,.20,1);else level=value;contact=pressure;InvalidateVisual();}
        protected override void OnRender(DrawingContext dc){
            dc.PushTransform(new ScaleTransform(ActualWidth/24,ActualHeight/24));dc.DrawGeometry(Brushes.White,null,Geometry.Parse("M 2,9 L 6,9 L 11,5 L 11,19 L 6,15 L 2,15 Z"));
            var pen=new Pen(Brushes.White,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};double a=Known?Math.Max(0,Math.Min(1,level*10)):0,b=Known?Math.Max(0,Math.Min(1,(level-.2)/.4)):0;
            dc.PushTransform(new ScaleTransform(1+.12*contact,1+.08*contact,12,12));dc.PushOpacity(a);dc.DrawGeometry(null,pen,Geometry.Parse("M 14,8 Q 18,12 14,16"));dc.Pop();dc.PushOpacity(b);dc.DrawGeometry(null,pen,Geometry.Parse("M 17,5 Q 24,12 17,19"));dc.Pop();dc.Pop();
            dc.Pop();
        }
    }
    public sealed class AnimatedLyricsScroll : ScrollViewer {
        public static readonly DependencyProperty MotionOffsetProperty=DependencyProperty.Register("MotionOffset",typeof(double),typeof(AnimatedLyricsScroll),new PropertyMetadata(0.0,delegate(DependencyObject d,DependencyPropertyChangedEventArgs e){((AnimatedLyricsScroll)d).ScrollToVerticalOffset((double)e.NewValue);}));
        public double MotionOffset {get{return (double)GetValue(MotionOffsetProperty);}set{SetValue(MotionOffsetProperty,value);}}
        public void MoveTo(double target,bool animate){BeginAnimation(MotionOffsetProperty,null);MotionOffset=VerticalOffset;if(!animate){MotionOffset=target;return;}BeginAnimation(MotionOffsetProperty,new DoubleAnimation(MotionOffset,target,TimeSpan.FromMilliseconds(360)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}
    }
    public sealed class RollingLyric : Grid {
        TextBlock front,back;string current="";
        public string CurrentText {get{return current;}}
        public RollingLyric(){ClipToBounds=true;front=Make();back=Make();Children.Add(back);Children.Add(front);}
        TextBlock Make(){return new TextBlock{FontFamily=new FontFamily("Segoe UI"),FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=Brushes.White,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,RenderTransform=new TranslateTransform(),Effect=new BlurEffect{Radius=0}};}
        public void SetLine(string text,bool animate){
            text=text??"";if(text==current){if(!animate)Snap();return;}back.Text=current;current=text;front.Text=text;
            foreach(var t in new[]{front,back}){t.BeginAnimation(OpacityProperty,null);((TranslateTransform)t.RenderTransform).BeginAnimation(TranslateTransform.YProperty,null);((BlurEffect)t.Effect).BeginAnimation(BlurEffect.RadiusProperty,null);}
            if(!animate){Snap();return;}
            front.Opacity=0;back.Opacity=1;((TranslateTransform)front.RenderTransform).Y=12;((TranslateTransform)back.RenderTransform).Y=0;((BlurEffect)front.Effect).Radius=3;((BlurEffect)back.Effect).Radius=0;
            front.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(260)));back.BeginAnimation(OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(180)));
            ((TranslateTransform)front.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(12,0,TimeSpan.FromMilliseconds(320)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
            ((TranslateTransform)back.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-12,TimeSpan.FromMilliseconds(220)));
            ((BlurEffect)front.Effect).BeginAnimation(BlurEffect.RadiusProperty,new DoubleAnimation(3,0,TimeSpan.FromMilliseconds(260)));((BlurEffect)back.Effect).BeginAnimation(BlurEffect.RadiusProperty,new DoubleAnimation(0,3,TimeSpan.FromMilliseconds(180)));
        }
        void Snap(){foreach(var t in new[]{front,back}){t.BeginAnimation(OpacityProperty,null);((TranslateTransform)t.RenderTransform).BeginAnimation(TranslateTransform.YProperty,null);((BlurEffect)t.Effect).BeginAnimation(BlurEffect.RadiusProperty,null);}front.Opacity=1;back.Opacity=0;((TranslateTransform)front.RenderTransform).Y=0;((BlurEffect)front.Effect).Radius=0;}
    }
}
