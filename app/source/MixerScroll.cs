using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Iris {
    // Wheel motion changes only the content transform. Native scrolling is committed
    // at rest, so there is no repeated measure/arrange or integer row jump per frame.
    public sealed class MixerScroll : ScrollViewer,IUiMotion {
        readonly UiMotionClock clock;
        readonly TranslateTransform shift=new TranslateTransform();
        FrameworkElement body;
        double position,velocity,target,pendingOffset;
        bool initialized,committing;
        int lastDirection;
        public event Action PositionPainted;
        public double DisplayOffset {get{return position;}}
        public double TargetOffset {get{return target;}}
        public double ScrollVelocity {get{return velocity;}}
        public bool Moving {get{return Math.Abs(position-target)>.01||Math.Abs(velocity)>.05;}}
        public MixerScroll(UiMotionClock clock){
            this.clock=clock;clock.Register(this);CanContentScroll=false;UseLayoutRounding=false;SnapsToDevicePixels=false;
            Loaded+=delegate{AttachBody();};Unloaded+=delegate{Snap();clock.Cancel(this);};
        }
        void AttachBody(){var current=Content as FrameworkElement;if(current==body)return;body=current;if(body!=null)body.RenderTransform=shift;}
        double Clamp(double offset){return Math.Max(0,Math.Min(ScrollableHeight,offset));}
        public new void ScrollToVerticalOffset(double offset){
            AttachBody();position=target=Clamp(offset);velocity=0;lastDirection=0;initialized=true;clock.Cancel(this);Commit();Paint();
        }
        public new void ScrollToTop(){ScrollToVerticalOffset(0);}
        protected override void OnKeyDown(KeyEventArgs e){
            // Child sliders already consume their own volume keys before this route.
            if(e.Handled)return;double destination=target;
            if(e.Key==Key.PageDown)destination+=ViewportHeight*.9;
            else if(e.Key==Key.PageUp)destination-=ViewportHeight*.9;
            else if(e.Key==Key.Home)destination=0;
            else if(e.Key==Key.End)destination=ScrollableHeight;
            else{base.OnKeyDown(e);return;}
            target=Clamp(destination);initialized=true;lastDirection=0;e.Handled=true;clock.Wake(this);
        }
        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e){
            // Own the wheel before a child or the notch's tab navigation sees it.
            e.Handled=true;if(!IsMouseCaptureWithin)Wheel(e.Delta);
        }
        internal void Wheel(int delta){
            if(delta==0)return;int lines=SystemParameters.WheelScrollLines;if(lines==0)return;
            AttachBody();if(!initialized){position=target=VerticalOffset;initialized=true;}
            double distance=lines<0?Math.Max(36,ViewportHeight*.9):Math.Max(12,lines*12);
            double change=-delta/120.0*distance;int direction=Math.Sign(change);
            // A reversal aims from the visible position, rather than first consuming
            // an invisible queue of wheel steps in the previous direction.
            double origin=lastDirection!=0&&direction!=lastDirection?position:target;
            target=Clamp(origin+change);lastDirection=direction;
            clock.Wake(this);
        }
        public bool Step(double dt,bool animate){
            AttachBody();target=Clamp(target);
            if(animate)PlayerSpring.Step(ref position,ref velocity,target,dt,.32,1);else{position=target;velocity=0;}
            if(position<0){position=0;if(velocity<0)velocity=0;}if(position>ScrollableHeight){position=ScrollableHeight;if(velocity>0)velocity=0;}
            if(!Moving){position=target;velocity=0;lastDirection=0;Commit();}
            Paint();return Moving;
        }
        void Paint(){shift.Y=VerticalOffset-position;if(PositionPainted!=null)PositionPainted();}
        void Commit(){if(Math.Abs(VerticalOffset-position)>.001){committing=true;pendingOffset=position;base.ScrollToVerticalOffset(position);}}
        public void Snap(){Step(0,false);}
        protected override void OnScrollChanged(ScrollChangedEventArgs e){
            base.OnScrollChanged(e);AttachBody();
            bool ownCommit=committing&&Math.Abs(VerticalOffset-Clamp(pendingOffset))<.05;
            if(ownCommit)committing=false;
            if(!initialized||(!ownCommit&&Math.Abs(e.VerticalChange)>.001)){
                // Native keyboard/focus/automation scrolling remains authoritative.
                position=target=VerticalOffset;velocity=0;lastDirection=0;initialized=true;committing=false;clock.Cancel(this);
            }
            if(e.ExtentHeightChange!=0||e.ViewportHeightChange!=0){
                target=Clamp(target);if(position>ScrollableHeight){position=ScrollableHeight;velocity=0;}
                if(!Moving){position=target;velocity=0;clock.Cancel(this);}else clock.Wake(this);
            }
            Paint();
        }
    }
}
