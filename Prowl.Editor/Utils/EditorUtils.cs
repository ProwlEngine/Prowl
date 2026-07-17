// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

using Prowl.Runtime;

namespace Prowl.Editor.Utils;

public static class EditorUtils
{
    public static bool TryParseNonEmptyGuid(string? s, out Guid result)
    {
        result = Guid.Empty;
        return !string.IsNullOrEmpty(s) && Guid.TryParse(s, out result) && result != Guid.Empty;
    }

    public static IEnumerable<Type> GetAllTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (IsFrameworkAssembly(assembly)) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }
            foreach (var type in types)
                yield return type;
        }
    }

    public static bool IsFrameworkAssembly(Assembly assembly)
    {
        string? name = assembly.GetName().Name;
        if (string.IsNullOrEmpty(name)) return true;
        return name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft", StringComparison.Ordinal)
            || name == "mscorlib" || name == "netstandard" || name == "WindowsBase";
    }

    public static IEnumerable<MethodInfo> GetAllMethods(BindingFlags flags)
    {
        foreach (var type in GetAllTypes())
            foreach (var method in type.GetMethods(flags))
                yield return method;
    }

    public static string GetHierarchyPath(GameObject go)
    {
        var parent = go.Parent;
        if (!parent.IsValid()) return "";
        var parts = new List<string>();
        while (parent.IsValid())
        {
            parts.Add(parent.Name);
            parent = parent.Parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (Directory.Exists(absPath))
                    Process.Start("explorer.exe", absPath);
                else if (File.Exists(absPath))
                    Process.Start("explorer.exe", $"/select,\"{absPath}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", $"-R \"{absPath}\"");
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", $"\"{Path.GetDirectoryName(absPath)}\"");
        }
        catch { }
    }
}
