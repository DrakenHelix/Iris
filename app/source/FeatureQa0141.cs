using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Iris {
    public sealed partial class NotchWindow {
        void Verify0141(List<string> results){
            SetReducedMotion(false);uiMotion.PauseAutomatic();audioSnapshot=MixerFixture();RefreshMixer();AdvanceUi(70);SwitchPage("Mixer",false);SetReducedMotion(true);Open();SetQuickBar(true);SetReducedMotion(false);UpdateLayout();
            var first=mixerViews["fixture:music"];var survivor=mixerViews["fixture:browser"];double initialY=survivor.Y;
            audioSnapshot=MixerFixture().Where(s=>s.Key!="fixture:music").ToArray();RefreshMixer();AdvanceUi(4);
            if(first.Present||first.Alpha<=0||first.Alpha>=1||survivor.Y<=0||survivor.Y>=initialY)throw new Exception("Mixer source removal has no fade/reflow intermediate state");Capture("preview-mixer-reflow.png");
            double y=survivor.Y,velocity=survivor.YVelocity,alpha=first.Alpha,alphaVelocity=first.AlphaVelocity;audioSnapshot=MixerFixture();RefreshMixer();
            if(mixerViews["fixture:music"]!=first||mixerRows.Children.Count!=3||survivor.Y!=y||survivor.YVelocity!=velocity||first.Alpha!=alpha||first.AlphaVelocity!=alphaVelocity)throw new Exception("Mixer reappearance replaces a live view or jumps its pose/velocity");
            AdvanceUi(4);Capture("preview-mixer-reversal.png");AdvanceUi(70);
            audioSnapshot=MixerFixture().Where(s=>s.Key!="fixture:music").ToArray();RefreshMixer();AdvanceUi(70);if(mixerViews.Count!=2||mixerRows.Children.Count!=2||Math.Abs(survivor.Y)>.02)throw new Exception("Mixer retiring row leaves a gap or duplicate");
            var extra=new AudioSource{Key="fixture:extra",Name="Weitere Testquelle",Path="",Package="",ProcessName="extra",Volume=.6};audioSnapshot=MixerFixture().Concat(new[]{extra}).ToArray();RefreshMixer();AdvanceUi(70);UpdateLayout();mixerScroll.ScrollToVerticalOffset(36);UpdateLayout();if(mixerScroll.ScrollableHeight<20||!Hits(mixerViews[extra.Key].Slider)||!Hits(mixerViews[extra.Key].Mute))throw new Exception("Additional mixer sources cannot be reached while quick bar is open");Capture("preview-mixer-scroll.png");
            SetReducedMotion(true);audioSnapshot=new AudioSource[0];RefreshMixer();if(mixerRows.Children.Count!=0||mixerViews.Count!=0||!mixerEmpty.IsVisible)throw new Exception("Reduced-motion mixer removal did not settle immediately");audioSnapshot=MixerFixture();RefreshMixer();mixerScroll.ScrollToTop();UpdateLayout();SetReducedMotion(false);
            SetQuickBar(false);SetReducedMotion(true);CloseNotch(true);SetReducedMotion(false);
            HandleSystemVolume(new AudioLevel(.62,false));if(volumeHud.Value!=.62||!VolumeHudVisible())throw new Exception("System volume event waits for a poll");AdvanceUi(4);double reveal=volumeHud.Reveal;HandleSystemVolume(new AudioLevel(.28,false));if(volumeHud.Reveal!=reveal)throw new Exception("Repeated system volume event resets the visible animation");AdvanceUi(65);StopMorph();surface.Span=320;surface.Depth=ClosedDepth;UpdateContentClip();Capture("preview-volume-event.png");
            HandleSystemVolume(new AudioLevel(.62,true));if(volumeHud.Value!=0)throw new Exception("Mute notification did not target zero");AdvanceUi(65);Capture("preview-volume-muted.png");HandleSystemVolume(new AudioLevel(.62,false));if(volumeHud.Value!=.62)throw new Exception("Unmute did not restore the reported endpoint value");AdvanceUi(65);volumeHud.Show(false);hudDismiss.Stop();AdvanceUi(100);
            IntPtr buffer=Marshal.AllocHGlobal(32);try{for(int i=0;i<32;i++)Marshal.WriteByte(buffer,i,0);Marshal.WriteInt32(buffer,16,1);Marshal.Copy(BitConverter.GetBytes(.36f),0,IntPtr.Add(buffer,20),4);Marshal.WriteInt32(buffer,24,2);var parsed=SystemVolumeObserver.ReadNotification(buffer);if(!parsed.Muted||Math.Abs(parsed.Value-.36)>.001)throw new Exception("Native volume notification payload marshals incorrectly");}finally{Marshal.FreeHGlobal(buffer);}
            uiMotion.SnapAll();if(uiMotion.ActiveCount!=0)throw new Exception("Mixer/event motions keep the rendering clock running");
            results.Add("PASS: Mixer sources fade/reflow continuously, quick reappearance keeps identity/pose/velocity, retiring rows are removed, and extra sources remain reachable after scrolling with the quick bar open.");
            results.Add("PASS: System-volume/mute/unmute event handling targets feedback immediately, preserves an in-flight reveal, decodes the native BOOL/float payload and idles. Event injection uses fixtures; no system volume writes.");
        }
    }
}
