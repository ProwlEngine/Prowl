// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Prowl.OrigamiUI.CSharp;

/// <summary>
/// Finds the source of a symbol compiled into a referenced assembly from the assembly's portable PDB, when the files it
/// names exist on this machine.
/// </summary>
internal static class PdbSourceLocator
{
    private sealed record Pdb(DateTime Stamp, MetadataReaderProvider? Provider);

    private static readonly ConcurrentDictionary<string, Pdb> s_pdbs = new(StringComparer.OrdinalIgnoreCase);

    public static CodeLocation? Find(Compilation compilation, ISymbol symbol, string display, CodeSymbolKind kind)
    {
        symbol = symbol.OriginalDefinition;
        if (symbol.ContainingAssembly == null) return null;
        if (compilation.GetMetadataReference(symbol.ContainingAssembly) is not PortableExecutableReference { FilePath: { } dll }) return null;
        var reader = Reader(dll);
        if (reader == null) return null;

        // Where the symbol's methods were compiled from, first point of each method.
        var points = new List<(string Path, int Line, int Column)>();
        foreach (var method in MethodsOf(symbol))
        {
            if (method.MetadataToken == 0) continue;
            var handle = MetadataTokens.EntityHandle(method.MetadataToken);
            if (handle.Kind != HandleKind.MethodDefinition) continue;
            foreach (var point in reader.GetMethodDebugInformation((MethodDefinitionHandle)handle).GetSequencePoints())
            {
                if (point.IsHidden || point.Document.IsNil) continue;
                string path = reader.GetString(reader.GetDocument(point.Document).Name);
                if (File.Exists(path)) points.Add((path, point.StartLine, point.StartColumn));
                break;
            }
        }

        foreach (var file in points.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var near = points.First(p => string.Equals(p.Path, file, StringComparison.OrdinalIgnoreCase));
            if (Declaration(file, symbol, near.Line, near.Column) is { } at)
                return new CodeLocation { Path = file, Line = at.Line, Column = at.Character, Display = display, Kind = kind };
        }

        if (points.Count == 0) return null;
        var first = points[0];
        return new CodeLocation { Path = first.Path, Line = first.Line - 1, Column = first.Column - 1, Display = display, Kind = kind };
    }

    private static MetadataReader? Reader(string dll)
    {
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(dll); }
        catch (IOException) { return null; }

        var pdb = s_pdbs.AddOrUpdate(dll, path => Open(path, stamp), (path, old) => old.Stamp == stamp ? old : Open(path, stamp));
        return pdb.Provider?.GetMetadataReader();
    }

    // The PDB beside the assembly or embedded in it.
    private static Pdb Open(string dll, DateTime stamp)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(dll));
            pe.TryOpenAssociatedPortablePdb(dll, path => File.Exists(path) ? File.OpenRead(path) : null, out var provider, out _);
            return new Pdb(stamp, provider);
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
            return new Pdb(stamp, null);
        }
    }

    private static IEnumerable<IMethodSymbol> MethodsOf(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => new[] { method },
        IPropertySymbol property => new[] { property.GetMethod, property.SetMethod }.OfType<IMethodSymbol>(),
        IEventSymbol e => new[] { e.AddMethod, e.RemoveMethod }.OfType<IMethodSymbol>(),
        INamedTypeSymbol type => type.GetMembers().OfType<IMethodSymbol>(),
        IFieldSymbol field when field.ContainingType != null => field.ContainingType.GetMembers().OfType<IMethodSymbol>()
            .OrderBy(m => m.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor ? 0 : 1),
        _ => Array.Empty<IMethodSymbol>(),
    };

    // The declared name of the symbol in the file. A method is the one around its compiled code, so overloads resolve.
    private static LinePosition? Declaration(string path, ISymbol symbol, int line, int column)
    {
        SourceText text;
        try { text = SourceText.From(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        var tree = CSharpSyntaxTree.ParseText(text, path: path);
        var root = tree.GetRoot();

        SyntaxToken? name = null;
        if (symbol is IMethodSymbol && line > 0 && line <= text.Lines.Count)
        {
            int position = Math.Min(text.Lines[line - 1].Start + Math.Max(0, column - 1), text.Length);
            var member = root.FindToken(position).Parent?.AncestorsAndSelf().FirstOrDefault(n => n is MemberDeclarationSyntax or AccessorDeclarationSyntax);
            name = member switch
            {
                MethodDeclarationSyntax m => m.Identifier,
                ConstructorDeclarationSyntax c => c.Identifier,
                DestructorDeclarationSyntax d => d.Identifier,
                AccessorDeclarationSyntax { Parent.Parent: PropertyDeclarationSyntax p } => p.Identifier,
                _ => null,
            };
        }

        name ??= NamedDeclaration(root, symbol);
        return name is { } token ? token.GetLocation().GetLineSpan().StartLinePosition : null;
    }

    private static SyntaxToken? NamedDeclaration(SyntaxNode root, ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type == null) return null;
        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (declaration.Identifier.ValueText != type.Name) continue;
            if (ReferenceEquals(symbol, type) || symbol is INamedTypeSymbol) return declaration.Identifier;

            foreach (var member in declaration.ChildNodes())
            {
                SyntaxToken? token = member switch
                {
                    PropertyDeclarationSyntax p when p.Identifier.ValueText == symbol.Name => p.Identifier,
                    MethodDeclarationSyntax m when m.Identifier.ValueText == symbol.Name => m.Identifier,
                    EventDeclarationSyntax e when e.Identifier.ValueText == symbol.Name => e.Identifier,
                    EnumMemberDeclarationSyntax e when e.Identifier.ValueText == symbol.Name => e.Identifier,
                    ConstructorDeclarationSyntax c when symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } => c.Identifier,
                    BaseFieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault(v => v.Identifier.ValueText == symbol.Name)?.Identifier,
                    _ => null,
                };
                if (token is { } found && found.ValueText.Length > 0) return found;
            }
        }
        return null;
    }
}
