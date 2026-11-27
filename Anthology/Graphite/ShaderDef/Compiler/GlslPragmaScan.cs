using System;
using System.Collections.Generic;

using Prowl.Prism.Glsl;

using PrismToken = Prowl.Prism.Glsl.TokenKind;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Finds the variant and global pragmas a pass can reach, following its #if, #ifdef, #ifndef, #elif and #else as far as
/// they are decided. A condition on macros fixed for the pass, its own defines, the compiler's and the stage's, takes
/// one branch. A condition on a variant axis, or one it cannot read, keeps every branch, so no variant loses a pragma.
/// </summary>
internal sealed class GlslPragmaScan
{
    private enum Truth { False, True, Unknown }

    private readonly record struct Macro(Truth Defined, string? Value);

    private sealed class Branch
    {
        public Truth Parent;
        public Truth Active;
        public Truth Taken;     // whether a condition of the chain so far held
    }

    private readonly Func<string, string, string?> _resolve;
    private readonly HashSet<string> _axisMacros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Macro> _macros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Truth> _entered = new(StringComparer.Ordinal);
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private List<VariantSpace> _axes = [];
    private List<ShaderGlobal> _globals = [];

    private GlslPragmaScan(Func<string, string, string?> resolve) => _resolve = resolve;


    /// <summary>
    /// Scans each stage's source with its stage define, a null define leaving every stage define unknown, and returns
    /// the axes and globals of all of them in the order they are first declared.
    /// </summary>
    public static (VariantSpace[] Axes, ShaderGlobal[] Globals) Scan(string file, IReadOnlyList<(string? Define, string Source)> stages,
        IReadOnlyDictionary<string, string> defines, Func<string, string, string?> resolve)
    {
        GlslPragmaScan scan = new(resolve);
        while (true)
        {
            scan._axes = [];
            scan._globals = [];
            foreach ((string? define, string source) in stages)
            {
                scan._macros.Clear();
                scan._entered.Clear();
                foreach ((string name, string value) in defines)
                    scan._macros[name] = new Macro(Truth.True, value.Length > 0 ? value : "1");
                foreach (string stage in StageDefines)
                    scan._macros[stage] = define == null ? new Macro(Truth.Unknown, null) : new Macro(stage == define ? Truth.True : Truth.False, "1");
                scan.Read(source, file, Truth.True);
            }

            // An axis found late can open branches read before it, so the scan repeats until the axes stop growing
            int known = scan._axisMacros.Count;
            foreach (VariantSpace axis in scan._axes)
            {
                if (axis.IsToggle)
                    scan._axisMacros.Add(axis.Name);
                else
                    foreach (string value in axis.Values)
                        scan._axisMacros.Add($"{axis.Name}_{value}");
            }
            if (scan._axisMacros.Count == known)
                return ([.. scan._axes], [.. scan._globals]);
        }
    }


    private static readonly string[] StageDefines = ["GRAPHITE_VERTEX", "GRAPHITE_FRAGMENT", "GRAPHITE_COMPUTE"];


