// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;
using System.Text.RegularExpressions;

using Prowl.Slang;

namespace Quill.ShaderGen;

/// <summary>
/// Turns the Vulkan GLSL Slang emits into the Shared, Vertex and Fragment sections of a Graphite
/// GLSLPROGRAM. Graphite packs loose uniforms into its own block and assigns bindings itself, so
/// Slang's uniform block and resource declarations are replaced by loose declarations in the Shared
/// section, in Slang's parameter order, under the names Slang's reflection reports. Interface
/// variables keep Slang's locations and take their reflected names: the semantic for vertex inputs,
/// the field name for varyings.
///
/// Every declaration is matched to the reflection by its block member order, binding or location,
/// never by its mangled name, and anything unrecognised fails the run.
/// </summary>
internal static class GraphiteGlsl
{
    public const string FragmentOutput = "target";

    private static readonly Regex Token = new(@"[A-Za-z_]\w*|\d[\w.]*|\S", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"(?<dot>\.\s*)?\b(?<id>[A-Za-z_]\w*)\b(?:\s*\.\s*(?<member>[A-Za-z_]\w*))?", RegexOptions.Compiled);

    private static readonly HashSet<string> Qualifiers =
    [
        "uniform", "in", "out", "buffer", "flat", "smooth", "noperspective", "centroid",
        "highp", "mediump", "lowp", "readonly", "writeonly", "coherent", "restrict", "invariant",
    ];

    /// <summary>
    /// Builds the program sections. With a toggle, the fragment section compiles the toggle's entry
    /// point when its axis keyword is on and <paramref name="fragment"/> otherwise.
    /// </summary>
    public static string Build(SlangProgram program, string vertex, string fragment, (string Axis, string Fragment)? toggle = null)
    {
        List<Global> globals = Globals(program.Layout);

        Stage vs = Rewrite(program, vertex, globals);
        Stage fs = Rewrite(program, fragment, globals);
        Stage? alt = toggle is { } t ? Rewrite(program, t.Fragment, globals) : null;

        foreach (Stage stage in (Stage?[])[fs, alt])
        {
            if (stage == null)
                continue;

            foreach ((int location, string name) in stage.Inputs)
            {
                if (!vs.Outputs.TryGetValue(location, out string? written) || written != name)
                    throw new InvalidOperationException($"fragment input '{name}' at location {location} has no matching vertex output");
            }
        }

        StringBuilder shared = new();
        if (toggle is { } axis)
            shared.Append($"#pragma variant {axis.Axis}\n\n");

        foreach (Global g in globals)
        {
            if (g.Type == null)
                throw new InvalidOperationException($"'{g.Name}' is used by no entry point, so its GLSL type is unknown");
            shared.Append($"uniform {g.Type} {g.Name};\n");
        }

        string fragmentBody = toggle is { } on
            ? $"#ifdef {on.Axis}\n{alt!.Text}\n#else\n{fs.Text}\n#endif"
            : fs.Text;

        StringBuilder sb = new();
        Section(sb, "Shared", shared.ToString());
        sb.Append('\n');
        Section(sb, "Vertex", vs.Text);
        sb.Append('\n');
        Section(sb, "Fragment", fragmentBody);
        return sb.ToString().TrimEnd('\n');
    }

    private static void Section(StringBuilder sb, string name, string body)
    {
        sb.Append(name).Append("\n{\n");
        foreach (string line in body.TrimEnd('\n').Split('\n'))
            sb.Append(line.Length == 0 ? "" : "    " + line).Append('\n');
        sb.Append("}\n");
    }

    private sealed class Global(string name, bool opaque, uint binding, bool matrix)
    {
        public readonly string Name = name;
        public readonly bool Opaque = opaque;
        public readonly uint Binding = binding;
        public readonly bool Matrix = matrix;
        public string? Type;
    }

    private sealed record Stage(string Text, Dictionary<int, string> Inputs, Dictionary<int, string> Outputs);

