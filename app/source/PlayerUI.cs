using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Iris {
    public sealed partial class NotchWindow {
        sealed class MediaButtonMotion {public Button Button;public ScaleTransform Scale=new ScaleTransform(1,1);public double Value=1,Velocity,Target=1,PressAge,Compression,CompressionVelocity;public bool PressActive,Held,ReleaseQueued;}
        readonly List<MediaButtonMotion> mediaButtons=new List<MediaButtonMotion>();
        PlaybackPulse compactPulse,openPulse;
        SpeakerSymbol speakerSymbol;
        DispatcherTimer playerTimer;
        readonly System.Diagnostics.Stopwatch playerWatch=System.Diagnostics.Stopwatch.StartNew();
        bool volumeOpen,coverFolding,coverPending;
        DateTime volumeLeave;
        Grid volumeHost,artRoot;
        ScaleTransform artScale=new ScaleTransform(1,1);
        RotateTransform artTilt=new RotateTransform();
        double coverX=1,coverVelocity,coverRest=1,coverRestVelocity,volumeReveal,volumeRevealVelocity;
        ImageSource nextArtwork;
        int skipDirection=1;
        Border compactLyrics;
        RollingLyric compactLyricText;
        bool compactLyricMode;
        double lastLyricCompactSpan;
        bool volumeDirty;
        double pendingVolume;string pendingVolumeKey="";
        bool playTogglePending,playToggleExpected,playToggleAcknowledged;
        string playToggleCommand;
        DateTime playToggleDeadline;
        void BeginImmediatePlaybackToggle(){
            playToggleExpected=!(playTogglePending?playToggleExpected:isPlaying);playTogglePending=true;playToggleAcknowledged=false;playToggleCommand=null;playToggleDeadline=DateTime.UtcNow.AddSeconds(2);ApplyPlaybackGlyph();
        }
        void ApplyPlaybackGlyph(){
            if(playTogglePending&&DateTime.UtcNow>=playToggleDeadline)playTogglePending=false;
            playGlyph.AnimateChanges=MotionEnabled();playGlyph.Mode=(playTogglePending?playToggleExpected:isPlaying)?"pause":"play";
        }
        void ConfirmPlaybackSnapshot(){
            if(playTogglePending&&playToggleAcknowledged&&isPlaying==playToggleExpected)playTogglePending=false;
            ApplyPlaybackGlyph();
        }
        void ReadPlaybackCommandResult(Dictionary<string,object> data){
            if(!playTogglePending||!data.ContainsKey("id")||Convert.ToString(data["id"])!=playToggleCommand||!data.ContainsKey("success"))return;
            if(Convert.ToBoolean(data["success"]))playToggleAcknowledged=true;
            else{playTogglePending=false;ApplyPlaybackGlyph();}
        }
        void SetupPlayerAnimations(){
            volumeUpdating=true;try{volumeSlider.Value=uiTesting?.72:0;}catch{volumeSlider.Value=.7;}finally{volumeUpdating=false;}UpdatePlayerMuteVisual();
            compactPulse=new PlaybackPulse {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};Grid.SetColumn(compactPulse,2);compact.Children.Add(compactPulse);
            openPulse=new PlaybackPulse {HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,1,0),Visibility=Visibility.Collapsed};headerChrome.Children.Add(openPulse);
            compactLyrics=new Border {Width=350,Height=34,Visibility=Visibility.Collapsed,Background=Brushes.Transparent};Canvas.SetLeft(compactLyrics,(SceneWidth-350)/2);Canvas.SetTop(compactLyrics,0);canvas.Children.Add(compactLyrics);
            lyricsPanel.SetBinding(OpacityProperty,new System.Windows.Data.Binding("Opacity"){Source=expanded});
            lyricsLines.Margin=new Thickness(0,54,0,54);var lyricMask=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(0,1)};lyricMask.GradientStops.Add(new GradientStop(Colors.Transparent,0));lyricMask.GradientStops.Add(new GradientStop(Colors.White,.14));lyricMask.GradientStops.Add(new GradientStop(Colors.White,.86));lyricMask.GradientStops.Add(new GradientStop(Colors.Transparent,1));lyricMask.Freeze();lyricsScroll.OpacityMask=lyricMask;
            compactLyrics.SetBinding(OpacityProperty,new System.Windows.Data.Binding("Opacity"){Source=compact});compactLyrics.SetBinding(IsHitTestVisibleProperty,new System.Windows.Data.Binding("IsHitTestVisible"){Source=compact});
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());compactLyrics.Child=row;var note=new DesignSymbol("music",ink){Width=16,Height=16,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};row.Children.Add(note);compactLyricText=new RollingLyric();Grid.SetColumn(compactLyricText,1);row.Children.Add(compactLyricText);
            lastLyricCompactSpan=ClosedSpan;
            playerTimer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};playerTimer.Tick+=delegate{double dt=Math.Min(.06,playerWatch.Elapsed.TotalSeconds);playerWatch.Restart();bool active=seekPending||playTogglePending||isPlaying||isOpen&&page=="Musik"||coverFolding||coverPending||volumeOpen||volumeReveal>.001||volumeDirty||Math.Abs(coverX-1)>.001||Math.Abs(coverVelocity)>.01||mediaSeek.Highlight>.001||volumeSlider.Highlight>.001||Math.Abs(coverRest-(mediaAvailable&&!isPlaying?.86:1))>.001||compactPulse.Activity>.001||mediaButtons.Any(b=>b.PressActive||Math.Abs(b.Value-b.Target)>.001||b.Compression>.001||b.Button.Content is TransportSymbol&&((TransportSymbol)b.Button.Content).ActionRunning);if(!uiTesting&&active)StepPlayer(dt);else if(!uiTesting&&lyricsVisible){RefreshMediaProgress();UpdateCompactLyrics();}};playerTimer.Start();
        }
        void AnimateMediaButton(Button button){
            ExcludeButtonMotion(button);
            var state=new MediaButtonMotion {Button=button};button.RenderTransformOrigin=new Point(.5,.5);button.RenderTransform=state.Scale;mediaButtons.Add(state);
            if(button.Content==playGlyph)return;
            button.MouseEnter+=delegate{state.Target=1.13;};button.MouseLeave+=delegate{state.Target=1;};
            button.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(delegate{BeginMediaPress(state,false);}),true);
            button.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(delegate{state.Held=false;state.ReleaseQueued=true;}),true);
            button.LostMouseCapture+=delegate{state.Held=false;state.ReleaseQueued=true;};
            button.Click+=delegate{if(!state.PressActive)BeginMediaPress(state,true);var symbol=button.Content as TransportSymbol;if(symbol!=null)symbol.TriggerAction(MotionEnabled());};
        }
        void BeginMediaPress(MediaButtonMotion state,bool release){state.PressActive=true;state.Held=!release;state.ReleaseQueued=release;state.PressAge=0;}
        void StepMediaButton(MediaButtonMotion state,double dt,bool animate){
            state.PressAge+=dt;
            if(state.PressActive&&state.ReleaseQueued&&!state.Held&&state.PressAge>=.075){state.PressActive=false;state.ReleaseQueued=false;if(animate)state.Velocity=Math.Max(state.Velocity,3.6);}
            double target=state.PressActive?.84:state.Target;
            if(animate){PlayerSpring.Step(ref state.Value,ref state.Velocity,target,dt,state.PressActive?.115:.25,state.PressActive?1:.58);PlayerSpring.Step(ref state.Compression,ref state.CompressionVelocity,state.PressActive?1:0,dt,.13,.8);}
            else{state.Value=target;state.Velocity=0;state.Compression=state.PressActive?1:0;}
            state.Scale.ScaleX=state.Value*(1+.045*state.Compression);state.Scale.ScaleY=state.Value*(1-.035*state.Compression);
            var symbol=state.Button.Content as TransportSymbol;if(symbol!=null)symbol.StepAction(dt,animate);
        }
        void StepPlayer(double dt){
            bool animate=MotionEnabled();
            if(playTogglePending&&DateTime.UtcNow>=playToggleDeadline)ApplyPlaybackGlyph();
            if(seekPending&&DateTime.UtcNow>=seekDeadline)ConfirmSeekSnapshot();
            if(mediaAvailable){RefreshMediaProgress();}
            compactPulse.Visibility=mediaAvailable&&isPlaying?Visibility.Visible:Visibility.Collapsed;openPulse.Visibility=mediaAvailable&&isPlaying&&page=="Musik"?Visibility.Visible:Visibility.Collapsed;RefreshPassiveSlots();
            compactPulse.Step(dt,mediaAvailable&&isPlaying,animate);openPulse.Step(dt,mediaAvailable&&isPlaying,animate);
            foreach(var button in mediaButtons)StepMediaButton(button,dt,animate);
            double rest=mediaAvailable&&!isPlaying?.86:1;
            if(animate)PlayerSpring.Step(ref coverRest,ref coverRestVelocity,rest,dt,.34,.72);else coverRest=rest;
            if(!animate){if(coverPending)ApplyPendingArtwork();coverFolding=false;coverX=1;coverVelocity=0;}
            else{
                PlayerSpring.Step(ref coverX,ref coverVelocity,coverFolding?.04:1,dt,coverFolding?.20:.32,coverFolding?1:.68);
                if(coverFolding&&coverX<.12){ApplyPendingArtwork();coverFolding=false;}
                if(coverX<.03){coverX=.03;coverVelocity=0;}
            }
            artScale.ScaleX=coverX*coverRest;artScale.ScaleY=coverRest;artTilt.Angle=skipDirection*4*(1-coverX);
            if(volumeOpen&&volumeLeave!=DateTime.MinValue&&DateTime.UtcNow>=volumeLeave&&!volumeSlider.Dragging&&!volumeHost.IsMouseOver){volumeOpen=false;volumeLeave=DateTime.MinValue;}
            if(animate)PlayerSpring.Step(ref volumeReveal,ref volumeRevealVelocity,volumeOpen?1:0,dt,.28,.78);else volumeReveal=volumeOpen?1:0;
            volumeSlider.Width=Math.Max(0,70*volumeReveal);volumeSlider.Opacity=Math.Max(0,Math.Min(1,volumeReveal));volumeSlider.Visibility=volumeReveal>.005?Visibility.Visible:Visibility.Hidden;
            mediaSeek.Step(dt,animate);volumeSlider.Step(dt,animate);speakerSymbol.Step(dt,volumeSlider.Value,volumeSlider.Pressure,animate);UpdateCompactLyrics();
            if(volumeDirty&&!uiTesting)volumeDirty=false;
        }
        void ChangeArtwork(ImageSource next,bool animate){
            if(!animate||!MotionEnabled()||artwork.Source==null){artwork.Source=next;artworkPlaceholder.Visibility=next==null?Visibility.Visible:Visibility.Collapsed;coverPending=false;coverFolding=false;coverX=animate&&next!=null?.78:1;coverVelocity=0;return;}
            nextArtwork=next;coverPending=true;coverFolding=true;
        }
        void ApplyPendingArtwork(){if(!coverPending)return;artwork.Source=nextArtwork;artworkPlaceholder.Visibility=nextArtwork==null?Visibility.Visible:Visibility.Collapsed;coverPending=false;}
        void SetMediaArtwork(Dictionary<string,object> data,bool newIdentity){
            ImageSource decoded=null;
            if(data.ContainsKey("thumbnail")&&!string.IsNullOrEmpty(Convert.ToString(data["thumbnail"]))){try{using(var stream=new MemoryStream(Convert.FromBase64String(Convert.ToString(data["thumbnail"])))){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=256;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();decoded=bitmap;}}catch{}}
            if(decoded!=null){ChangeArtwork(decoded,newIdentity||artwork.Source==null||coverFolding);}
            else if(newIdentity)ChangeArtwork(null,true);
        }
        bool HasCompactLyrics(){return lyricsVisible&&mediaAvailable&&!countdown.Running;}
        double RestingSpan(){return cursorAvoiding?MinimalSpan:VolumeHudVisible()?320:HasCompactLyrics()?420:MinimalIdle()?MinimalSpan:HasDownloads()&&countdown.Running&&countdown.Remaining>=3600?230:ClosedSpan;}
        void UpdateCompactLyrics(){
            if(compactLyrics==null)return;RefreshPassiveSlots();bool show=HasCompactLyrics();compactLyricMode=show;compactLyrics.Visibility=show&&!cursorAvoiding&&!VolumeHudVisible()?Visibility.Visible:Visibility.Collapsed;compact.Visibility=show||VolumeHudVisible()||MinimalIdle()?Visibility.Hidden:Visibility.Visible;
            if(show){int index=lyricsData==null?-1:LyricsService.ActiveLine(lyricsData.Lines,CurrentMediaPosition());string line=index>=0?lyricsData.Lines[index].Text:lyricsData!=null&&lyricsData.Lines.Count>0?"♪ "+mediaTitle:lyricsData==null?lyricsStatus.Text:lyricsData.Instrumental?L.T("Instrumental · kein Liedtext"):!string.IsNullOrWhiteSpace(lyricsData.Plain)?lyricsData.Plain.Split('\n').FirstOrDefault(l=>!string.IsNullOrWhiteSpace(l)):L.T("Kein Liedtext verfügbar");compactLyricText.SetLine(line,MotionEnabled());}
            double width=RestingSpan();if(!isOpen&&(Math.Abs(lastLyricCompactSpan-width)>.1||Math.Abs(targetDepth-RestingDepth())>.1)){lastLyricCompactSpan=width;AnimateSurface(width,RestingDepth(),false);}
        }
        void AnimateLyricRows(int active){
            for(int i=0;i<lyricsLines.Children.Count;i++){
                var b=lyricsLines.Children[i] as Button;if(b==null)continue;var text=(TextBlock)b.Content;bool selected=i==active;
                text.FontWeight=selected?FontWeights.Bold:FontWeights.Normal;
                var scale=(ScaleTransform)text.RenderTransform;double size=selected?1:.91;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(scale.ScaleX,size,TimeSpan.FromMilliseconds(MotionEnabled()?280:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(scale.ScaleY,size,TimeSpan.FromMilliseconds(MotionEnabled()?280:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
                b.BeginAnimation(OpacityProperty,new DoubleAnimation(b.Opacity,selected?1:.38,TimeSpan.FromMilliseconds(MotionEnabled()?260:0)));
                var blur=(BlurEffect)text.Effect;blur.BeginAnimation(BlurEffect.RadiusProperty,new DoubleAnimation(blur.Radius,selected?0:.7,TimeSpan.FromMilliseconds(MotionEnabled()?260:0)));
            }
            lyricsLines.UpdateLayout();if(active>=0&&active<lyricsLines.Children.Count){var b=(Button)lyricsLines.Children[active];double top=b.TranslatePoint(new Point(),lyricsLines).Y+lyricsLines.Margin.Top;((AnimatedLyricsScroll)lyricsScroll).MoveTo(Math.Max(0,top+b.ActualHeight/2-lyricsScroll.ViewportHeight/2),MotionEnabled());}
            UpdateCompactLyrics();
        }
        void BuildInlineVolume(Grid controls){
            volumeHost=new Grid {Height=30};Grid.SetColumn(volumeHost,2);controls.Children.Add(volumeHost);
            var volume=SymbolButton("volume",L.T("Stummschalten · nur diese Quelle"),TogglePlayerMute);playerMuteButton=volume;volume.HorizontalAlignment=HorizontalAlignment.Right;speakerSymbol=new SpeakerSymbol {Width=24,Height=24};var symbolHost=new Grid{Width=28,Height=28,IsHitTestVisible=false};symbolHost.Children.Add(speakerSymbol);playerMuteMark=new MuteMark(uiMotion,false);symbolHost.Children.Add(playerMuteMark);volume.Content=symbolHost;volumeHost.Children.Add(volume);AnimateMediaButton(volume);
            volumeSlider=new MotionSlider {CapsuleMode=true,Minimum=0,Maximum=1,Width=0,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,36,0),Visibility=Visibility.Hidden,Opacity=0};volumeHost.Children.Add(volumeSlider);
            volumeSlider.ValueChanged+=delegate{UpdatePlayerMuteVisual();if(!volumeUpdating&&!uiTesting){pendingVolume=volumeSlider.Value;pendingVolumeKey=playerVolumeKey;volumeDirty=true;SetSourceVolume(pendingVolumeKey,pendingVolume);}};
            volumeHost.MouseEnter+=delegate{if(!uiTesting)OpenVolume();};volumeHost.MouseLeave+=delegate{volumeLeave=DateTime.UtcNow.AddMilliseconds(240);};
        }
        void AnimateTrackText(){foreach(var t in new[]{trackTitle,trackArtist}){var move=new TranslateTransform(0,4);t.RenderTransform=move;t.BeginAnimation(OpacityProperty,new DoubleAnimation(.3,1,TimeSpan.FromMilliseconds(MotionEnabled()?220:0)));move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(4,0,TimeSpan.FromMilliseconds(MotionEnabled()?300:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}}
        bool PlayerDragging(){return fileDragActive||MixerDragging()||mediaSeek!=null&&mediaSeek.Dragging||volumeSlider!=null&&volumeSlider.Dragging;}
        void VerifyPlayerAnimations(List<string> results){
            ReadMediaFixture();SwitchPage("Musik",false);Open();isPlaying=true;ShowEyes(false);StepPlayer(.03);double first=compactPulse.Activity;for(int i=0;i<20;i++)StepPlayer(1.0/60);if(compactPulse.Activity<=first||compactPulse.Activity<.9)throw new Exception("Playback pulse does not react to playing");
            var state=mediaButtons.First();state.Target=1.13;for(int i=0;i<25;i++)StepPlayer(1.0/60);if(state.Value<1.1)throw new Exception("Media button does not enlarge");state.Target=.86;for(int i=0;i<20;i++)StepPlayer(1.0/60);if(state.Value>.9)throw new Exception("Media button does not press down");state.Target=1;
            var previous=artwork.Source;ChangeArtwork(previous,true);for(int i=0;i<40;i++)StepPlayer(1.0/60);if(coverFolding||coverPending||Math.Abs(coverX-1)>.08)throw new Exception("Cover switch did not finish");
            isPlaying=false;for(int i=0;i<40;i++)StepPlayer(1.0/60);if(Math.Abs(coverRest-.86)>.02||compactPulse.Activity>.02)throw new Exception("Pause did not settle cover and pulse");
            if(compactPulse.Visibility!=Visibility.Collapsed||openPulse.Visibility!=Visibility.Collapsed)throw new Exception("Playback pulse remains visible while paused");
            isPlaying=true;StepPlayer(.016);if(compactPulse.Visibility!=Visibility.Visible||openPulse.Visibility!=Visibility.Visible)throw new Exception("Playback pulse does not return on resume");
            mediaAvailable=false;StepPlayer(.016);if(compactPulse.Visibility!=Visibility.Collapsed||openPulse.Visibility!=Visibility.Collapsed)throw new Exception("Playback pulse remains visible without a media session");mediaAvailable=true;isPlaying=false;StepPlayer(.016);
            if(compact.Children.OfType<DesignSymbol>().Any())throw new Exception("The left charging-style symbol remains in the compact notch");
            results.Add("PASS: Playback indicators appear only during active playback, hide on pause or missing media and return on resume; the compact left charging-style icon is removed without moving the clock.");
            OpenVolume();for(int i=0;i<30;i++)StepPlayer(1.0/60);if(volumeSlider.Width<65||volumeSlider.Visibility!=Visibility.Visible)throw new Exception("Inline volume did not expand");volumeSlider.TestHover=true;volumeSlider.Value=.31;for(int i=0;i<25;i++)StepPlayer(1.0/60);if(volumeSlider.Highlight<.9||Math.Abs(volumeSlider.DisplayedValue-.31)>.02)throw new Exception("Volume slider does not animate value and hover");Capture("preview-player-volume.png");
            volumeSlider.TestHover=false;volumeOpen=false;for(int i=0;i<35;i++)StepPlayer(1.0/60);
            mediaSeek.Value=.25;var input=PresentationSource.FromVisual(this);mediaSeek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,Environment.TickCount,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});mediaSeek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,Environment.TickCount,Key.Right){RoutedEvent=Keyboard.KeyDownEvent});mediaSeek.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,input,Environment.TickCount,Key.Right){RoutedEvent=Keyboard.KeyUpEvent});
            var seekFile=Directory.GetFiles(bridgeDirectory,"*.json").OrderBy(f=>f).Last();var seekCommand=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(seekFile));if(Convert.ToString(seekCommand["action"])!="seek"||Math.Abs(Convert.ToDouble(seekCommand["seconds"])-mediaDuration*.26)>.01||mediaSeeking)throw new Exception("The progress slider keyboard gesture does not commit the correct seek");
            seekPending=false;var data=new LyricsData {Lines=LyricsService.Parse("[00:00.00]Eigene Testzeile eins\n[00:01.00]Eigene Testzeile zwei\n[00:02.00]Eigene Testzeile drei")};lyricCache[CurrentLyricsKey()]=data;if(!lyricsVisible)ToggleLyrics();else LoadLyrics();
            mediaPosition=.2;mediaUpdated=DateTime.UtcNow;isPlaying=false;StepPlayer(.016);CloseNotch(true);for(int i=0;i<25;i++)StepPlayer(1.0/60);mediaPosition=1.2;StepPlayer(.016);if(lyricIndex!=1||compactLyricText.CurrentText!="Eigene Testzeile zwei"||isOpen||!compactLyricMode)throw new Exception("Lyrics stop updating without notch hover");
            SetReducedMotion(true);Open();CloseNotch(true);StepPlayer(.016);compactLyrics.GetBindingExpression(OpacityProperty).UpdateTarget();lyricsPanel.GetBindingExpression(OpacityProperty).UpdateTarget();if(surface.Span!=420)throw new Exception("Compact lyrics do not use the lyric strip");Capture("preview-compact-lyrics.png");
            var compactImage=RenderCanvas();byte[] cp=new byte[350*34*4];compactImage.CopyPixels(new Int32Rect(345,0,350,34),cp,350*4,0);int white=0;for(int i=0;i<cp.Length;i+=4)if(cp[i]>150&&cp[i+1]>150&&cp[i+2]>150)white++;if(white<80)throw new Exception("The compact lyric text is hidden in the rendered notch");var timed=lyricsData;lyricsData=new LyricsData {Plain="Eigene Zeile ohne Zeitmarken\nWeitere Zeile"};UpdateCompactLyrics();if(compactLyricText.CurrentText!="Eigene Zeile ohne Zeitmarken")throw new Exception("Plain lyric fallback remains stuck on loading");lyricsData=timed;SetReducedMotion(false);
            Open();mediaPosition=42;isPlaying=false;if(lyricsVisible)ToggleLyrics();SetQuickBar(false);UpdateCompactLyrics();
            VerifyPressReactions(results);
            VerifyToggleMorph(results);
            VerifyImmediatePlaybackToggle(results);
            VerifyMediaInteraction(results);
            results.Add("PASS: Playback pulse, hover/press scale, cover switching, pause scale and matching inline/progress sliders animate through the real player UI.");results.Add("PASS: Timed lyrics and compact lyric transitions update while the notch is closed and the pointer is outside.");
        }
        void VerifyImmediatePlaybackToggle(List<string> results){
            ReadMediaFixture();playGlyph.AnimateChanges=false;playGlyph.Mode="play";playGlyph.AnimateChanges=true;
            var button=mediaButtons.First(b=>b.Button.Content==playGlyph).Button;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));string first=playToggleCommand;
            if(!playTogglePending||playGlyph.Mode!="pause"||isPlaying)throw new Exception("Play click waits for native metadata or changes confirmed playback prematurely");
            StepPlayer(1.0/60);if(playGlyph.Pause<=0)throw new Exception("Play morph does not begin in its first frame");
            ReadMediaFixture();if(playGlyph.Mode!="pause"||!playTogglePending)throw new Exception("Stale playback metadata resets the immediate animation");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));string second=playToggleCommand;
            if(playGlyph.Mode!="play"||second==first)throw new Exception("Fast second click does not reverse the requested state");
            ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",first},{"success",false}}));
            if(!playTogglePending||playToggleCommand!=second)throw new Exception("An old command response changes the newest click");
            ReadMediaFixture();if(!playTogglePending)throw new Exception("A snapshot before command acknowledgement confirms too early");
            ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",second},{"success",true}}));ReadMediaFixture();
            if(playTogglePending||playGlyph.Mode!="play")throw new Exception("The confirmed final state does not settle");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",playToggleCommand},{"success",false}}));
            if(playTogglePending||playGlyph.Mode!="play")throw new Exception("A rejected playback command leaves the wrong glyph");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));playToggleDeadline=DateTime.UtcNow.AddSeconds(-1);StepPlayer(1.0/60);
            if(playTogglePending||playGlyph.Mode!="play")throw new Exception("An unconfirmed playback request never recovers");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));ReadMedia("{\"available\":false}");if(playTogglePending||playGlyph.Mode!="play")throw new Exception("A missing media session keeps its optimistic glyph");
            ReadMediaFixture();SetReducedMotion(true);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(playGlyph.Pause!=1)throw new Exception("Reduced motion click waits for a frame");
            ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"kind","command"},{"id",playToggleCommand},{"success",false}}));SetReducedMotion(false);
            results.Add("PASS: Real play/pause clicks set the morph target immediately and move in the first frame; stale snapshots and old command replies do not undo newer clicks; confirmation, rejection, timeout, missing session and reduced motion reconcile correctly.");
        }
        void VerifyPressReactions(List<string> results){
            var next=mediaButtons.First(m=>m.Button.Content is TransportSymbol&&((TransportSymbol)m.Button.Content).Mode=="next");var symbol=(TransportSymbol)next.Button.Content;
            next.Target=1;next.Value=1;next.Velocity=0;
            next.Button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});next.Button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent});next.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            double min=1,max=1;for(int i=0;i<4;i++){StepPlayer(1.0/60);min=Math.Min(min,next.Value);}
            if(min>.91||symbol.ActionPhase<=0||!next.PressActive)throw new Exception("A fast click loses its own press/symbol animation");Capture("preview-button-press.png");
            double before=symbol.ActionPhase;next.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(Math.Abs(symbol.ActionPhase-before)>.0001)throw new Exception("Repeated skip resets its symbol phase");
            for(int i=0;i<65;i++){StepPlayer(1.0/60);max=Math.Max(max,next.Value);}if(max<1.025||next.PressActive||Math.Abs(next.Value-1)>.015)throw new Exception("Button release has no independent rebound");
            volumeOpen=true;volumeSlider.TestHover=true;for(int i=0;i<35;i++)StepPlayer(1.0/60);double hover=volumeSlider.TrackThickness;
            volumeSlider.BeginContact();volumeSlider.EndContact();for(int i=0;i<4;i++)StepPlayer(1.0/60);if(volumeSlider.TrackThickness<hover+1||volumeSlider.Pressure<.6)throw new Exception("A short slider click has no separate pressure animation");Capture("preview-volume-press.png");
            volumeSlider.BeginContact();volumeSlider.SetPointerFraction(1.3);for(int i=0;i<20;i++)StepPlayer(1.0/60);if(volumeSlider.Value!=1||volumeSlider.ElasticOffset<2)throw new Exception("Slider overscroll changes logical bounds or has no elastic response");volumeSlider.EndContact();for(int i=0;i<50;i++)StepPlayer(1.0/60);if(Math.Abs(volumeSlider.ElasticOffset)>.03||volumeSlider.Pressure>.015)throw new Exception("Slider contact does not settle after release");
            volumeSlider.Value=0;for(int i=0;i<50;i++)StepPlayer(1.0/60);if(speakerSymbol.Level>.015)throw new Exception("Speaker symbol does not follow volume");
            volumeSlider.TestHover=false;volumeOpen=false;
            results.Add("PASS: Real routed down/up/click events within one frame retain a visible press; skip triangles animate independently and repeated clicks preserve their phase.");results.Add("PASS: Slider clicks have their own pressure, release rebound and bounded edge elasticity; speaker waves follow the value.");
        }
        void RunPressDemo(){
            prefs.ReduceMotion=false;countdown.Reset(300);timerFinished=false;DemoTrack(1,true,10);SwitchPage("Musik",false);Open();SetQuickBar(false);
            var next=mediaButtons.First(m=>m.Button.Content is TransportSymbol&&((TransportSymbol)m.Button.Content).Mode=="next");var previous=mediaButtons.First(m=>m.Button.Content is TransportSymbol&&((TransportSymbol)m.Button.Content).Mode=="previous");var play=mediaButtons.First(m=>m.Button.Content==playGlyph);var volume=mediaButtons.Last();
            Directory.CreateDirectory(System.IO.Path.Combine(root,"press-frames"));var trace=new List<string>{"frame,time,button_scale,phase,pressure,track_height,elastic"};int frame=0,eventIndex=0;var watch=System.Diagnostics.Stopwatch.StartNew();double last=0;
            double[] times={.65,1.0,1.45,1.62,2.15,2.8,3.25,3.7,4.2,4.35,4.9,5.5,6.0,6.25,6.7};
            Action[] actions={delegate{next.Target=1.13;},delegate{next.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{next.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{next.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{next.Target=1;previous.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{play.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));mediaPosition=CurrentMediaPosition();mediaUpdated=DateTime.UtcNow;isPlaying=false;playGlyph.Mode="play";},delegate{play.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));mediaUpdated=DateTime.UtcNow;isPlaying=true;playGlyph.Mode="pause";},delegate{volume.Button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));volumeSlider.TestHover=true;},delegate{volumeSlider.BeginContact();volumeSlider.Value=.35;},delegate{volumeSlider.EndContact();},delegate{volumeSlider.BeginContact();volumeSlider.SetPointerFraction(1.3);},delegate{volumeSlider.EndContact();},delegate{volumeSlider.BeginContact();volumeSlider.Value=.18;},delegate{volumeSlider.EndContact();},delegate{volumeSlider.TestHover=false;volumeOpen=false;}};
            var recorder=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};recorder.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(eventIndex<times.Length&&now>=times[eventIndex]){actions[eventIndex]();eventIndex++;}StepPlayer(Math.Min(.06,now-last));last=now;Capture("press-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+next.Value.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+((TransportSymbol)next.Button.Content).ActionPhase.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+volumeSlider.Pressure.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+volumeSlider.TrackThickness.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+volumeSlider.ElasticOffset.ToString("F4",System.Globalization.CultureInfo.InvariantCulture));frame++;if(now>=7.7){recorder.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"press-trace.csv"),trace);Close();}};recorder.Start();
        }
        void DemoTrack(int number,bool playing,double position){
            var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRoundedRectangle(new SolidColorBrush(number==1?Color.FromRgb(35,85,87):Color.FromRgb(127,42,72)),null,new Rect(0,0,140,140),20,20);dc.DrawEllipse(new SolidColorBrush(number==1?Color.FromRgb(157,244,208):Color.FromRgb(255,184,108)),null,new Point(70,70),45,45);dc.DrawEllipse(Brushes.Black,null,new Point(70,70),18,18);dc.DrawEllipse(Brushes.White,null,new Point(70,70),6,6);}
            var bmp=new RenderTargetBitmap(140,140,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));string art;using(var stream=new MemoryStream()){encoder.Save(stream);art=Convert.ToBase64String(stream.ToArray());}
            ReadMedia(serializer.Serialize(new Dictionary<string,object>{{"available",true},{"title",number==1?"Erster Titel · Medientest":"Zweiter Titel · Medientest"},{"artist","Iris Vorschau"},{"album","Eigene Testdaten"},{"source","Iris QA"},{"playing",playing},{"position",position},{"duration",120.0},{"canSeek",true},{"artChanged",true},{"thumbnail",art}}));
        }
        void RunPlayerDemo(){
            prefs.ReduceMotion=false;countdown.Reset(300);timerFinished=false;DemoTrack(1,true,10);SwitchPage("Musik",false);Open();SetQuickBar(false);
            Directory.CreateDirectory(System.IO.Path.Combine(root,"player-frames"));var trace=new List<string>{"frame,time,playing,cover_x,cover_scale,volume_reveal,lyric,open"};int frame=0,eventIndex=0;var watch=System.Diagnostics.Stopwatch.StartNew();double last=0;
            double[] times={1.0,1.35,1.55,2.1,2.25,3.5,4.35,5.2,6.0,6.5,7.1,7.8,8.3,10.1,12.0,13.2};
            Action[] actions={delegate{mediaButtons[2].Target=1.13;},delegate{mediaButtons[2].Target=.86;},delegate{mediaButtons[2].Target=1;},delegate{MediaAction("next");},delegate{DemoTrack(2,true,0);},delegate{mediaPosition=CurrentMediaPosition();mediaUpdated=DateTime.UtcNow;isPlaying=false;playGlyph.Mode="play";},delegate{mediaUpdated=DateTime.UtcNow;isPlaying=true;playGlyph.Mode="pause";},delegate{OpenVolume();volumeSlider.TestHover=true;},delegate{volumeSlider.Value=.25;},delegate{volumeSlider.Value=.82;},delegate{volumeOpen=false;volumeSlider.TestHover=false;mediaSeek.TestHover=true;},delegate{Seek(38);},delegate{mediaSeek.TestHover=false;mediaPosition=0;mediaUpdated=DateTime.UtcNow;var data=new LyricsData {Lines=LyricsService.Parse("[00:00.00]Diese Vorschau zeigt eigene Testzeilen\n[00:01.40]Die aktuelle Zeile gleitet weich nach oben\n[00:02.80]Auch ohne Maus läuft der Text weiter\n[00:04.20]Cover und Buttons reagieren auf Eingaben\n[00:05.60]Alles bleibt auf reinem Schwarz")};lyricCache[CurrentLyricsKey()]=data;if(!lyricsVisible)ToggleLyrics();else LoadLyrics();},delegate{CloseNotch(true);},delegate{Open();},delegate{if(lyricsVisible)ToggleLyrics();}};
            var recorder=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};recorder.Tick+=delegate{
                double now=watch.Elapsed.TotalSeconds;while(eventIndex<times.Length&&now>=times[eventIndex]){actions[eventIndex]();eventIndex++;}StepPlayer(Math.Min(.06,now-last));last=now;
                Capture("player-frames/frame-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+","+(isPlaying?1:0)+","+coverX.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+","+coverRest.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+","+volumeReveal.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+","+lyricIndex+","+(isOpen?1:0));frame++;
                if(now>=14.2){recorder.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"player-trace.csv"),trace);Close();}
            };recorder.Start();
        }
    }
}