    private void Read(string source, string file, Truth entry)
    {
        List<Branch> branches = [];
        Truth Active() => branches.Count == 0 ? entry : branches[^1].Active;

        foreach (string line in GlslPragmas.StripComments(source).Replace("\\\r\n", "").Replace("\\\n", "").Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith('#'))
                continue;

            string directive = trimmed[1..].TrimStart();
            string word = Identifier(directive, 0);
            string rest = directive[word.Length..].Trim();
            switch (word)
            {
                case "if":
                case "ifdef":
                case "ifndef":
                {
                    Truth parent = Active();
                    Truth condition = parent == Truth.False ? Truth.False : word switch
                    {
                        "if" => Condition(rest),
                        "ifdef" => Lookup(Identifier(rest, 0)).Defined,
                        _ => Not(Lookup(Identifier(rest, 0)).Defined),
                    };
                    branches.Add(new Branch { Parent = parent, Active = And(parent, condition), Taken = condition });
                    break;
                }
                case "elif":
                    if (branches.Count > 0)
                    {
                        Branch b = branches[^1];
                        Truth condition = b.Parent == Truth.False || b.Taken == Truth.True ? Truth.False : Condition(rest);
                        b.Active = And(b.Parent, And(Not(b.Taken), condition));
                        b.Taken = Or(b.Taken, condition);
                    }
                    break;
                case "else":
                    if (branches.Count > 0)
                    {
                        Branch b = branches[^1];
                        b.Active = And(b.Parent, Not(b.Taken));
                        b.Taken = Truth.True;
                    }
                    break;
                case "endif":
                    if (branches.Count > 0)
                        branches.RemoveAt(branches.Count - 1);
                    break;
                default:
                    Truth active = Active();
                    if (active == Truth.False)
                        break;
                    if (word == "define")
                        Define(rest, active);
                    else if (word == "undef")
                        Undefine(Identifier(rest, 0), active);
                    else if (word == "include")
                        Include(rest.Trim('"', '<', '>'), file, active);
                    else if (word == "pragma")
                        GlslPragmas.Read(trimmed, file, _axes, _globals);
                    break;
            }
        }
    }


    // Each file is entered once per stage, so a file entered on only some paths so far is read here as maybe active
    private void Include(string name, string from, Truth active)
    {
        Truth entered = _entered.GetValueOrDefault(name, Truth.False);
        if (entered == Truth.True || _open.Contains(name) || _resolve(name, from) is not { } text)
            return;

        _open.Add(name);
        Read(text, name, entered == Truth.Unknown ? And(active, Truth.Unknown) : active);
        _open.Remove(name);
        _entered[name] = active == Truth.True ? Truth.True : Truth.Unknown;
    }


    private void Define(string rest, Truth active)
    {
        string name = Identifier(rest, 0);
        if (name.Length == 0)
            return;
        bool function = rest.Length > name.Length && rest[name.Length] == '(';
        Macro macro = new(Truth.True, function ? null : rest[name.Length..].Trim());
        _macros[name] = active == Truth.True || Lookup(name) == macro ? macro : new Macro(Truth.Unknown, null);
    }


    private void Undefine(string name, Truth active)
    {
        if (name.Length > 0)
            _macros[name] = active == Truth.True || Lookup(name).Defined == Truth.False ? new Macro(Truth.False, null) : new Macro(Truth.Unknown, null);
    }


    // Macros not defined by the pass are undefined, except the axes' and the predefined ones, GL_ extensions, VULKAN and __LINE__ style names
    private Macro Lookup(string name)
    {
        if (_macros.TryGetValue(name, out Macro macro))
            return macro;
        bool unknown = _axisMacros.Contains(name) || name.StartsWith("GL_", StringComparison.Ordinal) || name.StartsWith("__", StringComparison.Ordinal) || name == "VULKAN";
        return new Macro(unknown ? Truth.Unknown : Truth.False, null);
    }


    // An #if expression of defined, integer literals, macros holding one, parentheses, !, && and ||, unknown otherwise
    private Truth Condition(string expression)
    {
        List<Token> tokens;
        try
        {
            tokens = Lexer.Tokenize(expression);
        }
        catch (GlslParseException)
        {
            return Truth.Unknown;
        }

        Expression e = new(this, tokens);
        long? value = e.Or();
        if (e.Failed || tokens[e.At].Kind != PrismToken.End || value is not { } v)
            return Truth.Unknown;
        return v != 0 ? Truth.True : Truth.False;
    }


    private sealed class Expression(GlslPragmaScan scan, List<Token> tokens)
    {
        public int At;
        public bool Failed;

        private Token Peek => tokens[At];

        private bool Accept(string text)
        {
            if (Peek.Text != text)
                return false;
            At++;
            return true;
        }

        public long? Or()
        {
            long? left = And();
            while (Accept("||"))
            {
                long? right = And();
                left = IsTrue(left) || IsTrue(right) ? 1 : left == 0 && right == 0 ? 0 : null;
            }
            return left;
        }

        private static bool IsTrue(long? value) => value is { } v && v != 0;

        private long? And()
        {
            long? left = Unary();
            while (Accept("&&"))
            {
                long? right = Unary();
                left = left == 0 || right == 0 ? 0 : left != null && right != null ? 1 : null;
            }
            return left;
        }

        private long? Unary()
        {
            if (Accept("!"))
                return Unary() is { } v ? (v == 0 ? 1 : 0) : null;
            return Primary();
        }

        private long? Primary()
        {
            Token t = Peek;
            if (Accept("("))
            {
                long? inner = Or();
                if (!Accept(")"))
                    Failed = true;
                return inner;
            }

            if (t.Kind == PrismToken.IntConst)
            {
                At++;
                return Integer(t.Text);
            }

            if (t.Kind is not (PrismToken.Identifier or PrismToken.BoolConst))
            {
                Failed = true;
                return null;
            }

            At++;
            if (t.Text == "defined")
            {
                bool paren = Accept("(");
                Token name = Peek;
                if (name.Kind is not (PrismToken.Identifier or PrismToken.BoolConst))
                {
                    Failed = true;
                    return null;
                }
                At++;
                if (paren && !Accept(")"))
                    Failed = true;
                return scan.Lookup(name.Text).Defined switch { Truth.True => 1, Truth.False => 0, _ => null };
            }

            if (Peek.Kind == PrismToken.LParen)
            {
                Failed = true;
                return null;
            }

            Macro macro = scan.Lookup(t.Text);
            return macro.Defined switch
            {
                Truth.False => 0,
                Truth.True when macro.Value is { } value => Integer(value),
                _ => null,
            };
        }

        private static long? Integer(string text)
        {
            string t = text.TrimEnd('u', 'U');
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(t[2..], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out long hex) ? hex : null;
            if (t.Length > 1 && t[0] == '0')
            {
                try { return Convert.ToInt64(t, 8); }
                catch (FormatException) { return null; }
                catch (OverflowException) { return null; }
            }
            return long.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long value) ? value : null;
        }
    }


    private static string Identifier(string text, int start)
    {
        int end = start;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
            end++;
        return text[start..end];
    }


    private static Truth Not(Truth a) => a switch { Truth.True => Truth.False, Truth.False => Truth.True, _ => Truth.Unknown };

    private static Truth And(Truth a, Truth b) => a == Truth.False || b == Truth.False ? Truth.False : a == Truth.True && b == Truth.True ? Truth.True : Truth.Unknown;

    private static Truth Or(Truth a, Truth b) => a == Truth.True || b == Truth.True ? Truth.True : a == Truth.False && b == Truth.False ? Truth.False : Truth.Unknown;
}
