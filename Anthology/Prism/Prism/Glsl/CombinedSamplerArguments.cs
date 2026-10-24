namespace Prowl.Prism.Glsl;

/// <summary>
/// Lets a combined sampler built on the spot, <c>f(sampler2D(tex, smp))</c>, be passed to a user function. SPIR-V passes
/// opaque values by pointer, so only a sampler variable can be an argument, and a constructed one is not a variable.
/// Each such call is pointed at a copy of the function that takes the texture and the sampler separately, which are
/// variables, and builds the combined sampler again where the body uses it. Bindless renderers build every material
/// texture this way. When the texture or sampler was picked with <c>nonuniformEXT</c>, the copy rebuilds it with
/// <c>nonuniformEXT</c> too, so the access stays marked non uniform inside the function. Programs without such a call
/// come back unchanged.
/// </summary>
public static class CombinedSamplerArguments
{
    public static TranslationUnit Split(TranslationUnit unit)
    {
        Dictionary<string, List<FunctionDef>> functions = [];
        foreach (FunctionDef f in unit.Declarations.OfType<FunctionDef>())
        {
            if (!functions.TryGetValue(f.Name, out List<FunctionDef>? list))
                functions[f.Name] = list = [];
            list.Add(f);
        }
        if (!functions.Values.SelectMany(l => l).Any(f => f.Parameters.Any(p => Parts(p.Type.Name) is not null && p.Type.Array is not { Count: > 0 } && p.Array.Count == 0)))
            return unit;

        Splitter splitter = new(functions);
        List<Decl> declarations = [];
        foreach (Decl d in unit.Declarations)
            declarations.Add(d is FunctionDef f ? f with { Body = splitter.Block(f.Body) } : d);
        if (splitter.Copies.Count == 0)
            return unit;

        // Each copy goes right after the function it copies, along with a prototype after the original's prototype,
        // so it is declared wherever the original is
        List<Decl> result = [];
        foreach (Decl d in declarations)
        {
            result.Add(d);
            string? name = d switch { FunctionDef f => f.Name, FunctionProto p => p.Name, _ => null };
            foreach ((FunctionDef original, FunctionDef copy) in splitter.Copies)
            {
                if (name != original.Name)
                    continue;
                if (d is FunctionDef def && ReferenceEquals(Original(splitter, def), original))
                    result.Add(copy);
                else if (d is FunctionProto proto && SameSignature(proto.Parameters, original.Parameters))
                    result.Add(new FunctionProto(copy.Qualifiers, copy.ReturnType, copy.Name, copy.Parameters) { Line = proto.Line, FloatRelaxed = proto.FloatRelaxed, IntRelaxed = proto.IntRelaxed });
            }
        }
        return unit with { Declarations = result };
    }

    // The declaration list holds rewritten bodies, so a definition is matched to its original by name and parameters.
    private static FunctionDef? Original(Splitter splitter, FunctionDef rewritten)
        => splitter.Functions.TryGetValue(rewritten.Name, out List<FunctionDef>? list) ? list.FirstOrDefault(f => SameSignature(f.Parameters, rewritten.Parameters)) : null;

    private static bool SameSignature(IReadOnlyList<Param> a, IReadOnlyList<Param> b)
        => a.Count == b.Count && a.Zip(b).All(x => x.First.Type.Name == x.Second.Type.Name);

    // The texture and sampler types a combined sampler type is made of, or null for any other type.
    private static (string Texture, string Sampler)? Parts(string type)
    {
        string prefix = type.StartsWith('i') || type.StartsWith('u') ? type[..1] : "";
        string rest = type[prefix.Length..];
        if (!rest.StartsWith("sampler") || rest == "sampler" || rest == "samplerShadow")
            return null;
        string shape = rest["sampler".Length..];
        bool shadow = shape.EndsWith("Shadow");
        if (shadow)
            shape = shape[..^"Shadow".Length];
        if (shape.Length == 0 || shape.StartsWith("Buffer"))
            return null;
        return (prefix + "texture" + shape, shadow ? "samplerShadow" : "sampler");
    }

    private sealed class Splitter(Dictionary<string, List<FunctionDef>> functions)
    {
        public readonly Dictionary<string, List<FunctionDef>> Functions = functions;
        public readonly List<(FunctionDef Original, FunctionDef Copy)> Copies = [];
        private readonly Dictionary<string, FunctionDef> _byKey = [];

        public Block Block(Block b) => b with { Statements = [.. b.Statements.Select(Stmt)] };