    private static List<Global> Globals(ShaderReflection layout)
    {
        List<Global> globals = [];
        foreach (VariableLayoutReflection p in layout.Parameters)
        {
            TypeLayoutReflection type = p.TypeLayout;
            switch (p.Category)
            {
                case ParameterCategory.Uniform:
                    if (type.Kind == TypeKind.Matrix && type.RowCount != type.ColumnCount)
                        throw new InvalidOperationException($"'{p.Name}' is a non-square matrix, which Graphite does not reflect");
                    globals.Add(new Global(p.Name, false, 0, type.Kind == TypeKind.Matrix));
                    break;

                case ParameterCategory.DescriptorTableSlot when p.BindingSpace == 0:
                    globals.Add(new Global(p.Name, true, p.BindingIndex, false));
                    break;

                default:
                    throw new InvalidOperationException($"global '{p.Name}' is a {p.Category} parameter, only loose uniforms and resources are supported");
            }
        }
        return globals;
    }

    private static Stage Rewrite(SlangProgram program, string entryPoint, List<Global> globals)
    {
        EntryPointReflection reflection = program.EntryPoint(entryPoint);
        bool vertex = reflection.Stage == ShaderStage.Vertex;

        Dictionary<int, string> inputNames = [];
        foreach (VariableLayoutReflection p in reflection.Parameters)
            Leaves(p, ParameterCategory.VaryingInput, 0, vertex, inputNames);

        Dictionary<int, string> outputNames = [];
        if (vertex)
            Leaves(reflection.ResultVarLayout, ParameterCategory.VaryingOutput, 0, false, outputNames);

        List<Decl> decls = [.. Split(program.Code(entryPoint)).Select(Parse)];
        List<Decl> kept = [];
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        Dictionary<string, string> members = new(StringComparer.Ordinal);
        string? instance = null;
        bool rowMajor = false;
        Dictionary<int, string> inputs = [], outputs = [];

        foreach (Decl d in decls)
        {
            if (d.Directive)
            {
                if (!d.Text.StartsWith("#version"))
                    kept.Add(d);
                continue;
            }

            if (d.QualifierOnly)
            {
                if (d.Qualifiers.Contains("uniform"))
                    rowMajor = d.Layout.ContainsKey("row_major") || rowMajor && !d.Layout.ContainsKey("column_major");
                continue;
            }

            if (d.BlockName != null)
            {
                if (!d.Qualifiers.Contains("uniform") || instance != null)
                    throw new InvalidOperationException($"{entryPoint}: block '{d.BlockName}' is not the single default uniform block");

                instance = d.Name ?? throw new InvalidOperationException($"{entryPoint}: the uniform block has no instance name");
                List<Global> uniforms = [.. globals.Where(g => !g.Opaque)];
                if (d.Members.Count != uniforms.Count)
                    throw new InvalidOperationException($"{entryPoint}: the uniform block has {d.Members.Count} members, the reflection {uniforms.Count}");

                for (int i = 0; i < uniforms.Count; i++)
                {
                    Member m = d.Members[i];
                    Assign(uniforms[i], m.Type, entryPoint);
                    bool transpose = uniforms[i].Matrix && (m.RowMajor ?? rowMajor);
                    members[m.Name] = transpose ? $"transpose({uniforms[i].Name})" : uniforms[i].Name;
                }
                continue;
            }

            if (d.Qualifiers.Contains("uniform") && d.Name != null)
            {
                int binding = LayoutInt(d, "binding", entryPoint);
                if (d.Layout.TryGetValue("set", out string? set) && set != "0")
                    throw new InvalidOperationException($"{entryPoint}: '{d.Name}' is in descriptor set {set}, only set 0 is supported");

                Global g = globals.Find(x => x.Opaque && x.Binding == binding)
                    ?? throw new InvalidOperationException($"{entryPoint}: '{d.Name}' at binding {binding} matches no reflected resource");
                Assign(g, d.Type!, entryPoint);
                names[d.Name] = g.Name;
                continue;
            }

            if ((d.Qualifiers.Contains("in") || d.Qualifiers.Contains("out")) && d.Name != null)
            {
                bool isInput = d.Qualifiers.Contains("in");
                int location = LayoutInt(d, "location", entryPoint);
                string name = isInput
                    ? inputNames.GetValueOrDefault(location) ?? throw new InvalidOperationException($"{entryPoint}: input '{d.Name}' at location {location} matches no reflected parameter")
                    : vertex
                        ? outputNames.GetValueOrDefault(location) ?? throw new InvalidOperationException($"{entryPoint}: output '{d.Name}' at location {location} matches no reflected field")
                        : location == 0 ? FragmentOutput : FragmentOutput + location;

                (isInput ? inputs : outputs)[location] = name;
                names[d.Name] = name;
                kept.Add(d);
                continue;
            }

            kept.Add(d);
        }

        DropUnusedStructs(kept);

        List<string> texts = [.. kept.Select(d => d.Directive ? d.Text : Rename(d.Text, instance, members, names))];
        string text = string.Join("\n\n", texts);

        foreach (string old in names.Keys.Append(instance ?? "").Where(n => n.Length > 0))
        {
            if (Regex.IsMatch(text, $@"\b{Regex.Escape(old)}\b"))
                throw new InvalidOperationException($"{entryPoint}: '{old}' is still referenced after the rewrite");
        }

        return new Stage(text, inputs, outputs);
    }

