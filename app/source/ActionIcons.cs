using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Reflection;using System.Windows;using System.Windows.Media;using System.Windows.Media.Imaging;
namespace Iris {
    public static class ActionIconArtwork {
        static readonly Dictionary<string,DrawingGroup> cache=new Dictionary<string,DrawingGroup>();
        static readonly Dictionary<string,BitmapSource> brands=new Dictionary<string,BitmapSource>();
        static readonly Brush white=Brushes.White,blue=Frozen(151,213,255),purple=Frozen(214,183,255),red=Frozen(255,119,140);
        static Brush Frozen(byte r,byte g,byte b){var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush;}
        public static IEnumerable<string> Kinds {get{return ActionIconPaths.Actions.Keys.OrderBy(k=>k).ToArray();}}
        static BitmapSource Brand(string name){BitmapSource image;if(brands.TryGetValue(name,out image))return image;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Iris.Logos."+name+".png")){if(stream!=null){var decoder=new PngBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);image=decoder.Frames[0];image.Freeze();}brands.Add(name,image);return image;}}
        static void Shape(DrawingContext dc,string name,int units,Rect target,Brush color){Geometry geometry;if(!ActionIconPaths.Shapes.TryGetValue(name+"/"+units,out geometry))return;dc.PushTransform(new TranslateTransform(target.X,target.Y));dc.PushTransform(new ScaleTransform(target.Width/units,target.Height/units));dc.DrawGeometry(color,null,geometry);dc.Pop();dc.Pop();}
        static void Custom(DrawingContext dc,string glyph){GlyphTypeface font;if(!new Typeface(new FontFamily("Segoe MDL2 Assets"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out font))return;ushort index;if(!font.CharacterToGlyphMap.TryGetValue(glyph[0],out index))return;var geometry=font.GetGlyphOutline(index,18,18);var bounds=geometry.Bounds;dc.PushTransform(new TranslateTransform(16-(bounds.X+bounds.Width/2),10-(bounds.Y+bounds.Height/2)));dc.DrawGeometry(purple,null,geometry);dc.Pop();}
        public static DrawingGroup Action(string kind,string custom){
            string key=(kind??"")+"|"+(custom??"");DrawingGroup drawing;if(cache.TryGetValue(key,out drawing))return drawing;drawing=new DrawingGroup();
            using(var dc=drawing.Open()){
                if(!string.IsNullOrEmpty(custom))Custom(dc,custom);
                else{string[] spec;if(kind!=null&&ActionIconPaths.Actions.TryGetValue(kind,out spec)){
                    bool branded=spec[2].Length>0;
                    var image=branded?Brand(spec[2]):null;
                    if(image!=null)dc.DrawImage(image,new Rect(0,0,20,20));
                    else if(branded)Shape(dc,spec[1].Length==0?"Keyboard":spec[1],spec[1].Length==0?24:16,new Rect(6,0,20,20),purple);
                    else Shape(dc,spec[0],24,new Rect(6,0,20,20),kind.StartsWith("windows-")||kind=="screenshot"||kind=="folders"?(kind=="windows-shutdown"||kind=="windows-recording"?red:blue):purple);
                    if(spec[1].Length>0&&(!branded||image!=null)){var badge=new Rect(21.5,9,10,10);if(!branded)dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(22,22,24)),null,new Point(26.5,14),5.5,5.5);Shape(dc,spec[1],16,badge,white);}
                }else{var pen=new Pen(new SolidColorBrush(Color.FromRgb(155,155,160)),1.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};dc.DrawLine(pen,new Point(10,10),new Point(22,10));dc.DrawLine(pen,new Point(16,4),new Point(16,16));}}
            }
            drawing.Freeze();if(cache.Count>512)cache.Clear();cache[key]=drawing;return drawing;
        }
        public static DrawingGroup SteamGame(ImageSource image){var drawing=new DrawingGroup();using(var dc=drawing.Open()){dc.DrawImage(image,new Rect(0,0,20,20));var badge=new Rect(21.5,9,10,10);var brand=Brand("Steam");if(brand!=null)dc.DrawImage(brand,badge);else Shape(dc,"Games",16,badge,white);}drawing.Freeze();return drawing;}
        public static DrawingGroup Package(string id){string key="package|"+id;DrawingGroup drawing;if(cache.TryGetValue(key,out drawing))return drawing;drawing=new DrawingGroup();using(var dc=drawing.Open()){
            string brand=id=="iris.discord"?"Discord":id=="iris.steam"?"Steam":id=="iris.chatgpt"?"ChatGPT":id=="iris.spotify"?"Spotify":id=="iris.applemusic"?"AppleMusic":id=="iris.obs"?"OBS":id=="iris.coding"?"VSCode":null;
            var image=brand==null?null:Brand(brand);
            if(image!=null)dc.DrawImage(image,new Rect(6,0,20,20));
            else if(id=="iris.windows"){for(int y=0;y<2;y++)for(int x=0;x<2;x++)dc.DrawRectangle(blue,null,new Rect(7+x*9.5,1+y*9.5,8.5,8.5));}
            else if(brand=="Steam")Shape(dc,"Games",24,new Rect(6,0,20,20),purple);
            else if(brand=="Spotify"||brand=="AppleMusic")Shape(dc,"Music Note 2",16,new Rect(6,0,20,20),purple);
            else if(brand=="Discord"||brand=="ChatGPT")Shape(dc,"Chat Multiple",16,new Rect(6,0,20,20),purple);
            else if(brand=="OBS")Shape(dc,"Video Recording",24,new Rect(6,0,20,20),purple);
            else Shape(dc,id=="iris.browser"?"Globe":id=="iris.hardware"?"Data Histogram":id=="iris.productivity"?"Timer":"Keyboard",24,new Rect(6,0,20,20),purple);
        }drawing.Freeze();cache[key]=drawing;return drawing;}
    }
    public sealed class AddonActionIcon : FrameworkElement {
        DrawingGroup artwork;string kind,custom,package;ImageSource steamImage;
        public AddonActionIcon(){Width=32;Height=20;IsHitTestVisible=false;RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);}
        public void Set(string actionKind,string userGlyph){if(artwork!=null&&kind==actionKind&&custom==userGlyph&&package==null)return;kind=actionKind;custom=userGlyph;package=null;steamImage=null;artwork=ActionIconArtwork.Action(kind,custom);InvalidateVisual();}
        public void SetSteamGame(string id,ImageSource image){if(package=="steam-game:"+id&&object.ReferenceEquals(steamImage,image))return;kind="steam";custom="";package="steam-game:"+id;steamImage=image;artwork=ActionIconArtwork.SteamGame(image);InvalidateVisual();}
        public void SetPackage(string id){if(artwork!=null&&package==id)return;package=id;artwork=ActionIconArtwork.Package(id);InvalidateVisual();}
        protected override void OnRender(DrawingContext dc){base.OnRender(dc);if(artwork!=null)dc.DrawDrawing(artwork);}
    }
}
