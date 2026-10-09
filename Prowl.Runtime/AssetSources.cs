// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;

using Prowl.Echo;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

#region File sources

/// <summary>
/// Where a <see cref="SourceAssetBackend"/> reads its files: a folder, a zip, a web server, or anything else that can
/// list files and open them.
/// </summary>
public abstract class AssetFileSource
{
    /// <summary>Goes into every GUID the source's files get, so the same path in two sources is two different assets.</summary>
    public abstract string Name { get; }

    /// <summary>Every file, as paths relative to the source with '/' between folders.</summary>
    public abstract IReadOnlyList<string> ListFiles();

    public abstract Stream Open(string path);

    /// <summary>The file's full path on disk, for importers that also read the files beside it. Null when it is not on disk.</summary>
    public virtual string? LocalPath(string path) => null;

    public byte[] ReadAllBytes(string path)
    {
        using Stream stream = Open(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public string ReadAllText(string path)
    {
        using var reader = new StreamReader(Open(path));
        return reader.ReadToEnd();
    }

    /// <summary>A path relative to <paramref name="from"/>'s folder, with "./" and "../" resolved, as a path in this source.</summary>
    public static string Combine(string from, string relative)
    {
        var parts = new List<string>();
        int slash = from.LastIndexOf('/');
        if (slash >= 0) parts.AddRange(from[..slash].Split('/'));

        foreach (string part in relative.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return string.Join('/', parts);
    }
}

/// <summary>Files in a folder on disk, such as the folder of assets shipped beside a game or a mod's folder.</summary>
public sealed class FolderAssetSource : AssetFileSource
{
    public string Root { get; }

    public override string Name { get; }

    /// <param name="name">Named after the folder when left out. Two folders with one name give their files the same GUIDs.</param>
    public FolderAssetSource(string root, string? name = null)
    {
        Root = Path.GetFullPath(root);
        Name = name ?? Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    public override IReadOnlyList<string> ListFiles()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(Root, file).Replace('\\', '/'))
            .ToList();
    }

    public override Stream Open(string path) => File.OpenRead(LocalPath(path));

    public override string LocalPath(string path) => Path.Combine(Root, path);
}

/// <summary>Files in a zip archive, for a content pack or a download kept as one file.</summary>
public sealed class ZipAssetSource : AssetFileSource, IDisposable
{
    private readonly ZipArchive _archive;
    private readonly object _lock = new();

    public override string Name { get; }

    /// <param name="name">Named after the zip file when left out.</param>
    public ZipAssetSource(string zipPath, string? name = null)
        : this(File.OpenRead(zipPath), name ?? Path.GetFileNameWithoutExtension(zipPath)) { }

    public ZipAssetSource(Stream zip, string name)
    {
        _archive = new ZipArchive(zip, ZipArchiveMode.Read);
        Name = name;
    }

    public override IReadOnlyList<string> ListFiles()
    {
        lock (_lock)
            return _archive.Entries.Where(entry => entry.Name.Length > 0).Select(entry => entry.FullName.Replace('\\', '/')).ToList();
    }

    // An archive reads one entry at a time, so each file is copied out whole.
    public override Stream Open(string path)
    {
        lock (_lock)
        {
            ZipArchiveEntry entry = _archive.GetEntry(path) ?? throw new FileNotFoundException($"'{path}' is not in the zip '{Name}'.");
            var memory = new MemoryStream();
            using (Stream stream = entry.Open()) stream.CopyTo(memory);
            memory.Position = 0;
            return memory;
        }
    }

    public void Dispose() => _archive.Dispose();
}

/// <summary>
/// Files on a web server. The server lists them in an index file, one path per line relative to the root URL, and
/// serves each at the root URL plus its path. Reading downloads the file, so prefer loading these assets asynchronously.
/// </summary>
public sealed class HttpAssetSource : AssetFileSource
{
    private readonly HttpClient _client;
    private readonly Uri _root;
    private readonly string _index;

    public override string Name { get; }

    public HttpAssetSource(Uri root, string name, HttpClient? client = null, string indexFile = "index.txt")
    {
        _root = root.AbsoluteUri.EndsWith('/') ? root : new Uri(root.AbsoluteUri + "/");
        _client = client ?? new HttpClient();
        _index = indexFile;
        Name = name;
    }

    public override IReadOnlyList<string> ListFiles()
    {
        string index = _client.GetStringAsync(new Uri(_root, _index)).GetAwaiter().GetResult();
        return index.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    public override Stream Open(string path)
        => new MemoryStream(_client.GetByteArrayAsync(new Uri(_root, path)).GetAwaiter().GetResult());
}

#endregion

#region Importers

/// <summary>
/// Turns one kind of source file into assets at runtime, for a <see cref="SourceAssetBackend"/>. Register your own
/// with <see cref="RuntimeImporters.Register"/> to load a format the engine does not know.
/// </summary>
public abstract class RuntimeImporter
{
    /// <summary>The file extensions this importer reads, with the dot, such as ".png".</summary>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <summary>The type of the file's main asset.</summary>
    public abstract Type AssetType { get; }

    /// <summary>Builds the file's main asset, and any sub assets, through <paramref name="context"/>. Runs on the asset loader thread.</summary>
    public abstract void Import(RuntimeImportContext context);
}

/// <summary>What an importer reads from and hands its assets to.</summary>
public sealed class RuntimeImportContext
{
    private readonly Func<string?, Guid> _guidOf;
    private readonly Func<string, Type, Guid> _find;
    private readonly HashSet<string> _subNames = new(StringComparer.OrdinalIgnoreCase);

    internal RuntimeImportContext(AssetFileSource source, string path, Func<string?, Guid> guidOf, Func<string, Type, Guid> find)
    {
        Source = source;
        Path = path;
        _guidOf = guidOf;
        _find = find;
    }

    public AssetFileSource Source { get; }

    /// <summary>The file's path within its source.</summary>
    public string Path { get; }

    public Stream Open() => Source.Open(Path);

    public byte[] ReadAllBytes() => Source.ReadAllBytes(Path);

    public string ReadAllText() => Source.ReadAllText(Path);

    /// <summary>The file on disk, when the source is a folder.</summary>
    public string? LocalPath => Source.LocalPath(Path);

    /// <summary>
    /// Another asset in the same source by its load path, beside this file first and from the source's root after,
    /// without loading it. Null when there is none.
    /// </summary>
    public T? Find<T>(string loadPath) where T : Asset
    {
        Guid guid = _find(AssetFileSource.Combine(Path, loadPath), typeof(T));
        if (guid == Guid.Empty) guid = _find(loadPath, typeof(T));
        return guid == Guid.Empty ? null : AssetDatabase.Get<T>(guid);
    }

    internal Asset? Main { get; private set; }

    internal List<(string Name, Asset Asset)> SubAssets { get; } = [];

    public void SetMain(Asset asset)
    {
        asset.Name = System.IO.Path.GetFileNameWithoutExtension(Path);
        asset.SetIdentity(_guidOf(null), Path);
        Main = asset;
    }

    /// <summary>
    /// Adds an asset the file carries besides its main one, such as a mesh of a model, findable at "path#name". It gets its
    /// GUID straight away, so anything serialized after this refers to it rather than copying it. Returns the name used,
    /// which gains a number when taken.
    /// </summary>
    public string AddSubAsset(string name, Asset asset)
    {
        string unique = string.IsNullOrWhiteSpace(name) ? asset.GetType().Name : name;
        for (int i = 2; !_subNames.Add(unique); i++) unique = $"{name} {i}";

        asset.Name = unique;
        asset.SetIdentity(_guidOf(unique), $"{Path}#{unique}");
        SubAssets.Add((unique, asset));
        return unique;
    }
}

/// <summary>The importers a <see cref="SourceAssetBackend"/> picks from, by file extension.</summary>
public static class RuntimeImporters
{
    private static readonly ConcurrentDictionary<string, RuntimeImporter> s_byExtension = new(StringComparer.OrdinalIgnoreCase);

    static RuntimeImporters()
    {
        Register(new TextureRuntimeImporter());
        Register(new ModelRuntimeImporter());
        Register(new MaterialRuntimeImporter());
        Register(new ShaderRuntimeImporter());
        Register(new ComputeShaderRuntimeImporter());
        Register(new AudioRuntimeImporter());
    }

    /// <summary>Makes <paramref name="importer"/> the one for each of its extensions, in place of any before it.</summary>
    public static void Register(RuntimeImporter importer)
    {
        foreach (string extension in importer.Extensions)
            s_byExtension[extension] = importer;
    }

    public static RuntimeImporter? For(string path)
        => s_byExtension.TryGetValue(Path.GetExtension(path), out RuntimeImporter? importer) ? importer : null;
}

/// <summary>Images, as textures that repeat and have mipmaps.</summary>
internal sealed class TextureRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".exr", ".hdr", ".webp"];

    public override Type AssetType => typeof(Texture2D);

    public override void Import(RuntimeImportContext context)
    {
        using Stream stream = context.Open();
        Texture2D texture = Texture2D.FromStream(stream, generateMipmaps: true);
        texture.SetWrapModes(TextureWrap.Repeat, TextureWrap.Repeat);
        context.SetMain(texture);
    }
}

/// <summary>
/// Models, as a prefab of the model's objects with its meshes, materials, animations and rig as sub assets. A model in
/// a folder also reads the files beside it, such as an .obj's .mtl and the textures it names. A material the model names
/// is taken from a .mat of that name beside the model or in a Materials folder, when the source has one.
/// </summary>
internal sealed class ModelRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".obj", ".fbx", ".gltf", ".glb", ".vrm"];

    public override Type AssetType => typeof(PrefabAsset);

    public override void Import(RuntimeImportContext context)
    {
        var importer = new ModelImporter();
        var settings = new ModelImporterSettings { MaterialResolver = new MaterialFiles(context) };
        string? local = context.LocalPath;
        ModelImportResult result;
        if (local != null) result = importer.Import(new FileInfo(local), settings);
        else
        {
            using Stream stream = context.Open();
            result = importer.Import(stream, context.Path, settings);
        }

        foreach (Mesh mesh in result.Meshes) context.AddSubAsset(mesh.Name, mesh);
        foreach (Material material in result.Materials) context.AddSubAsset(material.Name, material);
        foreach (AnimationClip clip in result.Animations) context.AddSubAsset(clip.Name, clip);
        if (result.Avatar != null) context.AddSubAsset("Avatar", result.Avatar);

        var prefab = new PrefabAsset { InstanceType = PrefabInstanceType.Model };
        if (result.RootGO != null) prefab.GameObjectData = Serializer.Serialize(typeof(object), result.RootGO);
        context.SetMain(prefab);
    }

    private sealed class MaterialFiles(RuntimeImportContext context) : IModelMaterialResolver
    {
        public Material? Resolve(string materialName)
        {
            Material? beside = context.Find<Material>(materialName);
            return beside.IsValid() ? beside : context.Find<Material>($"Materials/{materialName}");
        }
    }
}

