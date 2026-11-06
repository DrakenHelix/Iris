using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Iris {
    public sealed class AddonRecordingHost : IAddonPlatformHost {
        public readonly List<string> Calls=new List<string>();public string Missing="";public AddonResult PlatformResult;
        public bool FileExists(string path){return path!=Missing;}public bool DirectoryExists(string path){return path!=Missing;}
        public void Launch(string target,string arguments,string directory){Calls.Add("launch:"+target+"|"+arguments);}
        public void Folder(string path){Calls.Add("folder:"+path);}public void Shortcut(string process,ushort[] keys){Calls.Add("keys:"+process+":"+string.Join(",",keys));}public void DiscordShare(){Calls.Add("discord:share");}
        public AddonEnvironmentSnapshot Inspect(System.Threading.CancellationToken token){var env=new AddonEnvironmentSnapshot{SpotifyConnected=true,DiscordTarget=@"C:\QA\Discord\Update.exe",ChatGptTarget="OpenAI.Codex_qa!App"};env.Protocols.UnionWith(new[]{"steam","codex","ms-settings"});env.Processes.UnionWith(new[]{"Discord","Codex","notepad","msedge","AppleMusic","obs64"});return env;}
        public AddonResult ExecutePlatform(AddonDefinition d,AddonAction a,AddonExecutionContext context,System.Threading.CancellationToken token){token.ThrowIfCancellationRequested();Calls.Add("platform:"+d.Operation+":"+context.Confirmed);return PlatformResult??AddonResult.State("QA-Aufzeichnung, nicht nativ ausgeführt","fixture");}
    }
    public sealed partial class NotchWindow {
        void PumpMenuQa(){Dispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));UpdateLayout();}
        void CaptureMenuQa(FrameworkElement surface,string name){
            surface.UpdateLayout();var bounds=VisualTreeHelper.GetDescendantBounds(surface);int width=(int)Math.Ceiling(Math.Max(surface.ActualWidth,bounds.Right)),height=(int)Math.Ceiling(Math.Max(surface.ActualHeight,bounds.Bottom));
            if(width<30||height<20)throw new Exception("Native popup did not render");
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(surface);
            var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
            for(int y=10;y<height-10;y++){int offset=(y*width+5)*4;if(pixels[offset]>110||pixels[offset+1]>110||pixels[offset+2]>110)throw new Exception("Light left menu gutter at row "+y);}
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(System.IO.Path.Combine(root,name)))encoder.Save(file);
        }
        void RunAddonMenuQa(){var results=new List<string>();try{
            addonDeck=AddonFixture();addonTestHost=new AddonRecordingHost();RefreshAddonTiles();uiMotion.PauseAutomatic();SetReducedMotion(true);SwitchPage("Add-ons",false);Open();SetQuickBar(true);UpdateLayout();
            foreach(double scale in new[]{1.0,1.5})foreach(bool occupied in new[]{true,false}){
                addonDeck=AddonFixture();if(!occupied)addonDeck.Slots.RemoveAll(s=>s.Position==0);RefreshAddonTiles();
                var menu=addonButtons[0].ContextMenu;menu.LayoutTransform=new System.Windows.Media.ScaleTransform(scale,scale);menu.PlacementTarget=addonButtons[0];menu.IsOpen=true;PumpMenuQa();uiMotion.SnapAll();menu.UpdateLayout();
                var clear=(MenuItem)menu.Items[1];var cancel=(MenuItem)menu.Items[2];var moves=(MenuItem)menu.Items[3];
                if(clear.IsEnabled!=occupied||cancel.IsEnabled)throw new Exception("Empty/bound menu availability changed");
                string tag=(occupied?"bound":"empty")+"-"+(int)(scale*100);
                CaptureMenuQa(menu,"preview-addon-menu-"+tag+".png");
                moves.IsSubmenuOpen=true;PumpMenuQa();uiMotion.SnapAll();
                var popup=moves.Template.FindName("PART_Popup",moves) as System.Windows.Controls.Primitives.Popup;
                if(popup==null||!popup.IsOpen||moves.Items.Count!=7||popup.Child==null)throw new Exception("Swap submenu is not a real seven-destination popup");
                CaptureMenuQa((FrameworkElement)popup.Child,"preview-addon-submenu-"+tag+".png");
                if(!((MenuItem)moves.Items[0]).IsVisible)throw new Exception("Submenu items invisible");
                moves.IsSubmenuOpen=false;menu.IsOpen=false;PumpMenuQa();
            }
            addonDeck=AddonFixture();RefreshAddonTiles();var active=addonButtons[0].ContextMenu;active.LayoutTransform=System.Windows.Media.Transform.Identity;active.IsOpen=true;PumpMenuQa();SetReducedMotion(false);uiMotion.PauseAutomatic();
            var edit=(MenuItem)active.Items[0];edit.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});AdvanceUi(3);
            var surface=(Border)edit.Template.FindName("MenuSurface",edit);var transform=(System.Windows.Media.ScaleTransform)surface.RenderTransform;if(transform.ScaleX>=1||transform.ScaleX<=.98)throw new Exception("Menu press has no intermediate feedback");CaptureMenuQa(active,"preview-addon-menu-press-mid.png");
            edit.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent});AdvanceUi(90);if(Math.Abs(transform.ScaleX-1)>.001)throw new Exception("Menu pressure did not settle");
            var swap=(MenuItem)active.Items[3];swap.IsSubmenuOpen=true;PumpMenuQa();var first=addonDeck.At(0);var second=addonDeck.At(1);((MenuItem)swap.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));if(addonDeck.At(1)!=first||addonDeck.At(0)!=second)throw new Exception("Swap command changed destination");swap.IsSubmenuOpen=false;active.IsOpen=false;PumpMenuQa();
            if(((AddonRecordingHost)addonTestHost).Calls.Count!=0)throw new Exception("Opening/swapping menus executed a foreign action");
            SetReducedMotion(true);uiMotion.SnapAll();AdvanceUi(100);if(uiMotion.ActiveCount!=0)throw new Exception("Menu clock remains active after close");
            results.Add("PASS: Native WPF menus for empty and occupied tiles at 100/150%; actual rendered left-edge pixels dark, no system icon gutter.");results.Add("PASS: Real seven-item swap submenu renders dark and invokes the correct local swap. Existing disabled states retained; no foreign action executed.");results.Add("PASS: Actual press intermediate rendered; pressure settles, reduced motion snaps and clock stops after closing.");results.Add("ALL ADD-ON MENU CHECKS PASSED");
        }catch(Exception ex){Environment.ExitCode=1;results.Add("FAILED: "+ex);}File.WriteAllLines(System.IO.Path.Combine(root,"addon-menu-test.txt"),results);Close();}
        void WaitAddonUi(){if(lastAddonUiTask==null)return;var job=lastAddonUiTask;var frame=new DispatcherFrame();var stop=DateTime.UtcNow.AddSeconds(4);var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=delegate{if(job.IsCompleted||DateTime.UtcNow>stop){timer.Stop();frame.Continue=false;}};timer.Start();if(!job.IsCompleted)Dispatcher.PushFrame(frame);timer.Stop();if(!job.IsCompleted)throw new Exception("Add-on UI completion timed out");job.GetAwaiter().GetResult();}
        AddonDeck AddonFixture(){var deck=new AddonDeck{Packages=AddonCatalog.Builtins().ToList()};
            string[] kinds={"discord-mute","discord-share","steam","screenshot","folders","program","hotkey","website"};string[] titles={"Mikrofon","Bildschirm","Mein Spiel","Screenshot","3 Ordner","Editor","Kürzel","Website"};
            for(int i=0;i<8;i++){string kind=kinds[i];var package=deck.Packages.First(p=>p.Actions.Any(a=>a.Kind==kind));var action=package.Actions.First(a=>a.Kind==kind).Copy();action.Title=titles[i];if(kind=="steam")action.Target="570";if(kind=="folders")action.Paths=new List<string>{@"C:\Beispiele\Projekt",@"C:\Beispiele\Videos",@"C:\Beispiele\Musik"};if(kind=="program"){action.Target=@"C:\Beispiele\Editor.exe";action.Arguments="--project \"C:\\Beispiele\\Mein Projekt\"";}if(kind=="hotkey"){action.Process="Discord";action.Shortcut="Ctrl+Shift+D";}if(kind=="website")action.Target="https://example.com/";deck.Put(new DeckSlot{Position=i,PackageId=package.Id,Action=action});}return deck;
        }
        void EndAddonQaWindow(){if(addonWindow==null)return;CloseAddonWindow();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));if(addonWindow!=null)throw new Exception("Add-on window did not close");}
        void VerifyAddons(List<string> results){
            var saved=addonDeck;bool readOnly=addonReadOnly;addonReadOnly=false;addonLoadError="";addonDeck=new AddonDeck();RefreshAddonTiles();uiMotion.PauseAutomatic();SetReducedMotion(true);SwitchPage("Add-ons",false);Open();SetQuickBar(true);UpdateLayout();
            if(nav.Children.Count!=6||addonButtons.Count!=8||addonButtons.Values.Any(b=>!Hits(b)))throw new Exception("Empty add-on grid or fifth quick bar tab is unreachable");Capture("preview-addons-empty.png");
            var fixture=AddonFixture();var config=System.IO.Path.Combine(root,dataFolder,"addon-roundtrip.json");AddonCatalog.Save(config,fixture);var loaded=AddonCatalog.Load(config);if(loaded.Slots.Count!=8||loaded.At(4).Action.Paths.Count!=3||loaded.At(5).Action.Arguments!=fixture.At(5).Action.Arguments)throw new Exception("Add-on config lost commands, slots or folders");
            foreach(var package in fixture.Packages)if(AddonCatalog.CheckPackage(package)!=null)throw new Exception("Built-in add-on does not validate");
            var shareable=AddonCatalog.ForExport(fixture.Packages.First(p=>p.Id=="iris.custom"),fixture);if(shareable.Actions.Count!=3||!shareable.Actions.Any(a=>a.Arguments==fixture.At(5).Action.Arguments)||AddonCatalog.CheckPackage(shareable)!=null)throw new Exception("Export loses configured actions or arguments");
            var imported=System.IO.Path.Combine(root,dataFolder,"fixture.iris-addon.json");var exported=fixture.Packages.First(p=>p.Id=="iris.discord");File.WriteAllText(imported,serializer.Serialize(exported));if(AddonCatalog.ReadPackage(imported).Actions.Count!=exported.Actions.Count)throw new Exception("Add-on package import failed");
            exported.Format=2;File.WriteAllText(imported,serializer.Serialize(exported));bool rejected=false;try{AddonCatalog.ReadPackage(imported);}catch(InvalidDataException){rejected=true;}exported.Format=1;if(!rejected)throw new Exception("Unsupported package format accepted");
            foreach(string kind in new[]{"shell","powershell","dll","remote-script"})if(AddonCatalog.Check(new AddonAction{Id="a",Title="a",Kind=kind},false)==null)throw new Exception("Unknown action kind accepted");
            var bad=fixture.At(7).Action.Copy();bad.Target="file:///C:/Windows/test.exe";if(AddonCatalog.Check(bad,true)==null)throw new Exception("Website action accepts local execution protocol");bad=fixture.At(2).Action.Copy();bad.Target="570/../../run";if(AddonCatalog.Check(bad,true)==null)throw new Exception("Steam app ID allows URI injection");
            ushort[] chord;foreach(string valid in new[]{"Ctrl+Shift+M","Strg+Alt+F9","Win+Shift+S"})if(!AddonRunner.TryChord(valid,out chord))throw new Exception("Valid chord rejected: "+valid);foreach(string invalid in new[]{"hello","Ctrl+Ctrl+A","A+B","Ctrl+Shift","Ctrl+{script}"})if(AddonRunner.TryChord(invalid,out chord))throw new Exception("Invalid chord accepted: "+invalid);
            var host=new AddonRecordingHost();using(var engine=new AddonActionEngine(host)){foreach(var slot in fixture.Slots){var package=fixture.Packages.First(p=>p.Id==slot.PackageId);var task=engine.ExecuteAsync(package,slot.Action,new AddonExecutionContext{BindingId=slot.BindingId,PackageId=slot.PackageId},System.Threading.CancellationToken.None);if(!task.Wait(4000)||!task.Result.Success)throw new Exception("Fixture engine dispatch failed");}}if(host.Calls.Count!=10||!host.Calls.Contains("launch:steam://rungameid/570|")||host.Calls.Count(c=>c=="platform:Discord:False")!=2||host.Calls.Contains("keys:Discord:17,16,77"))throw new Exception("Action routing lost target, shortcut, Steam ID, folders or arguments");
            int calls=host.Calls.Count;host.Missing=fixture.At(4).Action.Paths[2];rejected=false;try{AddonRunner.Run(fixture.At(4).Action,host);}catch(DirectoryNotFoundException){rejected=true;}if(!rejected||host.Calls.Count!=calls)throw new Exception("Missing folder causes partial launch before validation");host.Missing="";
            addonDeck=fixture;addonTestHost=host;RefreshAddonTiles();SetReducedMotion(false);UpdateLayout();Capture("preview-addons.png");
            foreach(var item in addonButtons){var rect=new Rect(item.Value.TranslatePoint(new Point(),canvas),item.Value.RenderSize);if(!Hits(item.Value)||rect.Left<previousPageButton.TranslatePoint(new Point(),canvas).X+previousPageButton.ActualWidth||rect.Right>nextPageButton.TranslatePoint(new Point(),canvas).X)throw new Exception("Tile overlaps navigation or is blocked by the quick bar");}
            calls=host.Calls.Count;addonButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitAddonUi();if(host.Calls.Count!=calls+1)throw new Exception("Tile click does not execute exactly once");AdvanceUi(4);UiButtonMotion tilePressure;if(addonMarks[0].Phase<=0||addonMarks[0].Phase>=.99||!buttonMotions.TryGetValue(addonButtons[0],out tilePressure)||tilePressure.Scale>=.999)throw new Exception("Tile action has no animated intermediate feedback/pressure");Capture("preview-addon-press.png");AdvanceUi(80);
            fixture.Packages.First(p=>p.Id=="iris.discord").Enabled=false;calls=host.Calls.Count;addonButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(host.Calls.Count!=calls)throw new Exception("Disabled add-on still executes");fixture.Packages.First(p=>p.Id=="iris.discord").Enabled=true;
            SwapAddonSlots(0,7);if(addonDeck.At(7).Action.Kind!="discord-mute"||addonDeck.At(0).Action.Kind!="website")throw new Exception("Swap lost action identity");SwapAddonSlots(0,7);
            SetReducedMotion(true);addonMarks[0].Set(0);if(addonMarks[0].Phase!=0)throw new Exception("Reduced-motion feedback still waits");ShowSettings();settingsSection="Add-ons";RenderSettingsSection();uiMotion.SnapAll();settings.UpdateLayout();if(settingsPages.Current.RenderSize.Height>settingsBody.ActualHeight+.5)throw new Exception("Add-on settings overflow");CaptureWindow(settings,"preview-addon-settings.png");
            ShowAddonLibrary();uiMotion.SnapAll();addonWindow.UpdateLayout();CaptureWindow(addonWindow,"preview-addon-library.png");EndAddonQaWindow();
            foreach(var slot in fixture.Slots){ShowAddonEditor(slot.Position);uiMotion.SnapAll();addonWindow.UpdateLayout();if(addonDialogPages.Current.RenderSize.Height>addonWindow.ActualHeight-70)throw new Exception("Add-on editor exceeds dialog");if(slot.Position==4)CaptureWindow(addonWindow,"preview-addon-folders.png");if(slot.Position==5)CaptureWindow(addonWindow,"preview-addon-program.png");if(slot.Position==6)CaptureWindow(addonWindow,"preview-addon-shortcut.png");EndAddonQaWindow();}
            ShowAddonEditor(2);uiMotion.SnapAll();addonTargetBox.Text="bad";addonSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(string.IsNullOrEmpty(addonEditorError.Text)||addonDeck.At(2).Action.Target!="570")throw new Exception("Invalid edit overwrites saved action");addonTargetBox.Text="730";addonNameBox.Text="Mein Spiel";addonSaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));if(addonWindow!=null||addonDeck.At(2).Action.Target!="730"||AddonCatalog.Load(AddonPath).At(2).Action.Target!="730")throw new Exception("Editor did not save real deck state");
            addonDeck=new AddonDeck();ClearAddonSlot(0);ShowAddonEditor(0);uiMotion.SnapAll();if(addonDialogPages.Current==null)throw new Exception("Empty slot has no provider chooser");CaptureWindow(addonWindow,"preview-addon-choose.png");EndAddonQaWindow();
            settings.Close();uiMotion.SnapAll();Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(delegate{}));
            addonDeck=saved;addonReadOnly=readOnly;addonTestHost=null;foreach(int i in Enumerable.Range(0,8))ResetAddonFeedback(i);RefreshAddonTiles();SetReducedMotion(false);AdvanceUi(100);uiMotion.SnapAll();if(uiMotion.ActiveCount!=0)throw new Exception("Add-on motion does not stop at rest");
            results.Add("PASS: Eight 4x2 tiles and fifth navigation entry are hit-testable with quick bar open; native editor/settings/library render, validate, persist and close correctly.");
            results.Add("PASS: Optional packages import declaratively, reject unsupported formats/kinds and injection, retain arguments/folders; disabled packages do not execute; shortcut/Steam/Discord/Windows routing verified with recording host only, no external app commands or microphone.");
            results.Add("PASS: Tile feedback has rendered spring intermediates, reduced-motion snapping and idle clock; swap and invalid edits preserve actions.");
        }
        void RunAddonDemo(){
            addonDeck=AddonFixture();addonTestHost=new AddonRecordingHost();RefreshAddonTiles();uiMotion.PauseAutomatic();SetReducedMotion(true);SwitchPage("Add-ons",false);Open();SetQuickBar(true);SetReducedMotion(false);
            Directory.CreateDirectory(System.IO.Path.Combine(root,"addon-frames"));var trace=new List<string>{"frame,seconds,dialog"};int index=0,frame=0;double previous=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            double[] times={.6,1.3,2,2.8,3.6,4.9,5.5,6.2};Action[] actions={delegate{addonButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{addonButtons[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));},delegate{SwitchPage("Mixer",true);},delegate{SwitchPage("Add-ons",true);},delegate{ShowAddonEditor(4);},delegate{CloseAddonWindow();},delegate{ShowAddonLibrary();},delegate{CloseAddonWindow();}};
            var timer=new DispatcherTimer(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};timer.Tick+=delegate{double now=watch.Elapsed.TotalSeconds;while(index<times.Length&&now>=times[index])actions[index++]();uiMotion.Advance(Math.Min(.06,now-previous));StepPlayer(Math.Min(.06,now-previous));previous=now;Capture("addon-frames/main-"+frame.ToString("0000")+".png");bool show=addonWindow!=null&&addonWindow.IsVisible;if(show)CaptureWindow(addonWindow,"addon-frames/dialog-"+frame.ToString("0000")+".png");trace.Add(frame+","+now.ToString("F4",CultureInfo.InvariantCulture)+","+(show?1:0));frame++;if(now>=7){timer.Stop();File.WriteAllLines(System.IO.Path.Combine(root,"addon-trace.csv"),trace);Close();}};timer.Start();
        }
    }
}
