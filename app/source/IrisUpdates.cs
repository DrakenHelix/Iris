using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace Iris {
    internal sealed class IrisUpdate {
        internal static readonly Version Current = new Version(ReleaseVersion.Number);
        internal const string Manifest = "Iris.program-files.txt";
        const string Repo = "DrakenHelix/Iris";
        const string StagePrefix = ".iris-update-";
        const long MaxPackageSize = 128 * 1024 * 1024;
        static readonly Uri Latest = new Uri("https://api.github.com/repos/"+Repo+"/releases/latest");
        static readonly HttpClient Client = CreateClient();

        static HttpClient CreateClient() {
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            return new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){
                Timeout=Timeout.InfiniteTimeSpan
            };
        }

        public sealed class ReleaseJson {
            public string tag_name {get;set;}
            public string body {get;set;}
            public bool draft {get;set;}
            public bool prerelease {get;set;}
            public AssetJson[] assets {get;set;}
        }

        public sealed class AssetJson {
            public string name {get;set;}
            public string browser_download_url {get;set;}
            public string digest {get;set;}
            public long size {get;set;}
        }

        internal Version Version;
        internal string Notes,Name,Digest;
        internal Uri Url;
        internal long Size;

        internal static IrisUpdate Parse(string json,bool installed,Version current) {
            var release=new JavaScriptSerializer{MaxJsonLength=1024*1024,RecursionLimit=32}.Deserialize<ReleaseJson>(json);
            Version version;
            if(release==null||release.draft||release.prerelease||
                !Regex.IsMatch(release.tag_name??"",@"\Av[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,5}\z")||
                !Version.TryParse(release.tag_name.Substring(1),out version)||"v"+version.ToString(3)!=release.tag_name)
                throw new InvalidDataException("Invalid Iris release version.");
            if(version<=current)return null;
            string suffix=installed?"-Setup.exe":"-Portable.zip";
            var assets=release.assets??new AssetJson[0];
            string name="Iris-"+version.ToString(3)+suffix;
            var asset=assets.SingleOrDefault(item=>item!=null&&item.name==name);
            if(asset==null){
                name="Iris-"+version.ToString(2)+suffix;
                asset=assets.SingleOrDefault(item=>item!=null&&item.name==name);
            }
            string expected="https://github.com/"+Repo+"/releases/download/"+release.tag_name+"/"+name;
            if(asset==null||!string.Equals(asset.browser_download_url,expected,StringComparison.Ordinal)||
                asset.size<=0||asset.size>MaxPackageSize||!Regex.IsMatch(asset.digest??"",@"\Asha256:[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("The matching Iris download or its SHA-256 digest is missing.");
            return new IrisUpdate{Version=version,Name=name,Url=new Uri(expected),Size=asset.size,Digest=asset.digest.Substring(7),Notes=release.body??""};
        }

        static async Task<HttpResponseMessage> GetAsync(Uri uri,bool download,CancellationToken token) {
            for(int redirects=0;redirects<=4;redirects++){
                using(var request=new HttpRequestMessage(HttpMethod.Get,uri)){
                    request.Headers.UserAgent.ParseAdd("Iris/"+ReleaseVersion.Number);
                    if(!download)request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    var response=await Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
                    int code=(int)response.StatusCode;
                    if(download&&code>=300&&code<400){
                        var location=response.Headers.Location;
                        response.Dispose();
                        if(location==null)break;
                        uri=new Uri(uri,location);
                        if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||
                            (uri.Host!="release-assets.githubusercontent.com"&&uri.Host!="objects.githubusercontent.com"&&uri.Host!="github.com"))break;
                        continue;
                    }
                    if(response.IsSuccessStatusCode)return response;
                    response.Dispose();
                    throw new HttpRequestException("GitHub HTTP "+code+".");
                }
            }
            throw new HttpRequestException("Unexpected GitHub download redirect.");
        }

        static async Task CopyAsync(Stream input,Stream output,long limit,CancellationToken token,IProgress<int> progress=null) {
            var buffer=new byte[65536];
            long total=0;int last=-1;
            int length;
            while((length=await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false))!=0){
                total+=length;
                if(total>limit)throw new InvalidDataException("The download exceeds its expected size.");
                await output.WriteAsync(buffer,0,length,token).ConfigureAwait(false);
                int percent=(int)(total*100/limit);
                if(progress!=null&&percent!=last){last=percent;progress.Report(percent);}
            }
            if(progress!=null&&total!=limit)throw new InvalidDataException("The download is incomplete.");
        }

        internal static async Task<IrisUpdate> CheckAsync(bool installed,CancellationToken cancel) {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using(var response=await GetAsync(Latest,false,timeout.Token).ConfigureAwait(false))
                using(timeout.Token.Register(response.Dispose))
                using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using(var buffer=new MemoryStream()){
                    await CopyAsync(input,buffer,1024*1024,timeout.Token).ConfigureAwait(false);
                    return Parse(Encoding.UTF8.GetString(buffer.ToArray()),installed,Current);
                }
            }
        }

        internal async Task<string> DownloadAsync(string root,bool installed,IProgress<int> progress,CancellationToken cancel) {
            string parent=installed?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Iris","Updates"):root;
            string stage=Path.Combine(parent,StagePrefix+Guid.NewGuid().ToString("N"));
            if(!installed)CheckPath(root);
            Directory.CreateDirectory(stage);
            string package=Path.Combine(stage,Name);
            try {
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
                    timeout.CancelAfter(TimeSpan.FromMinutes(3));
                    using(var response=await GetAsync(Url,true,timeout.Token).ConfigureAwait(false))
                    using(timeout.Token.Register(response.Dispose))
                    using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using(var output=new FileStream(package,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                        await CopyAsync(input,output,Size,timeout.Token,progress).ConfigureAwait(false);
                    }
                }
                cancel.ThrowIfCancellationRequested();
                if(new FileInfo(package).Length!=Size)throw new InvalidDataException("The download is incomplete.");
                VerifyDigest(package,Digest);
                if(installed){
                    if(new Version(FileVersionInfo.GetVersionInfo(package).FileVersion).ToString(3)!=Version.ToString(3))
                        throw new InvalidDataException("The setup version does not match the release.");
                    return package;
                }
                ExpandPortable(package,Path.Combine(stage,"next"),Version);
                File.Delete(package);
                string helper=Path.Combine(stage,"Iris.Update.exe");
                File.Copy(typeof(IrisUpdate).Assembly.Location,helper);
                return helper;
            } catch { Discard(stage);throw; }
        }

        internal static void VerifyDigest(string path,string expected) {
            using(var stream=File.OpenRead(path))using(var hash=SHA256.Create()){
                string digest=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");
                if(!string.Equals(digest,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA-256 verification failed.");
            }
        }

        static string RelativeFile(string path) {
            if(!Regex.IsMatch(path??"",@"\A[A-Za-z0-9_-][A-Za-z0-9_.-]*(/[A-Za-z0-9_-][A-Za-z0-9_.-]*)*\z")||path.Length>180||
                path.Split('/').Any(part=>part.EndsWith(".",StringComparison.Ordinal)||
                    Regex.IsMatch(part,@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|\z)",RegexOptions.IgnoreCase))||
                Regex.IsMatch(path,@"\A(?:Data(?:-qa)?(?:/|\z)|Iris\.data-location\.json\z|error\.log\z)",RegexOptions.IgnoreCase))
                throw new InvalidDataException("Unsafe path in the Iris program files.");
            return path;
        }

        static HashSet<string> Inventory(string root) {
            string path=Path.Combine(root,Manifest);
            CheckPath(path);
            if(new FileInfo(path).Length>65536)throw new InvalidDataException("Invalid Iris file list.");
            var files=File.ReadAllLines(path).Select(RelativeFile).ToArray();
            var result=new HashSet<string>(files,StringComparer.OrdinalIgnoreCase);
            if(files.Length!=result.Count||files.Length>512||!result.Contains(Manifest)||!result.Contains("Iris.exe"))
                throw new InvalidDataException("Invalid Iris file list.");
            return result;
        }

        internal static void ExpandPortable(string package,string next,Version version) {
            Directory.CreateDirectory(next);
            using(var input=File.OpenRead(package))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)){
                if(zip.Entries.Count>512)throw new InvalidDataException("Too many Iris files.");
                long bytes=0;
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries){
                    string relative=RelativeFile(entry.FullName);
                    if(!names.Add(relative)||entry.Length<0||entry.Length>MaxPackageSize-bytes)
                        throw new InvalidDataException("Invalid Iris archive.");
                    bytes+=entry.Length;
                    string target=Path.Combine(next,relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using(var source=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew)){
                        var buffer=new byte[65536];long remaining=entry.Length;int count;
                        while((count=source.Read(buffer,0,buffer.Length))>0){
                            remaining-=count;
                            if(remaining<0)throw new InvalidDataException("Invalid Iris archive size.");
                            output.Write(buffer,0,count);
                        }
                        if(remaining!=0)throw new InvalidDataException("Incomplete Iris archive entry.");
                    }
                }
                if(!names.SetEquals(Inventory(next))||new Version(FileVersionInfo.GetVersionInfo(Path.Combine(next,"Iris.exe")).FileVersion).ToString(3)!=version.ToString(3))
                    throw new InvalidDataException("The Iris archive does not match its version or file list.");
            }
        }

        static void CheckPath(string path) {
            for(string part=Path.GetFullPath(path);!string.IsNullOrEmpty(part);part=Path.GetDirectoryName(part)){
                try {
                    if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Iris updates require regular files and folders.");
                } catch(FileNotFoundException) { }
                catch(DirectoryNotFoundException) { }
            }
        }

        internal static void ApplyPortable(string root,string next,string backup) {
            var old=Inventory(root);
            var incoming=Inventory(next);
            var paths=old.Union(incoming,StringComparer.OrdinalIgnoreCase).ToArray();
            foreach(string relative in paths){
                string target=Path.Combine(root,relative);
                CheckPath(target);
                if(Directory.Exists(target)||!old.Contains(relative)&&File.Exists(target))throw new IOException("A file in the update conflicts with an existing item: "+relative);
            }
            var moved=new List<string>();var installed=new List<string>();
            try {
                foreach(string relative in paths){
                    string target=Path.Combine(root,relative);
                    if(File.Exists(target)){
                        string saved=Path.Combine(backup,relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(saved));
                        File.Move(target,saved);moved.Add(relative);
                    }
                    if(incoming.Contains(relative)){
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Move(Path.Combine(next,relative),target);installed.Add(relative);
                    }
                }
            } catch(Exception installError){
                var errors=new List<Exception>{installError};
                foreach(string relative in installed)try{File.Delete(Path.Combine(root,relative));}catch(Exception error){errors.Add(error);}
                foreach(string relative in moved)try{File.Move(Path.Combine(backup,relative),Path.Combine(root,relative));}catch(Exception error){errors.Add(error);}
                if(errors.Count>1)throw new AggregateException("The update could not restore all files. Backups: "+backup,errors);
                throw;
            }
        }

        internal static void RunPortable(string[] args) {
            L.Set(args.Length==4?args[3]:"en");
            try {
                int pid;long started;Guid id;
                string stage=Path.GetDirectoryName(typeof(IrisUpdate).Assembly.Location);
                string name=Path.GetFileName(stage);
                if(args.Length!=4||!int.TryParse(args[1],out pid)||!long.TryParse(args[2],out started)||
                    !name.StartsWith(StagePrefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(StagePrefix.Length),"N",out id))
                    throw new InvalidDataException("Invalid Iris update invocation.");
                string root=Path.GetDirectoryName(stage),app=Path.Combine(root,"Iris.exe");
                if(DataLocation.IsInstalled(root))throw new InvalidDataException("Use the Iris installer to update this copy.");
                CheckPath(stage);
                if(!IrisUpdateProcess.WaitForExit(pid,started,app))throw new IOException("Iris is still running.");
                using(var mutex=new Mutex(false,ReleaseVersion.InstanceMutex)){
                    bool acquired;
                    try{acquired=mutex.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
                    if(!acquired)throw new IOException("Iris is still running.");
                    try{
                        string next=Path.Combine(stage,"next");
                        if(new Version(FileVersionInfo.GetVersionInfo(Path.Combine(next,"Iris.exe")).FileVersion)<=
                            new Version(FileVersionInfo.GetVersionInfo(app).FileVersion))throw new InvalidDataException("The staged update is no longer newer than Iris.");
                        ApplyPortable(root,next,Path.Combine(stage,"backup"));
                    }
                    finally{mutex.ReleaseMutex();}
                }
                using(var self=Process.GetCurrentProcess()){
                    var launch=new ProcessStartInfo(app,"--finish-update "+id.ToString("N")+" "+IrisUpdateProcess.Arguments(self)){
                        UseShellExecute=false,WorkingDirectory=root
                    };
                    using(var restarted=Process.Start(launch)){}
                }
            } catch(Exception error){
                MessageBox.Show(L.T("Iris konnte das Update nicht installieren. ")+error.Message,"Iris",MessageBoxButton.OK,MessageBoxImage.Error);
            }
        }

        internal static void CleanAfterRestart(string[] args,bool installed) {
            Guid id;int pid;long started;
            if(args.Length!=4||!Guid.TryParseExact(args[1],"N",out id)||!int.TryParse(args[2],out pid)||!long.TryParse(args[3],out started))return;
            string parent=installed?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Iris","Updates"):AppDomain.CurrentDomain.BaseDirectory;
            string stage=Path.Combine(parent,StagePrefix+id.ToString("N"));
            Task.Run(delegate {
                try{
                    string executable=installed?Directory.GetFiles(stage,"Iris-*-Setup.exe").SingleOrDefault():Path.Combine(stage,"Iris.Update.exe");
                    if(executable!=null&&File.Exists(executable)&&IrisUpdateProcess.WaitForExit(pid,started,executable))Discard(stage);
                }catch(IOException){}catch(UnauthorizedAccessException){}catch(InvalidOperationException){}
            });
        }

        internal static void Discard(string stage) {
            try{CheckPath(stage);if(Directory.Exists(stage))Directory.Delete(stage,true);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