/// <summary>Materials written as Echo text, whose textures and shader can be named by load path as well as by GUID.</summary>
internal sealed class MaterialRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".mat"];

    public override Type AssetType => typeof(Material);

    public override void Import(RuntimeImportContext context)
        => context.SetMain(Serializer.Deserialize<Material>(EchoObject.ReadFromString(context.ReadAllText()))!);
}

/// <summary>Shaders, whose includes are looked up beside the shader first and among the built-in includes after.</summary>
internal sealed class ShaderRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".shader"];

    public override Type AssetType => typeof(Shader);

    public override void Import(RuntimeImportContext context)
    {
        string? Include(string path)
        {
            // Already relative to the source's root, the parser joins it to the including file's folder
            try { return context.Source.ReadAllText(AssetFileSource.Combine("", path)); }
            catch (Exception) { }
            try { return EmbeddedResources.ReadAllText(path); }
            catch (Exception) { }
            try { return EmbeddedResources.ReadAllText($"Assets/Defaults/{path}"); }
            catch (Exception) { return null; }
        }

        if (!ShaderParser.ParseShader(context.Path, context.ReadAllText(), Include, out Shader? shader) || shader.IsNotValid())
            throw new InvalidDataException($"'{context.Path}' is not a shader that parses.");
        context.SetMain(shader!);
    }
}