        private Stmt Stmt(Stmt s) => s switch
        {
            Block b => Block(b),
            VarDeclStmt v => v with { Declarators = [.. v.Declarators.Select(d => d with { Init = d.Init is null ? null : Expr(d.Init) })] },
            ExprStmt e => e with { Expression = Expr(e.Expression) },
            IfStmt i => i with { Cond = Expr(i.Cond), Then = Stmt(i.Then), Else = i.Else is null ? null : Stmt(i.Else) },
            ForStmt f => f with { Init = f.Init is null ? null : Stmt(f.Init), Cond = f.Cond is null ? null : Expr(f.Cond), Update = f.Update is null ? null : Expr(f.Update), Body = Stmt(f.Body) },
            WhileStmt w => w with { Cond = Expr(w.Cond), Body = Stmt(w.Body) },
            DoWhileStmt d => d with { Body = Stmt(d.Body), Cond = Expr(d.Cond) },
            SwitchStmt sw => sw with { Value = Expr(sw.Value), Cases = [.. sw.Cases.Select(c => c with { Body = [.. c.Body.Select(Stmt)] })] },
            ReturnStmt r => r with { Value = r.Value is null ? null : Expr(r.Value) },
            _ => s,
        };

        private Expr Expr(Expr e) => e switch
        {
            Unary u => u with { Operand = Expr(u.Operand) },
            Binary b => b with { Left = Expr(b.Left), Right = Expr(b.Right) },
            Conditional c => c with { Cond = Expr(c.Cond), Then = Expr(c.Then), Else = Expr(c.Else) },
            Index i => i with { Target = Expr(i.Target), Idx = Expr(i.Idx) },
            Member m => m with { Target = Expr(m.Target) },
            Method m => m with { Target = Expr(m.Target), Args = [.. m.Args.Select(Expr)] },
            Call c => Call(c with { Args = [.. c.Args.Select(Expr)] }),
            _ => e,
        };

        // A call passing a constructed combined sampler to a user function goes to the copy taking its parts.
        private Expr Call(Call c)
        {
            if (!Functions.TryGetValue(c.Name, out List<FunctionDef>? overloads) || c.IsArray)
                return c;
            List<FunctionDef> fits = overloads.Where(f => f.Parameters.Count == c.Args.Count && Splits(f, c).Count > 0).ToList();
            if (fits.Count != 1)
                return c;
            FunctionDef target = fits[0];
            List<int> split = Splits(target, c);
            bool[] uniform = [.. split.Select(i => !NonUniform(c.Args[i]))];
            FunctionDef copy = Copy(target, split, uniform);
            List<Expr> args = [];
            for (int i = 0; i < c.Args.Count; i++)
            {
                if (split.Contains(i) && c.Args[i] is Call constructor)
                    args.AddRange(constructor.Args);
                else
                    args.Add(c.Args[i]);
            }
            return c with { Name = copy.Name, Args = args };
        }

        // Which parameters get a combined sampler constructed for exactly their type.
        private static List<int> Splits(FunctionDef f, Call c)
        {
            List<int> split = [];
            for (int i = 0; i < f.Parameters.Count; i++)
            {
                Param p = f.Parameters[i];
                if (Parts(p.Type.Name) is not null && p.Array.Count == 0 && p.Type.Array is not { Count: > 0 }
                    && c.Args[i] is Call { Name: var name, Args.Count: 2, IsArray: false } && name == p.Type.Name)
                    split.Add(i);
            }
            return split;
        }

        private static bool NonUniform(Expr e) => e switch
        {
            Call { Name: "nonuniformEXT" } => true,
            Call c => c.Args.Any(NonUniform),
            Unary u => NonUniform(u.Operand),
            Binary b => NonUniform(b.Left) || NonUniform(b.Right),
            Conditional c => NonUniform(c.Cond) || NonUniform(c.Then) || NonUniform(c.Else),
            Index i => NonUniform(i.Target) || NonUniform(i.Idx),
            Member m => NonUniform(m.Target),
            Method m => NonUniform(m.Target) || m.Args.Any(NonUniform),
            _ => false,
        };

        private FunctionDef Copy(FunctionDef f, List<int> split, bool[] uniform)
        {
            string key = $"{f.Name}({string.Join(",", f.Parameters.Select(p => p.Type.Name))})[{string.Join(",", split.Select((s, k) => $"{s}{(uniform[k] ? "" : "n")}"))}]";
            if (_byKey.TryGetValue(key, out FunctionDef? made))
                return made;

            string name = $"{f.Name}_split{_byKey.Count}";
            List<Param> parameters = [];
            Dictionary<string, Expr> replace = [];
            for (int i = 0; i < f.Parameters.Count; i++)
            {
                Param p = f.Parameters[i];
                int at = split.IndexOf(i);
                if (at < 0)
                {
                    parameters.Add(p);
                    continue;
                }
                (string texture, string sampler) = Parts(p.Type.Name)!.Value;
                string textureName = $"{p.Name}_texture", samplerName = $"{p.Name}_sampler";
                parameters.Add(p with { Type = new TypeRef(texture), Name = textureName });
                parameters.Add(p with { Type = new TypeRef(sampler), Name = samplerName });
                Expr t = new Ident(textureName), s = new Ident(samplerName);
                if (!uniform[at])
                {
                    t = new Call("nonuniformEXT", [t]);
                    s = new Call("nonuniformEXT", [s]);
                }
                if (p.Name is not null)
                    replace[p.Name] = new Call(p.Type.Name, [t, s]);
            }
            made = f with { Name = name, Parameters = parameters, Body = (Block)Rename(f.Body, replace) };
            _byKey[key] = made;
            // The copy's own calls may pass the rebuilt sampler on, which splits those in turn
            made = made with { Body = Block(made.Body) };
            _byKey[key] = made;
            Copies.Add((f, made));
            return made;
        }

