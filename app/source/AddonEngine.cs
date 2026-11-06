using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Iris {
    public static class AddonPromptBuilder {
        public static string CreateUri(string instruction,string text){if(string.IsNullOrWhiteSpace(text))throw new InvalidOperationException(L.T("Kopiere zuerst den gewünschten Text."));if(text.Length>2000)throw new InvalidOperationException(L.T("Für diesen Entwurfsweg sind höchstens 2000 Zeichen möglich."));string uri="codex://threads/new?prompt="+Uri.EscapeDataString(instruction+"\n\n"+text);if(uri.Length>24000)throw new InvalidOperationException(L.T("Der Entwurf ist zu lang für die Desktop-Verknüpfung."));return uri;}
    }
    public enum AddonResultCode {Succeeded,Failed,Unavailable,Disabled,Busy,Cancelled,ConfirmationRequired,Unconfirmed}
    public sealed class AddonResult {
        public AddonResultCode Code;public string Message,ObservedState;public DateTime ObservedUtc;
        public bool Success {get{return Code==AddonResultCode.Succeeded;}}
        public static AddonResult Of(AddonResultCode code,string message){return new AddonResult{Code=code,Message=message,ObservedUtc=DateTime.UtcNow};}
        public static AddonResult State(string message,string state){var r=Of(AddonResultCode.Succeeded,message);r.ObservedState=state;return r;}
    }
    public sealed class AddonExecutionContext {public string BindingId,PackageId,ProfileId="default";public bool Confirmed;public IntPtr TargetWindow;}
    public interface IAddonPlatformHost : IAddonHost {
        AddonEnvironmentSnapshot Inspect(CancellationToken token);
        AddonResult ExecutePlatform(AddonDefinition definition,AddonAction action,AddonExecutionContext context,CancellationToken token);
    }
    public sealed class AddonActionEngine : IDisposable {
        sealed class Job {public string Package;public CancellationTokenSource Cancellation;}
        readonly object gate=new object();readonly Dictionary<string,Job> jobs=new Dictionary<string,Job>();
        readonly Dictionary<string,AddonResult> results=new Dictionary<string,AddonResult>();readonly IAddonPlatformHost host;bool disposed;
        public AddonActionEngine(IAddonPlatformHost host){if(host==null)throw new ArgumentNullException("host");this.host=host;}
        public int ActiveCount {get{lock(gate)return jobs.Count;}}
        public bool IsRunning(string binding){lock(gate)return binding!=null&&jobs.ContainsKey(binding);}
        public AddonResult LastResult(string binding){lock(gate){AddonResult result;return results.TryGetValue(binding,out result)?result:null;}}
        public void Cancel(string binding){lock(gate){Job job;if(jobs.TryGetValue(binding,out job))job.Cancellation.Cancel();}}
        public void CancelPackage(string package){lock(gate)foreach(var job in jobs.Values)if(job.Package==package)job.Cancellation.Cancel();}
        public Task<AddonResult> ExecuteAsync(AddonPackage package,AddonAction action,AddonExecutionContext context,CancellationToken cancellation){
            if(context==null||string.IsNullOrEmpty(context.BindingId)||action==null)return Task.FromResult(AddonResult.Of(AddonResultCode.Failed,L.T("Ungültiger Ausführungsauftrag.")));
            // Snapshot values on the caller's thread. Editing a binding never mutates an in-flight action.
            var copy=action.Copy();var ctx=new AddonExecutionContext{BindingId=context.BindingId,PackageId=context.PackageId,ProfileId=context.ProfileId,Confirmed=context.Confirmed,TargetWindow=context.TargetWindow};
            if(package==null||!package.Enabled||package.Id!=ctx.PackageId)return Task.FromResult(AddonResult.Of(AddonResultCode.Disabled,L.T("Add-on deaktiviert oder entfernt.")));
            Job work;
            lock(gate){if(disposed)return Task.FromResult(AddonResult.Of(AddonResultCode.Cancelled,L.T("Iris wird beendet.")));if(jobs.ContainsKey(ctx.BindingId))return Task.FromResult(AddonResult.Of(AddonResultCode.Busy,L.T("Diese Aktion läuft bereits.")));work=new Job{Package=ctx.PackageId,Cancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellation)};jobs.Add(ctx.BindingId,work);}
            return Task.Factory.StartNew(delegate{
                AddonResult result;
                try{
                    var token=work.Cancellation.Token;token.ThrowIfCancellationRequested();string error=AddonCatalog.Check(copy,true);if(error!=null)result=AddonResult.Of(AddonResultCode.Failed,error);
                    else{var definition=AddonRegistry.Find(copy.Kind);if(definition.Confirm&&!ctx.Confirmed)result=AddonResult.Of(AddonResultCode.ConfirmationRequired,definition.Description);
                        else{var env=definition.Dependency.Length>0||copy.Kind=="hotkey"||definition.Configuration==AddonConfiguration.AppShortcut?host.Inspect(token):new AddonEnvironmentSnapshot();token.ThrowIfCancellationRequested();var availability=AddonRegistry.Availability(copy,env);
                            if(!availability.CanExecute)result=AddonResult.Of(AddonResultCode.Unavailable,availability.Message);
                            else{token.ThrowIfCancellationRequested();switch(definition.Route){
                                case AddonRoute.Legacy:result=AddonResult.Of(AddonResultCode.Succeeded,AddonRunner.Run(copy,host,token));break;
                                case AddonRoute.Uri:host.Launch(string.IsNullOrEmpty(definition.Preset)?copy.Target:definition.Preset,"","");result=AddonResult.Of(AddonResultCode.Succeeded,L.T("Öffnung angefordert"));break;
                                case AddonRoute.Keys:ushort[] keys=definition.Keys;if(definition.Configuration==AddonConfiguration.AppShortcut){if(!AddonRunner.TryChord(copy.Shortcut,out keys))throw new InvalidOperationException(L.T("Ungültiges App-Kürzel."));}host.Shortcut(definition.Configuration==AddonConfiguration.AppShortcut?copy.Process:"",keys);result=AddonResult.Of(AddonResultCode.Succeeded,L.T("Tastenkürzel gesendet; App-Zustand nicht ausgelesen"));break;
                                case AddonRoute.Platform:result=host.ExecutePlatform(definition,copy,ctx,token);break;
                                default:result=AddonResult.Of(AddonResultCode.Unavailable,definition.UnsupportedReason);break;
                            }}
                        }
                    }
                }catch(OperationCanceledException){result=AddonResult.Of(AddonResultCode.Cancelled,L.T("Abgebrochen; bereits übergebene Windows-Befehle werden nicht rückgängig gemacht."));}
                catch(Exception ex){result=AddonResult.Of(AddonResultCode.Failed,ex.Message);}
                lock(gate){jobs.Remove(ctx.BindingId);if(!disposed){results[ctx.BindingId]=result;if(results.Count>128)results.Clear();}work.Cancellation.Dispose();}return result;
            },CancellationToken.None,TaskCreationOptions.None,TaskScheduler.Default);
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;foreach(var job in jobs.Values)job.Cancellation.Cancel();results.Clear();}}
    }
}
