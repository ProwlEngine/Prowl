// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Prowl.Editor.Theming;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.Registries;

/// <summary>
/// Registers icons for file extensions. One or more extensions (leading dot, lowercase)
/// map to a single icon string (typically an <see cref="EditorIcons"/> constant).
/// Users can register icons for custom file types by decorating a static method with
/// <see cref="FileIconProviderAttribute"/> or by calling <see cref="FileIconRegistry.Register"/>
/// directly from an InitializeOnLoad method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class FileIconAttribute : Attribute
{
    public string[] Extensions { get; }
    public FileIconAttribute(params string[] extensions) => Extensions = extensions;
}

/// <summary>
/// Decorate a static method <c>void Register()</c> to register one or many file icons at
/// startup. An alternative to <see cref="FileIconAttribute"/> for bulk registration.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FileIconProviderAttribute : Attribute { }

public static class FileIconRegistry
{
    private static readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private static string _defaultIcon = EditorIcons.File;
    private static bool _initialized;

    public static void Reinitialize() { _initialized = false; Initialize(); }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _icons.Clear();
        RegisterBuiltIns();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    // [FileIcon(".ext", ...)] on a static method returning the icon string
                    foreach (var attr in method.GetCustomAttributes<FileIconAttribute>())
                    {
                        if (method.ReturnType != typeof(string) || method.GetParameters().Length != 0) continue;
                        try
                        {
                            var icon = (string?)method.Invoke(null, null);
                            if (string.IsNullOrEmpty(icon)) continue;
                            foreach (var ext in attr.Extensions) Register(ext, icon!);
                        }
                        catch (Exception ex) { Debug.LogWarning($"FileIconRegistry: {type.Name}.{method.Name} threw: {ex.Message}"); }
                    }

                    // [FileIconProvider] on a static void method that calls Register(...) itself
                    if (method.GetCustomAttribute<FileIconProviderAttribute>() != null
                        && method.ReturnType == typeof(void) && method.GetParameters().Length == 0)
                    {
                        try { method.Invoke(null, null); }
                        catch (Exception ex) { Debug.LogWarning($"FileIconRegistry: {type.Name}.{method.Name} threw: {ex.Message}"); }
                    }
                }
            }
        }
    }

    /// <summary>Register an icon for one extension. Extension should include the leading dot.</summary>
    public static void Register(string extension, string icon)
    {
        if (string.IsNullOrEmpty(extension) || string.IsNullOrEmpty(icon)) return;
        _icons[extension] = icon;
    }

    /// <summary>Register the same icon for many extensions.</summary>
    public static void Register(string icon, params string[] extensions)
    {
        foreach (var ext in extensions) Register(ext, icon);
    }

    /// <summary>Override the icon returned when no mapping is found (default: plain file).</summary>
    public static void SetDefault(string icon) { if (!string.IsNullOrEmpty(icon)) _defaultIcon = icon; }

    /// <summary>Look up an icon for an extension (must include leading dot, case-insensitive).</summary>
    public static string GetIconForExtension(string extension)
        => !string.IsNullOrEmpty(extension) && _icons.TryGetValue(extension, out var icon) ? icon : _defaultIcon;

    /// <summary>Look up an icon for a file name by extracting its extension.</summary>
    public static string GetIconForFile(string fileName)
        => GetIconForExtension(Path.GetExtension(fileName ?? ""));

    private static void RegisterBuiltIns()
    {
        Register(EditorIcons.FileCode, ".cs", ".js", ".ts", ".py", ".lua");
        Register(EditorIcons.WandMagicSparkles, ".shader", ".glsl", ".hlsl", ".shadergraph");
        Register(EditorIcons.FileImage, ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tga", ".psd", ".hdr");
        Register(EditorIcons.FileAudio, ".mp3", ".wav", ".ogg", ".flac");
        Register(EditorIcons.FileVideo, ".mp4", ".avi", ".mkv", ".mov");
        Register(EditorIcons.VectorSquare, ".fbx", ".obj", ".gltf", ".glb", ".dae", ".mesh");
        Register(EditorIcons.Cubes, ".scene", ".prefab");
        Register(EditorIcons.Palette, ".mat", ".material");
        Register(EditorIcons.FilePdf, ".pdf");
        Register(EditorIcons.FileLines, ".txt", ".md", ".log", ".json", ".xml", ".yaml", ".yml");
        Register(EditorIcons.FileZipper, ".zip", ".rar", ".7z", ".tar", ".gz", ".prowlpackage");
        Register(EditorIcons.Gear, ".exe", ".dll", ".so");
    }
}
