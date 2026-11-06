using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Iris {
    public sealed class SettingsPageMotion : Grid,IUiMotion {
        sealed class Page {
            public string Key;public FrameworkElement View;public TranslateTransform Move=new TranslateTransform();public BlurEffect Blur=new BlurEffect();
            public double X,VX,Alpha,VA,Radius,VR,TargetX,TargetAlpha;
        }
        readonly List<Page> pages=new List<Page>();
        readonly UiMotionClock clock;
        string selected;
        public FrameworkElement Current {get{var page=pages.FirstOrDefault(p=>p.Key==selected&&p.TargetAlpha==1);return page==null?null:page.View;}}
        public int PageCount {get{return pages.Count;}}
        public SettingsPageMotion(UiMotionClock clock){this.clock=clock;clock.Register(this);Unloaded+=delegate{clock.Cancel(this);};}
        public void Show(string key,FrameworkElement view,int direction){
            var previous=pages.FirstOrDefault(p=>p.Key==key);bool initial=pages.Count==0;
            var item=new Page {Key=key,View=view,X=initial?0:direction*10,Alpha=initial?1:0,Radius=initial?0:1.8,TargetAlpha=1};
            if(previous!=null){item.X=previous.X;item.VX=previous.VX;item.Alpha=previous.Alpha;item.VA=previous.VA;item.Radius=previous.Radius;item.VR=previous.VR;Children.Remove(previous.View);pages.Remove(previous);}
            foreach(var page in pages){page.TargetAlpha=0;page.TargetX=-direction*10;page.View.IsHitTestVisible=false;}
            selected=key;view.VerticalAlignment=VerticalAlignment.Top;view.RenderTransform=item.Move;view.IsHitTestVisible=true;pages.Add(item);Children.Add(view);Paint(item);clock.Wake(this);
        }
        void Paint(Page page){page.Move.X=page.X;page.View.Opacity=Math.Max(0,Math.Min(1,page.Alpha));page.Blur.Radius=Math.Max(0,page.Radius);page.View.Effect=page.Radius>.02?page.Blur:null;}
        public bool Step(double dt,bool animate){bool active=false;foreach(var page in pages.ToArray()){
            if(animate){PlayerSpring.Step(ref page.X,ref page.VX,page.TargetX,dt,.30,1);PlayerSpring.Step(ref page.Alpha,ref page.VA,page.TargetAlpha,dt,page.TargetAlpha==1?.23:.14,1);PlayerSpring.Step(ref page.Radius,ref page.VR,page.TargetAlpha==1?0:1.8,dt,.24,1);}else{page.X=page.TargetX;page.Alpha=page.TargetAlpha;page.Radius=page.TargetAlpha==1?0:1.8;page.VX=page.VA=page.VR=0;}
            Paint(page);bool moving=Math.Abs(page.X-page.TargetX)>.01||Math.Abs(page.VX)>.03||Math.Abs(page.Alpha-page.TargetAlpha)>.001||Math.Abs(page.VA)>.01||Math.Abs(page.Radius-(page.TargetAlpha==1?0:1.8))>.02;
            if(!moving&&page.TargetAlpha==0){Children.Remove(page.View);pages.Remove(page);}else active|=moving;
        }return active;}
        public void Snap(){Step(0,false);}
        public void Dispose(){clock.Cancel(this);Children.Clear();pages.Clear();}
    }
    sealed class SettingsSelectionMotion : IUiMotion {
        readonly UiMotionClock clock;readonly Canvas host;readonly Border highlight;readonly Grid sidebar;readonly TranslateTransform move=new TranslateTransform();
        double value,velocity,target;bool initialized;
        public double Position {get{return value;}}
        public SettingsSelectionMotion(UiMotionClock clock,Grid sidebar){this.clock=clock;this.sidebar=sidebar;clock.Register(this);host=new Canvas {IsHitTestVisible=false};sidebar.Children.Insert(0,host);highlight=new Border {Width=170,Height=32,CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(Color.FromRgb(43,43,46)),RenderTransform=move};host.Children.Add(highlight);Canvas.SetLeft(highlight,10);}
        public void Select(Button button){sidebar.UpdateLayout();target=button.TranslatePoint(new Point(),sidebar).Y;if(!initialized){value=target;initialized=true;}clock.Wake(this);}
        public bool Step(double dt,bool animate){if(animate)PlayerSpring.Step(ref value,ref velocity,target,dt,.28,1);else{value=target;velocity=0;}move.Y=value;return Math.Abs(value-target)>.02||Math.Abs(velocity)>.05;}
        public void Snap(){Step(0,false);}
        public void Dispose(){clock.Cancel(this);}
    }
    sealed class ShelfMotion : IUiMotion {
        sealed class Item {public string Path;public Button Button;public TransformGroup Transform=new TransformGroup();public TranslateTransform Move=new TranslateTransform();public ScaleTransform Scale=new ScaleTransform(1,1);public double X,VX,TargetX,Alpha,VA,TargetAlpha=1;}
        readonly UiMotionClock clock;readonly StackPanel panel;readonly TextBlock empty;readonly Func<string,Button> make;
        readonly Dictionary<string,Item> items=new Dictionary<string,Item>(StringComparer.OrdinalIgnoreCase);
        public int Count {get{return items.Count;}}
        public ShelfMotion(UiMotionClock clock,StackPanel panel,TextBlock empty,Func<string,Button> make){this.clock=clock;this.panel=panel;this.empty=empty;this.make=make;clock.Register(this);}
        public void SetPaths(IEnumerable<string> paths){var wanted=paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();foreach(var item in items.Values){item.TargetAlpha=0;item.Button.IsHitTestVisible=false;}
            for(int i=0;i<wanted.Length;i++){Item item;if(!items.TryGetValue(wanted[i],out item)){item=new Item {Path=wanted[i],Button=make(wanted[i]),X=i*93+8};item.Transform.Children.Add(item.Scale);item.Transform.Children.Add(item.Move);item.Button.RenderTransformOrigin=new Point(.5,.5);item.Button.RenderTransform=item.Transform;items.Add(wanted[i],item);panel.Children.Add(item.Button);}item.TargetAlpha=1;item.TargetX=i*93;item.Button.IsHitTestVisible=true;}
            empty.Visibility=wanted.Length==0?Visibility.Visible:Visibility.Collapsed;clock.Wake(this);
        }
        public bool Step(double dt,bool animate){bool active=false;foreach(var item in items.Values.ToArray()){
            if(animate){PlayerSpring.Step(ref item.X,ref item.VX,item.TargetX,dt,.29,1);PlayerSpring.Step(ref item.Alpha,ref item.VA,item.TargetAlpha,dt,.22,1);}else{item.X=item.TargetX;item.Alpha=item.TargetAlpha;item.VX=item.VA=0;}
            bool moving=Math.Abs(item.X-item.TargetX)>.02||Math.Abs(item.VX)>.05||Math.Abs(item.Alpha-item.TargetAlpha)>.001||Math.Abs(item.VA)>.01;
            if(!moving&&item.TargetAlpha==0){panel.Children.Remove(item.Button);items.Remove(item.Path);}else active|=moving;
        }
            foreach(var item in items.Values){int index=panel.Children.IndexOf(item.Button);item.Move.X=item.X-index*93;item.Move.Y=4*(1-item.Alpha);item.Scale.ScaleX=item.Scale.ScaleY=.94+.06*item.Alpha;item.Button.Opacity=Math.Max(0,Math.Min(1,item.Alpha));}return active;
        }
        public void Snap(){Step(0,false);}
    }
    public sealed partial class NotchWindow {
        SettingsPageMotion settingsPages;
        SettingsSelectionMotion settingsSelection;
        UiSurfaceMotion settingsWindowMotion;
        bool settingsMayClose;
        string shownSettingsSection;
        ShelfMotion shelfMotion;
        MotionSlider settingsOffset;
        readonly Dictionary<int,UiSelectionMotion> screenSelections=new Dictionary<int,UiSelectionMotion>();
        readonly Dictionary<int,Button> screenButtons=new Dictionary<int,Button>();
        void AddScreenSelection(Button button,int index){var motion=new UiSelectionMotion(button,index==prefs.Monitor);uiMotion.Register(motion);screenSelections[index]=motion;screenButtons[index]=button;}
        void UpdateScreenSelection(){foreach(var item in screenSelections){item.Value.Select(item.Key==prefs.Monitor);uiMotion.Wake(item.Value);}}
        void ShowSettingsPage(FrameworkElement page){
            string[] order={"Allgemein","Anzeige","Design","Musik & Liedtexte","Timer","Ablage","Add-ons","Gaming","Downloads","Über Iris"};
            int direction=shownSettingsSection==null||shownSettingsSection==settingsSection?0:Math.Sign(Array.IndexOf(order,settingsSection)-Array.IndexOf(order,shownSettingsSection));
            settingsPages.Show(settingsSection,page,direction);page.VerticalAlignment=VerticalAlignment.Stretch;shownSettingsSection=settingsSection;
            if(settings!=null&&settings.IsLoaded)settingsSelection.Select(settingsTabs[settingsSection]);
        }
        void SetupSettingsMotion(Window window,Border shell,Grid sidebar){
            shownSettingsSection=null;settingsMayClose=false;settingsPages=new SettingsPageMotion(uiMotion);settingsBody.Child=settingsPages;settingsSelection=new SettingsSelectionMotion(uiMotion,sidebar);
            settingsWindowMotion=new UiSurfaceMotion(uiMotion,shell,delegate{window.Dispatcher.BeginInvoke(new Action(delegate{if(settings==window&&settingsWindowMotion!=null&&settingsWindowMotion.Closing){settingsMayClose=true;window.Close();}}));},true);
            window.Loaded+=delegate{settingsSelection.Select(settingsTabs[settingsSection]);};
            window.PreviewMouseDown+=delegate{if(settingsWindowMotion!=null&&settingsWindowMotion.Closing)settingsWindowMotion.Show(false);};
            window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!settingsMayClose&&!shuttingDown){e.Cancel=true;settingsWindowMotion.Close();}};
            window.Closed+=delegate{settingsWindowMotion.Dispose();settingsPages.Dispose();settingsSelection.Dispose();settingsWindowMotion=null;settingsPages=null;settingsSelection=null;shownSettingsSection=null;gamingStatus=null;gamingGrant=null;designStatus=null;designSelections.Clear();screenSelections.Clear();screenButtons.Clear();settingsOffset=null;sizePicker=null;sizeNote=null;settingsBody=null;settingsSidebar=null;settingsTabs.Clear();};
            settingsWindowMotion.Show(true);
        }
    }
}
