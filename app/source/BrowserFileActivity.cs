using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Iris {
    sealed class BrowserFileActivity {
        public const string Id = ChromeDownloadCounter.TargetId;
        const int FreshSeconds = 6;

        sealed class Observation {
            public long Length;
            public DateTime Written;
            public DateTime Changed;
        }

        readonly string[] directories;
        Dictionary<string, Observation> previous = new Dictionary<string, Observation>(StringComparer.OrdinalIgnoreCase);
        bool hasBaseline;

        public BrowserFileActivity(IEnumerable<string> directories) {
            this.directories = directories.Where(IsLocalDirectory).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
        }

        public bool Available { get { return directories.Length > 0; } }

        public static IEnumerable<string> Discover(IEnumerable<BrowserDownloadProfile> profiles) {
            Guid downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            IntPtr pointer = IntPtr.Zero;
            string downloads = null;
            try {
                if (SHGetKnownFolderPath(ref downloadsId, 0, IntPtr.Zero, out pointer) == 0)
                    downloads = Marshal.PtrToStringUni(pointer);
            } finally {
                if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            }
            yield return downloads ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            foreach (var profile in profiles.Where(p => !p.Firefox)) {
                string directory = null;
                try {
                    var preferences = new FileInfo(Path.Combine(Path.GetDirectoryName(profile.Path), "Preferences"));
                    if (!preferences.Exists || preferences.Length > 1024 * 1024) continue;
                    using (var stream = new FileStream(preferences.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream)) {
                        var data = IntegrationJson.Parse(reader.ReadToEnd());
                        directory = IntegrationJson.Text(IntegrationJson.Get(data, "download"), "default_directory");
                    }
                } catch { }
                if (!string.IsNullOrWhiteSpace(directory)) yield return directory;
            }
        }

        static bool IsLocalDirectory(string path) {
            try {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || path.StartsWith(@"\\")) return false;
                string root = Path.GetPathRoot(path);
                return root.Length == 3 && new DriveInfo(root).DriveType != DriveType.Network;
            } catch { return false; }
        }

        public ActiveDownload Read(DateTime now, CancellationToken token) {
            var current = new Dictionary<string, Observation>(StringComparer.OrdinalIgnoreCase);
            DateTime latest = DateTime.MinValue;
            foreach (string directory in directories) {
                if (token.IsCancellationRequested) return null;
                try {
                    foreach (string path in Directory.EnumerateFiles(directory, "*.crdownload").Take(64)) {
                        if (token.IsCancellationRequested) return null;
                        if (!path.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)) continue;
                        try {
                            var file = new FileInfo(path);
                            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                            // Query the open file's length because directory metadata can lag behind writes.
                            // Length is used solely to detect a change. Chromium can preallocate a file.
                            long length;
                            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                                length = stream.Length;
                            var sample = new Observation { Length = length, Written = file.LastWriteTimeUtc };
                            Observation old;
                            if (previous.TryGetValue(path, out old)) {
                                if (length > old.Length || length > 0 && length == old.Length && sample.Written > old.Written)
                                    sample.Changed = now;
                                else if (length == old.Length)
                                    sample.Changed = old.Changed;
                            } else if (hasBaseline && length > 0 && sample.Written >= now.AddSeconds(-2) && sample.Written <= now.AddSeconds(2)) {
                                sample.Changed = now;
                            }
                            current[path] = sample;
                            if (sample.Changed > latest) latest = sample.Changed;
                        } catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            previous = current;
            hasBaseline = true;
            if (now - latest >= TimeSpan.FromSeconds(FreshSeconds)) return null;
            return new ActiveDownload {
                Id = Id, Source = "Browser", Name = L.T("Download-Aktivität"), Received = 0, Total = -1,
                Utc = latest, ActivitySeconds = FreshSeconds,
                ChromeCounterEligible = current.Values.Count(sample => now - sample.Changed < TimeSpan.FromSeconds(FreshSeconds)) == 1,
                ProgressNote = L.T("Teildatei ändert sich · Fortschritt unbekannt")
            };
        }

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
    }
}
