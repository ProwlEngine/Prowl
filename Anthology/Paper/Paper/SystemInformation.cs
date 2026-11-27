using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.PaperUI;

/// <summary>The side of the parent menu on which a submenu should open.</summary>
public enum SubmenuSide { Left, Right }

/// <summary>
/// System-specific input and UI preferences. Each value is read from the OS on
/// first access and cached until <see cref="Refresh"/> is called. Thread-safe.
/// If the OS does not expose a value, a conventional default is returned.
/// Pixel values are in the platform's native units: pixels at the system DPI on
/// Windows (subject to the process's DPI awareness), points on macOS, and
/// logical (unscaled) pixels on Linux.
/// </summary>
public static partial class SystemInformation
{
    // A lazily initialized cache of all the settings.
    private sealed class Cache(Backend b)
    {
        public readonly Lazy<float> DoubleClickTime = new(() => b.GetDoubleClickTime() / 1000f);
        public readonly Lazy<Int2> DoubleClickDistance = new(b.GetDoubleClickDistance);
        public readonly Lazy<float> MenuShowDelay = new(() => b.GetMenuShowDelay() / 1000f);
        public readonly Lazy<float> ToolTipDelay = new(() => b.GetToolTipDelay() / 1000f);
        public readonly Lazy<float> KeyRepeatDelay = new(() => b.GetKeyRepeatDelay() / 1000f);
        public readonly Lazy<float> KeyRepeatInterval = new(() => b.GetKeyRepeatInterval() / 1000f);
        public readonly Lazy<SubmenuSide> SubmenuSide = new(b.GetSubmenuSide);
        public readonly Lazy<Int2> DragThreshold = new(b.GetDragThreshold);
    }
    private static volatile Cache _cache = new(CreateBackend());

    /// <summary>The maximum delay between two clicks that form a double-click, in seconds.</summary>
    public static float DoubleClickTime => _cache.DoubleClickTime.Value;

    /// <summary>A double-click is performed if the pointer moves no more than this many pixels (between each click),
    /// on both the X and Y axes.</summary>
    public static Int2 DoubleClickDistance => _cache.DoubleClickDistance.Value;

    /// <summary>The hover time before a submenu opens, in seconds.</summary>
    public static float MenuShowDelay => _cache.MenuShowDelay.Value;

    /// <summary>The hover time before a tooltip appears, in seconds.</summary>
    public static float ToolTipDelay => _cache.ToolTipDelay.Value;

    /// <summary>How long a key is held before it starts repeating, in seconds.</summary>
    public static float KeyRepeatDelay => _cache.KeyRepeatDelay.Value;

    /// <summary>Time between repeats once repeating has started, in seconds.</summary>
    public static float KeyRepeatInterval => _cache.KeyRepeatInterval.Value;

    /// <summary>The side that submenus should open (left or right).</summary>
    public static SubmenuSide SubmenuSide => _cache.SubmenuSide.Value;

    /// <summary>A drag is performed if the mouse moves more than this distance, in pixels (on either axis).</summary>
    public static Int2 DragThreshold => _cache.DragThreshold.Value;

    /// <summary>Discards all cached values. Call this when the OS reports a settings change.</summary>
    public static void Refresh() => _cache = new Cache(CreateBackend());

    /// <summary>Creates a <see cref="Backend"/> instance that retrieves the values for the current platform.</summary>
    private static Backend CreateBackend()
    {
#if NET9_0_OR_GREATER
        if (OperatingSystem.IsWindows()) return new WindowsBackend();
        if (OperatingSystem.IsMacOS()) return new MacBackend();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()) return new LinuxBackend();
#endif
        return new Backend();
    }

    /// <summary>Generic defaults, used as-is on unrecognised platforms.</summary>
    private class Backend
    {
        // All times are in milliseconds.
        public virtual int GetDoubleClickTime() => 500;
        public virtual Int2 GetDoubleClickDistance() => new(2, 2);
        public virtual int GetMenuShowDelay() => 400;
        public virtual int GetToolTipDelay() => 500;
        public virtual int GetKeyRepeatDelay() => 500;
        public virtual int GetKeyRepeatInterval() => 33;
        public virtual Int2 GetDragThreshold() => new(4, 4);
        public virtual SubmenuSide GetSubmenuSide() => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? SubmenuSide.Left : SubmenuSide.Right;
    }

    // netstandard2.1 does not support NativeLibrary.TryLoad().