        private static Stmt Rename(Stmt s, Dictionary<string, Expr> replace)
        {
            Expr E(Expr e) => RenameExpr(e, replace);
            return s switch
            {
                Block b => b with { Statements = Scope(b.Statements, replace) },
                VarDeclStmt v => Declare(v, ref replace),
                ExprStmt e => e with { Expression = E(e.Expression) },
                IfStmt i => i with { Cond = E(i.Cond), Then = Rename(i.Then, replace), Else = i.Else is null ? null : Rename(i.Else, replace) },
                ForStmt f => For(f, replace),
                WhileStmt w => w with { Cond = E(w.Cond), Body = Rename(w.Body, replace) },
                DoWhileStmt d => d with { Body = Rename(d.Body, replace), Cond = E(d.Cond) },
                SwitchStmt sw => Switch(sw, replace),
                ReturnStmt r => r with { Value = r.Value is null ? null : E(r.Value) },
                _ => s,
            };
        }

        // A declaration of a replaced name hides the parameter from there to the end of its scope.
        private static List<Stmt> Scope(IEnumerable<Stmt> statements, Dictionary<string, Expr> replace)
        {
            List<Stmt> result = [];
            foreach (Stmt s in statements)
                result.Add(s is VarDeclStmt v ? Declare(v, ref replace) : Rename(s, replace));
            return result;
        }

        // An initializer still sees the parameter, since a name's scope starts after its initializer.
        private static VarDeclStmt Declare(VarDeclStmt v, ref Dictionary<string, Expr> replace)
        {
            List<Declarator> declarators = [];
            foreach (Declarator d in v.Declarators)
            {
                declarators.Add(d with { Init = d.Init is null ? null : RenameExpr(d.Init, replace) });
                if (replace.ContainsKey(d.Name))
                    replace = replace.Where(x => x.Key != d.Name).ToDictionary();
            }
            return v with { Declarators = declarators };
        }

        private static ForStmt For(ForStmt f, Dictionary<string, Expr> replace)
        {
            Stmt? init = f.Init is VarDeclStmt v ? Declare(v, ref replace) : f.Init is null ? null : Rename(f.Init, replace);
            return f with { Init = init, Cond = f.Cond is null ? null : RenameExpr(f.Cond, replace), Update = f.Update is null ? null : RenameExpr(f.Update, replace), Body = Rename(f.Body, replace) };
        }

        // The cases of a switch share one scope.
        private static SwitchStmt Switch(SwitchStmt sw, Dictionary<string, Expr> replace)
        {
            Expr value = RenameExpr(sw.Value, replace);
            List<SwitchCase> cases = [];
            foreach (SwitchCase c in sw.Cases)
            {
                List<Stmt> body = [];
                foreach (Stmt s in c.Body)
                    body.Add(s is VarDeclStmt v ? Declare(v, ref replace) : Rename(s, replace));
                cases.Add(c with { Body = body });
            }
            return sw with { Value = value, Cases = cases };
        }

        private static Expr RenameExpr(Expr e, Dictionary<string, Expr> replace) => e switch
        {
            Ident i when replace.TryGetValue(i.Name, out Expr? with) => Located(with, i),
            Unary u => u with { Operand = RenameExpr(u.Operand, replace) },
            Binary b => b with { Left = RenameExpr(b.Left, replace), Right = RenameExpr(b.Right, replace) },
            Conditional c => c with { Cond = RenameExpr(c.Cond, replace), Then = RenameExpr(c.Then, replace), Else = RenameExpr(c.Else, replace) },
            Index i => i with { Target = RenameExpr(i.Target, replace), Idx = RenameExpr(i.Idx, replace) },
            Member m => m with { Target = RenameExpr(m.Target, replace) },
            Method m => m with { Target = RenameExpr(m.Target, replace), Args = [.. m.Args.Select(a => RenameExpr(a, replace))] },
            Call c => c with { Args = [.. c.Args.Select(a => RenameExpr(a, replace))] },
            _ => e,
        };

        // The rebuilt sampler reports errors at the use it replaced.
        private static Expr Located(Expr with, Expr at)
        {
            Expr copy = with switch
            {
                Call c => c with { Args = [.. c.Args.Select(a => Located(a, at))] },
                _ => with with { },
            };
            copy.Line = at.Line;
            copy.AnchorLine = at.AnchorLine;
            copy.Column = at.Column;
            copy.Length = at.Length;
            return copy;
        }
    }
}
