// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Prowl.CodeGen.Networking;

internal sealed partial class NetworkCodeGen
{
    /// <summary>Why a value can't be sent: what's wrong, said of a member <see cref="Path"/> below the value ("" for the value itself).</summary>
    private sealed record Problem(string Path, string Predicate)
    {
        public string Describe(string value) => $"{value}{(Path.Length == 0 ? "" : "." + Path)} {Predicate}";

        public Problem Under(string member) => this with { Path = Path.Length == 0 ? member : member + "." + Path };
    }

    /// <summary>
    /// <para>
    /// Which types can be sent, and the serializers the game's own need. A struct or class declared in these
    /// scripts is sent member by member, base class first: its public fields, its [SerializeField] fields and
    /// its public auto-properties, skipping [SerializeIgnore] and [NonSerialized]. The generated code reaches
    /// them through <c>UnsafeAccessor</c>, so private and readonly members are fine and nothing is reflected
    /// over at run time.
    /// </para>
    /// <para>
    /// A class or interface stands for itself and for every class of these scripts it can hold, abstract ones
    /// included as long as something concrete derives from them; references between the objects of one value,
    /// cycles too, arrive as they were (see the runtime's <c>NetTypeSerializer</c>). A type that implements
    /// <c>INetSerializable</c> writes itself and is only registered.
    /// </para>
    /// </summary>
    private sealed class SerializerPlan(Compilation compilation, Symbols symbols)
    {
        private readonly Dictionary<ITypeSymbol, Problem?> _problems = new(SymbolEqualityComparer.Default);
        private readonly List<(INamedTypeSymbol Type, List<IFieldSymbol> Members)> _shapes = new();
        private readonly List<INamedTypeSymbol> _bases = new();
        private readonly List<INamedTypeSymbol> _selfWritten = new();
        private List<INamedTypeSymbol>? _classes;

        /// <summary>Null if values of the type can be sent; otherwise why not.</summary>
        public Problem? ProblemWith(ITypeSymbol type)
        {
            if (_problems.TryGetValue(type, out Problem? known))
                return known;

            // Taken as sendable while its own members are looked at, so a class can hold one of itself.
            _problems[type] = null;
            return _problems[type] = Check(type);
        }

        private Problem? Check(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum || DerivesFrom(type, symbols.Component) || DerivesFrom(type, symbols.GameObject))
                return null;

            string name = type.ToDisplayString();
            Problem unsupported = new("", $"is a {name}, which isn't a type the network can send (see NetSerializer<T>)");
            if (type is not INamedTypeSymbol named || named.TypeKind is not (TypeKind.Struct or TypeKind.Class or TypeKind.Interface)
                || named.IsStatic || named.IsRefLikeType)
                return unsupported;

