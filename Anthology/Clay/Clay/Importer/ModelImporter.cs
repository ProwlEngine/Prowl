using Prowl.Clay.Formats.Fbx;
using Prowl.Clay.Formats.Gltf;
using Prowl.Clay.Formats.Obj;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Clay.PostProcess;

namespace Prowl.Clay.Importer;

/// <summary>
/// Main entry point. Loads a model from a path or stream into a fully-baked <see cref="Model"/>.
/// </summary>
public static class ModelImporter
{
    /// <summary>
    /// Loads a model from a file on disk. The format is detected from the file extension and,
    /// if that fails, from the first few bytes of the file.
    /// </summary>
    /// <exception cref="ImportException">If the file cannot be read or its format is unsupported.</exception>
    public static Model Load(
        string path,
        ModelImporterSettings? settings = null,
        IFileResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be null or empty.", nameof(path));
        settings ??= ModelImporterSettings.GameQuality;
        resolver ??= new DefaultFileResolver();

        string? format = FormatDetector.FromPath(path);
        using var stream = resolver.OpenRead(path);
        if (format is null && !FormatDetector.TryDetectFromStream(stream, out format))
            throw new ImportException("Could not determine model format from path or content.", path);

        return LoadCore(stream, format, settings, resolver, path, cancellationToken);
    }

    /// <summary>
    /// Loads a model from a stream. The caller must supply a <paramref name="formatHint"/>
    /// (<c>"gltf"</c>, <c>"glb"</c>, <c>"obj"</c>, <c>"fbx"</c>, or <c>"vrm"</c>) and, if external
    /// resources are referenced, an <paramref name="resolver"/>.
    /// </summary>
    public static Model Load(
        Stream stream,
        string formatHint,
        ModelImporterSettings? settings = null,
        IFileResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (string.IsNullOrEmpty(formatHint)) throw new ArgumentException("Format hint must not be null or empty.", nameof(formatHint));
        settings ??= ModelImporterSettings.GameQuality;
        resolver ??= new DefaultFileResolver();

        return LoadCore(stream, formatHint, settings, resolver, sourcePath: null, cancellationToken);
    }

    /// <summary>Convenience async wrapper. The import is performed on a thread-pool worker.</summary>
    public static Task<Model> LoadAsync(
        string path,
        ModelImporterSettings? settings = null,
        IFileResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Load(path, settings, resolver, cancellationToken), cancellationToken);
    }

    /// <summary>Peeks the stream's first bytes to identify the model format.</summary>
    public static bool TryDetectFormat(Stream peekableStream, out string format) =>
        FormatDetector.TryDetectFromStream(peekableStream, out format);

    private static Model LoadCore(
        Stream stream,
        string format,
        ModelImporterSettings settings,
        IFileResolver resolver,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        var log = new ImportLog { Sink = settings.OnLog };
        var context = new ImportContext
        {
            Settings = settings,
            Resolver = resolver,
            Log = log,
            CancellationToken = cancellationToken,
            SourcePath = sourcePath,
            Format = format,
        };

        IModelFormat reader = format switch
        {
            "gltf" or "glb" or "vrm" => new GltfFormat(),
            "obj"                    => new ObjFormat(),
            "fbx"                    => new FbxFormat(),
            _ => throw new ImportException($"Unsupported model format '{format}'.", sourcePath, format),
        };

        IntermediateScene scene;
        try
        {
            scene = reader.Read(stream, context);
        }
        catch (ImportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ImportException($"Failed to read {format} model: {ex.Message}", sourcePath, format, ex);
        }

        var pipeline = new Pipeline(context);
        pipeline.Run(scene);

        var model = SceneBaker.Bake(scene, context);
        return model;
    }
}
