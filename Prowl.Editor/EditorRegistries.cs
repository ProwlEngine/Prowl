using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Inspector;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Thumbnails;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Editor.Theming;
using Prowl.Editor.Core;
using Prowl.Editor.Projects;
using Prowl.Editor.Utils;

namespace Prowl.Editor;

public struct AssetMenuEntry
{
    public Type Type;
    public string Name;
    public string Extension;
    public string Icon;
    public int Order;
    public Func<EngineObject>? Factory;
}

public static class EditorRegistries
{
    #region Types

    public struct SettingsEntry
    {
        public Type Type;
        public string Name;
        public string Icon;
        public int Order;
        public bool ExportToBuild;
        public ProjectSettingsBase Instance;
    }

    public delegate bool AssetDoubleClickHandler(string relativePath, Guid guid);

    private struct SceneViewEditorEntry
    {
        public Type ComponentType;
        public Type EditorType;
        public int Priority;
    }

    private struct DropHandlerEntry
    {
        public Type AssetType;
        public int Order;
        public ISceneDropHandler Handler;
    }

    #endregion

    #region Data

    private static readonly Dictionary<Type, Type> _customEditorTypes = new();
    private static readonly Dictionary<Type, CustomEditor> _customEditorCache = new();
    private static readonly Dictionary<Type, Type> _propertyEditorTypes = new();
    private static readonly Dictionary<Type, PropertyEditor> _propertyEditorCache = new();
    private static readonly Dictionary<Type, Type> _assetEditorTypes = new();
    private static readonly Dictionary<Type, AssetImporterEditor> _assetEditorCache = new();

    private static readonly Dictionary<string, Type> _importersByExt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type> _importersByName = new(StringComparer.OrdinalIgnoreCase);
    public static IEnumerable<string> RegisteredImporterExtensions => _importersByExt.Keys;

    private static readonly Dictionary<Type, string> _componentIcons = new();

    private static readonly Dictionary<Type, IThumbnailGenerator> _thumbnailGenerators = new();

    private static readonly List<SceneViewEditorEntry> _sceneViewEditors = [];
    private static readonly Dictionary<Type, ISceneViewEditor> _sceneViewEditorInstances = [];

    private static readonly List<DropHandlerEntry> _dropHandlers = [];

    private static readonly List<SettingsEntry> _settingsEntries = [];
    public static IReadOnlyList<SettingsEntry> SettingsEntries => _settingsEntries;

    private static readonly List<ScriptTemplate> _scriptTemplates = [];
    public static IReadOnlyList<ScriptTemplate> ScriptTemplates => _scriptTemplates;

    private static readonly Dictionary<string, string> _fileIcons = new(StringComparer.OrdinalIgnoreCase);
    private static string _defaultFileIcon = EditorIcons.File;

    private static readonly Dictionary<string, AssetDoubleClickHandler> _doubleClickHandlers = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<Action> _sceneSavedCallbacks = [];
    private static readonly List<Action> _undoRedoCallbacks = [];

    private static readonly List<MethodInfo> _initOnLoadMethods = [];

    #endregion

    #region Lifecycle

    private static bool _initialized;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { ClearAll(); Initialize(); OnProjectOpened(); }

