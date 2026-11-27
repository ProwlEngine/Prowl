// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Formats.Bvh;
using Prowl.Clay.Formats.Fbx;
using Prowl.Clay.Formats.Gltf;
using Prowl.Clay.Formats.Wavefront;
using Prowl.Clay.Intermediate;
using Prowl.Clay.PostProcess;

namespace Prowl.Clay.Importer;

/// <summary>
/// Main entry point. Loads a model from a path or stream into a fully-baked <see cref="Model"/>.
/// </summary>
public static class ModelImporter
{
    private static readonly List<IModelFormat> _formats = [];

    /// <summary>Registers built-in formats.</summary>
    static ModelImporter()
    {
        // Register built-in formats.
        Register<GltfFormat>();
        Register<GlbFormat>();
        Register<ObjFormat>();
        Register<FbxFormat>();
        Register<BvhFormat>();
    }

    /// <summary>
    /// Registers a custom importer.
    /// </summary>
    /// <typeparam name="T">The custom format implementation type.</typeparam>
    public static void Register<T>() where T : IModelFormat, new()
    {
        _formats.Add(new T());
    }

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
        resolver ??= new DefaultFileResolver();
        using Stream stream = resolver.OpenRead(path);
        IModelFormat? format = DetectFromPath(path);
        return LoadCore(stream, format, settings, resolver, path, cancellationToken);
    }

    /// <summary>
    /// Loads a model from a stream. The caller must supply a <paramref name="resolver"/> if external
    /// resources are referenced.
    /// </summary>
    public static Model Load(
        Stream stream,
        string? formatHint = null,
        ModelImporterSettings? settings = null,
        IFileResolver? resolver = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        IModelFormat? format = formatHint is null ? null : DetectFromPath(formatHint);
        return LoadCore(stream, format, settings, resolver, sourcePath: null, cancellationToken);
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

    private static Model LoadCore(
        Stream stream,
        IModelFormat? format,
        ModelImporterSettings? settings,
        IFileResolver? resolver,
        string? sourcePath,
        CancellationToken cancellationToken)
    {
        settings ??= ModelImporterSettings.GameQuality;
        resolver ??= new DefaultFileResolver();
        format ??= DetectFromStream(stream) ?? throw new ImportException("Could not determine model format from path or content.", sourcePath, format);

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

        IntermediateScene scene = Guarded(() => format.Read(stream, context), "read", sourcePath, format);

        return Guarded(() =>
        {
            new Pipeline(context).Run(scene);
            return SceneBaker.Bake(scene, context);
        }, "process", sourcePath, format);
    }

    /// <summary>
    /// Runs one stage of the import, turning anything it throws into an <see cref="ImportException"/>
    /// carrying the source path and format.
    /// </summary>
    /// <remarks>
    /// Every stage needs this, not just the format read. The bake indexes vertex arrays by the
    /// indices the file supplied, so a malformed one used to surface as a bare
    /// <see cref="IndexOutOfRangeException"/> naming neither the file nor the format, and callers
    /// catching <see cref="ImportException"/> did not catch it at all.
    /// </remarks>
    private static T Guarded<T>(Func<T> stage, string what, string? sourcePath, IModelFormat format)
    {
        try
        {
            return stage();
        }
        catch (ImportException ex)
        {
            throw ex.WithContext(sourcePath, format);
        }
        catch (OperationCanceledException)
        {
            // A cancelled import is not a malformed file, so it stays cancellation.
            throw;
        }
        catch (Exception ex)
        {
            throw new ImportException($"Failed to {what} {format} model: {ex.Message}", sourcePath, format, ex);
        }
    }

    /// <summary>
    /// Determines the <see cref="IModelFormat"/> implementation that can import the file with the given <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The <see cref="IModelFormat"/> implementation, or <see langword="null"/> if none can handle the file.</returns>
    internal static IModelFormat? DetectFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path);
        if (extension == string.Empty)
            // The path doesn't have a dot ('.') so assume the whole path is an extension (e.g. "obj").
            extension = path;
        else
            extension = extension.Substring(startIndex: 1);
        extension = extension.ToLowerInvariant();
        foreach (IModelFormat format in _formats)
        {
            if (format.CanRead(extension))
                return format;
        }
        return null;
    }

    /// <summary>
    /// Peeks the first few bytes of a seekable stream to identify the format. Restores the
    /// stream's position before returning.
    /// </summary>
    /// <param name="stream">The stream to read.</param>
    /// <returns>The <see cref="IModelFormat"/> implementation, or <see langword="null"/> if none can handle the file.</returns>
    internal static IModelFormat? DetectFromStream(Stream stream)
    {
        if (!stream.CanSeek)
            return null;
        long origin = stream.Position;
        try
        {
            Span<byte> head = stackalloc byte[32];
            int read = stream.Read(head);
            head = head.Slice(0, read);
            foreach (IModelFormat format in ModelImporter._formats)
            {
                if (format.CanRead(head))
                    return format;
            }
            return null;
        }
        finally
        {
            stream.Position = origin;
        }
    }
}
