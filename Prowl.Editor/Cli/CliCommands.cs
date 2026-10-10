// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using Prowl.Cli;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary> Exposes a static method to the prowl CLI and to agents driving the editor. Runs on the main thread. </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CliCommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public CliCommandAttribute(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}

/// <summary> Names and describes a parameter of a <see cref="CliCommandAttribute"/> method. Parameters without a default are required. </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CliArgAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public CliArgAttribute(string name, string description = "")
    {
        Name = name;
        Description = description;
    }
}

/// <summary> Every discovered CLI command, plus argument binding and turning results into JSON. Main thread only. </summary>
public static class CliCommands
{
    private sealed record Arg(ParameterInfo Parameter, string Name, string Description)
    {
        public bool IsBool => (Nullable.GetUnderlyingType(Parameter.ParameterType) ?? Parameter.ParameterType) == typeof(bool);
    }

    private sealed record Command(string Name, string Description, MethodInfo Method, Arg[] Args);

    private static readonly Dictionary<string, Command> s_commands = new(StringComparer.OrdinalIgnoreCase);

    public const int MaxResultDepth = 6;
    public const int MaxResultItems = 1000;

    internal static void Scan(MethodInfo method)
    {
        var attr = method.GetCustomAttribute<CliCommandAttribute>();
        if (attr == null) return;

        if (s_commands.TryGetValue(attr.Name, out var existing))
        {
            Debug.LogWarning($"[CLI] Command '{attr.Name}' on {method.DeclaringType?.FullName}.{method.Name} is already defined by {existing.Method.DeclaringType?.FullName}.{existing.Method.Name}, skipping it.");
            return;
        }

        var args = method.GetParameters()
            .Select(p => p.GetCustomAttribute<CliArgAttribute>() is { } a
                ? new Arg(p, a.Name, a.Description)
                : new Arg(p, p.Name ?? $"arg{p.Position}", ""))
            .ToArray();
        s_commands[attr.Name] = new Command(attr.Name, attr.Description, method, args);
    }

    internal static void Clear() => s_commands.Clear();

    public static List<CliCommandInfo> Describe() => s_commands.Values
        .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .Select(c => new CliCommandInfo
        {
            Name = c.Name,
            Description = c.Description,
            Args = c.Args.Select(a => new CliArgInfo
            {
                Name = a.Name,
                Description = a.Description,
                Type = DescribeType(a.Parameter.ParameterType),
                Required = !a.Parameter.HasDefaultValue,
                Default = a.Parameter.HasDefaultValue ? a.Parameter.DefaultValue?.ToString() : null,
            }).ToList(),
        }).ToList();

    /// <summary> Binds the request's arguments and invokes the command. Returns what the method returned, which may be a Task still running. </summary>
    public static object? Invoke(CliRunRequest request)
    {
        if (!s_commands.TryGetValue(request.Command, out var command))
            throw new CliException($"Unknown command '{request.Command}'. Run 'prowl command' to list them.");