    [Runtime.OnAssemblyUnload]
    public static void ClearAll()
    {
        _initialized = false;

        _customEditorTypes.Clear(); _customEditorCache.Clear();
        _propertyEditorTypes.Clear(); _propertyEditorCache.Clear();
        _assetEditorTypes.Clear(); _assetEditorCache.Clear();

        _importersByExt.Clear();
        _importersByName.Clear();

        _componentIcons.Clear();
        _thumbnailGenerators.Clear();

        SceneViewPanel.DeactivateSceneViewEditor();
        _sceneViewEditors.Clear();
        _sceneViewEditorInstances.Clear();
        _dropHandlers.Clear();

        _settingsEntries.Clear();

        MenuItemAttribute.Clear();
        _scriptTemplates.Clear();

        _fileIcons.Clear();
        _defaultFileIcon = EditorIcons.File;
        _doubleClickHandlers.Clear();

        foreach (var del in _sceneSavedCallbacks) EditorSceneManager.OnSceneSaved -= del;
        _sceneSavedCallbacks.Clear();
        foreach (var del in _undoRedoCallbacks) Undo.OnUndoRedo -= del;
        _undoRedoCallbacks.Clear();

        _initOnLoadMethods.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        RegisterBuiltInFileIcons();

        var methodFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var type in EditorUtils.GetAllTypes())
        {
            // A single type/method with unresolvable reflection metadata (e.g. an attribute referencing
            // a mismatched assembly version - seen from test-host assemblies like
            // Microsoft.TestPlatform.CoreUtilities, which Prowl never intended to scan in the first
            // place) must not abort scanning every other type in the AppDomain.
            try
            {
                ScanCustomEditor(type);
                ScanPropertyEditor(type);
                ScanAssetEditor(type);
                ScanImporter(type);
                ScanComponentIcon(type);
                ScanThumbnailGenerator(type);
                ScanSceneViewEditor(type);
                ScanSceneDropHandler(type);
                ScanProjectSettings(type);
                ScanAssetMenuEntry(type);

                foreach (var method in type.GetMethods(methodFlags))
                {
                    MenuItemAttribute.Scan(method);
                    ScanScriptTemplate(method);
                    ScanFileIconMethod(method);
                    ScanDoubleClickHandler(method);
                    ScanEditorCallback(method);
                    ScanInitializeOnLoad(method);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"EditorRegistries: skipped scanning '{type.FullName}': {ex.Message}");
            }
        }

