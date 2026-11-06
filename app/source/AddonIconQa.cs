using System;using System.Collections.Generic;using System.Linq;using System.Windows;using System.Windows.Media;
namespace Iris {
    public sealed partial class NotchWindow {
        void VerifyActionIcons0211(List<string> results){
            var definitions=AddonRegistry.Definitions.ToArray();if(definitions.Length!=AddonRegistry.Definitions.Count()||!new HashSet<string>(definitions.Select(d=>d.Kind)).SetEquals(ActionIconArtwork.Kinds))throw new Exception("Registry actions and dedicated artwork differ");
            foreach(var d in definitions){var drawing=ActionIconArtwork.Action(d.Kind,"");var bounds=drawing.Bounds;if(!drawing.IsFrozen||bounds.IsEmpty||bounds.Left<-.01||bounds.Top<-.01||bounds.Right>32.01||bounds.Bottom>20.01)throw new Exception("Icon is mutable, absent or clipped: "+d.Kind+" "+bounds);}
            var previous=addonDeck;uiMotion.PauseAutomatic();SetReducedMotion(true);ReadMedia("{\"available\":false}");CloseNotch(true);SwitchPage("Add-ons",false);Open();SetQuickBar(true);addonDeck=new AddonDeck{Packages=AddonCatalog.Builtins().ToList()};
            for(int page=0;page<(definitions.Length+7)/8;page++){addonDeck.Slots.Clear();for(int i=0;i<8&&page*8+i<definitions.Length;i++){var d=definitions[page*8+i];addonDeck.Put(new DeckSlot{Position=i,PackageId=d.PackageId,Action=d.Template()});}RefreshAddonTiles();UpdateLayout();foreach(var b in addonButtons.Values)if(!Hits(b))throw new Exception("Icon intercepts a tile or quick bar hit target");Capture("preview-action-icons-"+(page+1).ToString("00")+".png");}
            var original=ActionIconArtwork.Action("discord-mute","");var custom=ActionIconArtwork.Action("discord-mute","\uE734");if(object.ReferenceEquals(original,custom)||custom.Bounds.IsEmpty)throw new Exception("Legacy user glyph override lost");
            SetNotchSize(150);UpdateLayout();Capture("preview-action-icons-scale-150.png");SetNotchSize(0);addonDeck=previous;RefreshAddonTiles();SetReducedMotion(false);uiMotion.SnapAll();
            results.Add("PASS: All registered actions have frozen artwork within the icon canvas. All native tile pages render with unobstructed hit targets and 150% scaling; previous custom-symbol overrides remain supported. No actions executed.");
        }
        void RunActionIconQaOnly(){var results=new List<string>();try{VerifyActionIcons0211(results);results.Add("ALL ACTION ICON UI CHECKS PASSED");}catch(Exception ex){Environment.ExitCode=1;results.Add("FAILED: "+ex);}System.IO.File.WriteAllLines(System.IO.Path.Combine(root,"action-icon-ui-test.txt"),results);Close();}
    }
}