        var values = Bind(command, request);
        try { return command.Method.Invoke(null, values); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static object?[] Bind(Command command, CliRunRequest request)
    {
        var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>(request.Positional);

        foreach (var (key, value) in request.Args)
            AddNamed(command, named, key, value);
        ParseArgv(command, request.Argv, named, positional);

        var values = new object?[command.Args.Length];
        int next = 0;

        for (int i = 0; i < command.Args.Length; i++)
        {
            var arg = command.Args[i];
            if (named.TryGetValue(arg.Name, out string? text))
                values[i] = Convert(text, arg.Parameter.ParameterType, arg.Name);
            else if (next < positional.Count)
                values[i] = Convert(positional[next++], arg.Parameter.ParameterType, arg.Name);
            else if (arg.Parameter.HasDefaultValue)
                values[i] = arg.Parameter.DefaultValue;
            else
                throw new CliException($"Command '{command.Name}' needs argument '{arg.Name}'.");
        }

        if (next < positional.Count)
            throw new CliException($"Command '{command.Name}' got {positional.Count - next} more argument(s) than it takes.");

        return values;
    }

    private static void ParseArgv(Command command, List<string> argv, Dictionary<string, string> named, List<string> positional)
    {
        bool optionsEnded = false;
        for (int i = 0; i < argv.Count; i++)
        {
            string token = argv[i];
            if (!optionsEnded && token == "--")
            {
                optionsEnded = true;
                continue;
            }
            if (optionsEnded || !token.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(token);
                continue;
            }

            string key = token[2..];
            string? value = null;
            int equals = key.IndexOf('=');
            if (equals >= 0)
            {
                value = key[(equals + 1)..];
                key = key[..equals];
            }

            var arg = FindArg(command, key);
            if (value == null)
            {
                if (arg.IsBool)
                    value = i + 1 < argv.Count && bool.TryParse(argv[i + 1], out _) ? argv[++i] : "true";
                else if (i + 1 < argv.Count)
                    value = argv[++i];
                else
                    throw new CliException($"Argument '{arg.Name}' needs a value.");
            }

            AddNamed(command, named, arg.Name, value);
        }
    }

    private static void AddNamed(Command command, Dictionary<string, string> named, string key, string value)
    {
        var arg = FindArg(command, key);
        if (!named.TryAdd(arg.Name, value))
            throw new CliException($"Argument '{arg.Name}' was given more than once.");
    }

    private static Arg FindArg(Command command, string key)
        => command.Args.FirstOrDefault(a => a.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
           ?? throw new CliException($"Command '{command.Name}' has no argument '{key}'.");

    private static object? Convert(string text, Type type, string argName)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            if (target == typeof(string)) return text;
            if (target == typeof(bool)) return text.Length == 0 || bool.Parse(text);
            if (target.IsEnum) return ParseEnum(text, target);
            if (target == typeof(Guid)) return Guid.Parse(text);
            if (typeof(IConvertible).IsAssignableFrom(target)) return System.Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
            return JsonSerializer.Deserialize(text, target);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException or JsonException)
        {
            throw new CliException($"Argument '{argName}' expects {DescribeType(type)}, got '{text}'.");
        }
    }

    private static object ParseEnum(string text, Type type)
    {
        bool flags = type.IsDefined(typeof(FlagsAttribute));
        object value = Enum.Parse(type, flags ? text.Replace('|', ',') : text, ignoreCase: true);
        if (!flags && !Enum.IsDefined(type, value))
            throw new ArgumentException($"{text} is not a {type.Name}");
        return value;
    }

