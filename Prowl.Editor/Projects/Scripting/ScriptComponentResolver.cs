// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Prowl.Editor.GUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Scripting;

/// <summary>
/// Resolves `.cs` script assets (and <see cref="AssetDragPayload"/> instances) to compiled
/// <see cref="Component"/> types that can be added to a <see cref="GameObject"/>.
/// </summary>
public static class ScriptComponentResolver
{
    private readonly record struct DeclaredTypeName(string FullName, string SimpleName, bool HasBaseList);

    private readonly record struct ParsedFileCacheEntry(byte[] ContentHash, DeclaredTypeName[] DeclaredTypes);

    private sealed record ComponentTypeIndex(
        int Version,
        Dictionary<string, Type> ByFullName,
        Dictionary<string, List<Type>> BySimpleNameExact,
        Dictionary<string, List<Type>> BySimpleNameIgnoreCase);

    private static int s_typeVersion = 1;
    private static ComponentTypeIndex? s_typeIndex;
    private static readonly Lock s_typeIndexLock = new();
    private static readonly ConcurrentDictionary<string, ParsedFileCacheEntry> s_parsedFileCache = new(StringComparer.Ordinal);

    static ScriptComponentResolver()
    {
        AppDomain.CurrentDomain.AssemblyLoad += (_, _) => Interlocked.Increment(ref s_typeVersion);
        ScriptAssemblyManager.CompileFinished += _ => Interlocked.Increment(ref s_typeVersion);
    }

