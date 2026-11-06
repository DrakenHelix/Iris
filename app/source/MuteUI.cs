using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Iris {
    public sealed class MuteMark : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;readonly bool badge;bool muted;double phase,velocity;
        public double Phase {get{return phase;}}
        public bool Muted {get{return muted;}}
        public MuteMark(UiMotionClock clock,bool badge){this.clock=clock;this.badge=badge;IsHitTestVisible=false;clock.Register(this);Unloaded+=delegate{clock.Cancel(this);};}
        public void SetMuted(bool value){if(muted==value)return;muted=value;clock.Wake(this);}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref phase,ref velocity,muted?1:0,dt,.22,1);else{phase=muted?1:0;velocity=0;}Opacity=Math.Max(0,Math.Min(1,phase));InvalidateVisual();return Math.Abs(phase-(muted?1:0))>.001||Math.Abs(velocity)>.01;}
        public void Snap(){Step(0,false);}
        protected override void OnRender(DrawingContext dc){
            double p=Math.Max(0,Math.Min(1,phase));if(p<.001)return;
            dc.PushTransform(new ScaleTransform(ActualWidth/28,ActualHeight/28));
            if(badge){
                dc.PushTransform(new ScaleTransform(.72+.28*p,.72+.28*p,21,21));dc.DrawEllipse(Brushes.Black,new Pen(new SolidColorBrush(Color.FromRgb(75,75,79)),.7),new Point(21,21),7,7);
                dc.DrawGeometry(Brushes.White,null,Geometry.Parse("M 16,19.5 L 18,19.5 L 20.5,17.5 L 20.5,24.5 L 18,22.5 L 16,22.5 Z"));
                var start=new Point(16,26);var end=new Point(16+10*p,26-10*p);dc.DrawLine(new Pen(Brushes.Black,3.4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,end);dc.DrawLine(new Pen(Brushes.White,1.35){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,end);dc.Pop();
            }else{
                var start=new Point(5,23);var end=new Point(23,5);dc.DrawLine(new Pen(Brushes.Black,4.4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,new Point(start.X+(end.X-start.X)*p,start.Y+(end.Y-start.Y)*p));dc.DrawLine(new Pen(Brushes.White,1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},start,new Point(start.X+(end.X-start.X)*p,start.Y+(end.Y-start.Y)*p));
            }dc.Pop();
        }
    }
    public sealed partial class NotchWindow {
        Button playerMuteButton;MuteMark playerMuteMark;
        bool playerVolumeKnown;
        readonly Dictionary<string,double> sourceRestore=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        void RememberSourceLevel(string key,double value){if(!string.IsNullOrEmpty(key)&&value>.001)sourceRestore[key]=value;}
        double SourceLevel(string key){MixerRow row;if(key==playerVolumeKey)return volumeSlider.Value;if(mixerViews.TryGetValue(key,out row))return row.Slider.Value;var source=audioSnapshot.FirstOrDefault(s=>s.Key==key);return source==null?0:ShownVolume(source);}
        bool ChangeSourceMute(string key,bool? muted){
            if(string.IsNullOrEmpty(key)||!audioSnapshot.Any(s=>s.Key==key))return false;double current=SourceLevel(key);bool shouldMute=muted??current>.001;
            if(shouldMute){RememberSourceLevel(key,current);SetSourceVolume(key,0);}
            else{double restore;var source=audioSnapshot.First(s=>s.Key==key);if(!sourceRestore.TryGetValue(key,out restore))restore=source.Volume>.001?source.Volume:.5;SetSourceVolume(key,restore);}
            return true;
        }
        void TogglePlayerMute(){volumeOpen=true;volumeLeave=DateTime.MinValue;ChangeSourceMute(playerVolumeKey,null);}
        void UpdatePlayerMuteVisual(){
            if(playerMuteMark==null)return;bool known=playerVolumeKnown||uiTesting&&!string.IsNullOrEmpty(playerVolumeKey);bool muted=known&&volumeSlider.Value<.001;playerMuteMark.SetMuted(muted);speakerSymbol.Known=known;playerMuteButton.ToolTip=known?(muted?L.T("Ton einschalten · nur diese Quelle"):L.T("Stummschalten · nur diese Quelle")):L.T("Audioquelle wird ermittelt · Stummzustand noch unbekannt");System.Windows.Automation.AutomationProperties.SetName(playerMuteButton,Convert.ToString(playerMuteButton.ToolTip));
        }
        void UpdateSourceMuteVisual(string key,double value){
            RememberSourceLevel(key,value);MixerRow row;if(mixerViews.TryGetValue(key,out row)&&row.Mark!=null){row.Mark.SetMuted(value<.001);row.Mute.ToolTip=value<.001?L.T("Ton einschalten"):L.T("Diese Quelle stummschalten");System.Windows.Automation.AutomationProperties.SetName(row.Mute,row.Source.Name+" · "+Convert.ToString(row.Mute.ToolTip));}
            if(key==playerVolumeKey)UpdatePlayerMuteVisual();
        }
    }
}
