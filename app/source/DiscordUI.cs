using System;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Windows;using System.Windows.Controls;
namespace Iris {
    public sealed partial class NotchWindow {
        CancellationTokenSource discordPickerCancellation;int discordPickerRevision;Task<DiscordNavigationView> discordPickerTask;DiscordReply discordPickerFixture;
        void StopDiscordPicker(){discordPickerRevision++;if(discordPickerCancellation!=null){discordPickerCancellation.Cancel();discordPickerCancellation.Dispose();discordPickerCancellation=null;}}
        void AddDiscordChannelField(StackPanel panel,int position,AddonPackage package,AddonAction draft){
            addonTargetBox=new TextBox{Text=draft.Target??"",Visibility=Visibility.Collapsed};panel.Children.Add(addonTargetBox);
            var choose=AddonChoice(string.IsNullOrWhiteSpace(draft.Target)?L.T("Kanal aus Discord auswählen"):draft.Title==L.T("Server oder Kanal öffnen")?L.T("Gespeicherten Discord-Kanal ändern"):draft.Title,L.T("Geöffneten Kanal übernehmen · kein Link nötig"),delegate{draft.Title=addonNameBox.Text.Trim();draft.Target=addonTargetBox.Text;ShowDiscordChannelPicker(position,package,draft);});DecorateAddonChoice(choose,"discord-channel",null);panel.Children.Add(choose);panel.Children.Add(AddonNote(L.T("Öffnet den gewählten Kanal später in der Discord-Desktop-App. Auswählen und Speichern führt nichts aus.")));
        }
    }
}
