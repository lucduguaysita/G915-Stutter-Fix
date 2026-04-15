using Microsoft.Win32;
using System;
using System.Windows.Forms;

public static class StartupManager
{
    private const string RUN_KEY =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string APP_NAME = "KeyboardRepeatFilter";

    public static bool IsInStartup()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, false))
        {
            if (key == null)
                return false;

            object value = key.GetValue(APP_NAME);
            return value != null;
        }
    }

    public static void AddToStartup()
    {
        string exePath = Application.ExecutablePath;

        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
        {
            key.SetValue(APP_NAME, exePath);
        }
    }

    public static void RemoveFromStartup()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
        {
            key.DeleteValue(APP_NAME, false);
        }
    }

    public static void ToggleStartup()
    {
        if (IsInStartup())
            RemoveFromStartup();
        else
            AddToStartup();
    }
}
