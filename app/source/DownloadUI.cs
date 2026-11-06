using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Windows;using System.Windows.Controls;using System.Windows.Media;using System.Windows.Threading;using Microsoft.Win32;
namespace Iris {
    public sealed class DownloadIndicator : FrameworkElement,IUiMotion {
        readonly UiMotionClock clock;
        readonly Typeface face;
        readonly Brush foreground;
        readonly Pen arrowPen;
        FormattedText cachedLabel;
        string cachedText;
        double cachedDpi;
        bool active,arrowOnly;
        double? fraction;
        double phase,alpha,velocity;
        public Action Hidden;
        public bool Active{get{return active;}}
        public bool Present{get{return active||alpha>.001;}}

        public DownloadIndicator(UiMotionClock clock,TextBlock clockLabel){
            this.clock=clock;
            face=new Typeface(clockLabel.FontFamily,clockLabel.FontStyle,clockLabel.FontWeight,clockLabel.FontStretch);
            foreground=clockLabel.Foreground;
            arrowPen=new Pen(foreground,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            Width=52;Height=28;IsHitTestVisible=false;Visibility=Visibility.Collapsed;
            clock.Register(this);
            IsVisibleChanged+=delegate{if(IsVisible)clock.Wake(this);};
        }
        public void Set(DownloadSnapshot snapshot){
            bool next=snapshot!=null&&snapshot.Active;
            double? value=next?snapshot.Fraction:fraction;
            bool nextArrowOnly=next?!value.HasValue&&snapshot.Items.All(item=>item.Source=="Steam"):arrowOnly;
            if(next==active&&value==fraction&&nextArrowOnly==arrowOnly)return;
            active=next;fraction=value;arrowOnly=nextArrowOnly;
            if(active)Visibility=Visibility.Visible;
            clock.Wake(this);
            InvalidateVisual();
        }
        public bool Step(double dt,bool animate){if(!IsVisible&&active)return false;if(animate){PlayerSpring.Step(ref alpha,ref velocity,active?1:0,dt,.24,1);if(active)phase=(phase+dt*.8)%1;}else{alpha=active?1:0;velocity=0;phase=0;}if(!active&&alpha<.001&&Visibility!=Visibility.Collapsed){Visibility=Visibility.Collapsed;if(Hidden!=null)Hidden();}InvalidateVisual();return animate&&(active||Math.Abs(alpha)>.001||Math.Abs(velocity)>.01);}
        public void Snap(){Step(0,false);}
        protected override void OnRender(DrawingContext dc){
            if(alpha<.001)return;
            string text=fraction.HasValue?Math.Min(99,(int)Math.Floor(fraction.Value*100+1e-9))+"%":"…";
            var label=arrowOnly?null:Label(text);
            const double iconWidth=10,gap=3.5;
            double labelWidth=label==null?0:gap+label.Width;
            double x=(ActualWidth-labelWidth)/2,y=ActualHeight/2;
            double tip=y+.5+1.1*(1-Math.Cos(2*Math.PI*phase));
            dc.PushOpacity(Math.Max(0,Math.Min(1,alpha)));
            dc.DrawLine(arrowPen,new Point(x,tip-7),new Point(x,tip));
            dc.DrawLine(arrowPen,new Point(x-3,tip-3),new Point(x,tip));
            dc.DrawLine(arrowPen,new Point(x,tip),new Point(x+3,tip-3));
            dc.DrawLine(arrowPen,new Point(x-4,y+5.25),new Point(x+4,y+5.25));
            if(label!=null)dc.DrawText(label,new Point(x+iconWidth/2+gap,(ActualHeight-label.Height)/2));
            dc.Pop();
        }
        FormattedText Label(string text){double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;if(cachedLabel==null||cachedText!=text||cachedDpi!=dpi){cachedText=text;cachedDpi=dpi;cachedLabel=new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,face,13,foreground,dpi);}return cachedLabel;}
    }
    public sealed partial class NotchWindow {
        DownloadWatcher downloadWatcher;DownloadSnapshot downloads=new DownloadSnapshot();DownloadIndicator compactDownload,headerDownload,lyricDownload;DispatcherTimer downloadRefresh;string[] passivePlacement=new string[2];TextBlock downloadSettingsStatus;int passiveKey=-1;
        bool HasDownloads(){return prefs.DownloadsEnabled&&downloads.Active;}
        void SetupDownloads(){compactDownload=new DownloadIndicator(uiMotion,clock){HorizontalAlignment=HorizontalAlignment.Center};compactDownload.Hidden=RefreshPassiveSlots;Grid.SetColumn(compactDownload,2);compact.Children.Add(compactDownload);headerDownload=new DownloadIndicator(uiMotion,clock){HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};headerChrome.Children.Add(headerDownload);var lyrics=(Grid)compactLyrics.Child;lyrics.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});lyricDownload=new DownloadIndicator(uiMotion,clock);Grid.SetColumn(lyricDownload,2);lyrics.Children.Add(lyricDownload);downloadRefresh=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};downloadRefresh.Tick+=delegate{if(downloadWatcher!=null)ApplyDownloads(downloadWatcher.Latest());};RestartDownloads();RefreshInstalledDownloadExtension();}
        void RestartDownloads(){if(downloadWatcher!=null){downloadWatcher.Dispose();downloadWatcher=null;}if(downloadRefresh!=null)downloadRefresh.Stop();if(prefs.DownloadsEnabled&&!uiTesting){downloadWatcher=new DownloadWatcher(Path.Combine(root,dataFolder),prefs.BrowserDownloads,prefs.SteamDownloads);downloadRefresh.Start();}ApplyDownloads(new DownloadSnapshot());}
        void DisposeDownloads(){if(downloadRefresh!=null)downloadRefresh.Stop();if(downloadWatcher!=null)downloadWatcher.Dispose();downloadWatcher=null;}
        void ApplyDownloads(DownloadSnapshot value){
            bool before=HasDownloads();
            downloads=value??new DownloadSnapshot();
            bool show=HasDownloads();
            if(compactDownload==null)return;
            DownloadSnapshot indicator=show?downloads:null;
            if(show&&downloads.Items.Any(item=>item.Source=="Steam")){
                var chrome=downloads.Items.Where(item=>item.Source=="Chrome").ToArray();
                if(chrome.Length>0)indicator=new DownloadSnapshot{Items=chrome};
            }
            compactDownload.Set(indicator);headerDownload.Set(indicator);lyricDownload.Set(indicator);
            ((Grid)compactLyrics.Child).ColumnDefinitions[2].Width=new GridLength(show?52:0);
            RefreshPassiveSlots();
            if(before!=show){if(show)ShowEyes(false);UpdateCompactLyrics();}
            if(downloadSettingsStatus!=null)downloadSettingsStatus.Text=show?downloads.Description:(downloadWatcher==null?L.T("Keine frischen aktiven Downloads gemeldet."):downloadWatcher.BrowserStatus+L.T("\nKeine frischen aktiven Downloads gemeldet."));
        }
        void RefreshPassiveSlots(){if(compactDownload==null)return;bool fpsPresent=gamingVisible||compactFps!=null&&(compactFps.Showing||compactFps.Reveal>.001);bool wide=HasDownloads()||compactDownload.Present;int key=(fpsPresent?1:0)+(wide?2:0)+(mediaAvailable&&isPlaying?4:0)+(BatteryVisible()?8:0)+(page=="Musik"?16:0)+(wide&&countdown.Running&&countdown.Remaining>=3600?32:0);if(key==passiveKey){PaintPassiveVisibility();return;}passiveKey=key;var active=new List<string>();if(fpsPresent)active.Add("fps");if(wide)active.Add("download");if(mediaAvailable&&isPlaying)active.Add("music");if(BatteryVisible())active.Add("battery");passivePlacement=PassiveSlots.Select(active,passivePlacement);
            compact.Width=wide&&countdown.Running&&countdown.Remaining>=3600?186:154;Canvas.SetLeft(compact,(SceneWidth-compact.Width)/2);compact.ColumnDefinitions[0].Width=new GridLength(wide?52:28);compact.ColumnDefinitions[2].Width=new GridLength(wide?52:28);UpdateContentClip();
            if(compactFps!=null&&gamingVisible)Grid.SetColumn(compactFps,Array.IndexOf(passivePlacement,"fps")==1?2:0);
            if(HasDownloads())Grid.SetColumn(compactDownload,Array.IndexOf(passivePlacement,"download")==0?0:2);
            bool musicVisible=Array.IndexOf(passivePlacement,"music")>=0;if(compactPulse!=null&&musicVisible)Grid.SetColumn(compactPulse,Array.IndexOf(passivePlacement,"music")==0?0:2);
            if(compactBatteryHost!=null){int place=Array.IndexOf(passivePlacement,"battery");if(place>=0)Grid.SetColumn(compactBatteryHost,place==0?0:2);}PaintPassiveVisibility();
        }
        void PaintPassiveVisibility(){if(compactPulse!=null)compactPulse.Visibility=mediaAvailable&&isPlaying&&Array.IndexOf(passivePlacement,"music")>=0?Visibility.Visible:Visibility.Collapsed;if(openPulse!=null)openPulse.Visibility=mediaAvailable&&isPlaying&&page=="Musik"&&!HasDownloads()?Visibility.Visible:Visibility.Collapsed;if(compactBatteryHost!=null)compactBatteryHost.Visibility=Array.IndexOf(passivePlacement,"battery")>=0?Visibility.Visible:Visibility.Collapsed;if(headerBatteryHost!=null)headerBatteryHost.Visibility=BatteryVisible()&&!HasDownloads()?Visibility.Visible:Visibility.Collapsed;}
        void BuildDownloadSettings(StackPanel panel){
            SettingsLabel(panel,L.T("Fortschritt neben der Uhr"));SettingsGroup(panel,CompactAppearanceToggle(L.T("Downloads anzeigen"),L.T("Zum Beispiel: ↓ 45 %"),prefs.DownloadsEnabled,v=>{prefs.DownloadsEnabled=v;RestartDownloads();Save();}));
            SettingsLabel(panel,"Steam");SettingsGroup(panel,CompactAppearanceToggle(L.T("Steam-Downloads"),L.T("Automatisch · keine zusätzliche Installation nötig"),prefs.SteamDownloads,v=>{prefs.SteamDownloads=v;RestartDownloads();Save();}));
            SettingsLabel(panel,"Browser");SettingsGroup(panel,CompactAppearanceToggle(L.T("Browser-Downloads"),L.T("Chrome-Prozente im Hintergrund nach einmaliger Einrichtung"),prefs.BrowserDownloads,v=>{prefs.BrowserDownloads=v;RestartDownloads();Save();}));
            var setup=Btn(L.T("Chrome einmal verbinden …"),ShowDownloadSetup);setup.Height=34;setup.Margin=new Thickness(0,6,0,10);panel.Children.Add(setup);
            downloadSettingsStatus=Text(HasDownloads()?downloads.Description:L.T("Gerade kein Download erkannt."),11,muted);downloadSettingsStatus.TextWrapping=TextWrapping.Wrap;downloadSettingsStatus.Margin=new Thickness(0,0,0,8);panel.Children.Add(downloadSettingsStatus);
            panel.Children.Add(AddonNote(L.T("Ein Spiel läuft? Dann siehst du FPS und Download. Musik läuft weiter; nur ihr Puls-Symbol macht bei wenig Platz Platz.")));
            var help=Btn(L.T("Warum manchmal keine Prozentzahl?"),delegate{ShowIntegrationResult(L.T("Steam-Downloads"),L.T("Chrome meldet nach der einmaligen Verbindung die aktuellen Zähler im Hintergrund. Wenn die Gesamtgröße fehlt, zeigt Iris den Pfeil mit Auslassungspunkten. Andere Browser können über ihre lokalen Profile erkannt werden. Bei Steam wird zusätzlich die lesbare Downloads-Anzeige ausgewertet. Wenn Steam keine aktuellen Zahlen zugänglich macht, bleibt der Pfeil ohne Prozentzahl. Die Steam-Downloads-Seite zu öffnen kann helfen. Alte Zwischenstände werden nicht hochgerechnet."));});help.Height=28;panel.Children.Add(help);
        }
    }
}
