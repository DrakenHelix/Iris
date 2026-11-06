using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Iris {
    public sealed partial class NotchWindow {
        byte[] MixerPixels(){
            var point=mixerScroll.TranslatePoint(new Point(),canvas);var image=RenderCanvas();int width=(int)mixerScroll.ActualWidth,height=(int)mixerScroll.ActualHeight;var bytes=new byte[width*height*4];image.CopyPixels(new Int32Rect((int)Math.Round(point.X),(int)Math.Round(point.Y),width,height),bytes,width*4,0);return bytes;
        }
        void VerifyMixerEdges(List<string> results){
            SetupScrollFixture();mixerScroll.ScrollToVerticalOffset(12);UpdateLayout();
            mixerEdges.Enabled=false;mixerEdges.Update();Capture("preview-mixer-edges-sharp.png");var sharp=MixerPixels();
            mixerEdges.Enabled=true;mixerEdges.BlurEnabled=false;mixerEdges.Update();Capture("preview-mixer-edges-fade-only.png");var faded=MixerPixels();
            mixerEdges.BlurEnabled=true;mixerEdges.Update();Capture("preview-mixer-edges-blur-fade.png");var blurred=MixerPixels();
            int width=(int)mixerScroll.ActualWidth,height=(int)mixerScroll.ActualHeight,spread=0,changed=0,centerChanges=0;long sharpEnergy=0,edgeEnergy=0;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                int i=(y*width+x)*4;bool edge=y<24||y>=height-24;
                if(edge){sharpEnergy+=sharp[i]+sharp[i+1]+sharp[i+2];edgeEnergy+=blurred[i]+blurred[i+1]+blurred[i+2];if(blurred[i]+blurred[i+1]+blurred[i+2]>faded[i]+faded[i+1]+faded[i+2]+15)spread++;if(blurred[i]!=sharp[i]||blurred[i+1]!=sharp[i+1]||blurred[i+2]!=sharp[i+2])changed++;}
                if(y>=30&&y<height-30&&(blurred[i]!=sharp[i]||blurred[i+1]!=sharp[i+1]||blurred[i+2]!=sharp[i+2]))centerChanges++;
            }
            if(changed<100||edgeEnergy>=sharpEnergy*.98||spread<20)throw new Exception("Actual mixer edge render does not both fade and spread with Gaussian blur: "+changed+", "+spread+", "+edgeEnergy+" / "+sharpEnergy);
            if(centerChanges!=0)throw new Exception("Mixer edge effect changes crisp center pixels: "+centerChanges);
            if(mixerScroll.OpacityMask==null||mixerEdges.TopStrength<.99||mixerEdges.BottomStrength<.99)throw new Exception("Middle scroll position lacks both edge masks");
            mixerScroll.ScrollToTop();UpdateLayout();mixerEdges.Update();if(mixerEdges.TopStrength!=0||mixerEdges.BottomStrength<.99)throw new Exception("Top of list dims a complete first row");Capture("preview-mixer-edges-top.png");
            mixerScroll.ScrollToVerticalOffset(mixerScroll.ScrollableHeight);UpdateLayout();mixerEdges.Update();if(mixerEdges.TopStrength<.99||mixerEdges.BottomStrength!=0)throw new Exception("Bottom of list dims a complete last row");Capture("preview-mixer-edges-bottom.png");
            mixerScroll.ScrollToVerticalOffset(36);UpdateLayout();MixerWheel(-120);AdvanceUi(4);UpdateLayout();double position=mixerScroll.DisplayOffset,velocity=mixerScroll.ScrollVelocity,target=mixerScroll.TargetOffset;mixerEdges.Update();if(mixerScroll.DisplayOffset!=position||mixerScroll.ScrollVelocity!=velocity||mixerScroll.TargetOffset!=target)throw new Exception("Edge rendering changes the approved scroll physics");
            MixerWheel(120);AdvanceUi(80);UpdateLayout();var visible=mixerViews.Values.First(row=>{double y=row.Slider.TranslatePoint(new Point(0,row.Slider.ActualHeight/2),mixerScroll).Y;return y>28&&y<mixerScroll.ActualHeight-28;});if(!Hits(visible.Mute)||!Hits(visible.Slider))throw new Exception("Edge effects intercept mixer input");
            SetReducedMotion(true);MixerWheel(-120);UpdateLayout();if(mixerScroll.Moving)throw new Exception("Blur/fade reintroduces reduced-motion scrolling");
            audioSnapshot=ScrollFixture().Take(2).ToArray();RefreshMixer();UpdateLayout();mixerEdges.Update();if(mixerScroll.OpacityMask!=null||mixerRows.Children.Cast<FrameworkElement>().Any(view=>view.Effect is BlurEffect))throw new Exception("A short list retains its edge effect");
            SetReducedMotion(false);AdvanceUi(90);UpdateLayout();uiMotion.SnapAll();if(uiMotion.ActiveCount!=0)throw new Exception("Edge fading keeps a drawing clock active");
            results.Add("PASS: Native image comparison proves fading plus Gaussian blur at both mixer edges, while center pixels are unchanged; no fade at the top/bottom when the list ends there.");
            results.Add("PASS: Edge feedback follows the displayed scroll position without changing its position/velocity/target; controls remain reachable, short lists stay crisp, reduced motion and idle clock preserved.");
        }
    }
}
