using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public sealed class FpsIndicator : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;readonly bool inline;double alpha,velocity;int? number;bool shown;public Action Hidden;
        public bool Showing{get{return shown;}}public double Reveal{get{return alpha;}}
        public FpsIndicator(UiMotionClock clock,bool inline){this.clock=clock;this.inline=inline;clock.Register(this);Width=inline?48:28;Height=inline?26:30;IsHitTestVisible=false;Visibility=Visibility.Collapsed;}
        public void Set(double? fps,bool awaiting=false){
            bool next=fps.HasValue||awaiting;
            int? rounded=fps.HasValue?(int?)Math.Round(fps.Value):null;
            if(next==shown&&(!next||number==rounded))return;
            if(next){
                number=rounded;Visibility=Visibility.Visible;
                System.Windows.Automation.AutomationProperties.SetName(this,number.HasValue?number+L.T(" Bilder pro Sekunde"):L.T("FPS-Messung wartet auf neue Frames"));
            }
            if(next!=shown){shown=next;clock.Wake(this);}
            InvalidateVisual();
        }
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref alpha,ref velocity,shown?1:0,dt,.23,1);else{alpha=shown?1:0;velocity=0;}InvalidateVisual();bool active=Math.Abs(alpha-(shown?1:0))>.001||Math.Abs(velocity)>.01;if(!active&&!shown){Visibility=Visibility.Collapsed;if(Hidden!=null)Hidden();}return active;}
        public void Snap(){Step(0,false);}
        protected override void OnRender(DrawingContext dc){if(alpha<.001)return;dc.PushOpacity(Math.Max(0,Math.Min(1,alpha)));var type=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;string value=number.HasValue?number.Value.ToString():"—";var digits=new FormattedText(inline?value+" FPS":value,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,type,inline?11:number>=1000?10:12,Brushes.White,dpi);dc.DrawText(digits,new Point((Width-digits.Width)/2,inline?(Height-digits.Height)/2:1));if(!inline){var caption=new FormattedText("FPS",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),8,Brushes.White,dpi);dc.DrawText(caption,new Point((Width-caption.Width)/2,17));}dc.Pop();}
    }
    public sealed partial class NotchWindow {
        GamingReader gamingReader;DispatcherTimer gamingPoll;FpsIndicator compactFps,headerFps,lyricFps;FrameworkElement lyricMusicNote;TextBlock gamingStatus;Button gamingGrant;
        GameTarget gamingTarget;bool gamingResolving,gamingVisible;int gamingGeneration,lastMeasuredPid;long lastMeasuredStart;
        DateTime gamingResolvedAt,lastGamingMeasurement;double? displayedGamingFps;string gamingState=L.T("FPS-Messung aus");
        void SetupGaming(){
            compactFps=new FpsIndicator(uiMotion,false){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(compactFps,0);compact.Children.Add(compactFps);compactFps.Hidden=RefreshGamingBattery;
            headerFps=new FpsIndicator(uiMotion,true){HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(105,0,0,0),VerticalAlignment=VerticalAlignment.Center};headerChrome.Children.Add(headerFps);
            var lyricRow=(Grid)compactLyrics.Child;lyricMusicNote=lyricRow.Children.OfType<DesignSymbol>().First();lyricFps=new FpsIndicator(uiMotion,false){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};lyricRow.Children.Add(lyricFps);lyricFps.Hidden=RefreshGamingLyricIcon;
            gamingPoll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};gamingPoll.Tick+=delegate{PollGaming();};if(prefs.GamingEnabled&&!uiTesting){gamingPoll.Start();PollGaming();}
        }
        void SetGamingEnabled(bool value){prefs.GamingEnabled=value;gamingGeneration++;gamingTarget=null;gamingResolvedAt=DateTime.MinValue;if(!value){gamingPoll.Stop();if(gamingReader!=null)gamingReader.Dispose();gamingReader=null;gamingState=L.T("FPS-Messung aus");SetGamingSample(null,null);}else{gamingState=L.T("Wartet auf ein erkanntes Spiel");if(!uiTesting){gamingPoll.Start();PollGaming();}}RefreshGamingStatus();Save();}
        void SetGamingMode(bool fullscreen,bool value){if(fullscreen)prefs.GamingFullscreen=value;else prefs.GamingWindowed=value;SetGamingSample(gamingTarget,gamingReader==null?null:gamingReader.Latest());Save();}
        void RefreshGamingBattery(){if(compactDownload!=null){RefreshPassiveSlots();return;}if(compactFps!=null&&compactBatteryHost!=null)compactBatteryHost.Visibility=BatteryVisible()&&compactFps.Reveal<=.001&&!compactFps.Showing?Visibility.Visible:Visibility.Collapsed;}
        void RefreshGamingLyricIcon(){lyricMusicNote.Visibility=lyricFps.Showing||lyricFps.Reveal>.001?Visibility.Collapsed:Visibility.Visible;}
        void SetGamingSample(GameTarget game,FpsSample sample){
            bool allowed=prefs.GamingEnabled&&game!=null&&(game.Fullscreen?prefs.GamingFullscreen:prefs.GamingWindowed);
            var now=DateTime.UtcNow;
            displayedGamingFps=allowed&&sample!=null&&sample.Pid==game.Pid&&sample.Fps>0?(double?)sample.Fps:null;
            if(displayedGamingFps.HasValue){
                lastMeasuredPid=game.Pid;lastMeasuredStart=game.Started;lastGamingMeasurement=now;
            }else if(!allowed||game.Pid!=lastMeasuredPid||game.Started!=lastMeasuredStart||sample!=null){
                lastGamingMeasurement=DateTime.MinValue;
            }
            bool awaiting=allowed&&sample==null&&now-lastGamingMeasurement<TimeSpan.FromSeconds(1);
            gamingVisible=displayedGamingFps.HasValue||awaiting;
            compactFps.Set(displayedGamingFps,awaiting);headerFps.Set(displayedGamingFps,awaiting);lyricFps.Set(displayedGamingFps,awaiting);
            RefreshGamingBattery();RefreshGamingLyricIcon();if(gamingVisible)ShowEyes(false);UpdateCompactLyrics();if(!preview&&!uiTesting)RefreshFullscreenVisibility();
        }
        int ForegroundGamePid(out bool fullscreen){fullscreen=false;IntPtr window=GetForegroundWindow();uint pid;GetWindowThreadProcessId(window,out pid);if(window==IntPtr.Zero||pid==(uint)Process.GetCurrentProcess().Id)return 0;RECT rect;var bounds=System.Windows.Forms.Screen.FromHandle(window).Bounds;if(GetWindowRect(window,out rect)&&(GetWindowLong(window,-16)&0x00C00000)==0)fullscreen=rect.Left<=bounds.Left&&rect.Top<=bounds.Top&&rect.Right>=bounds.Right&&rect.Bottom>=bounds.Bottom;return (int)pid;}
        void PollGaming(){
            if(uiTesting||shuttingDown||!prefs.GamingEnabled)return;bool full;int pid=ForegroundGamePid(out full);
            if(pid==0){gamingTarget=null;if(gamingReader!=null)gamingReader.Target(null);SetGamingSample(null,null);if(gamingReader==null)gamingState=L.T("Wartet auf ein erkanntes Spiel");RefreshGamingStatus();return;}
            if(gamingTarget!=null&&gamingTarget.Pid==pid){gamingTarget.Fullscreen=full;if(gamingReader!=null)gamingReader.Target(gamingTarget);SetGamingSample(gamingTarget,gamingReader==null?null:gamingReader.Latest());}
            else{gamingTarget=null;if(gamingReader!=null)gamingReader.Target(null);SetGamingSample(null,null);}
            if(!gamingResolving&&(DateTime.UtcNow-gamingResolvedAt).TotalSeconds>=1){gamingResolving=true;gamingResolvedAt=DateTime.UtcNow;int revision=gamingGeneration;var registered=prefs.GamingGames.ToArray();
                Task.Factory.StartNew(delegate{return GameCatalog.Resolve(pid,full,registered);}).ContinueWith(task=>Dispatcher.BeginInvoke(new Action(delegate{gamingResolving=false;if(shuttingDown||revision!=gamingGeneration||!prefs.GamingEnabled)return;bool currentFull;if(ForegroundGamePid(out currentFull)!=pid)return;if(!task.IsFaulted&&task.Result!=null)gamingTarget=task.Result;if(gamingTarget!=null){gamingTarget.Fullscreen=currentFull;if(gamingReader==null)StartGamingReader(false);if(gamingReader!=null)gamingReader.Target(gamingTarget);}SetGamingSample(gamingTarget,gamingReader==null?null:gamingReader.Latest());RefreshGamingStatus();})));}
            RefreshGamingStatus();
        }
        void StartGamingReader(bool elevate){
            if(uiTesting)return;if(gamingReader!=null)gamingReader.Dispose();gamingGeneration++;gamingState=L.T("FPS-Messung startet …");
            var current=new GamingReader(delegate(string state,bool permission){Dispatcher.BeginInvoke(new Action(delegate{if(shuttingDown)return;gamingState=state;RefreshGamingStatus();}));});gamingReader=current;current.Start(elevate);if(gamingTarget!=null)current.Target(gamingTarget);RefreshGamingStatus();
        }
        void RefreshGamingStatus(){
            if(gamingStatus!=null){string status=prefs.GamingEnabled?gamingReader==null?gamingState:gamingReader.Status:L.T("FPS-Messung aus");if(gamingVisible&&gamingTarget!=null)status=gamingTarget.Name+" · "+(gamingTarget.Fullscreen?L.T("Vollbild / randlos"):L.T("Fenstermodus"))+" · "+(displayedGamingFps.HasValue?Math.Round(displayedGamingFps.Value)+" FPS":L.T("Wartet auf neue Frames"));gamingStatus.Text=status.Length>200?status.Substring(0,197)+"…":status;}
            if(gamingGrant!=null)gamingGrant.IsEnabled=prefs.GamingEnabled&&!uiTesting;
        }
        void BuildGamingSettings(StackPanel panel){
            SettingsLabel(panel,L.T("Spiel-FPS"));SettingsGroup(panel,CompactAppearanceToggle(L.T("FPS anzeigen"),L.T("Nur beim aktiven erkannten Spiel"),prefs.GamingEnabled,SetGamingEnabled),CompactAppearanceToggle(L.T("Im Fenstermodus"),L.T("FPS links in der Notch anzeigen"),prefs.GamingWindowed,delegate(bool v){SetGamingMode(false,v);}),CompactAppearanceToggle(L.T("Im Vollbild / randlos"),L.T("FPS bei erkanntem Vollbildspiel anzeigen"),prefs.GamingFullscreen,delegate(bool v){SetGamingMode(true,v);}));
            SettingsLabel(panel,L.T("Messung"));gamingStatus=Text("",12,muted);gamingStatus.TextWrapping=TextWrapping.Wrap;gamingStatus.Margin=new Thickness(13,10,13,10);gamingStatus.MaxHeight=66;SettingsGroup(panel,gamingStatus);
            var actions=new Grid();actions.ColumnDefinitions.Add(new ColumnDefinition());actions.ColumnDefinitions.Add(new ColumnDefinition());gamingGrant=Btn(L.T("Windows-Freigabe"),delegate{StartGamingReader(true);});gamingGrant.ToolTip=L.T("Windows um Messrechte für den getrennten FPS-Helfer bitten");gamingGrant.Height=32;gamingGrant.Margin=new Thickness(0,0,4,0);actions.Children.Add(gamingGrant);var manage=Btn(L.T("Spiele verwalten"),ShowGamingGames);manage.Height=32;manage.Margin=new Thickness(4,0,0,0);Grid.SetColumn(manage,1);actions.Children.Add(manage);panel.Children.Add(actions);
            var note=Text(L.T("Steam-Spiele werden automatisch erkannt. Andere Spiele über ihre .exe hinzufügen. Ohne aktuelle Messwerte bleibt die Anzeige aus."),11,muted);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,12,0,12);panel.Children.Add(note);var limitation=Text(L.T("Randloses Vollbild wird unterstützt. Echtes exklusives Vollbild kann die Notch verdecken."),11,muted);limitation.TextWrapping=TextWrapping.Wrap;panel.Children.Add(limitation);RefreshGamingStatus();
        }
        void ShowGamingGames(){if(addonWindow!=null){addonWindow.Activate();return;}OpenAddonWindow(L.T("Gaming · Spiele"));RefreshGameSuggestions();}
        void RenderGamingGames(){
            var panel=AddonPanel();panel.Children.Add(AddonNote(L.T("Steam-Spiele werden automatisch erkannt. Wähle weitere laufende Spiele als Vorschlag oder füge ihre .exe manuell hinzu. Die Auswahl startet nichts.")));
            RenderGameSuggestions(panel);panel.Children.Add(AddonChoice(L.T("Spieldatei manuell auswählen …"),L.T("Programmdatei (.exe) auswählen"),delegate{var dialog=new Microsoft.Win32.OpenFileDialog{Filter=L.T("Spielprogramm|*.exe")};if(dialog.ShowDialog(addonWindow)==true){AddGamePath(dialog.FileName);RenderGamingGames();}}));
            panel.Children.Add(AddonNote(L.T("Deine zusätzlichen Spiele · anklicken zum Entfernen")));foreach(string entry in prefs.GamingGames.ToArray()){string path=entry;panel.Children.Add(AddonChoice(System.IO.Path.GetFileNameWithoutExtension(path),path+L.T("\nZuordnung entfernen"),delegate{prefs.GamingGames.Remove(path);gamingTarget=null;gamingResolvedAt=DateTime.MinValue;if(gamingReader!=null)gamingReader.Target(null);SetGamingSample(null,null);Save();RenderGamingGames();}));}ShowAddonDialogPage("gaming-games",AddonScroll(panel),1);
        }
        void RefreshFullscreenVisibility(){ApplyDesignContext(qaDesignFullscreen??IsFullscreen());}
    }
}
