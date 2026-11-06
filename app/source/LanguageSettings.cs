using System;
namespace Iris {
    public sealed partial class NotchWindow {
        void BuildLanguageSettings(System.Windows.Controls.StackPanel panel){
            SettingsLabel(panel,L.T("Sprache"));
            var row=new System.Windows.Controls.Grid{Margin=new System.Windows.Thickness(13,8,13,8)};
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            foreach(string language in new[]{"de","en"}){
                var button=Btn(language=="de"?"Deutsch":"English",delegate{ChangeLanguage(language);});
                button.Height=31;
                button.Margin=new System.Windows.Thickness(3,0,3,0);
                button.IsEnabled=!settingsReadOnly;
                var selection=new UiSelectionMotion(button,L.Language==language);
                uiMotion.Register(selection);
                System.Windows.Automation.AutomationProperties.SetName(button,(language=="de"?"Deutsch":"English")+(L.Language==language?L.T(" · ausgewählt"):""));
                System.Windows.Controls.Grid.SetColumn(button,language=="de"?0:1);
                row.Children.Add(button);
            }
            SettingsGroup(panel,row,SettingsInfo(L.T("Oberflächensprache"),L.T("Iris startet nach dem Wechsel neu und öffnet diese Seite wieder.")));
        }
        void ChangeLanguage(string language){
            if(settingsReadOnly||language==L.Language)return;
            string previous=prefs.Language;
            prefs.Language=language;
            Save();
            if(settingsStorageNote.Length>0){
                prefs.Language=previous;
                System.Windows.MessageBox.Show(settings,settingsStorageNote,"Iris");
                return;
            }
            if(uiTesting)return;
            App.ReopenSettingsAfterRestart=true;
            Close();
        }
    }
}
