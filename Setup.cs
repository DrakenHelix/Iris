using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Iris;

[assembly: AssemblyTitle("Iris Setup")]
[assembly: AssemblyProduct("Iris")]
[assembly: AssemblyVersion(ReleaseVersion.AssemblyNumber)]
[assembly: AssemblyFileVersion(ReleaseVersion.AssemblyNumber)]

namespace IrisSetup {
    static class Program {
        const string UpgradeCode="{7E7B9118-284E-483D-AD6D-E0C8E06CB69C}";
        internal static readonly Version Version=new Version(ReleaseVersion.Number);
        static string ProgramFiles {get{return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);}}
        internal static string DefaultFolder {get{return Path.Combine(ProgramFiles,"Iris");}}
        internal static string InstalledFolder {
            get{
                using(var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Iris\Installer")){
                    var path=key==null?null:key.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(path)?DefaultFolder:path.TrimEnd('\\','/');
                }
            }
        }
        internal static bool German=Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName=="de";
        internal static string Text(string de,string en){return German?de:en;}
        [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern int MsiQueryProductStateW(string product);
        [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern int MsiEnumRelatedProductsW(string upgrade,uint reserved,uint index,StringBuilder product);
        [DllImport("msi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
        static extern int MsiGetProductInfoW(string product,string property,StringBuilder value,ref uint length);
        internal static bool SamePackageInstalled(){return MsiQueryProductStateW(InstallerIdentity.ProductCode)==5;}
        internal static Version InstalledVersion(){
            var product=new StringBuilder(39);
            Version newest=null;
            for(uint i=0;MsiEnumRelatedProductsW(UpgradeCode,0,i,product)==0;i++){
                if(MsiQueryProductStateW(product.ToString())!=5)continue;
                var value=new StringBuilder(32);uint length=32;
                Version version;
                if(MsiGetProductInfoW(product.ToString(),"VersionString",value,ref length)==0&&
                    System.Version.TryParse(value.ToString(),out version)&&(newest==null||version>newest))newest=version;
            }
            return newest;
        }

        [STAThread]
        static int Main(string[] args){
            if(Environment.OSVersion.Version.Major<10){
                MessageBox.Show(Text("Iris benötigt Windows 10 oder neuer (64 Bit).","Iris requires 64-bit Windows 10 or later."),
                    "Iris Setup",MessageBoxButton.OK,MessageBoxImage.Error);
                return 1150;
            }
            Guid attempt;
            if(args.Length==4&&args[0]=="--install"&&Guid.TryParseExact(args[2],"N",out attempt))return Install(args[1],attempt,args[3]);
            bool qa=args.Length==1&&(args[0]=="--qa"||args[0]=="--qa-en");
            int previousPid=0;long previousStart=0;
            bool update=args.Length==4&&args[0]=="--update"&&int.TryParse(args[1],out previousPid)&&previousPid>0&&long.TryParse(args[2],out previousStart);
            if(args.Length>0&&!qa&&!update)return 87;
            if(qa)German=args[0]!="--qa-en";
            if(update)German=args[3]=="de";
            var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};
            return app.Run(new SetupWindow(qa,update?previousPid:0,previousStart));
        }
        internal static bool IrisIsRunning(){
            try{
                Mutex mutex;
                if(!Mutex.TryOpenExisting(ReleaseVersion.InstanceMutex,out mutex))return false;
                mutex.Dispose();
                return true;
            }catch(UnauthorizedAccessException){return true;}
        }
        internal static string DestinationIn(string parent){
            parent=Path.GetFullPath(parent).TrimEnd('\\','/');
            return parent.EndsWith(@"\Iris",StringComparison.OrdinalIgnoreCase)?parent:parent+@"\Iris";
        }
        internal static void ValidateDestination(string path){
            string root=Path.GetPathRoot(path);
            if(path.IndexOf('"')>=0||root==null||root.Length!=3||root[1]!=':'||!Directory.Exists(root)||
                new DriveInfo(root).DriveType!=DriveType.Fixed||!string.Equals(path,Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)||
                !string.Equals(Path.GetFileName(path),"Iris",StringComparison.OrdinalIgnoreCase))
                throw new IOException(Text("Wähle einen Ordner auf einem lokalen Laufwerk.","Choose a folder on a local drive."));
            var directory=new DirectoryInfo(path);
            for(var parent=directory;parent!=null;parent=parent.Parent){
                if(File.Exists(parent.FullName)||parent.Exists&&(parent.Attributes&(FileAttributes.ReparsePoint|FileAttributes.Encrypted))!=0)
                    throw new IOException(Text("Wähle einen normalen, unverschlüsselten Ordner.","Choose a regular, unencrypted folder."));
            }
            if(directory.Exists){
                using(var entries=directory.EnumerateFileSystemInfos().GetEnumerator()){
                    if(entries.MoveNext())throw new IOException(Text("Der Iris-Ordner ist bereits belegt. Wähle einen anderen Ordner.","The Iris folder already contains files. Choose another folder."));
                }
            }
        }
        static string WorkFolder(Guid attempt){return Path.Combine(ProgramFiles,"IrisSetup-"+attempt.ToString("N"));}
        internal static string LogPath(Guid attempt){return Path.Combine(WorkFolder(attempt),"Installation.log");}
        internal static int StartInstaller(string destination,Guid attempt){
            string sid;
            using(var identity=WindowsIdentity.GetCurrent())sid=identity.User.Value;
            string arguments="--install \""+destination+"\" "+attempt.ToString("N")+" "+sid;
            var start=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,arguments){
                UseShellExecute=true,Verb="runas",WorkingDirectory=Environment.SystemDirectory,WindowStyle=ProcessWindowStyle.Hidden
            };
            using(var child=Process.Start(start)){
                if(child==null)return 1603;
                child.WaitForExit();
                return child.ExitCode;
            }
        }
        static int Install(string destination,Guid attempt,string callerSid){
            using(var identity=WindowsIdentity.GetCurrent()){
                if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))return 740;
                string directory=WorkFolder(attempt);
                string package=Path.Combine(directory,"Iris.msi");
                string log=LogPath(attempt);
                bool staged=false,success=false;
                try{
                    if(Directory.Exists(directory)||File.Exists(directory))return 183;
                    // Stage under Program Files so non-administrators cannot substitute the package.
                    var acl=new DirectorySecurity();
                    acl.SetAccessRuleProtection(true,false);
                    var administrators=new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null);
                    acl.SetOwner(administrators);
                    foreach(var sid in new[]{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),administrators})
                        acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                    acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(callerSid),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
                    Directory.CreateDirectory(directory,acl);
                    staged=true;
                    Version existing=InstalledVersion();
                    if(existing!=null){
                        if(!string.Equals(destination,InstalledFolder,StringComparison.OrdinalIgnoreCase))return 87;
                        if(existing>Version)return 1638;
                    }else ValidateDestination(destination);
                    using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Iris.Package"))
                    using(var output=new FileStream(package,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        input.CopyTo(output);
                    string options="/i \""+package+"\" /quiet /norestart /l*v \""+log+"\" INSTALLDIR=\""+destination+"\"";
                    if(existing==Version&&SamePackageInstalled())options+=" REINSTALL=ALL REINSTALLMODE=vamus";
                    // Upgrade costing runs before the old package removes its files.
                    else if(existing!=null)options+=" REINSTALLMODE=amus";
                    using(var installer=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),options){
                        UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Environment.SystemDirectory
                    })){
                        if(installer==null)return 1603;
                        installer.WaitForExit();
                        int code=installer.ExitCode;
                        if(code==0&&!HasExpectedExecutable(destination)){
                            File.AppendAllText(log,Environment.NewLine+"Iris.exe is missing or has an unexpected file version."+Environment.NewLine);
                            return 1603;
                        }
                        success=code==0||code==3010||code==1641;
                        return code;
                    }
                }catch(Exception ex){
                    if(staged)try{File.AppendAllText(log,ex.ToString()+Environment.NewLine);}catch{}
                    return 1603;
                }
                finally{
                    if(staged)try{
                        if(File.Exists(package))File.Delete(package);
                        if(success&&File.Exists(log))File.Delete(log);
                        if(!File.Exists(log))Directory.Delete(directory);
                    }catch{}
                }
            }
        }

        internal static bool HasExpectedExecutable(string destination){
            string executable=Path.Combine(destination,"Iris.exe");
            if(!File.Exists(executable))return false;
            var info=FileVersionInfo.GetVersionInfo(executable);
            return new Version(info.FileMajorPart,info.FileMinorPart,info.FileBuildPart,info.FilePrivatePart)
                ==new Version(ReleaseVersion.AssemblyNumber);
        }
    }

    sealed class SetupWindow : Window {
        readonly bool qa,repair,upgrade,update;
        readonly Version existingVersion;
        readonly Border shell;
        readonly TextBlock heading,detail,status,step,path;
        readonly Button primary,close,browse,showLog;
        readonly ProgressBar progress;
        Guid attempt;
        bool installing,installed,reboot;
        static Brush BrushFrom(string hex){return (Brush)new BrushConverter().ConvertFromString(hex);}
        static string T(string de,string en){return Program.Text(de,en);}
        static readonly Brush Ink=BrushFrom("#F6F6F8"),Muted=BrushFrom("#B6B6BD"),Pink=BrushFrom("#FF5B87");

        internal SetupWindow(bool qa,int previousPid,long previousStart){
            this.qa=qa;
            update=previousPid>0;
            existingVersion=qa?null:Program.InstalledVersion();
            repair=existingVersion!=null;
            upgrade=repair&&(existingVersion<Program.Version||existingVersion==Program.Version&&!Program.SamePackageInstalled());
            Title="Iris Setup";
            Width=Math.Min(660,SystemParameters.WorkArea.Width);
            Height=Math.Min(410,SystemParameters.WorkArea.Height);
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            WindowStyle=WindowStyle.None;
            AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;
            Background=Brushes.Transparent;UseLayoutRounding=true;
            FontFamily=new FontFamily("Segoe UI");Foreground=Ink;
            var icon=new BitmapImage();
            using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("Iris.Brand")){
                icon.BeginInit();icon.CacheOption=BitmapCacheOption.OnLoad;icon.StreamSource=source;icon.EndInit();
            }
            Icon=icon;
            shell=new Border{CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),BorderBrush=BrushFrom("#39393D"),Background=BrushFrom("#1C1C1E"),ClipToBounds=true};
            Content=shell;
            var layout=new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(176)});
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            shell.Child=layout;
            var sidebar=new Border{CornerRadius=new CornerRadius(13,0,0,13),Background=BrushFrom("#151516")};
            layout.Children.Add(sidebar);
            var brand=new StackPanel{Margin=new Thickness(21,43,21,20)};
            sidebar.Child=brand;
            brand.Children.Add(new Image{Source=icon,Width=78,Height=78,HorizontalAlignment=HorizontalAlignment.Left});
            var name=Label("Iris",27,Ink);name.FontWeight=FontWeights.Bold;name.Margin=new Thickness(0,13,0,1);brand.Children.Add(name);
            brand.Children.Add(Label("Version "+ReleaseVersion.Number,12,Muted));
            step=Label("",12,Ink);step.Margin=new Thickness(0,33,0,0);
            brand.Children.Add(new Border{CornerRadius=new CornerRadius(8),Background=BrushFrom("#29292B"),Margin=new Thickness(-4,28,-4,0),Padding=new Thickness(9,7,9,7),Child=step});
            step.Margin=new Thickness(0);

            var body=new Grid{Margin=new Thickness(28,45,26,21)};
            Grid.SetColumn(body,1);layout.Children.Add(body);
            body.RowDefinitions.Add(new RowDefinition());
            body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var panel=new StackPanel();
            var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            body.Children.Add(scroll);
            heading=Label("",24,Ink);heading.FontWeight=FontWeights.SemiBold;panel.Children.Add(heading);
            detail=Label("",13,Muted);detail.Margin=new Thickness(0,7,0,17);panel.Children.Add(detail);
            var location=new StackPanel();
            location.Children.Add(Label(T("Installationsordner","Install location"),11,Muted));
            path=Label(repair?Program.InstalledFolder:Program.DefaultFolder,13,Ink);path.Margin=new Thickness(0,4,0,0);location.Children.Add(path);
            var locationRow=new Grid();
            locationRow.ColumnDefinitions.Add(new ColumnDefinition());
            locationRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            locationRow.Children.Add(location);
            browse=MakeSecondaryButton(T("Durchsuchen …","Browse…"));
            browse.Margin=new Thickness(12,0,0,0);browse.VerticalAlignment=VerticalAlignment.Center;
            browse.Visibility=repair?Visibility.Collapsed:Visibility.Visible;
            browse.Click+=delegate{ChooseFolder();};
            Grid.SetColumn(browse,1);locationRow.Children.Add(browse);
            panel.Children.Add(new Border{Background=BrushFrom("#272729"),CornerRadius=new CornerRadius(10),Padding=new Thickness(13,10,13,11),Child=locationRow});
            status=Label("",12,Muted);status.Margin=new Thickness(0,13,0,0);panel.Children.Add(status);
            showLog=MakeSecondaryButton(T("Installationsprotokoll öffnen","Open installation log"));
            showLog.HorizontalAlignment=HorizontalAlignment.Left;showLog.Margin=new Thickness(0,8,0,0);showLog.Visibility=Visibility.Collapsed;
            showLog.Click+=delegate{
                try{Process.Start(new ProcessStartInfo(Program.LogPath(attempt)){UseShellExecute=true});}
                catch{status.Text=Program.LogPath(attempt);}
            };
            panel.Children.Add(showLog);
            progress=new ProgressBar{Height=4,IsIndeterminate=true,Visibility=Visibility.Collapsed,Foreground=Pink,Background=BrushFrom("#343438"),Margin=new Thickness(0,12,0,0)};
            System.Windows.Automation.AutomationProperties.SetName(progress,T("Installationsfortschritt","Installation progress"));
            panel.Children.Add(progress);
            primary=MakeButton("",true);
            primary.MinWidth=136;primary.HorizontalAlignment=HorizontalAlignment.Right;primary.Margin=new Thickness(0,13,0,0);primary.IsDefault=true;
            primary.Click+=async delegate{if(installed)OpenIris();else await InstallAsync();};
            Grid.SetRow(primary,1);body.Children.Add(primary);

            var drag=new Border{Background=Brushes.Transparent,Height=31,VerticalAlignment=VerticalAlignment.Top};
            Grid.SetColumnSpan(drag,2);Panel.SetZIndex(drag,1);layout.Children.Add(drag);
            drag.MouseLeftButtonDown+=delegate{if(Mouse.LeftButton==MouseButtonState.Pressed)try{DragMove();}catch(InvalidOperationException){}};
            close=MakeButton("×",false);
            close.Width=27;close.Height=26;close.Padding=new Thickness(0);close.Margin=new Thickness(6,3,0,0);
            close.HorizontalAlignment=HorizontalAlignment.Left;close.VerticalAlignment=VerticalAlignment.Top;
            close.ToolTip=T("Setup schließen","Close setup");
            System.Windows.Automation.AutomationProperties.SetName(close,Convert.ToString(close.ToolTip));
            close.Click+=delegate{Close();};Panel.SetZIndex(close,2);layout.Children.Add(close);
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape&&!installing)Close();};
            Closing+=delegate(object sender,CancelEventArgs e){e.Cancel=installing;};
            SetReady();
            Loaded+=async delegate{
                if(SystemParameters.ClientAreaAnimation&&!qa){
                    shell.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(170)));
                    var shift=new TranslateTransform();shell.RenderTransform=shift;
                    shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(6,0,TimeSpan.FromMilliseconds(200)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
                }
                if(qa)await Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(RunQa)).Task;
                if(update&&upgrade){
                    primary.IsEnabled=false;
                    status.Text=T("Warte, bis Iris beendet ist …","Waiting for Iris to close…");
                    string installedExe=Path.Combine(path.Text,"Iris.exe");
                    bool exited=await Task.Run(()=>IrisUpdateProcess.WaitForExit(previousPid,previousStart,installedExe));
                    if(!IsLoaded)return;
                    primary.IsEnabled=true;
                    if(!exited){status.Text=T("Iris läuft noch. Beende Iris und versuche es erneut.","Iris is still running. Quit Iris and try again.");return;}
                    await InstallAsync();
                    if(installed&&!reboot)OpenIris();
                }
            };
        }

        static TextBlock Label(string value,double size,Brush color){
            return new TextBlock{Text=value,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap};
        }
        Button MakeButton(string title,bool accent){
            var template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
                <Border x:Name='frame' CornerRadius='8' BorderThickness='1' BorderBrush='Transparent' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
                    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Opacity' Value='.91'/></Trigger>
                    <Trigger Property='IsPressed' Value='True'><Setter TargetName='frame' Property='Opacity' Value='.77'/></Trigger>
                    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='White'/></Trigger>
                    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='frame' Property='Opacity' Value='.45'/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate>");
            return new Button{Content=title,Template=template,Height=36,FontSize=accent?13:17,FontWeight=accent?FontWeights.SemiBold:FontWeights.Normal,
                Foreground=accent?BrushFrom("#220D18"):Ink,Background=accent?Pink:Brushes.Transparent,Padding=new Thickness(14,4,14,4),Cursor=Cursors.Hand};
        }
        Button MakeSecondaryButton(string title){
            var button=MakeButton(title,false);
            button.Height=30;button.FontSize=12;button.FontWeight=FontWeights.SemiBold;button.Background=BrushFrom("#38383B");
            return button;
        }
        sealed class DialogOwner : System.Windows.Forms.IWin32Window {
            public IntPtr Handle {get;private set;}
            internal DialogOwner(Window owner){Handle=new WindowInteropHelper(owner).Handle;}
        }
        void ChooseFolder(){
            using(var picker=new System.Windows.Forms.FolderBrowserDialog{
                Description=T("Wähle den Ordner, in dem Iris installiert werden soll. Setup legt darin den Unterordner „Iris“ an.",
                    "Choose where to install Iris. Setup creates an Iris subfolder there."),
                SelectedPath=System.IO.Directory.Exists(path.Text)?path.Text:Path.GetDirectoryName(path.Text)
            }){
                if(picker.ShowDialog(new DialogOwner(this))!=System.Windows.Forms.DialogResult.OK)return;
                string destination=Program.DestinationIn(picker.SelectedPath);
                if(TryDestination(destination)){
                    path.Text=destination;
                    SetReady();
                }
            }
        }
        bool TryDestination(string destination){
            try{Program.ValidateDestination(destination);return true;}
            catch(IOException ex){status.Text=ex.Message;}
            catch(UnauthorizedAccessException){status.Text=T("Der Ordner ist nicht zugänglich. Wähle einen anderen Ordner.","This folder is inaccessible. Choose another folder.");}
            return false;
        }
        void SetReady(){
            showLog.Visibility=Visibility.Collapsed;
            step.Text=T("01  Vorbereitung","01  Ready");
            heading.Text=upgrade?T("Iris aktualisieren","Update Iris"):repair?T("Iris reparieren","Repair Iris"):T("Iris installieren","Install Iris");
            detail.Text=T("Deine Windows-Notch mit Musik, Timer, Mixer und Add-ons.","Your Windows notch with music, a timer, a volume mixer, and add-ons.");
            status.Text=T("Windows fragt einmal nach der Freigabe. Deine Einstellungen bleiben in deinem Benutzerprofil.","Windows asks for permission once. Your settings stay in your user profile.");
            primary.Content=upgrade?T("Aktualisieren","Update"):repair?T("Reparieren","Repair"):T("Installieren","Install");
        }
        async Task InstallAsync(){
            if(installing)return;
            if(!qa&&Program.IrisIsRunning()){
                status.Text=T("Beende die laufende Iris über ihr Symbol im Infobereich und versuche es erneut.","Quit Iris from the notification area, then try again.");
                return;
            }
            string destination=path.Text;
            if(!repair&&!TryDestination(destination))return;
            var currentAttempt=Guid.NewGuid();
            attempt=currentAttempt;
            SetInstalling();
            int result;
            try{result=qa?0:await Task.Run(()=>Program.StartInstaller(destination,currentAttempt));}
            catch(Win32Exception ex){result=ex.NativeErrorCode==1223?1602:ex.NativeErrorCode;}
            catch{result=1603;}
            SetResult(result);
        }
        void SetInstalling(){
            installing=true;primary.IsEnabled=close.IsEnabled=false;
            browse.IsEnabled=false;showLog.Visibility=Visibility.Collapsed;
            step.Text=T("02  Installation","02  Installing");
            heading.Text=T("Iris wird eingerichtet","Setting up Iris");
            detail.Text=T("Windows kopiert die Programmdateien und erstellt den Startmenü-Eintrag.","Windows copies the program files and creates the Start menu shortcut.");
            status.Text=T("Nach der Windows-Freigabe läuft die Einrichtung automatisch weiter.","Setup continues automatically after you grant Windows permission.");
            progress.Visibility=Visibility.Visible;
        }
        void SetResult(int result){
            installing=false;primary.IsEnabled=close.IsEnabled=true;progress.Visibility=Visibility.Collapsed;
            if(result==0||result==3010||result==1641){
                installed=true;reboot=result!=0;
                browse.Visibility=Visibility.Collapsed;
                step.Text=T("03  Fertig","03  Complete");
                heading.Text=T("Iris ist bereit","Iris is ready");
                detail.Text=T("Du findest Iris im Windows-Startmenü.","You can find Iris in the Windows Start menu.");
                status.Text=reboot?T("Starte Windows neu, um die Installation abzuschließen.","Restart Windows to complete installation."):
                    T("Vorhandene Einstellungen einer ZIP-Version kannst du unter Einstellungen → Über Iris übernehmen.","To bring settings over from a ZIP version, open Settings → About Iris.");
                primary.Content=reboot?T("Schließen","Close"):T("Iris öffnen","Open Iris");
                return;
            }
            SetReady();
            browse.IsEnabled=true;
            status.Text=result==1602?T("Die Windows-Freigabe wurde abgebrochen.","The Windows permission request was canceled."):
                result==1618?T("Windows installiert bereits ein anderes Programm. Warte kurz und versuche es erneut.","Windows is installing another program. Wait for it to finish, then try again."):
                result==1638?T("Eine neuere Iris-Version ist bereits installiert.","A newer version of Iris is already installed."):
                T("Windows konnte Iris nicht installieren. Fehlercode ","Windows could not install Iris. Error code ")+result+".";
            if(File.Exists(Program.LogPath(attempt)))showLog.Visibility=Visibility.Visible;
        }
        void OpenIris(){
            if(reboot){Close();return;}
            try{
                string arguments="";
                string stage=Path.GetFileName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                const string prefix=".iris-update-";
                Guid id;
                if(update&&stage.StartsWith(prefix,StringComparison.Ordinal)&&Guid.TryParseExact(stage.Substring(prefix.Length),"N",out id))
                    using(var self=Process.GetCurrentProcess())arguments="--finish-installed-update "+id.ToString("N")+" "+IrisUpdateProcess.Arguments(self);
                using(var next=Process.Start(new ProcessStartInfo(Path.Combine(path.Text,"Iris.exe"),arguments){UseShellExecute=false,WorkingDirectory=path.Text})){
                    if(next==null)throw new InvalidOperationException("Iris process did not start.");
                }
                Close();
            }
            catch{status.Text=T("Iris konnte nicht geöffnet werden. Starte es über das Windows-Startmenü.","Could not open Iris. Try launching it from the Windows Start menu.");}
        }
        async void RunQa(){
            try{
                if(!primary.IsEnabled||Icon==null||heading.ActualWidth<=0)throw new InvalidOperationException("Setup did not render.");
                string destination=Program.DestinationIn(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"QA destination"));
                if(!TryDestination(destination)||Program.DestinationIn(destination)!=destination)throw new InvalidOperationException("Custom destination rejected.");
                path.Text=destination;
                var preview=RenderInMemory();
                if(Environment.GetEnvironmentVariable("IRIS_SETUP_QA_RENDER")=="1"){
                    var encoder=new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(preview));
                    using(var stream=new MemoryStream()){
                        encoder.Save(stream);
                        Console.WriteLine(Convert.ToBase64String(stream.ToArray()));
                    }
                }
                SetInstalling();RenderInMemory();
                if(primary.IsEnabled||browse.IsEnabled||progress.Visibility!=Visibility.Visible)throw new InvalidOperationException("Missing progress state.");
                SetResult(1602);RenderInMemory();
                if(!primary.IsEnabled||!browse.IsEnabled||installed||path.Text!=destination)throw new InvalidOperationException("Cancellation cannot be retried.");
                SetInstalling();SetResult(1603);RenderInMemory();
                if(!status.Text.Contains("1603")||!browse.IsEnabled||path.Text!=destination)throw new InvalidOperationException("Installation error cannot be retried.");
                await InstallAsync();RenderInMemory();
                if(!installed||!primary.IsEnabled||progress.Visibility!=Visibility.Collapsed)throw new InvalidOperationException("Missing completion state.");
                Application.Current.Shutdown(0);
            }catch(Exception ex){Console.Error.WriteLine(ex);Application.Current.Shutdown(1);}
        }
        RenderTargetBitmap RenderInMemory(){
            UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(shell);
            byte[] pixel=new byte[4];
            bitmap.CopyPixels(new Int32Rect(180,10,1,1),pixel,4,0);
            if(pixel[3]==0)throw new InvalidOperationException("Setup window rendered blank.");
            return bitmap;
        }
    }
}
