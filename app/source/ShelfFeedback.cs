using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Iris {
    public sealed partial class NotchWindow {
        ShelfDropFeedback shelfFeedback;
        bool fileDragActive;
        void SetupShelfFeedback(){
            shelfFeedback=new ShelfDropFeedback(uiMotion,surface){Width=SceneWidth,Height=SceneHeight};
            canvas.Children.Insert(canvas.Children.IndexOf(expanded),shelfFeedback);
            shelfFeedback.Changed=delegate(double amount){fileList.Opacity=1-.82*amount;emptyShelf.Opacity=Math.Max(0,1-4*amount);};
        }
        void HandleFileDrag(object sender,DragEventArgs e){
            Point point=e.GetPosition(canvas);
            if(prefs.Shelf&&e.Data.GetDataPresent(DataFormats.FileDrop)&&surface.ContainsPoint(point)){
                fileDragActive=true;collapse.Stop();Open();if(page!="Ablage")SwitchPage("Ablage",false);shelfFeedback.Move(point);shelfFeedback.Caption=prefs.Files.Count>=4?L.T("Ablage voll · maximal 4 Einträge"):L.T("Loslassen · ")+Math.Max(0,4-prefs.Files.Count)+L.T(" freie Plätze");shelfFeedback.Hover(true);e.Effects=prefs.Files.Count>=4?DragDropEffects.None:DragDropEffects.Link;
            }else{fileDragActive=false;shelfFeedback.Hover(false);e.Effects=DragDropEffects.None;}
            e.Handled=true;
        }
        void LeaveFileDrag(object sender,DragEventArgs e){
            if(surface.ContainsPoint(e.GetPosition(canvas)))return;fileDragActive=false;shelfFeedback.Hover(false);if(!uiTesting)collapse.Start();
        }
    }
    public sealed class ShelfDropFeedback : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;readonly NotchSurface surface;
        bool hovered;double amount,velocity,receipt,receiptVelocity,caption,captionVelocity;Point pointer=new Point(520,100);
        public Action<double> Changed;public string Caption=L.T("Loslassen zum Ablegen");
        public double Amount {get{return amount;}}
        public Point Pointer {get{return pointer;}}
        public ShelfDropFeedback(UiMotionClock clock,NotchSurface surface){this.clock=clock;this.surface=surface;clock.Register(this);IsHitTestVisible=false;}
        public void Move(Point point){pointer=point;InvalidateVisual();}
        public void Hover(bool value){if(hovered==value)return;hovered=value;clock.Wake(this);}
        public void Receive(){hovered=false;receipt=1;receiptVelocity=0;clock.Wake(this);}
        public bool Step(double dt,bool animate){
            if(animate){PlayerSpring.Step(ref amount,ref velocity,hovered?1:0,dt,hovered?.26:.32,1);PlayerSpring.Step(ref receipt,ref receiptVelocity,0,dt,.60,1);PlayerSpring.Step(ref caption,ref captionVelocity,hovered?1:0,dt,.14,1);}else{amount=caption=hovered?1:0;velocity=captionVelocity=receipt=receiptVelocity=0;}
            if(Changed!=null)Changed(amount);InvalidateVisual();return Math.Abs(amount-(hovered?1:0))>.001||Math.Abs(velocity)>.01||receipt>.001||Math.Abs(receiptVelocity)>.01||Math.Abs(caption-(hovered?1:0))>.001||Math.Abs(captionVelocity)>.01;
        }
        public void Snap(){Step(0,false);}
        static RadialGradientBrush Glow(Color color){return new RadialGradientBrush(new GradientStopCollection{new GradientStop(color,0),new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),1)}){Center=new Point(.5,.5),GradientOrigin=new Point(.5,.5),RadiusX=.5,RadiusY=.5};}
        protected override void OnRender(DrawingContext dc){
            double alpha=Math.Max(0,Math.Min(1,amount)),flash=Math.Max(0,receipt);if(alpha<.001&&flash<.001)return;
            dc.PushClip(surface.GetOutline());double left=(ActualWidth-438)/2;
            // The feedback is confined to the black notch. There is no external glow or idle gradient.
            dc.PushOpacity(Math.Min(1,alpha+flash*.6));dc.DrawRectangle(Glow(Color.FromArgb(130,55,168,244)),null,new Rect(pointer.X-175,pointer.Y-100,350,260));dc.DrawRectangle(Glow(Color.FromArgb(95,116,74,230)),null,new Rect(left+160,30,310,200));dc.Pop();
            if(alpha>.001){
                dc.PushOpacity(alpha);var area=new Rect(left+34,43,370,94);dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(110,20,24,32)),new Pen(new SolidColorBrush(Color.FromArgb(125,220,231,245)),1){DashStyle=new DashStyle(new double[]{3,3},0)},area,16,16);
                dc.PushOpacity(Math.Max(0,Math.Min(1,caption)));dc.PushTransform(new TranslateTransform(left+205,57+4*(1-alpha)));dc.DrawGeometry(null,new Pen(Brushes.White,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},Geometry.Parse("M 14,0 L 14,14 M 9,9 L 14,14 L 19,9 M 4,14 L 4,22 L 24,22 L 24,14"));dc.Pop();
                var text=new FormattedText(Caption,L.Culture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.White,1.0);dc.DrawText(text,new Point(left+(438-text.Width)/2,94));dc.Pop();dc.Pop();
            }
            if(flash>.001){double progress=1-Math.Min(1,flash);var pen=new Pen(new SolidColorBrush(Color.FromArgb((byte)(95*flash),116,202,255)),1.4);dc.DrawRoundedRectangle(null,pen,new Rect(left+34-progress*18,43-progress*9,370+progress*36,94+progress*18),16+progress*6,16+progress*6);}
            dc.Pop();
        }
    }
}
