using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        void VerifyIris(List<string> results){
            uiMotion.PauseAutomatic();audioSnapshot=MixerFixture();volumeWrites.Clear();RefreshMixer();SetReducedMotion(true);ReadMediaFixture();playerVolumeKey=audioSnapshot[0].Key;SetSourceVolume(playerVolumeKey,.72);SwitchPage("Musik",false);Open();OpenVolume();SetQuickBar(true);SetReducedMotion(false);AdvanceUi(70);UpdateLayout();
            var source=mixerViews[playerVolumeKey];var other=mixerViews[audioSnapshot[1].Key];double otherVolume=other.Slider.Value;
            playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(volumeSlider.Value!=0||source.Slider.Value!=0||!playerMuteMark.Muted||!source.Mark.Muted||other.Slider.Value!=otherVolume)throw new Exception("Player mute is not immediate or changes another source");
            AdvanceUi(4);StepPlayer(4.0/60);if(playerMuteMark.Phase<=0||playerMuteMark.Phase>=.99)throw new Exception("Player mute has no animated intermediate state");Capture("preview-player-mute-mid.png");double phase=playerMuteMark.Phase;
            playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(Math.Abs(volumeSlider.Value-.72)>.001||playerMuteMark.Muted||playerMuteMark.Phase!=phase)throw new Exception("Mute reversal does not restore source level/presentation");AdvanceUi(80);
            SwitchPage("Mixer",false);SetReducedMotion(true);Open();SetQuickBar(true);SetReducedMotion(false);UpdateLayout();if(!Hits(source.Mute))throw new Exception("Source badge blocks its own mute button");source.Mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));AdvanceUi(4);Capture("preview-mixer-mute-mid.png");AdvanceUi(70);Capture("preview-mixer-muted.png");if(!source.Mark.Muted||source.Mark.Phase<.99||other.Mark.Muted)throw new Exception("Mixer mute marker does not match its source");
            source.Mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(Math.Abs(source.Slider.Value-.72)>.001)throw new Exception("Mixer unmute does not restore original source level");AdvanceUi(70);
            string key=playerVolumeKey;playerVolumeKey="";double preserved=volumeSlider.Value;playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(volumeSlider.Value!=preserved)throw new Exception("Missing source falls back to a volume write");playerVolumeKey=key;
            SetReducedMotion(true);SwitchPage("Musik",false);Open();playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(playerMuteMark.Phase!=1)throw new Exception("Reduced-motion mute waits for animation");playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(playerMuteMark.Phase!=0)throw new Exception("Reduced-motion unmute did not clear mark");
            results.Add("PASS: Player speaker and mixer icons mute/unmute the same source immediately, restore volume, show animated reversible marks, preserve other sources and reduced motion; native audio writes suppressed in QA.");
        }
        void RunIrisDemo(){
            uiMotion.PauseAutomatic();audioSnapshot=MixerFixture();RefreshMixer();ReadMediaFixture();playerVolumeKey=audioSnapshot[0].Key;SetSourceVolume(playerVolumeKey,.72);SetReducedMotion(true);SwitchPage("Musik",false);Open();OpenVolume();SetReducedMotion(false);
            var dir=System.IO.Path.Combine(root,"iris-frames");Directory.CreateDirectory(dir);var trace=new List<string>{"frame,seconds,page,settings"};int index=0,frame=0;double previous=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            double[] times={.7,1.4,2,2.5,3.1,3.7,4.1,4.8,6.3,6.9};Action[] actions={delegate{playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{playerMuteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{SwitchPage("Mixer",true);SetQuickBar(true);},delegate{mixerViews[playerVolumeKey].Mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{mixerViews[playerVolumeKey].Mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{CloseNotch(true);},delegate{ToggleTimer();},delegate{ShowSettings();settingsSection="Timer";RenderSettingsSection();},delegate{settings.Close();},delegate{SwitchPage("Timer",true);Open();}};
            var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(index<times.Length&&now>=times[index])actions[index++]();double dt=Math.Min(.06,now-previous);uiMotion.Advance(dt);StepPlayer(dt);previous=now;Capture("iris-frames/main-"+frame.ToString("0000")+".png");bool show=settings!=null&&settings.IsVisible;if(show)CaptureWindow(settings,"iris-frames/settings-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+page+","+(show?1:0));frame++;if(now>=7.8){timer.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"iris-trace.csv"),trace);Close();}};timer.Start();
        }
    }
}
