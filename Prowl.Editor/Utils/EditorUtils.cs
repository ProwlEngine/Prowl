using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Prowl.Editor.Utils;

public static class EditorUtils
{
    public static void OpenUrl(string url)
    {
        try
        {
            // For Windows: Launching via shell
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            // For macOS: Use the 'open' command
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            // For Linux: Use 'xdg-open'
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to open link: {ex.Message}");
        }
    }

    public static void OpenFileSystemPath(string absPath)
    {
        try
        {
            // For Windows: Launching via shell
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (Directory.Exists(absPath))
                {
                    Process.Start("explorer.exe", absPath);
                }
                else if (File.Exists(absPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{absPath}\"");
                }
            }
            // For macOS: Use the 'open' command
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // The -R flag tells Finder to "reveal" the item (select it)
                Process.Start("open", $"-R \"{absPath}\"");
            }
            // For Linux: Use 'xdg-open'
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Linux varies by distro; 'dbus-send' or 'xdg-open' are common alternatives
                Process.Start("xdg-open", $"\"{Path.GetDirectoryName(absPath)}\"");
            }
        }
        catch { }
    }
}