#if NET9_0_OR_GREATER

    /// <summary>
    /// Windows-specific system information.
    /// </summary>
    private sealed partial class WindowsBackend : Backend
    {
        private const int SM_CXDOUBLECLK = 36, SM_CYDOUBLECLK = 37;
        private const int SM_CXDRAG = 68, SM_CYDRAG = 69;

        private const uint SPI_GETKEYBOARDSPEED = 0x000A;
        private const uint SPI_GETKEYBOARDDELAY = 0x0016;
        private const uint SPI_GETMENUDROPALIGNMENT = 0x001B;
        private const uint SPI_GETMENUSHOWDELAY = 0x006A;

        [LibraryImport("user32.dll", EntryPoint = "GetDoubleClickTime")]
        private static partial uint NativeDoubleClickTime();

        [LibraryImport("user32.dll")]
        private static partial int GetSystemMetrics(int index);

        [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SystemParametersInfo(uint action, uint param, ref uint value, uint winIni);

        private static int? Query(uint action)
        {
            uint value = 0;
            return SystemParametersInfo(action, 0, ref value, 0) ? (int)value : null;
        }

        private static int Metric(int index, int fallback)
        {
            int value = GetSystemMetrics(index);
            return value > 0 ? value : fallback;
        }

        public override int GetDoubleClickTime() => (int)NativeDoubleClickTime();
        public override Int2 GetDoubleClickDistance() => new(Metric(SM_CXDOUBLECLK, 4) / 2, Metric(SM_CYDOUBLECLK, 4) / 2);
        public override int GetMenuShowDelay() => Query(SPI_GETMENUSHOWDELAY) ?? 400;

        // Native Win32 tooltips use the double-click time as their initial delay.
        // (SPI_GETMOUSEHOVERTIME is a separate setting, for WM_MOUSEHOVER.)
        public override int GetToolTipDelay() => GetDoubleClickTime();

        // 0..3 maps to roughly 250..1000 ms.
        public override int GetKeyRepeatDelay() => ((Query(SPI_GETKEYBOARDDELAY) ?? 1) + 1) * 250;

        // 0..31 maps to roughly 2.5..30 repeats per second.
        public override int GetKeyRepeatInterval() => (int)(1000 / (2.5f + (Query(SPI_GETKEYBOARDSPEED) ?? 31) * (27.5f / 31)));


        // TRUE means menus are right-aligned, so they cascade to the left.
        public override SubmenuSide GetSubmenuSide() =>
            Query(SPI_GETMENUDROPALIGNMENT) switch
            {
                null => base.GetSubmenuSide(),
                0 => SubmenuSide.Right,
                _ => SubmenuSide.Left,
            };

        // Windows defines these as the allowance on each side of the press point.
        public override Int2 GetDragThreshold() =>
            new(Metric(SM_CXDRAG, 4), Metric(SM_CYDRAG, 4));
    }

    /// <summary>
    /// MacOS-specific system information.
    /// </summary>
    private sealed partial class MacBackend : Backend
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr objc_getClass(string name);

        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr sel_registerName(string name);

        [LibraryImport(ObjC)]
        private static partial IntPtr objc_autoreleasePoolPush();

        [LibraryImport(ObjC)]
        private static partial void objc_autoreleasePoolPop(IntPtr pool);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr Send(IntPtr receiver, IntPtr selector, string argument);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial double SendDouble(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial double SendDouble(IntPtr receiver, IntPtr selector, IntPtr argument);

        /// <summary>Reads a class property of NSEvent that is expressed in seconds.</summary>
        private static int? EventInterval(string property)
        {
            if (!NativeLibrary.TryLoad(AppKit, out _)) return null;
            IntPtr cls = objc_getClass("NSEvent");
            if (cls == IntPtr.Zero) return null;
            double seconds = SendDouble(cls, sel_registerName(property));
            return seconds > 0 ? (int)(seconds * 1000) : null;
        }

        public override int GetDoubleClickTime() => EventInterval("doubleClickInterval") ?? 500;
        public override int GetKeyRepeatDelay() => EventInterval("keyRepeatDelay") ?? 500;
        public override int GetKeyRepeatInterval() => EventInterval("keyRepeatInterval") ?? 33;
        public override int GetMenuShowDelay() => 200;  // ~200-250ms matches standard AppKit NSMenu behaviour.

        // NSInitialToolTipDelay (milliseconds) is only present if someone set it.
        public override int GetToolTipDelay()
        {
            int fallback = 1000;
            if (!NativeLibrary.TryLoad(AppKit, out _)) return fallback;

            IntPtr pool = objc_autoreleasePoolPush();
            try
            {
                IntPtr defaults = Send(objc_getClass("NSUserDefaults"),
                    sel_registerName("standardUserDefaults"));
                IntPtr key = Send(objc_getClass("NSString"),
                    sel_registerName("stringWithUTF8String:"), "NSInitialToolTipDelay");
                double ms = SendDouble(defaults, sel_registerName("doubleForKey:"), key);
                return ms > 0 ? (int)ms : fallback;
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }
    }

    /// <summary>
    /// Linux-specific system information.
    /// </summary>
    /// <remarks>
    /// Sources, in priority order: the desktop's own configuration (KDE or GNOME),
    /// the X server (key repeat only), GTK's settings.ini files, then defaults.
    /// </remarks>
    private sealed partial class LinuxBackend : Backend
    {
        private const string Mouse = "org.gnome.desktop.peripherals.mouse";
        private const string Keyboard = "org.gnome.desktop.peripherals.keyboard";

        private readonly string[] _desktops =
            (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
                .ToUpperInvariant()
                .Split(':', StringSplitOptions.RemoveEmptyEntries);

        private readonly ConcurrentDictionary<string, Dictionary<(string, string), string>>
            _iniFiles = new();

        private readonly Lazy<(int Delay, int Interval)?> _x11Repeat = new(QueryX11Repeat);

        private bool IsKde => _desktops.Contains("KDE");
        private bool IsGnome => _desktops.Any(d => d is "GNOME" or "UNITY" or "BUDGIE" or "PANTHEON");

        public override int GetDoubleClickTime() =>
            Kde("kdeglobals", "KDE", "DoubleClickInterval", 400)
            ?? Gnome(Mouse, "double-click")
            ?? Gtk("gtk-double-click-time")
            ?? 400;

        public override Int2 GetDoubleClickDistance()
        {
            int distance = Gtk("gtk-double-click-distance") ?? 5;
            return new(distance, distance);
        }

        public override int GetMenuShowDelay() => Gtk("gtk-menu-popup-delay") ?? 225;
        public override int GetToolTipDelay() => Gtk("gtk-tooltip-timeout") ?? 500;
        public override int GetKeyRepeatDelay() =>
            Kde("kcminputrc", "Keyboard", "RepeatDelay", 600)
            ?? Gnome(Keyboard, "delay")
            ?? _x11Repeat.Value?.Delay
            ?? 500;

        public override int GetKeyRepeatInterval()
        {
            // KDE stores a rate in Hz, which may be fractional.
            if (IsKde)
            {
                double rate = FromIni("kcminputrc", "Keyboard", "RepeatRate") ?? 25;
                if (rate > 0) return (int)(1000 / rate);
            }

            return Gnome(Keyboard, "repeat-interval") ?? _x11Repeat.Value?.Interval ?? 33;
        }

        public override Int2 GetDragThreshold()
        {
            int d = Kde("kdeglobals", "KDE", "StartDragDist", 10)
                ?? Gnome(Mouse, "drag-threshold")
                ?? Gtk("gtk-dnd-drag-threshold")
                ?? 8;
            return new(d, d);
        }

        private int? Kde(string file, string group, string key, int kdeDefault) => IsKde ? (int?)FromIni(file, group, key) ?? kdeDefault : null;
        private int? Gnome(string schema, string key) => IsGnome ? RunGSettings(schema, key) : null;
        private int? Gtk(string key) => (int?)(FromIni("gtk-4.0/settings.ini", "Settings", key) ?? FromIni("gtk-3.0/settings.ini", "Settings", key));

        /// <summary>Searches the user's config directory, then the system ones.</summary>
        private double? FromIni(string relativePath, string group, string key)
        {
            foreach (string directory in ConfigDirectories())
            {
                var values = _iniFiles.GetOrAdd(Path.Combine(directory, relativePath), ReadIni);
                if (values.TryGetValue((group, key), out var text) && Number(text) is double n)
                    return n;
            }
            return null;
        }

        private static IEnumerable<string> ConfigDirectories()
        {
            string? user = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(user))
                user = Path.Combine(
                    Environment.GetEnvironmentVariable("HOME") ?? "/", ".config");
            yield return user;

            string? system = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");
            if (string.IsNullOrEmpty(system)) system = "/etc/xdg";
            foreach (var d in system.Split(':', StringSplitOptions.RemoveEmptyEntries))
                yield return d;

            yield return "/etc"; // GTK's usual system-wide location
        }

        private static Dictionary<(string, string), string> ReadIni(string path)
        {
            var result = new Dictionary<(string, string), string>();
            try
            {
                if (!File.Exists(path)) return result;
                string section = "";
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        section = line[1..^1];
                        continue;
                    }
                    int equals = line.IndexOf('=');
                    if (equals > 0 && line[0] != '#' && line[0] != ';')
                        result[(section, line[..equals].Trim())] = line[(equals + 1)..].Trim();
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An unreadable file is treated like a missing one.
            }
            return result;
        }

        private static double? Number(string text) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.IsFinite(n) && n >= 0 ? n : null;

        private static int? RunGSettings(string schema, string key)
        {
            // Use an absolute path: a bare name could resolve to a file in the
            // application's directory or the current directory.
            string? exe = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(':', StringSplitOptions.RemoveEmptyEntries)
                .Where(Path.IsPathRooted)
                .Select(d => Path.Combine(d, "gsettings"))
                .FirstOrDefault(File.Exists);
            if (exe is null) return null;

            try
            {
#if NET11_0_OR_GREATER
                var result = Process.RunAndCaptureText(
                    fileName: exe,
                    arguments: ["get", schema, key],
                    timeout: TimeSpan.FromSeconds(1));
                if (result.ExitStatus.ExitCode != 0 || result.ExitStatus.Canceled)
                    return null;

                string output = result.StandardOutput;
#else
                var info = new ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                info.ArgumentList.Add("get");
                info.ArgumentList.Add(schema);
                info.ArgumentList.Add(key);

                using var process = Process.Start(info);
                if (process is null) return null;

                var outputTask = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(1000))
                {
                    process.Kill();
                    return null;
                }
                if (process.ExitCode != 0) return null;
                string output = outputTask.GetAwaiter().GetResult();
#endif

                // Output looks like "400" or "uint32 500".
                string[] parts = output.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? (int?)Number(parts[^1]) : null;
            }
            catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException or TimeoutException)
            {
                return null;
            }
        }

        [LibraryImport("libX11.so.6")]
        private static partial IntPtr XOpenDisplay(IntPtr name);

        [LibraryImport("libX11.so.6")]
        private static partial int XCloseDisplay(IntPtr display);

        [LibraryImport("libX11.so.6")]
        private static partial int XkbGetAutoRepeatRate(IntPtr display, uint deviceSpec, out uint delay, out uint interval);

        private static (int Delay, int Interval)? QueryX11Repeat()
        {
            // Under Wayland, XWayland's values need not match the compositor's.
            if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland")
                return null;

            const uint XkbUseCoreKbd = 0x0100;
            try
            {
                IntPtr display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return null;
                try
                {
                    return XkbGetAutoRepeatRate(display, XkbUseCoreKbd, out uint delay, out uint interval) != 0
                            ? ((int)delay, (int)interval)
                            : null;
                }
                finally
                {
                    _ = XCloseDisplay(display);
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        }
    }

#endif
}