/// <summary>Compute shaders, whose includes are found the same way as a shader's.</summary>
internal sealed class ComputeShaderRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".compute"];

    public override Type AssetType => typeof(ComputeShader);

    public override void Import(RuntimeImportContext context)
    {
        string? Include(string path)
        {
            // Already relative to the source's root, the parser joins it to the including file's folder
            try { return context.Source.ReadAllText(AssetFileSource.Combine("", path)); }
            catch (Exception) { }
            try { return EmbeddedResources.ReadAllText($"Assets/Defaults/{System.IO.Path.GetFileName(path)}"); }
            catch (Exception) { return null; }
        }

        string source = ShaderParser.ExpandIncludes(context.Path, context.ReadAllText(), Include);
        context.SetMain(ComputeShader.FromSource(System.IO.Path.GetFileNameWithoutExtension(context.Path), source));
    }
}

/// <summary>Sounds, decoded from the file's bytes.</summary>
internal sealed class AudioRuntimeImporter : RuntimeImporter
{
    public override IReadOnlyList<string> Extensions { get; } = [".wav", ".mp3", ".flac", ".ogg"];

    public override Type AssetType => typeof(AudioClip);

    public override void Import(RuntimeImportContext context) => context.SetMain(new AudioClip(context.ReadAllBytes()));
}

