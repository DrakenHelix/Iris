using System;
using System.IO;
using Microsoft.Win32;

namespace Iris {
    static class WindowsStartup {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Entry = "IrisNotch";
        static readonly Version FirstAutomaticRegistration = new Version(1, 0, 1);

        internal static string CommandFor(string executable) {
            return "\"" + executable + "\"";
        }

        internal static bool NeedsInitialRegistration(Version installedVersion, Func<string> readCommand) {
            if (installedVersion != null && installedVersion >= FirstAutomaticRegistration) return false;
            string command = readCommand();
            if (string.IsNullOrEmpty(command)) return true;

            // Earlier Iris versions wrote one quoted path with no arguments.
            return command.Length > 2 && command[0] == '"' && command.IndexOf('"', 1) == command.Length - 1
                && !File.Exists(command.Substring(1, command.Length - 2));
        }

        internal static string Read() {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) {
                return key == null ? null : key.GetValue(Entry, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
        }

        internal static void Write(string command) {
            if (command == null) {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) {
                    if (key != null) key.DeleteValue(Entry, false);
                }
                return;
            }
            // Windows limits Run commands to 260 characters.
            if (command.Length > 260) throw new ArgumentException("The program folder path is too long for Windows startup.");
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                if (key == null) throw new InvalidOperationException("Windows startup settings are unavailable.");
                key.SetValue(Entry, command, RegistryValueKind.String);
            }
        }
    }
}
