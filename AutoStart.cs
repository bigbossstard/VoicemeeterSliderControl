using Microsoft.Win32;
using System.Windows.Forms;

namespace VoicemeeterSliderControl
{
    public static class AutoStart
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "VoicemeeterSliderControl";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return false;

                if (enabled)
                    key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
                else
                    key.DeleteValue(AppName, false);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
