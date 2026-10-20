// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>
/// One C# project that every open editor and every extra script of the host lives in, so features see across files.
/// Mutate from one thread (the UI thread). Queries read immutable <see cref="Solution"/> snapshots and may run anywhere.
/// </summary>
public sealed class CSharpWorkspace : IDisposable
{
    private static readonly StringComparer s_pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _lock = new();
    private readonly Dictionary<string, DocumentId> _documentsByPath = new(s_pathComparer);
    private readonly Dictionary<string, SourceText> _extraTexts = new(s_pathComparer);
    private readonly HashSet<DocumentId> _openDocuments = new();
    private Solution _solution;
    private int _untitledCount;

    public AdhocWorkspace Workspace { get; }

    /// <summary>Analyzers whose diagnostics show in open editors next to the compiler's, such as a host's own script rules.</summary>
    public ImmutableArray<DiagnosticAnalyzer> Analyzers { get; set; } = ImmutableArray<DiagnosticAnalyzer>.Empty;

    public ProjectId ProjectId { get; }

    /// <summary>Creates the workspace. With no <paramref name="references"/> the running app's trusted platform assemblies are referenced.</summary>
    public CSharpWorkspace(IEnumerable<MetadataReference>? references = null, string assemblyName = "Scripts")
    {
        Workspace = new AdhocWorkspace(MefHostServices.DefaultHost);
        ProjectId = ProjectId.CreateNewId(assemblyName);

        var info = ProjectInfo.Create(ProjectId, VersionStamp.Create(), assemblyName, assemblyName, LanguageNames.CSharp,
            compilationOptions: DefaultCompilationOptions,
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
            metadataReferences: references ?? GetPlatformReferences());
        Workspace.AddProject(info);
        _solution = Workspace.CurrentSolution;
    }

    /// <summary>Library output, nullable enabled and unsafe allowed.</summary>
    public static CSharpCompilationOptions DefaultCompilationOptions { get; } = new(
        OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true);

    /// <summary>The latest immutable snapshot. Safe to read from any thread.</summary>
    public Solution CurrentSolution
    {
        get { lock (_lock) return _solution; }
    }

    public Project Project => CurrentSolution.GetProject(ProjectId)!;

    public CSharpParseOptions ParseOptions => (CSharpParseOptions)Project.ParseOptions!;

    public LanguageVersion LanguageVersion
    {
        get => ParseOptions.LanguageVersion;
        set => Mutate(s => s.WithProjectParseOptions(ProjectId, ((CSharpParseOptions)s.GetProject(ProjectId)!.ParseOptions!).WithLanguageVersion(value)));
    }

    public void SetPreprocessorSymbols(IEnumerable<string> symbols)
    {
        var list = symbols.ToArray();
        Mutate(s => s.WithProjectParseOptions(ProjectId, ((CSharpParseOptions)s.GetProject(ProjectId)!.ParseOptions!).WithPreprocessorSymbols(list)));
    }

    public void SetCompilationOptions(CSharpCompilationOptions options)
        => Mutate(s => s.WithProjectCompilationOptions(ProjectId, options));

    public void SetReferences(IEnumerable<MetadataReference> references)
    {
        var list = references.ToArray();
        Mutate(s => s.WithProjectMetadataReferences(ProjectId, list));
    }

    public void SetReferencePaths(IEnumerable<string> paths) => SetReferences(paths.Select(CreateReference));

    public void AddReference(string path) => AddReference(CreateReference(path));

    public void AddReference(MetadataReference reference) => Mutate(s => s.AddMetadataReference(ProjectId, reference));

    /// <summary>Adds another source file of the project, or replaces its text. Edits reported for it are offsets into this text.</summary>
    public void AddOrUpdateDocument(string path, string text)
    {
        var source = SourceText.From(text);
        lock (_lock)
        {
            _extraTexts[path] = source;
            if (_documentsByPath.TryGetValue(path, out var id))
            {
                if (!_openDocuments.Contains(id))
                    _solution = _solution.WithDocumentText(id, source);
                return;
            }
            id = DocumentId.CreateNewId(ProjectId, path);
            _documentsByPath[path] = id;
            _solution = _solution.AddDocument(CreateInfo(id, path, source));
        }
    }

    public void RemoveDocument(string path)
    {
        lock (_lock)
        {
            if (!_extraTexts.Remove(path) || !_documentsByPath.TryGetValue(path, out var id) || _openDocuments.Contains(id))
                return;
            _documentsByPath.Remove(path);
            _solution = _solution.RemoveDocument(id);
        }
    }

    public Document? GetDocument(DocumentId id) => CurrentSolution.GetDocument(id);

    /// <summary>Path or name of every document in the project.</summary>
    public IReadOnlyList<string> DocumentPaths
    {
        get { lock (_lock) return _documentsByPath.Keys.ToArray(); }
    }

    internal DocumentId OpenDocument(string? path, SourceText text)
    {
        lock (_lock)
        {
            path ??= $"Untitled{++_untitledCount}.cs";
            if (_documentsByPath.TryGetValue(path, out var id))
            {
                if (_openDocuments.Contains(id))
                    throw new InvalidOperationException($"'{path}' is already open in another session.");
                _solution = _solution.WithDocumentText(id, text);
            }
            else
            {
                id = DocumentId.CreateNewId(ProjectId, path);
                _documentsByPath[path] = id;
                _solution = _solution.AddDocument(CreateInfo(id, path, text));
            }
            _openDocuments.Add(id);
            return id;
        }
    }

    internal void UpdateOpenDocument(DocumentId id, SourceText text)
    {
        lock (_lock)
        {
            if (_openDocuments.Contains(id))
                _solution = _solution.WithDocumentText(id, text);
        }
    }

    /// <summary>Closes an editor's document. One that is also an extra source file goes back to its last known text.</summary>
    internal void CloseDocument(DocumentId id)
    {
        lock (_lock)
        {
            if (!_openDocuments.Remove(id)) return;
            string? path = _documentsByPath.FirstOrDefault(p => p.Value == id).Key;
            if (path != null && _extraTexts.TryGetValue(path, out var text))
            {
                _solution = _solution.WithDocumentText(id, text);
                return;
            }
            if (path != null) _documentsByPath.Remove(path);
            _solution = _solution.RemoveDocument(id);
        }
    }

    private void Mutate(Func<Solution, Solution> change)
    {
        lock (_lock) _solution = change(_solution);
    }

    private DocumentInfo CreateInfo(DocumentId id, string path, SourceText text)
    {
        bool isPath = Path.IsPathRooted(path);
        return DocumentInfo.Create(id, Path.GetFileName(path), sourceCodeKind: SourceCodeKind.Regular,
            loader: TextLoader.From(TextAndVersion.Create(text, VersionStamp.Create())), filePath: isPath ? path : null);
    }

    /// <summary>A reference to the assembly at <paramref name="path"/>, with its xml docs when a matching .xml sits beside it.</summary>
    public static MetadataReference CreateReference(string path)
    {
        string xml = Path.ChangeExtension(path, ".xml");
        var docs = File.Exists(xml) ? XmlDocumentationProvider.CreateFromFile(xml) : null;
        return MetadataReference.CreateFromFile(path, documentation: docs);
    }

    /// <summary>Every assembly in the running app's TRUSTED_PLATFORM_ASSEMBLIES.</summary>
    public static IReadOnlyList<MetadataReference> GetPlatformReferences()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string tpa)
            return Array.Empty<MetadataReference>();

        var refs = new List<MetadataReference>();
        foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                refs.Add(CreateReference(path));
        return refs;
    }

    public void Dispose() => Workspace.Dispose();
}
