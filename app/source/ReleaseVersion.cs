using System;
using System.Diagnostics;
using System.IO;

namespace Iris {
    internal static class ReleaseVersion {
        internal const string Number = "1.0.3";
        internal const string AssemblyNumber = Number + ".0";
        internal const string InstanceMutex = @"Local\IrisNotch.Personal.v1";
    }

    internal static class IrisUpdateProcess {
        internal static string Arguments(Process process) {
            return process.Id+" "+process.StartTime.ToUniversalTime().Ticks;
        }

        internal static bool WaitForExit(int id,long started,string expectedPath) {
            Process process;
            try { process=Process.GetProcessById(id); }
            catch(ArgumentException) { return true; }
            using(process) {
                try {
                    if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=started)return true;
                    if(!string.Equals(Path.GetFullPath(process.MainModule.FileName),expectedPath,StringComparison.OrdinalIgnoreCase))return false;
                    return process.WaitForExit(30000);
                } catch(InvalidOperationException) { return false; }
                catch(System.ComponentModel.Win32Exception) { return false; }
            }
        }
    }
}