    private static string DescribeType(Type type)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsEnum)
            return target.IsDefined(typeof(FlagsAttribute))
                ? $"any of {string.Join(",", Enum.GetNames(target))}"
                : string.Join("|", Enum.GetNames(target));
        return target == typeof(string) ? "string"
            : target == typeof(bool) ? "bool"
            : target == typeof(int) ? "int"
            : target == typeof(float) ? "float"
            : target == typeof(double) ? "double"
            : target == typeof(long) ? "long"
            : target.Name;
    }

    /// <summary>
    /// Turns a command's return value into JSON. Collections, dictionaries and anonymous objects keep their shape, anything
    /// else becomes its ToString. Collections past <see cref="MaxResultItems"/> end with a truncation marker.
    /// </summary>
    public static JsonNode? ToJson(object? value, int depth = 0)
    {
        switch (value)
        {
            case null: return null;
            case JsonNode node: return node.Parent == null ? node : node.DeepClone();
            case string s: return JsonValue.Create(Sanitize(s));
            case bool b: return JsonValue.Create(b);
            case Enum e: return JsonValue.Create(e.ToString());
            case char c: return JsonValue.Create(Sanitize(c.ToString()));
            case float f: return float.IsFinite(f) ? JsonValue.Create(f) : JsonValue.Create(f.ToString(CultureInfo.InvariantCulture));
            case double d: return double.IsFinite(d) ? JsonValue.Create(d) : JsonValue.Create(d.ToString(CultureInfo.InvariantCulture));
            case decimal m: return JsonValue.Create(m);
            case ulong ul: return JsonValue.Create(ul);
            case IConvertible when value.GetType().IsPrimitive: return JsonValue.Create(System.Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }

        if (depth >= MaxResultDepth) return JsonValue.Create(Sanitize(value.ToString() ?? ""));

        if (value is IDictionary dict)
        {
            var obj = new JsonObject();
            foreach (DictionaryEntry entry in dict)
            {
                if (obj.Count == MaxResultItems)
                {
                    obj["..."] = $"truncated after {MaxResultItems} entries";
                    break;
                }
                obj[Sanitize(entry.Key.ToString() ?? "")] = ToJson(entry.Value, depth + 1);
            }
            return obj;
        }

        if (value is IEnumerable items)
        {
            var array = new JsonArray();
            foreach (object? item in items)
            {
                if (array.Count == MaxResultItems)
                {
                    array.Add(JsonValue.Create($"... truncated after {MaxResultItems} items"));
                    break;
                }
                array.Add(ToJson(item, depth + 1));
            }
            return array;
        }

        Type type = value.GetType();
        if (type.IsDefined(typeof(CompilerGeneratedAttribute)) && type.Name.Contains("AnonymousType"))
        {
            var obj = new JsonObject();
            foreach (var prop in type.GetProperties())
                if (ToJson(prop.GetValue(value), depth + 1) is { } propValue) obj[prop.Name] = propValue;
            return obj;
        }

        return JsonValue.Create(Sanitize(value.ToString() ?? ""));
    }

    // Lone surrogates cannot be written as JSON, so they become the replacement character.
    private static string Sanitize(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }

            var sb = new StringBuilder(text.Length);
            for (int j = 0; j < text.Length; j++)
            {
                char c = text[j];
                if (char.IsHighSurrogate(c) && j + 1 < text.Length && char.IsLowSurrogate(text[j + 1])) { sb.Append(c).Append(text[++j]); }
                else sb.Append(char.IsSurrogate(c) ? '�' : c);
            }
            return sb.ToString();
        }
        return text;
    }

    [CliCommand("eval", "Compiles and runs C# on the editor main thread and returns the result. Accepts an expression or statements with a return, and await")]
    public static Task<object?> Eval([CliArg("code", "C# expression or statements. Leading using directives are allowed")] string code)
    {
        if (!EditorSettings.Instance.AllowCliEval)
            throw new CliException("eval is turned off in Editor Preferences > General > Command Line.");
        return CliEval.Run(code);
    }
}

/// <summary> A command failed in a way the caller should read as a plain message, without a stack trace. </summary>
public sealed class CliException(string message) : Exception(message);

/// <summary> Compiles a C# snippet against every live assembly, including the project's scripts, and runs it once. </summary>
public static class CliEval
{
    private static readonly string[] s_defaultUsings =
    [
        "System", "System.Collections.Generic", "System.Linq", "System.Threading.Tasks",
        "Prowl.Vector", "Prowl.Runtime", "Prowl.Runtime.Resources", "Prowl.Editor", "Prowl.Editor.Core",
    ];

    private static readonly ConditionalWeakTable<Assembly, MetadataReference> s_references = new();
    private static readonly CSharpParseOptions s_scriptParse = new(LanguageVersion.Latest, kind: SourceCodeKind.Script);
    private static readonly CSharpParseOptions s_parse = new(LanguageVersion.Latest);
    private static int s_counter;

