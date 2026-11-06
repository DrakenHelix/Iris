using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        AudioSource[] ScrollFixture(){return Enumerable.Range(1,8).Select(i=>new AudioSource{Key="scroll:"+i,Name="Audioquelle "+i+" · Test",Path="",Package="",ProcessName="fixture"+i,Volume=.12+i*.09,Active=true}).ToArray();}
        void SetupScrollFixture(){
            uiMotion.PauseAutomatic();SetReducedMotion(true);audioSnapshot=ScrollFixture();RefreshMixer();SwitchPage("Mixer",false);Open();SetQuickBar(true);UpdateLayout();mixerScroll.ScrollToTop();UpdateLayout();SetReducedMotion(false);AdvanceUi(70);UpdateLayout();
        }
        MouseWheelEventArgs MixerWheel(int delta){
            var args=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent};mixerViews["scroll:2"].Slider.RaiseEvent(args);return args;
        }
        void VerifyMixerScroll(List<string> results){
            SetupScrollFixture();if(mixerScroll.ScrollableHeight<150)throw new Exception("Scroll fixture does not overflow");
            var values=mixerViews.ToDictionary(p=>p.Key,p=>p.Value.Slider.Value);var wheel=MixerWheel(-120);
            if(!wheel.Handled||mixerScroll.TargetOffset<=0||mixerScroll.DisplayOffset!=0||page!="Mixer")throw new Exception("Wheel is not routed to mixer scroll or jumps immediately");
            AdvanceUi(1);UpdateLayout();if(mixerScroll.DisplayOffset<=0||mixerScroll.DisplayOffset>=mixerScroll.TargetOffset)throw new Exception("Mixer scroll does not start on the first frame");
            AdvanceUi(4);UpdateLayout();Capture("preview-mixer-scroll-down.png");double position=mixerScroll.DisplayOffset,velocity=mixerScroll.ScrollVelocity;
            MixerWheel(120);if(mixerScroll.DisplayOffset!=position||mixerScroll.ScrollVelocity!=velocity||mixerScroll.TargetOffset>=position)throw new Exception("Wheel reversal loses presentation position/velocity or keeps a stale queued destination");
            AdvanceUi(12);UpdateLayout();if(mixerScroll.DisplayOffset>=position)throw new Exception("Reversed scroll does not turn around promptly");Capture("preview-mixer-scroll-reverse.png");
            AdvanceUi(80);UpdateLayout();if(mixerScroll.Moving||mixerScroll.DisplayOffset!=0)throw new Exception("Mixer scroll does not settle at the top");
            MixerWheel(-30);AdvanceUi(1);UpdateLayout();double fractionalTarget=mixerScroll.TargetOffset;if(fractionalTarget<=0||fractionalTarget>=36)throw new Exception("High-resolution wheel input is discarded or inflated");
            AdvanceUi(80);UpdateLayout();if(Math.Abs(mixerScroll.VerticalOffset-mixerScroll.DisplayOffset)>.05||mixerScroll.Moving)throw new Exception("Native scroll offset does not commit at rest");
            for(int i=0;i<20;i++){MixerWheel(i%3==0?120:-120);AdvanceUi(2);UpdateLayout();if(double.IsNaN(mixerScroll.DisplayOffset)||mixerScroll.DisplayOffset<0||mixerScroll.DisplayOffset>mixerScroll.ScrollableHeight+.01)throw new Exception("Rapid scroll reversal escapes the viewport");}
            AdvanceUi(90);UpdateLayout();var visible=mixerViews.Values.FirstOrDefault(row=>{double y=row.Slider.TranslatePoint(new Point(0,row.Slider.ActualHeight/2),mixerScroll).Y;return y>10&&y<mixerScroll.ActualHeight-10;});
            if(visible==null||!Hits(visible.Slider)||!Hits(visible.Mute))throw new Exception("Scrolled mixer controls are no longer reachable with quick bar open");
            foreach(var value in values)if(mixerViews[value.Key].Slider.Value!=value.Value)throw new Exception("Scrolling changes a source volume");
            mixerScroll.ScrollToVerticalOffset(mixerScroll.ScrollableHeight);UpdateLayout();MixerWheel(-120);AdvanceUi(70);UpdateLayout();if(mixerScroll.Moving||Math.Abs(mixerScroll.DisplayOffset-mixerScroll.ScrollableHeight)>.01)throw new Exception("Bottom-edge input keeps the clock alive");
            SetReducedMotion(true);mixerScroll.ScrollToTop();UpdateLayout();MixerWheel(-120);if(mixerScroll.Moving||mixerScroll.DisplayOffset!=mixerScroll.TargetOffset)throw new Exception("Reduced motion still animates mixer scroll");UpdateLayout();
            SetReducedMotion(false);MixerWheel(-120);AdvanceUi(4);UpdateLayout();audioSnapshot=ScrollFixture().Take(2).ToArray();RefreshMixer();AdvanceUi(90);UpdateLayout();AdvanceUi(70);UpdateLayout();if(mixerScroll.ScrollableHeight>.01||mixerScroll.DisplayOffset>.01||mixerScroll.Moving)throw new Exception("Removed sources leave an invalid scroll position");
            mixerScroll.ScrollToTop();uiMotion.SnapAll();if(uiMotion.ActiveCount!=0)throw new Exception("Scroll keeps the shared rendering clock awake");
            results.Add("PASS: Actual routed wheel input starts mixer movement on its first frame, preserves position/velocity on reversal and accepts fractional wheel deltas.");
            results.Add("PASS: Scroll commits the native offset at rest, stays bounded through rapid reversals, preserves source volumes and reachable controls with the quick bar open, handles shrinking content/reduced motion and stops its rendering clock.");
        }
        void RunMixerScrollDemo(){
            SetupScrollFixture();var folder=System.IO.Path.Combine(root,"scroll-frames");Directory.CreateDirectory(folder);var trace=new List<string>{"frame,seconds,offset,target,velocity"};int frame=0,eventIndex=0;double previous=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            double[] times={.5,.78,1.05,1.45,1.62,2.1,2.4,2.65,3.05,3.4,3.8,4.1,4.35,4.9,5.15};int[] deltas={-120,-120,-120,120,120,-30,-120,-120,120,120,120,120,-120,-120,120};
            var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
            timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(eventIndex<times.Length&&now>=times[eventIndex]){MixerWheel(deltas[eventIndex++]);}uiMotion.Advance(Math.Min(.06,now-previous));previous=now;UpdateLayout();Capture("scroll-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+mixerScroll.DisplayOffset.ToString("F4",CultureInfo.InvariantCulture)+","+mixerScroll.TargetOffset.ToString("F4",CultureInfo.InvariantCulture)+","+mixerScroll.ScrollVelocity.ToString("F4",CultureInfo.InvariantCulture));frame++;if(now>=6.2){timer.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"scroll-trace.csv"),trace);Close();}};timer.Start();
        }
    }
}