            bool own = SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, compilation.Assembly);
            bool writesItself = named.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, symbols.INetSerializable)
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], named));
            if (!own && !writesItself)
                return symbols.BuiltIns.Contains(named) ? null : unsupported;

            if (!CanName(named))
                return new Problem("", $"is a {name}, which is private or protected, or inside a type that is. Make it internal or public");

            if (writesItself)
            {
                _selfWritten.Add(named);
                return null;
            }

            if (named.IsGenericType)
                return new Problem("", $"is a {name}, which is generic. Implement INetSerializable<T> on it to send it");

            if (named.TypeKind == TypeKind.Struct)
                return Shape(named);

            // A class or interface: itself, if it can be made, and whatever of ours derives from it.
            if (!named.IsAbstract && named.TypeKind == TypeKind.Class && Shape(named) is { } own_problem)
                return own_problem;

            foreach (INamedTypeSymbol derived in OwnClasses().Where(c => !SymbolEqualityComparer.Default.Equals(c, named) && IsA(c, named)))
            {
                if (ProblemWith(derived) is { } problem)
                    return new Problem("", $"can be a {derived.Name}, and {problem.Describe(derived.Name)}");
            }

            if (named.IsAbstract || named.TypeKind == TypeKind.Interface)
                _bases.Add(named);
            return null;
        }

        /// <summary>Plans a serializer for a struct or concrete class, member by member; or says why it can't have one.</summary>
        private Problem? Shape(INamedTypeSymbol type)
        {
            string name = type.ToDisplayString();
            if (type.TypeKind == TypeKind.Class && !type.InstanceConstructors.Any(c => c.Parameters.IsEmpty))
                return new Problem("", $"is a {name}, which has no parameterless constructor to make one with");

            List<IFieldSymbol> members = Members(type);
            if (members.Count == 0 && type.GetMembers().OfType<IFieldSymbol>().Any(f => !f.IsStatic && !f.IsConst))
                return new Problem("", $"is a {name}, which has nothing to send. Make the fields or auto-properties to send public, or mark fields [SerializeField]");

            foreach (IFieldSymbol member in members)
            {
                if (ProblemWith(member.Type) is { } problem)
                    return problem.Under((member.AssociatedSymbol ?? member).Name);
            }

            _shapes.Add((type, members));
            return null;
        }

        /// <summary>The fields a type is sent as, its base classes' first, auto-properties as their backing fields.</summary>
        private List<IFieldSymbol> Members(INamedTypeSymbol type)
        {
            Stack<INamedTypeSymbol> chain = new();
            for (INamedTypeSymbol? t = type; t != null && IsOwn(t); t = t.BaseType)
                chain.Push(t);
            return chain.SelectMany(t => t.GetMembers().OfType<IFieldSymbol>().Where(IsSent)).ToList();
        }

        private static bool IsSent(IFieldSymbol field)
        {
            if (field.IsStatic || field.IsConst || HasAttribute(field, "SerializeIgnore") || HasAttribute(field, "NonSerialized"))
                return false;
            if (field.AssociatedSymbol is IPropertySymbol property)
                return property.DeclaredAccessibility == Accessibility.Public && !HasAttribute(property, "SerializeIgnore");
            return !field.IsImplicitlyDeclared && (field.DeclaredAccessibility == Accessibility.Public || HasAttribute(field, "SerializeField"));
        }

        private static bool HasAttribute(ISymbol symbol, string name) =>
            symbol.GetAttributes().Any(a => a.AttributeClass?.Name == name + "Attribute");

        /// <summary>Whether generated code outside the type can write its name.</summary>
        private static bool CanName(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol? t = type; t != null; t = t.ContainingType)
            {
                if (t.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                    return false;
            }
            return true;
        }

        private bool IsOwn(ITypeSymbol type) => SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly);

        private static bool IsA(INamedTypeSymbol type, INamedTypeSymbol baseOrInterface) =>
            baseOrInterface.TypeKind == TypeKind.Interface
                ? type.AllInterfaces.Contains(baseOrInterface, SymbolEqualityComparer.Default)
                : DerivesFrom(type.BaseType, baseOrInterface);

        /// <summary>The non-generic classes these scripts declare, which a class or interface value can turn out to be. Components travel by reference instead.</summary>
        private List<INamedTypeSymbol> OwnClasses()
        {
            if (_classes != null)
                return _classes;

            _classes = new List<INamedTypeSymbol>();
            Stack<INamespaceOrTypeSymbol> pending = new([compilation.Assembly.GlobalNamespace]);
            while (pending.Count > 0)
            {
                INamespaceOrTypeSymbol container = pending.Pop();
                foreach (INamespaceOrTypeSymbol member in container is INamespaceSymbol space ? space.GetMembers() : container.GetTypeMembers())
                {
                    pending.Push(member);
                    if (member is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsGenericType: false } type
                        && !DerivesFrom(type, symbols.Component))
                        _classes.Add(type);
                }
            }
            return _classes;
        }


        private static readonly SymbolDisplayFormat s_runtimeName = new(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        /// <summary>
        /// One file that registers every serializer found needed, from a module initializer, so they're in
        /// place before the assembly's first value is sent. Null if none are. Two classes whose ids collide are
        /// reported to <paramref name="diagnostics"/>.
        /// </summary>
        public SyntaxTree? Generate(List<Diagnostic> diagnostics)
        {
            if (_shapes.Count == 0 && _bases.Count == 0 && _selfWritten.Count == 0)
                return null;

            const string accessor = "[global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind";
            StringBuilder code = new();
            code.AppendLine("// <auto-generated/> Prowl's script compiler: serializers for this assembly's own types that go over the network.");
            code.AppendLine("#nullable disable");
            code.AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
            code.AppendLine("internal static class __ProwlNetSerializers");
            code.AppendLine("{");
            code.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            code.AppendLine("    internal static void Register()");
            code.AppendLine("    {");
            foreach (INamedTypeSymbol type in _selfWritten)
                code.AppendLine($"        {TypeSerializer}.RegisterSelf<{Name(type)}>();");
            foreach (INamedTypeSymbol type in _bases)
                code.AppendLine($"        {TypeSerializer}.RegisterBase<{Name(type)}>();");

            Dictionary<uint, INamedTypeSymbol> ids = new();
            for (int i = 0; i < _shapes.Count; i++)
            {
                INamedTypeSymbol type = _shapes[i].Type;
                string create = Constructs(type) ? $"New{i}" : "static () => new()";
                if (type.TypeKind == TypeKind.Struct)
                {
                    code.AppendLine($"        {TypeSerializer}.RegisterStruct<{Name(type)}>({create}, Write{i}, Read{i});");
                    continue;
                }

                // Peers tell the classes apart by name, which is the same everywhere the same build runs.
                uint id = NetworkId(type.ToDisplayString(s_runtimeName));
                if (!ids.TryAdd(id, type))
                    diagnostics.Add(Diagnostic.Create(s_collision, type.Locations.FirstOrDefault() ?? Location.None, type.ToDisplayString(), ids[id].ToDisplayString()));
                code.AppendLine($"        {TypeSerializer}.RegisterClass<{Name(type)}>({id}u, {create}, Write{i}, Read{i});");
            }
            code.AppendLine("    }");

            for (int i = 0; i < _shapes.Count; i++)
            {
                (INamedTypeSymbol type, List<IFieldSymbol> members) = _shapes[i];
                string self = Name(type);
                string by = type.TypeKind == TypeKind.Struct ? "ref " : "";

                code.AppendLine();
                for (int j = 0; j < members.Count; j++)
                {
                    INamedTypeSymbol owner = members[j].ContainingType;
                    string target = owner.TypeKind == TypeKind.Struct ? $"ref {Name(owner)}" : Name(owner);
                    code.AppendLine($"    {accessor}.Field, Name = {SymbolDisplay.FormatLiteral(members[j].MetadataName, true)})] "
                        + $"private static extern ref {Name(members[j].Type)} F{i}_{j}({target} o);");
                }

                if (Constructs(type))
                    code.AppendLine($"    {accessor}.Constructor)] private static extern {self} New{i}();");

                code.Append($"    private static void Write{i}({WriterType} w, ref {self} v) {{ ");
                for (int j = 0; j < members.Count; j++)
                    code.Append($"{Serializer}<{Name(members[j].Type)}>.Write(w, F{i}_{j}({by}v)); ");
                code.AppendLine("}");

                code.Append($"    private static void Read{i}(ref {ReaderType} r, ref {self} v) {{ ");
                for (int j = 0; j < members.Count; j++)
                    code.Append($"F{i}_{j}({by}v) = {Serializer}<{Name(members[j].Type)}>.Read(ref r); ");
                code.AppendLine("}");
            }
            code.AppendLine("}");

            CSharpParseOptions? options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
            return CSharpSyntaxTree.ParseText(code.ToString(), options, path: "ProwlNetSerializers.g.cs", encoding: Encoding.UTF8);
        }

        /// <summary>
        /// Whether a value is made by calling its parameterless constructor whatever its access: always for a
        /// class, and for a struct whose own one only it can call. Otherwise a struct starts from <c>new()</c>.
        /// </summary>
        private static bool Constructs(INamedTypeSymbol type) =>
            type.TypeKind == TypeKind.Class || type.InstanceConstructors.Any(c => c.Parameters.IsEmpty && !c.IsImplicitlyDeclared
                && c.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal));

        private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