    /// <summary>
    /// Compiles on a worker thread, then runs the snippet on the thread that called this, which should be the main thread
    /// with the CLI's synchronization context current so the await resumes there.
    /// </summary>
    public static async Task<object?> Run(string code)
    {
        var assemblies = ScriptAssemblyManager.LiveAssemblies().Where(a => !a.IsDynamic).ToList();
        byte[] image = await Task.Run(() => Compile(code, assemblies));

        var context = new EvalLoadContext(assemblies);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(image));
            var method = assembly.GetType("ProwlEval")!.GetMethod("Run")!;
            Task<object?> task;
            try { task = (Task<object?>)method.Invoke(null, null)!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            return await task;
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary> Wraps and compiles the snippet. Throws a <see cref="CliException"/> listing the errors when it does not compile. </summary>
    public static byte[] Compile(string code, IReadOnlyList<Assembly> assemblies)
    {
        var (usings, body) = SplitUsings(code);
        string expression = body.Trim();
        if (expression.EndsWith(';')) expression = expression[..^1].TrimEnd();

        string runBody = IsExpression(expression)
            ? "return await V(async () =>\n#line 1\n" + expression + "\n#line default\n);"
            : "#line 1\n" + body + "\n#line default\nreturn null;";

        string source =
            string.Concat(s_defaultUsings.Select(u => $"using {u};\n")) + userUsingsLine(usings) +
            "#pragma warning disable CS1998, CS0162\n" +
            "public static class ProwlEval\n{\n" +
            "    public static async Task<object?> Run()\n    {\n" + runBody + "\n    }\n" +
            "    private static async Task<object?> V(Func<Task> f) { await f(); return null; }\n" +
            "    private static async Task<object?> V<T>(Func<Task<T>> f) => await f();\n" +
            "}\n";

        var compilation = CSharpCompilation.Create(
            $"ProwlEval{System.Threading.Interlocked.Increment(ref s_counter)}",
            [CSharpSyntaxTree.ParseText(source, s_parse)],
            GatherReferences(assemblies),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Annotations)
                .WithAllowUnsafe(true)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1701"] = ReportDiagnostic.Suppress,
                    ["CS1702"] = ReportDiagnostic.Suppress,
                    ["CS1705"] = ReportDiagnostic.Suppress,
                }));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (result.Success) return stream.ToArray();

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d =>
            {
                var line = d.Location.GetMappedLineSpan();
                string message = d.GetMessage(CultureInfo.InvariantCulture);
                return line.IsValid ? $"({line.StartLinePosition.Line + 1},{line.StartLinePosition.Character + 1}): {d.Id}: {message}" : $"{d.Id}: {message}";
            });
        throw new CliException(string.Join("\n", errors));

        static string userUsingsLine(string usings) => usings.Length == 0 ? "" : usings + "\n";
    }

    private static bool IsExpression(string text)
        => text.Length > 0 && !SyntaxFactory.ParseExpression(text, options: s_parse).ContainsDiagnostics;

    private static (string Usings, string Body) SplitUsings(string code)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(code, s_scriptParse).GetRoot();
        if (root.Usings.Count == 0) return ("", code);
        int end = root.Usings.Last().Span.End;
        return (code[..end], code[end..]);
    }

    private static List<MetadataReference> GatherReferences(IReadOnlyList<Assembly> assemblies)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        foreach (var assembly in assemblies)
        {
            string name = assembly.GetName().Name ?? "";
            if (!seen.Add(name)) continue;

            if (s_references.TryGetValue(assembly, out var cached))
            {
                references.Add(cached);
                continue;
            }

            MetadataReference? reference = null;
            try
            {
                if (!string.IsNullOrEmpty(assembly.Location))
                    reference = MetadataReference.CreateFromFile(assembly.Location);
                else if (ScriptAssemblyManager.GetAssemblyBytes(assembly) is { } bytes)
                    reference = MetadataReference.CreateFromImage(bytes);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException) { }

            if (reference == null) continue;
            s_references.AddOrUpdate(assembly, reference);
            references.Add(reference);
        }

        return references;
    }

    private sealed class EvalLoadContext(IReadOnlyList<Assembly> assemblies) : AssemblyLoadContext("ProwlEval", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
            => assemblies.FirstOrDefault(a => a.GetName().Name == name.Name);
    }
}
