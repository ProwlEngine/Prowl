// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Reflection;

namespace Prowl.Editor.Core;

/// <summary>Registers the editor with Linux desktops so its window gets a name and icon in the taskbar.</summary>
public static class DesktopEntry
{
    public const string AppId = "prowl-editor";

    // Wayland compositors find a window's name and icon by matching its app_id to <app_id>.desktop
    public static void Install()
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

            string iconPath = Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", AppId + ".png");
            using (Stream? icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("prowl.png"))
            {
                if (icon == null) return;
                using var buffer = new MemoryStream();
                icon.CopyTo(buffer);
                WriteIfChanged(iconPath, buffer.ToArray());
            }

            string entry =
                "[Desktop Entry]\n" +
                "Version=1.0\n" +
                "Type=Application\n" +
                "Terminal=false\n" +
                $"Exec=\"{ExecutablePath()}\"\n" +
                "Name=Prowl Editor\n" +
                $"Icon={iconPath}\n" +
                $"StartupWMClass={AppId}\n" +
                "Categories=Development;\n";
            WriteIfChanged(Path.Combine(dataHome, "applications", AppId + ".desktop"), System.Text.Encoding.UTF8.GetBytes(entry));
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogWarning($"Could not register the desktop entry: {ex.Message}");
        }
    }

    // Under `dotnet run` the process is the dotnet host, so prefer the apphost next to the editor
    private static string ExecutablePath()
    {
        string apphost = Path.Combine(AppContext.BaseDirectory, "Prowl.Editor");
        return File.Exists(apphost) ? apphost : Environment.ProcessPath ?? apphost;
    }

    private static void WriteIfChanged(string path, byte[] contents)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(contents)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
    }
}
