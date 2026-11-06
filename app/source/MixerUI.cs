using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        AudioSessions audioSessions;
        SystemVolumeObserver systemVolumeObserver;
        DispatcherTimer audioPoll,hudDismiss;
        Grid mixerPage;
        StackPanel mixerRows;
        MixerScroll mixerScroll;
        TextBlock mixerEmpty;
        AudioSource[] audioSnapshot=new AudioSource[0];
        string playerVolumeKey="";
        bool mixerUpdating;
        double lastMaster=double.NaN;
        VolumeHud volumeHud;
        readonly Dictionary<string,MixerRow> mixerViews=new Dictionary<string,MixerRow>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,VolumeWrite> volumeWrites=new Dictionary<string,VolumeWrite>(StringComparer.OrdinalIgnoreCase);
        sealed class VolumeWrite {public double Value;public DateTime Until;}
        sealed class MixerRow {public Grid View;public MotionSlider Slider;public TextBlock Name,Number;public Button Mute;public MuteMark Mark;public AudioSource Source;public double Restore=.5;public readonly TranslateTransform FlowShift=new TranslateTransform();public bool Present=true;public double Y,YVelocity,TargetY,Alpha,AlphaVelocity;}
        Grid BuildMixer(){
            var pageView=new Grid{Width=438,Height=148};mixerRows=new StackPanel();mixerFlow=new MixerFlowMotion(this);uiMotion.Register(mixerFlow);
            mixerScroll=new MixerScroll(uiMotion){Content=mixerRows,Margin=new Thickness(34,34,34,5),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,CanContentScroll=false};pageView.Children.Add(mixerScroll);mixerEdges=new MixerEdgeFade(mixerScroll,mixerRows);
            mixerEmpty=Text(L.T("Noch keine Audioquellen\nStarte Musik oder ein Video in einer App."),12,muted);mixerEmpty.TextAlignment=TextAlignment.Center;mixerEmpty.Margin=new Thickness(36,40,36,12);pageView.Children.Add(mixerEmpty);return pageView;
        }
        void SetupAudio(){
            volumeHud=new VolumeHud(uiMotion){Width=282,Height=34,Visibility=Visibility.Collapsed};Canvas.SetLeft(volumeHud,(SceneWidth-282)/2);Canvas.SetTop(volumeHud,0);Panel.SetZIndex(volumeHud,20);canvas.Children.Add(volumeHud);
            volumeHud.SettledHidden=delegate{UpdateCompactLyrics();};
            hudDismiss=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(1250)};hudDismiss.Tick+=delegate{hudDismiss.Stop();volumeHud.Show(false);UpdateCompactLyrics();};
            if(uiTesting)return;
            systemVolumeObserver=new SystemVolumeObserver(delegate(AudioLevel level){Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate{if(!shuttingDown)HandleSystemVolume(level);}));});
            audioSessions=new AudioSessions();audioPoll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};audioPoll.Tick+=delegate{PollAudio();};audioPoll.Start();PollAudio();
        }
        void PollAudio(){
            if(shuttingDown||audioSessions==null)return;
            audioSnapshot=audioSessions.Snapshot();RefreshMixer();RefreshPlayerVolume();
            double current=audioSessions.Master;if(!double.IsNaN(current)){if((systemVolumeObserver==null||!systemVolumeObserver.Connected)&&!double.IsNaN(lastMaster)&&Math.Abs(current-lastMaster)>.001)ShowVolumeHud(current,L.T("Windows-Lautstärke"));lastMaster=current;}
        }
        double ShownVolume(AudioSource source){
            VolumeWrite pending;
            if(volumeWrites.TryGetValue(source.Key,out pending)){
                if(DateTime.UtcNow<pending.Until&&(Math.Abs(source.Volume-pending.Value)>.005||source.Muted))return pending.Value;
                volumeWrites.Remove(source.Key);
            }
            return source.Muted?0:source.Volume;
        }
        void RefreshPlayerVolume(){
            if(uiTesting)return;ApplyPlayerVolumeSource(AudioSessions.FindSource(audioSnapshot,mediaSource));
        }
        void ApplyPlayerVolumeSource(AudioSource source){
            string nextKey=source==null?"":source.Key;if(nextKey!=playerVolumeKey){volumeDirty=false;playerVolumeKnown=false;if(volumeSlider.Dragging)volumeSlider.ReleaseMouseCapture();}playerVolumeKey=nextKey;playerVolumeKnown=source!=null;volumeSlider.IsEnabled=source!=null;playerMuteButton.IsEnabled=source!=null;
            volumeSlider.ToolTip=source==null?L.T("Für diesen Player ist noch keine Audioquelle verfügbar."):source.Name+L.T(" · nur diese Quelle");
            if(source!=null&&!volumeSlider.Dragging&&!volumeDirty){volumeUpdating=true;try{volumeSlider.Value=ShownVolume(source);}finally{volumeUpdating=false;}}UpdatePlayerMuteVisual();
        }
        void HandleSystemVolume(AudioLevel level){lastMaster=level.Value;ShowVolumeHud(level.Muted?0:level.Value,level.Muted?L.T("Windows-Lautstärke · stumm"):L.T("Windows-Lautstärke"));}
        void SetSourceVolume(string key,double value){
            if(string.IsNullOrEmpty(key))return;volumeWrites[key]=new VolumeWrite{Value=value,Until=DateTime.UtcNow.AddSeconds(1.5)};
            if(!uiTesting&&audioSessions!=null)audioSessions.Set(key,value);
            // Keep the two views of the same source in sync immediately, before the native reply.
            MixerRow row;if(mixerViews.TryGetValue(key,out row)&&!row.Slider.Dragging){mixerUpdating=true;row.Slider.Value=value;mixerUpdating=false;row.Number.Text=Math.Round(value*100)+"";}
            if(key==playerVolumeKey&&!volumeSlider.Dragging){playerVolumeKnown=true;volumeUpdating=true;volumeSlider.Value=value;volumeUpdating=false;}
            UpdateSourceMuteVisual(key,value);ShowVolumeHud(value,L.T("Lautstärke der Audioquelle"));
        }
        void RefreshMixer(){
            var keys=new HashSet<string>(audioSnapshot.Select(s=>s.Key),StringComparer.OrdinalIgnoreCase);
            bool changed=false;foreach(var row in mixerViews.Values)if(row.Present&&!keys.Contains(row.Source.Key)){row.Present=false;row.View.IsHitTestVisible=false;if(row.Slider.Dragging)row.Slider.ReleaseMouseCapture();changed=true;}
            foreach(var source in audioSnapshot){
                MixerRow row;if(!mixerViews.TryGetValue(source.Key,out row)){row=CreateMixerRow(source);row.Y=row.TargetY=mixerViews.Values.Count(r=>r.Present)*36;mixerViews.Add(source.Key,row);mixerRows.Children.Add(row.View);changed=true;}
                if(!row.Present){row.Present=true;row.View.IsHitTestVisible=true;changed=true;}row.Source=source;row.Name.Text=source.Name;row.View.ToolTip=source.Name+(source.Active?L.T(" · spielt Ton ab"):" · pausiert");
                if(!row.Slider.Dragging){mixerUpdating=true;row.Slider.Value=ShownVolume(source);mixerUpdating=false;}
                row.Number.Text=Math.Round(row.Slider.Value*100)+"";row.Mute.ToolTip=row.Slider.Value<.001?L.T("Ton einschalten"):L.T("Diese Quelle stummschalten");if(source.Muted)RememberSourceLevel(source.Key,source.Volume);UpdateSourceMuteVisual(source.Key,row.Slider.Value);
            }
            if(changed)RetargetMixerFlow();
        }
        MixerRow CreateMixerRow(AudioSource source){
            var row=new MixerRow{Source=source,View=new Grid{Height=34,Margin=new Thickness(0,0,0,2)}};
            foreach(double width in new[]{32.0,126.0,176.0,36.0})row.View.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width)});
            ImageSource icon=NativeIcons.Get(source.Path);UIElement content=icon==null?(UIElement)new DesignSymbol("volume",ink){Width=19,Height=19}:new Image{Source=icon,Width=23,Height=23};
            var iconHost=new Grid{Width=28,Height=28,IsHitTestVisible=false};iconHost.Children.Add(content);row.Mark=new MuteMark(uiMotion,true);iconHost.Children.Add(row.Mark);row.Mute=Btn(iconHost,delegate{ChangeSourceMute(row.Source.Key,null);});row.Mute.Width=30;row.Mute.Height=30;row.Mute.Padding=new Thickness(0);row.Mute.Background=Brushes.Transparent;row.View.Children.Add(row.Mute);
            row.Name=Text(source.Name,12,ink);row.Name.TextTrimming=TextTrimming.CharacterEllipsis;row.Name.Margin=new Thickness(6,0,8,0);Grid.SetColumn(row.Name,1);row.View.Children.Add(row.Name);
            row.Slider=new MotionSlider{CapsuleMode=true,Minimum=0,Maximum=1,Value=source.Muted?0:source.Volume,Margin=new Thickness(2,0,6,0),ToolTip=source.Name};new UiSliderMotion(row.Slider,uiMotion);Grid.SetColumn(row.Slider,2);row.View.Children.Add(row.Slider);
            row.Number=Text(Math.Round(row.Slider.Value*100)+"",11,muted);row.Number.TextAlignment=TextAlignment.Right;Grid.SetColumn(row.Number,3);row.View.Children.Add(row.Number);
            row.Slider.ValueChanged+=delegate{row.Number.Text=Math.Round(row.Slider.Value*100)+"";UpdateSourceMuteVisual(row.Source.Key,row.Slider.Value);if(!mixerUpdating)SetSourceVolume(row.Source.Key,row.Slider.Value);};
            row.View.Tag=row;row.View.RenderTransform=row.FlowShift;row.View.Opacity=0;System.Windows.Automation.AutomationProperties.SetName(row.Slider,source.Name+L.T(" · Quellenlautstärke"));return row;
        }
        bool MixerDragging(){return mixerViews.Values.Any(row=>row.Slider.Dragging);}
        bool VolumeHudVisible(){return volumeHud!=null&&volumeHud.Visible&&!isOpen;}
        void ShowVolumeHud(double value,string title){
            if(volumeHud==null||isOpen||timerFinished)return;volumeHud.Value=value;volumeHud.ToolTip=title;volumeHud.Show(true);hudDismiss.Stop();hudDismiss.Start();UpdateCompactLyrics();
        }
    }
    public sealed class VolumeHud : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;double reveal,revealVelocity,display,displayVelocity,pulse,pulseVelocity,pulseTarget,pulseAge;bool shown;
        public Action SettledHidden;
        public bool Visible {get{return shown||reveal>.001;}}
        public double Reveal {get{return reveal;}}
        public double Value {get;set;}
        public VolumeHud(UiMotionClock clock){this.clock=clock;clock.Register(this);IsHitTestVisible=false;}
        public void Show(bool show){shown=show;if(show){Visibility=Visibility.Visible;pulseTarget=1;pulseAge=0;}if(!show)pulseTarget=0;clock.Wake(this);}
        public bool Step(double dt,bool animate){
            pulseAge+=dt;if(pulseAge>=.085)pulseTarget=0;
            if(animate){PlayerSpring.Step(ref reveal,ref revealVelocity,shown?1:0,dt,shown?.28:.24,1);PlayerSpring.Step(ref display,ref displayVelocity,Value,dt,.20,1);PlayerSpring.Step(ref pulse,ref pulseVelocity,pulseTarget,dt,.3,.8);}else{reveal=shown?1:0;display=Value;revealVelocity=displayVelocity=pulseVelocity=pulse=0;}
            Opacity=Math.Max(0,Math.Min(1,reveal));InvalidateVisual();bool active=Math.Abs(reveal-(shown?1:0))>.001||Math.Abs(revealVelocity)>.01||Math.Abs(display-Value)>.001||Math.Abs(displayVelocity)>.01||Math.Abs(pulse)>.001||Math.Abs(pulseVelocity)>.01||pulseTarget>0;
            if(!shown&&reveal<=.001){Visibility=Visibility.Collapsed;if(SettledHidden!=null)SettledHidden();}return active;
        }
        public void Snap(){Step(0,false);}
        protected override void OnRender(DrawingContext dc){
            dc.PushTransform(new TranslateTransform(0,(1-reveal)*-5));dc.DrawGeometry(Brushes.White,null,Geometry.Parse("M 9,14 L 13,14 L 18,10 L 18,24 L 13,20 L 9,20 Z"));
            var pen=new Pen(Brushes.White,1.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};if(display>.01)dc.DrawGeometry(null,pen,Geometry.Parse("M 21,13 Q 25,17 21,21"));else{dc.DrawLine(pen,new Point(21,14),new Point(26,20));dc.DrawLine(pen,new Point(26,14),new Point(21,20));}
            double height=3+Math.Max(0,pulse)*1.4;var rect=new Rect(36,(34-height)/2,230,height);dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(65,65,67)),null,rect,2,2);if(display>.0001)dc.DrawRoundedRectangle(Brushes.White,null,new Rect(rect.X,rect.Y,230*Math.Max(0,Math.Min(1,display)),height),2,2);dc.Pop();
        }
    }
}