    private static void Assign(Global g, string type, string entryPoint)
    {
        type = type switch { "mat4x4" => "mat4", "mat3x3" => "mat3", "mat2x2" => "mat2", _ => type };
        if (g.Type != null && g.Type != type)
            throw new InvalidOperationException($"{entryPoint}: '{g.Name}' is declared as {type} here and {g.Type} in another stage");
        g.Type = type;
    }

    private static int LayoutInt(Decl d, string key, string entryPoint)
    {
        if (d.Layout.TryGetValue(key, out string? value) && int.TryParse(value, out int result))
            return result;
        throw new InvalidOperationException($"{entryPoint}: '{d.Name}' has no {key} qualifier");
    }

    /// <summary>
    /// Collects the varying leaves of a parameter by location. Vertex inputs are named after their
    /// semantic, which is what the vertex buffers bind by, varyings after their field.
    /// </summary>
    private static void Leaves(VariableLayoutReflection v, ParameterCategory category, uint baseLocation, bool bySemantic, Dictionary<int, string> names)
    {
        if (!v.Categories.Contains(category))
            return;

        uint location = baseLocation + v.GetOffset(category);
        if (v.TypeLayout.Kind == TypeKind.Struct)
        {
            foreach (VariableLayoutReflection field in v.TypeLayout.Fields)
                Leaves(field, category, location, bySemantic, names);
            return;
        }

        string name = bySemantic ? v.SemanticName + v.SemanticIndex : v.Name;
        if (names.ContainsValue(name) || !names.TryAdd((int)location, name))
            throw new InvalidOperationException($"varying '{name}' at location {location} collides with another");
    }

    private static void DropUnusedStructs(List<Decl> kept)
    {
        bool removed = true;
        while (removed)
        {
            removed = false;
            foreach (Decl s in kept.Where(d => d.StructName != null).ToList())
            {
                Regex use = new($@"\b{Regex.Escape(s.StructName!)}\b");
                if (!kept.Any(d => d != s && use.IsMatch(d.Text)))
                {
                    kept.Remove(s);
                    removed = true;
                }
            }
        }
    }

    /// <summary>
    /// Renames references to the replaced declarations. A name after a dot is a member access, so it
    /// is only ever renamed as part of the uniform block instance it belongs to.
    /// </summary>
    private static string Rename(string text, string? instance, Dictionary<string, string> members, Dictionary<string, string> names)
        => Reference.Replace(text, m =>
        {
            if (m.Groups["dot"].Success)
                return m.Value;

            string id = m.Groups["id"].Value;
            if (id == instance && m.Groups["member"].Success && members.TryGetValue(m.Groups["member"].Value, out string? uniform))
                return uniform;

            return names.TryGetValue(id, out string? renamed) ? renamed + m.Value[id.Length..] : m.Value;
        });

    private sealed record Member(string Type, string Name, bool? RowMajor);