#endregion

/// <summary>
/// Assets imported from source files as they are needed, the runtime counterpart of the editor's importers: point it
/// at a folder, a zip or a web server and <see cref="AssetDatabase.Mount"/> it. Every file a <see cref="RuntimeImporter"/>
/// knows is findable by its path without the extension, "Textures/Paint" for "Textures/Paint.png", and its sub assets at
/// "path#name" once the file has been loaded. GUIDs come from the source's name and the path, so they are the same every run.
/// </summary>
public sealed class SourceAssetBackend : AssetBackend
{
    private sealed record Entry(string FilePath, string? SubAsset, Type Type, RuntimeImporter Importer);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly object _importLock = new();

    // Assets a file's import made besides the one asked for, waiting for the database to ask for them in turn.
    private readonly Dictionary<string, Dictionary<Guid, Asset>> _waiting = new();

    private IReadOnlyList<ResourceEntry> _resources = [];

    public AssetFileSource Files { get; }

    public SourceAssetBackend(AssetFileSource files)
    {
        Files = files;
        Refresh();
    }

    /// <summary>Lists the source's files again, for files added or removed since it was mounted.</summary>
    public void Refresh()
    {
        var resources = new List<ResourceEntry>();
        var live = new HashSet<Guid>();
        foreach (string path in Files.ListFiles())
        {
            RuntimeImporter? importer = RuntimeImporters.For(path);
            if (importer == null) continue;

            Guid guid = GuidFor(path);
            _entries[guid] = new Entry(path, null, importer.AssetType, importer);
            live.Add(guid);
            resources.Add(new ResourceEntry(LoadPathOf(path), guid, importer.AssetType.FullName!));
        }

        foreach ((Guid guid, Entry entry) in _entries)
        {
            if (entry.SubAsset == null) continue;
            if (!live.Contains(GuidFor(entry.FilePath))) _entries.TryRemove(guid, out _);
            else resources.Add(new ResourceEntry($"{LoadPathOf(entry.FilePath)}#{entry.SubAsset}", guid, entry.Type.FullName!));
        }

        foreach (Guid guid in _entries.Keys)
            if (_entries[guid].SubAsset == null && !live.Contains(guid)) _entries.TryRemove(guid, out _);

        _resources = resources;
    }