        _scriptTemplates.Sort((a, b) =>
        {
            int c = a.Order.CompareTo(b.Order);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        _sceneViewEditors.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        _dropHandlers.Sort((a, b) => a.Order.CompareTo(b.Order));
        _settingsEntries.Sort((a, b) => a.Order.CompareTo(b.Order));

        foreach (var del in _sceneSavedCallbacks) EditorSceneManager.OnSceneSaved += del;
        foreach (var del in _undoRedoCallbacks) Undo.OnUndoRedo += del;

        foreach (var m in _initOnLoadMethods)
        {
            try { m.Invoke(null, null); }
            catch (Exception ex) { Debug.LogError($"[InitializeOnLoad] {m.DeclaringType?.Name}.{m.Name}: {ex.InnerException?.Message ?? ex.Message}"); }
        }

        Debug.Log($"EditorRegistries: {_customEditorTypes.Count} custom editors, {_propertyEditorTypes.Count} property editors, " +
                  $"{_assetEditorTypes.Count} asset editors, {_importersByName.Count} importers, " +
                  $"{_thumbnailGenerators.Count} thumbnail generators, {_settingsEntries.Count} settings.");
    }

    #endregion

    #region Scanners

    private static void ScanCustomEditor(Type type)
    {
        if (!typeof(CustomEditor).IsAssignableFrom(type) || type.IsAbstract) return;
        var target = type.GetCustomAttribute<CustomEditorAttribute>()?.TargetType;
        if (target != null) _customEditorTypes[target] = type;
    }

    private static void ScanPropertyEditor(Type type)
    {
        if (!typeof(PropertyEditor).IsAssignableFrom(type) || type.IsAbstract) return;
        var target = type.GetCustomAttribute<CustomPropertyEditorAttribute>()?.TargetType;
        if (target != null) _propertyEditorTypes[target] = type;
    }

    private static void ScanAssetEditor(Type type)
    {
        if (!typeof(AssetImporterEditor).IsAssignableFrom(type) || type.IsAbstract) return;
        var target = type.GetCustomAttribute<CustomAssetEditorAttribute>()?.TargetType;
        if (target != null) _assetEditorTypes[target] = type;
    }

    private static void ScanImporter(Type type)
    {
        if (!typeof(AssetImporter).IsAssignableFrom(type) || type.IsAbstract) return;
        var attr = type.GetCustomAttribute<ImporterForAttribute>();
        if (attr == null) return;
        _importersByName[type.Name] = type;
        foreach (var ext in attr.Extensions)
            _importersByExt[NormalizeExt(ext)] = type;
    }

    private static void ScanComponentIcon(Type type)
    {
        var attr = type.GetCustomAttribute<ComponentIconAttribute>(inherit: false);
        if (attr != null && !string.IsNullOrEmpty(attr.Icon))
            _componentIcons[type] = attr.Icon;
    }

    private static void ScanThumbnailGenerator(Type type)
    {
        if (type.IsAbstract || !typeof(IThumbnailGenerator).IsAssignableFrom(type)) return;
        var attr = type.GetCustomAttribute<CustomThumbnailGeneratorAttribute>();
        if (attr == null) return;
        try { _thumbnailGenerators[attr.TargetType] = (IThumbnailGenerator)Activator.CreateInstance(type)!; }
        catch { }
    }

    private static void ScanSceneViewEditor(Type type)
    {
        if (type.IsInterface || type.IsAbstract || !typeof(ISceneViewEditor).IsAssignableFrom(type)) return;
        var attr = type.GetCustomAttribute<SceneViewEditorForAttribute>();
        if (attr == null) return;
        try
        {
            var instance = (ISceneViewEditor)Activator.CreateInstance(type)!;
            _sceneViewEditorInstances[type] = instance;
            _sceneViewEditors.Add(new SceneViewEditorEntry { ComponentType = attr.ComponentType, EditorType = type, Priority = instance.Priority });
        }
        catch { }
    }

    private static void ScanSceneDropHandler(Type type)
    {
        if (type.IsAbstract || !typeof(ISceneDropHandler).IsAssignableFrom(type)) return;
        var attr = type.GetCustomAttribute<SceneDropHandlerAttribute>();
        if (attr == null) return;
        _dropHandlers.Add(new DropHandlerEntry
        {
            AssetType = attr.TargetType,
            Order = attr.Order,
            Handler = (ISceneDropHandler)Activator.CreateInstance(type)!,
        });
    }

    private static void ScanProjectSettings(Type type)
    {
        if (type.IsAbstract || !typeof(ProjectSettingsBase).IsAssignableFrom(type)) return;
        var attr = type.GetCustomAttribute<ProjectSettingsAttribute>();
        if (attr == null) return;
        _settingsEntries.Add(new SettingsEntry
        {
            Type = type,
            Name = attr.Name,
            Icon = attr.Icon,
            Order = attr.Order,
            ExportToBuild = attr.ExportToBuild,
            Instance = (ProjectSettingsBase)Activator.CreateInstance(type)!,
        });
    }

    private static void ScanAssetMenuEntry(Type type)
    {
        if (type.IsAbstract || !typeof(EngineObject).IsAssignableFrom(type)) return;
        var attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
        if (attr == null) return;
        var entry = new AssetMenuEntry
        {
            Type = type,
            Name = attr.Name,
            Extension = attr.Extension,
            Icon = attr.Icon,
            Order = attr.Order,
        };
        MenuItemAttribute.Register("Assets/Create/" + attr.Name, () =>
        {
            var task = new Core.Tasks.CreateAssetTask();
            task.TaskType = Core.Tasks.CreateAssetTask.AssetType.Asset;
            task.BeginCreateTask(entry, AssetCreateMenu.GetCurrentFolder());
        }, attr.Order, attr.Icon);
    }

    private static void ScanScriptTemplate(MethodInfo method)
    {
        var attr = method.GetCustomAttribute<ScriptTemplateAttribute>();
        if (attr == null) return;
        if (method.ReturnType != typeof(string) || method.GetParameters() is not { Length: 1 } p || p[0].ParameterType != typeof(string))
        {
            Debug.LogWarning($"EditorRegistries: {method.DeclaringType?.Name}.{method.Name} must be 'string Generate(string className)'");
            return;
        }
        try
        {
            var del = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), method);
            _scriptTemplates.Add(new ScriptTemplate(attr.Name, attr.Description, attr.Icon, attr.Order, del));
        }
        catch (Exception ex) { Debug.LogWarning($"EditorRegistries: failed to bind script template {method.Name}: {ex.Message}"); }
    }

    private static void ScanFileIconMethod(MethodInfo method)
    {
        foreach (var attr in method.GetCustomAttributes<FileIconAttribute>())
        {
            if (method.ReturnType != typeof(string) || method.GetParameters().Length != 0) continue;
            try
            {
                var icon = (string?)method.Invoke(null, null);
                if (string.IsNullOrEmpty(icon)) continue;
                foreach (var ext in attr.Extensions) RegisterFileIcon(ext, icon!);
            }
            catch (Exception ex) { Debug.LogWarning($"EditorRegistries: FileIcon method {method.Name} threw: {ex.Message}"); }
        }

        if (method.GetCustomAttribute<FileIconProviderAttribute>() != null
            && method.ReturnType == typeof(void) && method.GetParameters().Length == 0)
        {
            try { method.Invoke(null, null); }
            catch (Exception ex) { Debug.LogWarning($"EditorRegistries: FileIconProvider {method.Name} threw: {ex.Message}"); }
        }
    }

    private static void ScanDoubleClickHandler(MethodInfo method)
    {
        foreach (var attr in method.GetCustomAttributes<AssetDoubleClickHandlerAttribute>())
        {
            if (method.ReturnType != typeof(bool)) continue;
            var pars = method.GetParameters();
            if (pars.Length != 2 || pars[0].ParameterType != typeof(string) || pars[1].ParameterType != typeof(Guid)) continue;
            try
            {
                var del = (AssetDoubleClickHandler)Delegate.CreateDelegate(typeof(AssetDoubleClickHandler), method);
                foreach (var ext in attr.Extensions) RegisterDoubleClickHandler(ext, del);
            }
            catch (Exception ex) { Debug.LogWarning($"EditorRegistries: failed to bind double-click handler {method.Name}: {ex.Message}"); }
        }
    }

    private static void ScanEditorCallback(MethodInfo method)
    {
        if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) return;
        if (method.GetCustomAttribute<OnSceneSavedAttribute>() != null)
        {
            var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
            _sceneSavedCallbacks.Add(del);
        }
        if (method.GetCustomAttribute<OnUndoRedoAttribute>() != null)
        {
            var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
            _undoRedoCallbacks.Add(del);
        }
    }

    private static void ScanInitializeOnLoad(MethodInfo method)
    {
        if (method.GetCustomAttribute<InitializeOnLoadAttribute>() == null) return;
        if (method.GetParameters().Length != 0) return;
        _initOnLoadMethods.Add(method);
    }

    #endregion

    #region Lookups

    public static CustomEditor? GetCustomEditor(Type type) => LookupEditor(type, _customEditorTypes, _customEditorCache);
    public static PropertyEditor? GetPropertyEditor(Type type) => LookupEditor(type, _propertyEditorTypes, _propertyEditorCache, checkInterfaces: true);
    public static AssetImporterEditor? GetAssetEditor(Type type) => LookupEditor(type, _assetEditorTypes, _assetEditorCache);

    private static T? LookupEditor<T>(Type targetType, Dictionary<Type, Type> types, Dictionary<Type, T> cache, bool checkInterfaces = false) where T : class
    {
        if (cache.TryGetValue(targetType, out var cached)) return cached;
        for (var t = targetType; t != null; t = t.BaseType)
        {
            if (!types.TryGetValue(t, out var editorType)) continue;
            return cache[targetType] = (T)Activator.CreateInstance(editorType)!;
        }
        if (checkInterfaces)
            foreach (var iface in targetType.GetInterfaces())
                if (types.TryGetValue(iface, out var editorType))
                    return cache[targetType] = (T)Activator.CreateInstance(editorType)!;
        return null;
    }

    public static AssetImporter? GetImporter(string extension)
    {
        if (_importersByExt.TryGetValue(NormalizeExt(extension), out var type))
            return (AssetImporter)Activator.CreateInstance(type)!;
        return null;
    }

    public static string GetImporterTypeName(string extension)
        => _importersByExt.TryGetValue(NormalizeExt(extension), out var type) ? type.Name : "DefaultImporter";

    public static AssetImporter? CreateImporterByName(string typeName)
    {
        if (_importersByName.TryGetValue(typeName, out var type))
            return (AssetImporter)Activator.CreateInstance(type)!;
        return null;
    }

    private static string NormalizeExt(string ext)
        => ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();

    public static string GetComponentIcon(MonoBehaviour component) => GetComponentIcon(component.GetType());

    public static string GetComponentIcon(Type componentType)
    {
        for (var cur = componentType; cur != null && cur != typeof(object); cur = cur.BaseType)
            if (_componentIcons.TryGetValue(cur, out var icon)) return icon;
        return EditorIcons.PuzzlePiece;
    }

    public static IThumbnailGenerator? GetThumbnailGenerator(Type type)
    {
        if (_thumbnailGenerators.TryGetValue(type, out var gen)) return gen;
        for (var t = type.BaseType; t != null && t != typeof(object); t = t.BaseType)
            if (_thumbnailGenerators.TryGetValue(t, out gen)) return gen;
        return null;
    }

    public static ISceneViewEditor? FindSceneViewEditor(GameObject go)
    {
        foreach (var entry in _sceneViewEditors)
            if (go.GetComponent(entry.ComponentType) != null)
                return _sceneViewEditorInstances[entry.EditorType];
        return null;
    }

    public static ISceneDropHandler? FindSceneDropHandler(Type? assetType)
    {
        if (assetType == null) return null;
        foreach (var entry in _dropHandlers)
            if (entry.AssetType.IsAssignableFrom(assetType)) return entry.Handler;
        return null;
    }

    #endregion

    #region Settings

    public static T GetSettings<T>() where T : ProjectSettingsBase
    {
        foreach (var entry in _settingsEntries)
            if (entry.Instance is T t) return t;

        if (!_initialized)
        {
            Initialize();
            foreach (var entry in _settingsEntries)
                if (entry.Instance is T t) return t;
        }

        Debug.LogWarning($"Settings type {typeof(T).Name} not registered; returning a transient default.");
        return (T)Activator.CreateInstance(typeof(T))!;
    }

    public static void SaveSettings()
    {
        var project = Project.Current;
        if (project == null) return;
        Directory.CreateDirectory(project.ProjectSettingsPath);
        foreach (var entry in _settingsEntries) SaveSettings(entry);
    }

    public static void SaveSettings(SettingsEntry entry)
    {
        var project = Project.Current;
        if (project == null) return;
        string path = Path.Combine(project.ProjectSettingsPath, $"{entry.Name}.yaml");
        try
        {
            var echo = Prowl.Echo.Serializer.Serialize(entry.Instance, TypeMode.Auto);
            File.WriteAllText(path, echo.WriteToYaml());
        }
        catch (Exception ex) { Debug.LogError($"Failed to save settings '{entry.Name}': {ex.Message}"); }
    }

    public static void OnProjectOpened()
    {
        foreach (var entry in _settingsEntries)
            entry.Instance.ResetToDefaults();
        LoadSettings();
    }

    private static void LoadSettings()
    {
        var project = Project.Current;
        if (project == null) return;
        foreach (var entry in _settingsEntries)
        {
            string yamlPath = Path.Combine(project.ProjectSettingsPath, $"{entry.Name}.yaml");
            bool loaded = false;
            if (File.Exists(yamlPath))
            {
                try
                {
                    var serialized = EchoObject.ReadFromYaml(File.ReadAllText(yamlPath));
                    var data = (ProjectSettingsBase?)Prowl.Echo.Serializer.Deserialize(serialized, entry.Type);
                    if (data != null)
                    {
                        CopySettingsFields(data, entry.Instance);
                        Debug.Log($"Loaded settings: {entry.Name}");
                        loaded = true;
                    }
                }
                catch (Exception ex) { Debug.LogError($"Failed to load settings '{entry.Name}': {ex.Message}"); }
            }
            if (!loaded) entry.Instance.ResetToDefaults();
            entry.Instance.Apply();
        }
    }

    internal static void CopySettingsFields(object source, object target)
    {
        var type = source.GetType();
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            field.SetValue(target, field.GetValue(source));
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (prop.CanRead && prop.CanWrite)
                prop.SetValue(target, prop.GetValue(source));
    }

    #endregion

    #region File Icons

    public static string GetFileIcon(string fileName) => GetFileIconForExtension(Path.GetExtension(fileName ?? ""));

    public static string GetFileIconForExtension(string extension)
        => !string.IsNullOrEmpty(extension) && _fileIcons.TryGetValue(extension, out var icon) ? icon : _defaultFileIcon;

    public static void RegisterFileIcon(string extension, string icon)
    {
        if (string.IsNullOrEmpty(extension) || string.IsNullOrEmpty(icon)) return;
        _fileIcons[extension] = icon;
    }

    public static void RegisterFileIcons(string icon, params string[] extensions)
    {
        foreach (var ext in extensions) RegisterFileIcon(ext, icon);
    }

    private static void RegisterBuiltInFileIcons()
    {
        RegisterFileIcons(EditorIcons.FileCode, ".cs", ".js", ".ts", ".py", ".lua");
        RegisterFileIcons(EditorIcons.WandMagicSparkles, ".shader", ".glsl", ".hlsl", ".shadergraph");
        RegisterFileIcons(EditorIcons.FileImage, ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tga", ".psd", ".hdr");
        RegisterFileIcons(EditorIcons.FileAudio, ".mp3", ".wav", ".ogg", ".flac");
        RegisterFileIcons(EditorIcons.FileVideo, ".mp4", ".avi", ".mkv", ".mov");
        RegisterFileIcons(EditorIcons.VectorSquare, ".fbx", ".obj", ".gltf", ".glb", ".dae", ".mesh");
        RegisterFileIcons(EditorIcons.Cubes, ".scene", ".prefab");
        RegisterFileIcons(EditorIcons.Palette, ".mat", ".material");
        RegisterFileIcons(EditorIcons.FilePdf, ".pdf");
        RegisterFileIcons(EditorIcons.FileLines, ".txt", ".md", ".log", ".json", ".xml", ".yaml", ".yml");
        RegisterFileIcons(EditorIcons.FileZipper, ".zip", ".rar", ".7z", ".tar", ".gz", ".prowlpackage");
        RegisterFileIcons(EditorIcons.Gear, ".exe", ".dll", ".so");
    }

    #endregion

    #region Double-Click

    public static void RegisterDoubleClickHandler(string extension, AssetDoubleClickHandler handler)
    {
        if (string.IsNullOrEmpty(extension) || handler == null) return;
        _doubleClickHandlers[extension] = handler;
    }

    public static bool DispatchDoubleClick(string relativePath, Guid guid)
    {
        string ext = Path.GetExtension(relativePath ?? "").ToLowerInvariant();
        if (_doubleClickHandlers.TryGetValue(ext, out var handler))
        {
            try { return handler(relativePath!, guid); }
            catch (Exception ex) { Debug.LogError($"EditorRegistries: double-click handler for {ext} threw: {ex.Message}"); }
        }
        return false;
    }

    #endregion
}