    private sealed class Decl
    {
        public required string Text;
        public bool Directive;
        public bool QualifierOnly;
        public Dictionary<string, string?> Layout = new(StringComparer.Ordinal);
        public HashSet<string> Qualifiers = new(StringComparer.Ordinal);
        public string? Type;
        public string? Name;
        public string? BlockName;
        public string? StructName;
        public List<Member> Members = [];
    }

    /// <summary>Splits a translation unit into directives and top level declarations.</summary>
    private static List<string> Split(string glsl)
    {
        string s = glsl.Replace("\r\n", "\n");
        List<string> items = [];
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
            if (i >= s.Length)
                break;

            int start = i;
            if (s[i] == '#')
            {
                while (i < s.Length && s[i] != '\n')
                    i++;
                items.Add(s[start..i].TrimEnd());
                continue;
            }

            int parens = 0, braces = 0;
            bool function = false;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c is '(' or '[') parens++;
                else if (c is ')' or ']') parens--;
                else if (c == '{')
                {
                    if (braces == 0 && parens == 0)
                        function = s[start..i].TrimEnd().EndsWith(')');
                    braces++;
                }
                else if (c == '}' && --braces == 0 && parens == 0 && function)
                    break;
                else if (c == ';' && braces == 0 && parens == 0)
                    break;
            }

            if (i >= s.Length)
                throw new InvalidOperationException("unterminated declaration in Slang's GLSL: " + s[start..]);

            items.Add(s[start..(i + 1)]);
            i++;
        }
        return items;
    }

    private static Decl Parse(string text)
    {
        Decl d = new() { Text = text };
        if (text.StartsWith('#'))
        {
            d.Directive = true;
            return d;
        }

        List<string> t = [.. Token.Matches(text).Select(m => m.Value)];
        int p = 0;
        while (p < t.Count)
        {
            if (t[p] == "layout" && p + 1 < t.Count && t[p + 1] == "(")
            {
                int close = t.IndexOf(")", p);
                foreach (string item in string.Join(" ", t[(p + 2)..close]).Split(','))
                {
                    string[] kv = item.Split('=', 2, StringSplitOptions.TrimEntries);
                    d.Layout[kv[0].Replace(" ", "")] = kv.Length > 1 ? kv[1].Replace(" ", "") : null;
                }
                p = close + 1;
            }
            else if (Qualifiers.Contains(t[p]))
            {
                d.Qualifiers.Add(t[p++]);
            }
            else
            {
                break;
            }
        }

        if (p >= t.Count || t[p] == ";")
        {
            d.QualifierOnly = true;
            return d;
        }

        if (t[p] == "struct")
        {
            d.StructName = t[p + 1];
            return d;
        }

        if (p + 1 < t.Count && t[p + 1] == "{")
        {
            d.BlockName = t[p];
            int close = t.LastIndexOf("}");
            d.Members = Members(t[(p + 2)..close]);
            d.Name = close + 1 < t.Count && t[close + 1] != ";" ? t[close + 1] : null;
            return d;
        }

        if (p + 2 < t.Count && t[p + 2] is ";" or "[")
        {
            d.Type = t[p];
            d.Name = t[p + 1];
        }
        return d;
    }

    private static List<Member> Members(List<string> t)
    {
        List<Member> members = [];
        int p = 0;
        while (p < t.Count)
        {
            bool? rowMajor = null;
            while (t[p] is "row_major" or "column_major" or "layout" || Qualifiers.Contains(t[p]))
            {
                if (t[p] == "layout")
                {
                    int close = t.IndexOf(")", p);
                    if (t[(p + 2)..close].Contains("row_major")) rowMajor = true;
                    if (t[(p + 2)..close].Contains("column_major")) rowMajor = false;
                    p = close + 1;
                    continue;
                }

                if (t[p] == "row_major") rowMajor = true;
                if (t[p] == "column_major") rowMajor = false;
                p++;
            }

            int end = t.IndexOf(";", p);
            if (end != p + 2)
                throw new InvalidOperationException("unsupported uniform block member: " + string.Join(" ", t[p..end]));

            members.Add(new Member(t[p], t[p + 1], rowMajor));
            p = end + 1;
        }
        return members;
    }
}