    /// <summary>
    /// Returns true if <paramref name="assetPath"/> has a `.cs` file extension.
    /// </summary>
    public static bool IsScriptAsset(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        return string.Equals(Path.GetExtension(assetPath), ".cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if <paramref name="payload"/> contains at least one `.cs` script asset path.
    /// </summary>
    public static bool IsScriptAsset(AssetDragPayload? payload)
    {
        if (payload == null)
            return false;

        return payload.AssetPaths.Any(IsScriptAsset);
    }

    /// <summary>
    /// Resolves a single `.cs` script asset path to its concrete <see cref="Component"/> type,
    /// or null if the path is not a script, fails to compile, or does not uniquely identify a component.
    /// </summary>
    public static Type? ResolveComponentType(string? assetPath)
    {
        IReadOnlyList<Type> types = ResolveComponentTypes(assetPath);
        if (types.Count == 1)
            return types[0];

        if (types.Count > 1 && assetPath != null)
        {
            string stem = Path.GetFileNameWithoutExtension(assetPath);
            List<Type> stemMatches = types.Where(t => string.Equals(t.Name, stem, StringComparison.OrdinalIgnoreCase)).ToList();
            if (stemMatches.Count == 1)
                return stemMatches[0];
        }

        return null;
    }

    /// <summary>
    /// Resolves all concrete <see cref="Component"/> types associated with a `.cs` script asset path.
    /// When the `.cs` file exists on disk, its declared classes are parsed so scripts whose filename
    /// differs from the class name (or that live in namespaces) still resolve cleanly.
    /// </summary>
    public static IReadOnlyList<Type> ResolveComponentTypes(string? assetPath)
    {
        if (!IsScriptAsset(assetPath))
            return Array.Empty<Type>();

        ComponentTypeIndex index = GetTypeIndex();

        string? fullPath = ResolveDiskPath(assetPath!);
        if (fullPath != null)
        {
            DeclaredTypeName[] declaredTypes = GetDeclaredTypes(fullPath);
            if (declaredTypes.Length == 0)
                return Array.Empty<Type>();

            var resolved = new List<Type>(declaredTypes.Length);
            var seen = new HashSet<Type>();
            foreach (DeclaredTypeName decl in declaredTypes)
            {
                if (index.ByFullName.TryGetValue(decl.FullName, out Type? type) && seen.Add(type))
                    resolved.Add(type);
            }

            if (resolved.Count == 0)
            {
                foreach (DeclaredTypeName decl in declaredTypes)
                {
                    if (!decl.HasBaseList)
                        continue;

                    if (index.BySimpleNameExact.TryGetValue(decl.SimpleName, out List<Type>? simpleMatches) && simpleMatches.Count == 1)
                    {
                        if (seen.Add(simpleMatches[0]))
                            resolved.Add(simpleMatches[0]);
                    }
                }
            }

            return resolved;
        }

        // Fallback for virtual/in-memory script paths with no corresponding file on disk.
        string typeName = Path.GetFileNameWithoutExtension(assetPath!);
        if (string.IsNullOrWhiteSpace(typeName))
            return Array.Empty<Type>();

        if (index.BySimpleNameExact.TryGetValue(typeName, out List<Type>? exact) && exact.Count == 1)
            return exact;

        if (index.BySimpleNameIgnoreCase.TryGetValue(typeName, out List<Type>? ignoreCase) && ignoreCase.Count == 1)
            return ignoreCase;

        return Array.Empty<Type>();
    }

    /// <summary>
    /// The Assets-relative path of the `.cs` file that declares <paramref name="type"/>, or null when it is not a
    /// project script (an engine or package type) or no file declares it. A file named after the type is tried first.
    /// </summary>
    public static string? FindScriptPath(Type type)
    {
        if (!ScriptAssemblyManager.IsScriptAssembly(type.Assembly) || Project.Current is not { } project) return null;
        string assets = Path.GetFullPath(project.AssetsPath);
        if (!Directory.Exists(assets)) return null;

        IEnumerable<string> files = Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => string.Equals(Path.GetFileNameWithoutExtension(f), type.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(assets, file).Replace(Path.DirectorySeparatorChar, '/');
            if (ResolveComponentTypes(rel).Contains(type)) return rel;
        }
        return null;
    }

    /// <summary>
    /// Resolves an <see cref="AssetDragPayload"/> when it carries exactly one concrete <see cref="Component"/> type.
    /// </summary>
    public static Type? ResolveComponentType(AssetDragPayload? payload)
    {
        IReadOnlyList<Type> types = ResolveComponentTypes(payload);
        return types.Count == 1 ? types[0] : null;
    }

    /// <summary>
    /// Resolves all distinct concrete <see cref="Component"/> types across the `.cs` script paths
    /// carried by <paramref name="payload"/>, preserving selection order.
    /// </summary>
    public static IReadOnlyList<Type> ResolveComponentTypes(AssetDragPayload? payload)
    {
        if (payload == null || payload.AssetPaths.Length == 0)
            return Array.Empty<Type>();

        var result = new List<Type>();
        var seen = new HashSet<Type>();

        foreach (string path in payload.AssetPaths)
        {
            foreach (Type type in ResolveComponentTypes(path))
            {
                if (seen.Add(type))
                    result.Add(type);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns true if <paramref name="type"/> is a concrete <see cref="Component"/> type that can be constructed.
    /// </summary>
    public static bool CanConstructComponent(Type? type)
        => type != null
        && typeof(Component).IsAssignableFrom(type)
        && !type.IsAbstract
        && !type.ContainsGenericParameters
        && type != typeof(Component)
        && type != typeof(MissingComponent)
        && type.GetConstructor(Type.EmptyTypes) != null;

    /// <summary>
    /// Returns true if <paramref name="type"/> is a constructible <see cref="Component"/> that can be added to
    /// <paramref name="target"/> (respecting <see cref="DisallowMultipleComponentAttribute"/>).
    /// </summary>
    public static bool CanAddComponent(GameObject target, Type? type)
    {
        if (target.IsNotValid() || !CanConstructComponent(type))
            return false;

        for (Type? marked = type; marked != null && marked != typeof(Component); marked = marked.BaseType)
        {
            if (marked.IsDefined(typeof(DisallowMultipleComponentAttribute), false)
                && target.GetComponent(marked).IsValid())
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves only the component types in <paramref name="payload"/> that can be added to <paramref name="target"/>.
    /// </summary>
    public static IReadOnlyList<Type> ResolveAddableComponentTypes(AssetDragPayload? payload, GameObject target)
        => ResolveAddableComponentTypes(payload, [target]);

    /// <summary>
    /// Resolves the component types in <paramref name="payload"/> that can be added to at least one GameObject in <paramref name="targets"/>.
    /// </summary>
    public static IReadOnlyList<Type> ResolveAddableComponentTypes(AssetDragPayload? payload, IReadOnlyList<GameObject> targets)
    {
        IReadOnlyList<Type> resolved = ResolveComponentTypes(payload);
        if (resolved.Count == 0 || targets.Count == 0)
            return Array.Empty<Type>();

        return resolved.Where(type => targets.Any(go => CanAddComponent(go, type))).ToList();
    }

    private static ComponentTypeIndex GetTypeIndex()
    {
        int currentVersion = Volatile.Read(ref s_typeVersion);
        ComponentTypeIndex? cached = s_typeIndex;
        if (cached != null && cached.Version == currentVersion)
            return cached;

        lock (s_typeIndexLock)
        {
            currentVersion = Volatile.Read(ref s_typeVersion);
            cached = s_typeIndex;
            if (cached != null && cached.Version == currentVersion)
                return cached;

            var byFullName = new Dictionary<string, Type>(StringComparer.Ordinal);
            var bySimpleExact = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            var bySimpleIgnoreCase = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);

            foreach (Type type in ScriptAssemblyManager.GetAllTypes())
            {
                if (!CanConstructComponent(type))
                    continue;

                if (type.FullName != null)
                    byFullName.TryAdd(type.FullName, type);

                AddEntry(bySimpleExact, type.Name, type);
                AddEntry(bySimpleIgnoreCase, type.Name, type);
            }

            cached = new ComponentTypeIndex(currentVersion, byFullName, bySimpleExact, bySimpleIgnoreCase);
            s_typeIndex = cached;
            return cached;
        }
    }

    private static void AddEntry(Dictionary<string, List<Type>> map, string key, Type value)
    {
        if (!map.TryGetValue(key, out List<Type>? list))
        {
            list = [];
            map[key] = list;
        }
        list.Add(value);
    }

    /// <summary>
    /// Maps a script asset path to a file inside the current project. Rooted and relative paths alike must
    /// end up under the project's assets or root directory, so a crafted path such as `..\..\secret.cs`
    /// can never make the editor read and parse a file from outside the project.
    /// </summary>
    private static string? ResolveDiskPath(string assetPath)
    {
        string trimmed = assetPath.Trim();
        if (trimmed.Length == 0)
            return null;

        Project? project = Project.Current;
        if (project == null)
            return null;

        string[] roots = new[] { project.AssetsPath, project.RootPath }
            .Where(root => !string.IsNullOrEmpty(root))
            .Select(Path.GetFullPath)
            .ToArray();

        if (Path.IsPathRooted(trimmed))
        {
            string full = Path.GetFullPath(trimmed);
            return roots.Any(root => IsInside(root, full)) && File.Exists(full) ? full : null;
        }

        string relative = trimmed.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        foreach (string root in roots)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (IsInside(root, candidate) && File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>True when <paramref name="fullPath"/> is <paramref name="root"/> or lies beneath it.</summary>
    private static bool IsInside(string root, string fullPath)
    {
        // GetRelativePath compares the way the OS does, and yields a rooted path across drives.
        string relative = Path.GetRelativePath(root, fullPath);
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static DeclaredTypeName[] GetDeclaredTypes(string fullPath)
    {
        try
        {
            // The file is read on every call so the cache is keyed on content, not on timestamps, which are
            // too coarse to catch rapid same-length edits. Scripts are small and this runs on drop only.
            byte[] bytes = File.ReadAllBytes(fullPath);
            byte[] hash = SHA256.HashData(bytes);
            if (s_parsedFileCache.TryGetValue(fullPath, out ParsedFileCacheEntry cached)
                && cached.ContentHash.AsSpan().SequenceEqual(hash))
            {
                return cached.DeclaredTypes;
            }

            string source;
            using (var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true))
                source = reader.ReadToEnd();
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithPreprocessorSymbols(ScriptCompiler.EditorDefines));

            var names = new List<DeclaredTypeName>();
            var seenFullNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ClassDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (decl.TypeParameterList is { Parameters.Count: > 0 })
                    continue;
                if (decl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.AbstractKeyword)))
                    continue;

                string clrName = BuildClrFullName(decl);
                if (seenFullNames.Add(clrName))
                {
                    bool hasBaseList = decl.BaseList is { Types.Count: > 0 };
                    names.Add(new DeclaredTypeName(clrName, decl.Identifier.ValueText, hasBaseList));
                }
            }

            DeclaredTypeName[] result = names.ToArray();
            s_parsedFileCache[fullPath] = new ParsedFileCacheEntry(hash, result);
            return result;
        }
        catch (IOException)
        {
            return Array.Empty<DeclaredTypeName>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<DeclaredTypeName>();
        }
    }

    private static string BuildClrFullName(TypeDeclarationSyntax declaration)
    {
        var typeChain = new List<string>();
        var namespaceChain = new List<string>();

        for (SyntaxNode? node = declaration; node != null; node = node.Parent)
        {
            if (node is TypeDeclarationSyntax typeDecl)
            {
                typeChain.Add(typeDecl.Identifier.ValueText);
            }
            else if (node is BaseNamespaceDeclarationSyntax nsDecl)
            {
                namespaceChain.Add(nsDecl.Name.ToString());
            }
        }

        typeChain.Reverse();
        namespaceChain.Reverse();

        string nestedTypeName = string.Join("+", typeChain);
        if (namespaceChain.Count == 0)
            return nestedTypeName;

        return $"{string.Join(".", namespaceChain)}.{nestedTypeName}";
    }
}
