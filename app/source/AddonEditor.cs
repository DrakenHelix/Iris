using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Iris {
    public sealed partial class NotchWindow {
        TextBox addonNameBox,addonTargetBox,addonArgumentsBox,addonProcessBox,addonShortcutBox,addonPathsBox;TextBlock addonEditorError;Button addonSaveButton;
        void ShowAddonEditor(int position){
            if(addonWindow!=null){addonWindow.Activate();return;}OpenAddonWindow(L.T("Kachel ")+(position+1));
            if(addonReadOnly){var panel=AddonPanel();panel.Children.Add(AddonNote(addonLoadError));ShowAddonDialogPage("error",panel,0);return;}
            var slot=addonDeck.At(position);if(slot==null)ChooseAddonProvider(position);else EditAddonAction(position,addonDeck.Packages.First(p=>p.Id==slot.PackageId),slot.Action.Copy());
        }
        void ChooseAddonProvider(int position){ChooseAddonProvider021(position);}
        void ChooseAddonAction(int position,AddonPackage package){ChooseAddonAction021(position,package);}
        TextBox AddonField(StackPanel panel,string title,string value,bool multiline){var text=Text(title,11,muted);text.Margin=new Thickness(0,0,0,4);panel.Children.Add(text);var box=new TextBox{Text=value??"",Height=multiline?80:32,AcceptsReturn=multiline,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,Margin=new Thickness(0,0,0,10),MaxLength=multiline?17000:4096,Background=new SolidColorBrush(Color.FromRgb(39,39,41))};new UiFocusMotion(box,uiMotion);panel.Children.Add(box);return box;}
        void EditAddonAction(int position,AddonPackage package,AddonAction draft){
            if(draft.Kind=="steam"&&string.IsNullOrWhiteSpace(draft.Target)){ShowSteamGamePicker(position,package,draft);return;}
            if(draft.Kind=="discord-channel"&&string.IsNullOrWhiteSpace(draft.Target)){ShowDiscordChannelPicker(position,package,draft);return;}
            addonTargetBox=addonArgumentsBox=addonProcessBox=addonShortcutBox=addonPathsBox=null;var panel=AddonPanel();
            var back=AddonChoice(L.T("‹ Andere Aktion"),AddonCatalog.DisplayName(package),delegate{ChooseAddonAction(position,package);});DecorateAddonChoice(back,draft.Kind,null);panel.Children.Add(back);addonNameBox=AddonField(panel,L.T("Name auf der Kachel"),AddonCatalog.DisplayTitle(draft),false);addonNameBox.MaxLength=48;AddAddonIconPicker(panel,draft);addonDependencyRefresh=null;
            if(AddonRegistry.Find(draft.Kind).Configuration==AddonConfiguration.Text){addonTargetBox=AddonField(panel,AddonRegistry.Find(draft.Kind).InputLabel,draft.Target,false);panel.Children.Add(AddonNote(AddonRegistry.Find(draft.Kind).Description));}
            else if(draft.Kind=="steam"){
                AddSteamGameField(panel,position,package,draft);
            }else if(draft.Kind=="folders"){
                addonPathsBox=AddonField(panel,L.T("Ordner · ein vollständiger Pfad je Zeile"),string.Join(Environment.NewLine,draft.Paths),true);var browse=Btn(L.T("Ordner hinzufügen …"),delegate{using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=L.T("Ordner für diese Kachel auswählen")}){if(dialog.ShowDialog(new AddonDialogOwner(addonWindow))==System.Windows.Forms.DialogResult.OK){var paths=addonPathsBox.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).ToList();if(paths.Count>=8){addonEditorError.Text=L.T("Bis zu acht Ordner pro Kachel.");return;}paths.Add(dialog.SelectedPath);addonPathsBox.Text=string.Join(Environment.NewLine,paths);}}});browse.Height=28;panel.Children.Add(browse);
            }else if(draft.Kind=="program"){
                var target=AddonField(panel,L.T("Programmdatei (.exe)"),draft.Target,false);addonTargetBox=target;var browse=Btn(L.T("Programm auswählen …"),delegate{var dialog=new Microsoft.Win32.OpenFileDialog{Filter=L.T("Programme|*.exe")};if(dialog.ShowDialog(addonWindow)==true)target.Text=dialog.FileName;});browse.Height=28;browse.Margin=new Thickness(0,-4,0,9);panel.Children.Add(browse);addonArgumentsBox=AddonField(panel,L.T("Argumente · optional"),draft.Arguments,false);
            }else if(draft.Kind=="discord-channel")AddDiscordChannelField(panel,position,package,draft);
            else if(draft.Kind=="website")addonTargetBox=AddonField(panel,"Website · https://…",draft.Target,false);
            else if(draft.Kind=="hotkey"||AddonRegistry.Find(draft.Kind).Configuration==AddonConfiguration.AppShortcut){
                addonProcessBox=AddonField(panel,L.T("Prozessname ohne .exe · z. B. Discord"),draft.Process,false);addonShortcutBox=AddonField(panel,L.T("Tastenkürzel · z. B. Ctrl+Shift+M"),draft.Shortcut,false);panel.Children.Add(AddonNote(L.T("Iris holt dieses Programm nach vorn. Nur bei bestätigtem Fokus wird das Kürzel gesendet.")));
            }else if(AddonRegistry.Find(draft.Kind).Configuration==AddonConfiguration.Process){addonProcessBox=AddonField(panel,L.T("Prozessname ohne .exe"),draft.Process,false);panel.Children.Add(AddonNote(AddonRegistry.Find(draft.Kind).Description));}
            else if(AddonRegistry.Find(draft.Kind).Extended)panel.Children.Add(AddonNote(AddonRegistry.Find(draft.Kind).Description+"\n"+AddonRegistry.Availability(draft,addonEnvironment).Message));
            else panel.Children.Add(AddonNote(AddonRegistry.Find(draft.Kind).Description));
            addonEditorError=Text("",11,new SolidColorBrush(Color.FromRgb(255,136,145)));addonEditorError.TextWrapping=TextWrapping.Wrap;addonEditorError.Margin=new Thickness(0,7,0,7);panel.Children.Add(addonEditorError);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};bool enabled=addonDeck.Packages.Any(p=>p.Id==package.Id&&p.Enabled);addonSaveButton=Btn(enabled?L.T("Speichern"):addonDeck.Packages.Any(p=>p.Id==package.Id)?L.T("Speichern & aktivieren"):L.T("Speichern & hinzufügen"),delegate{SaveAddonDraft(position,package,draft);});addonSaveButton.Background=new SolidColorBrush(Color.FromRgb(74,69,110));addonSaveButton.Height=32;buttons.Children.Add(addonSaveButton);var clear=Btn(L.T("Leeren"),delegate{ClearAddonSlot(position);CloseAddonWindow();});clear.Margin=new Thickness(8,0,0,0);buttons.Children.Add(clear);panel.Children.Add(buttons);
            ShowAddonDialogPage("edit",AddonScroll(panel),1);
        }
        void SaveAddonDraft(int position,AddonPackage package,AddonAction draft){
            draft.Title=addonNameBox.Text.Trim();if(addonTargetBox!=null)draft.Target=addonTargetBox.Text.Trim();if(addonArgumentsBox!=null)draft.Arguments=addonArgumentsBox.Text;if(addonProcessBox!=null)draft.Process=addonProcessBox.Text.Trim();if(addonShortcutBox!=null)draft.Shortcut=addonShortcutBox.Text.Trim();if(addonPathsBox!=null)draft.Paths=addonPathsBox.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(p=>p.Trim()).Where(p=>p.Length>0).ToList();
            string error=AddonCatalog.Check(draft,true);if(error!=null){addonEditorError.Text=error;return;}if(!addonDeck.Packages.Any(p=>p.Id==package.Id)){if(addonDeck.Packages.Count>=40){addonEditorError.Text=L.T("Höchstens 40 Add-ons möglich.");return;}addonDeck.Packages.Add(package);}package.Enabled=true;
            var previous=addonDeck.At(position);CancelAddonAt(position);var binding=new DeckSlot{Position=position,PackageId=package.Id,Action=draft.Copy()};if(previous!=null){binding.BindingId=previous.BindingId;binding.ProfileId=previous.ProfileId;}addonDeck.Put(binding);if(!SaveAddons()){addonEditorError.Text=addonLoadError;return;}ResetAddonFeedback(position);RefreshAddonTiles();RefreshAddonSettings();CloseAddonWindow();
        }
        sealed class AddonDialogOwner : System.Windows.Forms.IWin32Window {readonly Window window;public AddonDialogOwner(Window window){this.window=window;}public IntPtr Handle{get{return new System.Windows.Interop.WindowInteropHelper(window).Handle;}}}
    }
}
