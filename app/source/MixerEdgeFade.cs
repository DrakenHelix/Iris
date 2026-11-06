using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls;

namespace Iris {
    // Spatial feedback follows the displayed scroll position; it has no independent
    // timer and cannot change the accepted scroll spring or its current velocity.
    sealed class MixerEdgeFade {
        const int Samples=12;
        const double FadeDepth=18,BlurDepth=26,MaxBlur=4.5;
        readonly MixerScroll scroll;
        readonly StackPanel rows;
        readonly LinearGradientBrush mask=new LinearGradientBrush{MappingMode=BrushMappingMode.Absolute,StartPoint=new Point(0,0),EndPoint=new Point(0,1)};
        readonly GradientStop[] top=new GradientStop[Samples+1],bottom=new GradientStop[Samples+1];
        readonly Dictionary<FrameworkElement,BlurEffect> effects=new Dictionary<FrameworkElement,BlurEffect>();
        double lastHeight=-1,lastTop=-1,lastBottom=-1;
        public bool Enabled=true,BlurEnabled=true;
        public double TopStrength {get;private set;}
        public double BottomStrength {get;private set;}
        public MixerEdgeFade(MixerScroll scroll,StackPanel rows){
            this.scroll=scroll;this.rows=rows;
            for(int i=0;i<=Samples;i++){top[i]=new GradientStop(Colors.White,0);bottom[i]=new GradientStop(Colors.White,1);mask.GradientStops.Add(top[i]);mask.GradientStops.Add(bottom[i]);}
            scroll.PositionPainted+=Update;scroll.SizeChanged+=delegate{Update();};rows.SizeChanged+=delegate{Update();};scroll.Loaded+=delegate{Update();};
        }
        static double Smooth(double value){double t=Math.Max(0,Math.Min(1,value));return t*t*t*(t*(6*t-15)+10);}
        static Color Alpha(double value){return Color.FromArgb((byte)Math.Round(Math.Max(0,Math.Min(1,value))*255),255,255,255);}
        public void Update(){
            double height=scroll.ActualHeight;
            TopStrength=Enabled&&scroll.ScrollableHeight>.01?Smooth(scroll.DisplayOffset/12):0;
            BottomStrength=Enabled&&scroll.ScrollableHeight>.01?Smooth((scroll.ScrollableHeight-scroll.DisplayOffset)/12):0;
            bool fading=height>0&&(TopStrength>.0001||BottomStrength>.0001);
            if(fading){
                if(lastHeight!=height||Math.Abs(lastTop-TopStrength)>.00001||Math.Abs(lastBottom-BottomStrength)>.00001){
                    double depth=Math.Min(FadeDepth,height/3);mask.EndPoint=new Point(0,height);
                    for(int i=0;i<=Samples;i++){double t=i/(double)Samples;top[i].Offset=t*depth/height;top[i].Color=Alpha(1-TopStrength*(1-Smooth(t)));bottom[i].Offset=1-depth/height+t*depth/height;bottom[i].Color=Alpha(1-BottomStrength*(1-Smooth(1-t)));}
                    lastHeight=height;lastTop=TopStrength;lastBottom=BottomStrength;
                }
                scroll.OpacityMask=mask;
            }else scroll.OpacityMask=null;
            var present=new HashSet<FrameworkElement>(rows.Children.Cast<FrameworkElement>());
            foreach(var retired in effects.Keys.Where(view=>!present.Contains(view)).ToArray())effects.Remove(retired);
            foreach(var view in present){
                double radius=0;
                if(fading&&BlurEnabled&&view.ActualHeight>0){
                    double center=view.TranslatePoint(new Point(0,view.ActualHeight/2),scroll).Y;
                    if(center>=-view.ActualHeight/2-MaxBlur&&center<=height+view.ActualHeight/2+MaxBlur){
                        double upper=Math.Max(0,Math.Min(1,1-center/BlurDepth));double lower=Math.Max(0,Math.Min(1,1-(height-center)/BlurDepth));
                        radius=MaxBlur*Math.Max(TopStrength*upper*upper,BottomStrength*lower*lower);
                    }
                }
                BlurEffect effect;
                if(radius>.02){if(!effects.TryGetValue(view,out effect)){effect=new BlurEffect{KernelType=KernelType.Gaussian,RenderingBias=RenderingBias.Quality};effects.Add(view,effect);}effect.Radius=radius;view.Effect=effect;}
                else if(effects.TryGetValue(view,out effect)){if(view.Effect==effect)view.Effect=null;effect.Radius=0;}
            }
        }
    }
    public sealed partial class NotchWindow {
        MixerEdgeFade mixerEdges;
        void UpdateMixerEdges(){if(mixerEdges!=null)mixerEdges.Update();}
    }
}