    /// <summary>The GUID a file in this source has, or one of its sub assets.</summary>
    public Guid GuidFor(string path, string? subAsset = null)
        => BuiltInAssets.DeterministicGuid(subAsset == null ? $"{Files.Name}:{path}" : $"{Files.Name}:{path}#{subAsset}");

    private Guid FindInSource(string loadPath, Type type)
    {
        foreach (ResourceEntry resource in _resources)
            if (string.Equals(resource.LoadPath, loadPath, StringComparison.OrdinalIgnoreCase) && _entries.TryGetValue(resource.Guid, out Entry? entry) && type.IsAssignableFrom(entry.Type))
                return resource.Guid;
        return Guid.Empty;
    }

    private static string LoadPathOf(string path)
    {
        int dot = path.LastIndexOf('.');
        return dot > path.LastIndexOf('/') ? path[..dot] : path;
    }

    public override Type? GetAssetType(Guid assetId) => _entries.TryGetValue(assetId, out Entry? entry) ? entry.Type : null;

    public override string? GetAssetPath(Guid assetId)
        => _entries.TryGetValue(assetId, out Entry? entry) ? entry.SubAsset == null ? entry.FilePath : $"{entry.FilePath}#{entry.SubAsset}" : null;

    public override IReadOnlyList<ResourceEntry> Resources => _resources;

    protected internal override bool ReadContent(Guid assetId, Asset staging, SerializationContext context)
    {
        if (!_entries.TryGetValue(assetId, out Entry? entry)) return false;

        Asset? built;
        lock (_importLock)
        {
            if (!TakeWaiting(entry.FilePath, assetId, out built))
            {
                if (!Import(entry)) return false;
                TakeWaiting(entry.FilePath, assetId, out built);
            }
        }

        if (ReferenceEquals(built, null) || built.GetType() != staging.GetType())
        {
            string made = ReferenceEquals(built, null) ? "nothing" : built.GetType().Name;
            Debug.LogError($"'{GetAssetPath(assetId)}' imported as {made}, not the {staging.GetType().Name} it was asked for.");
            return false;
        }

        AssetContent.Move(built, staging);
        GC.SuppressFinalize(built);
        return true;
    }

    private bool TakeWaiting(string filePath, Guid assetId, out Asset? asset)
    {
        asset = null;
        if (!_waiting.TryGetValue(filePath, out var assets) || !assets.Remove(assetId, out asset)) return false;
        if (assets.Count == 0) _waiting.Remove(filePath);
        return true;
    }

    // Imports a whole file, keeping everything it made until the database asks for it.
    private bool Import(Entry entry)
    {
        var context = new RuntimeImportContext(Files, entry.FilePath, sub => GuidFor(entry.FilePath, sub), FindInSource);
        try
        {
            entry.Importer.Import(context);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Could not import '{entry.FilePath}' from '{Files.Name}': {ex.Message}");
            return false;
        }

        if (context.Main == null)
        {
            Debug.LogError($"Importing '{entry.FilePath}' from '{Files.Name}' made no asset.");
            return false;
        }

        var assets = new Dictionary<Guid, Asset> { [GuidFor(entry.FilePath)] = context.Main };
        bool learned = false;
        foreach ((string name, Asset asset) in context.SubAssets)
        {
            Guid guid = GuidFor(entry.FilePath, name);
            assets[guid] = asset;
            learned |= _entries.TryAdd(guid, new Entry(entry.FilePath, name, asset.GetType(), entry.Importer));
        }
        _waiting[entry.FilePath] = assets;

        if (learned) Refresh();
        return true;
    }
}
