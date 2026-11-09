using System;
using Microsoft.Win32;

namespace Iris {
    static class WindowsStartup {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Entry = "IrisNotch";

        internal static string CommandFor(string executable) {
            return "\"" + executable + "\"";
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
