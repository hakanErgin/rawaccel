using System;
using System.Reflection;
using Microsoft.Win32;

namespace CurveEditor.Services
{
    /// <summary>
    /// The Raw Accel driver forgets its settings when Windows restarts. This registers
    /// "rawaccel-editor.exe --apply" to run at sign-in (per user, no admin needed), which sends
    /// settings.json to the driver and exits without opening a window.
    /// </summary>
    public static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "RawAccelCurveEditor";

        private static string Command => $"\"{Assembly.GetEntryAssembly().Location}\" --apply";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        return key?.GetValue(ValueName) is string value && value == Command;
                    }
                }
                catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }

        /// <summary> Returns null on success, otherwise why it failed. </summary>
        public static string SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled) key.SetValue(ValueName, Command);
                    else key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
                return null;
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is System.IO.IOException)
            {
                return e.Message;
            }
        }
    }
}
