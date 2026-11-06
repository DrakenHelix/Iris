using System;using System.Windows;using System.Windows.Controls;
namespace Iris {
    public sealed partial class NotchWindow {
        void DecorateAddonChoice(Button button,string kind,string package){
            var body=button.Content as FrameworkElement;if(body==null)return;button.Content=null;
            button.Content=AddonIconRow(body,kind,package);
        }
        FrameworkElement AddonActionNote(AddonAction action,string text){return AddonIconRow(AddonNote(text),action.Kind,null);}
        FrameworkElement AddonIconRow(UIElement body,string kind,string package){
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42)});row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon=new AddonActionIcon{HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};if(package!=null)icon.SetPackage(package);else icon.Set(kind,"");
            row.Children.Add(icon);Grid.SetColumn(body,1);row.Children.Add(body);return row;
        }
    }
}
