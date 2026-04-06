using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Prowl.Editor;

public static class WebService
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
}