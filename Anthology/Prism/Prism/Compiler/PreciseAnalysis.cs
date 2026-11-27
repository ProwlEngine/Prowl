using Prowl.Prism.Glsl;
using Index = Prowl.Prism.Glsl.Index;

namespace Prowl.Prism.Compiler;

/// <summary>
/// glslang's <c>PropagateNoContraction</c> (propagateNoContraction.cpp), over Prism's AST: starting from every
/// assignment to a <c>precise</c> object, walks backwards through the assignments of every object its value
/// depends on (call arguments, constructor arguments and ternary conditions included, index expressions not),
/// and marks the arithmetic operations involved. The lowerer decorates a marked operation NoContraction.
///
/// Objects are named by access chains like glslang's: a per-declaration symbol id, then one element per struct
/// member (a field name here, an index there; either only has to be consistent). Swizzles and array or vector
/// indexing name the whole object.
/// </summary>
sealed class PreciseAnalysis
{
    const char Delim = '/';

    /// <summary>Marked operations. The value is true when the mark came from the propagator's binary visit,
    /// which glslang applies only when the result is not a 32-bit signed int.</summary>
    public readonly Dictionary<object, bool> Marked = new(ReferenceEqualityComparer.Instance);

    // A typed object reference: a type name (struct, block, or anything else) and its remaining array depth.
    readonly record struct TypeName(string Name, int ArrayDepth);

    sealed class Symbol(string id, TypeName type, bool precise)
    {
        public readonly string Id = id;
        public readonly TypeName Type = type;
        public bool Precise = precise;
    }

    // One assignment: a Binary assignment op, a ++/-- Unary, or a declarator with an initializer.
    sealed class Definition
    {
        public required object Node;
        public Expr? Left;            // null for a declarator (the declared symbol itself is the assignee)
        public Symbol? DeclSymbol;    // the declared symbol, for a declarator
        public Expr? Right;           // the value expression (the operand for ++/--)
        public bool ArithAssign;      // compound arithmetic assignment or ++/--
    }

    readonly Dictionary<string, (string Field, TypeName Type)[]> _structs = new(StringComparer.Ordinal);
    readonly Dictionary<string, TypeName> _functionReturns = new(StringComparer.Ordinal);
    readonly HashSet<string> _preciseReturns = new(StringComparer.Ordinal);
    readonly Dictionary<string, Symbol> _globals = new(StringComparer.Ordinal);
    readonly Dictionary<object, string> _chain = new(ReferenceEqualityComparer.Instance);       // object node -> access chain
    readonly Dictionary<object, Symbol> _root = new(ReferenceEqualityComparer.Instance);        // object node -> root symbol
    readonly Dictionary<object, TypeName> _types = new(ReferenceEqualityComparer.Instance);     // object node -> type
    readonly Dictionary<string, List<Definition>> _definitions = new(StringComparer.Ordinal);   // symbol id -> its assignments
    readonly List<string> _work = [];
    readonly HashSet<string> _inWork = new(StringComparer.Ordinal);
    readonly HashSet<string> _added = new(StringComparer.Ordinal);                             // propagator's added set
    readonly HashSet<object> _preciseNodes = new(ReferenceEqualityComparer.Instance);           // checker/propagator marks
    readonly List<Dictionary<string, Symbol>> _scopes = [];
    int _nextId = 1;

    public static PreciseAnalysis Run(TranslationUnit unit)
    {
        var a = new PreciseAnalysis();
        a.Analyze(unit);
        return a;
    }

    static bool IsPreciseWord(Qualifiers q) => q.Has("precise");
    static int Depth(IReadOnlyList<Expr?>? dims) => dims?.Count ?? 0;

    void Analyze(TranslationUnit unit)
    {
        if (!Mentions(unit)) return;

        // Types first: structs, blocks and function return types, so member chains resolve anywhere.
        foreach (var d in unit.Declarations)
        {
            switch (d)
            {
                case StructDef s:
                    _structs[s.Name] = Fields(s.Members);
                    break;
                case InterfaceBlock b:
                    _structs["block:" + b.BlockName] = Fields(b.Members);
                    break;
                case FunctionDef f:
                    _functionReturns[f.Name] = new TypeName(f.ReturnType.Name, Depth(f.ReturnType.Array));
                    if (IsPreciseWord(f.Qualifiers)) _preciseReturns.Add(f.Name);
                    break;
                case FunctionProto p:
                    _functionReturns.TryAdd(p.Name, new TypeName(p.ReturnType.Name, Depth(p.ReturnType.Array)));
                    if (IsPreciseWord(p.Qualifiers)) _preciseReturns.Add(p.Name);
                    break;
            }
        }

        // Collect: globals, their initializers, every function body (glslang's TSymbolDefinitionCollectingTraverser).
        foreach (var d in unit.Declarations)
        {
            switch (d)
            {
                case GlobalVar g:
                    foreach (var dc in g.Declarators)
                    {
                        if (dc.Init is not null) CollectExpr(dc.Init);
                        var sym = new Symbol(NewId(dc.Name), new TypeName(g.Type.Name, Depth(g.Type.Array) + dc.Array.Count), IsPreciseWord(g.Qualifiers));
                        _globals[dc.Name] = sym;
                        if (dc.Init is not null) AddDeclDefinition(dc, sym);
                    }
                    break;
                case InterfaceBlock b:
                    if (b.InstanceName is { } inst)
                        _globals[inst] = new Symbol(NewId(inst), new TypeName("block:" + b.BlockName, b.InstanceArray.Count), IsPreciseWord(b.Qualifiers));
                    else
                    {
                        // Anonymous block members read as bare names but are members of the block object.
                        var block = new Symbol(NewId(b.BlockName), new TypeName("block:" + b.BlockName, 0), false);
                        foreach (var f in b.Members)
                            foreach (var dc in f.Declarators)
                                _globals[dc.Name] = new Symbol(block.Id + Delim + dc.Name, new TypeName(f.Type.Name, Depth(f.Type.Array) + dc.Array.Count),
                                                               IsPreciseWord(f.Qualifiers) || IsPreciseWord(b.Qualifiers));
                    }
                    break;
                case QualifierRedecl r when IsPreciseWord(r.Qualifiers):
                    Global(r.Name).Precise = true;
                    break;
            }
        }
        foreach (var f in unit.Declarations.OfType<FunctionDef>())
        {
            _scopes.Clear();
            var scope = new Dictionary<string, Symbol>(StringComparer.Ordinal);
            foreach (var p in f.Parameters)
                if (p.Name is { } pn)
                    // `precise` on an out parameter makes it a precise object (ParseHelper paramCheckFixStorage).
                    scope[pn] = new Symbol(NewId(pn), new TypeName(p.Type.Name, Depth(p.Type.Array) + p.Array.Count),
                                           IsPreciseWord(p.Qualifiers) && (p.Qualifiers.Has("out") || p.Qualifiers.Has("inout")));
            _scopes.Add(scope);
            _currentPreciseReturn = _preciseReturns.Contains(f.Name);
            CollectBlockBody(f.Body);
            _currentPreciseReturn = false;
            _scopes.Clear();
        }

        // Propagate (glslang's work-list loop). The work list is processed in insertion order.
        for (int i = 0; i < _work.Count; i++)
        {
            string precise = _work[i];
            if (!_definitions.TryGetValue(Front(precise), out var defs)) continue;
            foreach (var def in defs)
            {
                var (contains, remained) = CheckAssignee(def, precise);
                if (contains) PropagateInDefinition(def, remained);
            }
        }
    }

    // Cheap gate: nothing to do unless something is declared precise.
    static bool Mentions(TranslationUnit unit)
    {
        foreach (var d in unit.Declarations)
        {
            switch (d)
            {
                case GlobalVar g when IsPreciseWord(g.Qualifiers): return true;
                case InterfaceBlock b when IsPreciseWord(b.Qualifiers) || b.Members.Any(m => IsPreciseWord(m.Qualifiers)): return true;
                case QualifierRedecl r when IsPreciseWord(r.Qualifiers): return true;
                case FunctionDef f when IsPreciseWord(f.Qualifiers) || f.Parameters.Any(p => IsPreciseWord(p.Qualifiers)) || StmtMentions(f.Body): return true;
            }
        }
        return false;
    }
    static bool StmtMentions(Stmt? s) => s switch
    {
        VarDeclStmt v => IsPreciseWord(v.Qualifiers),
        Block b => b.Statements.Any(StmtMentions),
        IfStmt i => StmtMentions(i.Then) || StmtMentions(i.Else),
        ForStmt f => StmtMentions(f.Init) || StmtMentions(f.Body),
        WhileStmt w => StmtMentions(w.Body),
        DoWhileStmt d => StmtMentions(d.Body),
        SwitchStmt sw => sw.Cases.Any(c => c.Body.Any(StmtMentions)),
        _ => false,
    };

    (string Field, TypeName Type)[] Fields(IReadOnlyList<Field> members) =>
        [.. members.SelectMany(m => m.Declarators.Select(dc => (dc.Name, new TypeName(m.Type.Name, Depth(m.Type.Array) + dc.Array.Count))))];

    string NewId(string name) => $"{_nextId++}({name})";

    Symbol Global(string name)
    {
        if (!_globals.TryGetValue(name, out var s)) _globals[name] = s = new Symbol(name + "(builtin)", new TypeName("", 0), false);
        return s;
    }

    Symbol Resolve(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out var s)) return s;
        return Global(name);
    }

    int _inIndex;
    bool _currentPreciseReturn;

    void AddDefinition(string symbolId, Definition def)
    {
        if (_inIndex > 0) return;
        if (!_definitions.TryGetValue(symbolId, out var list)) _definitions[symbolId] = list = [];
        list.Add(def);
    }

    void AddDeclDefinition(Declarator dc, Symbol sym)
    {
        AddDefinition(sym.Id, new Definition { Node = dc, DeclSymbol = sym, Right = dc.Init });
        // An initialized precise declaration is an initial precise object.
        if (sym.Precise) Enqueue(sym.Id);
    }

    void Enqueue(string chain)
    {
        if (_inWork.Add(chain)) _work.Add(chain);
    }

    // ---- collection: scopes, chains, definitions ----

    void CollectBlockBody(Block b) { foreach (var s in b.Statements) CollectStmt(s); }

    void Push() => _scopes.Add(new Dictionary<string, Symbol>(StringComparer.Ordinal));
    void Pop() => _scopes.RemoveAt(_scopes.Count - 1);

    void CollectStmt(Stmt? s)
    {
        switch (s)
        {
            case null: break;
            case Block b: Push(); CollectBlockBody(b); Pop(); break;
            case VarDeclStmt v:
                foreach (var dc in v.Declarators)
                {
                    // The initializer sees the outer meaning of the name; the declaration takes effect after it.
                    if (dc.Init is not null) CollectExpr(dc.Init);
                    var sym = new Symbol(NewId(dc.Name), new TypeName(v.Type.Name, Depth(v.Type.Array) + dc.Array.Count), IsPreciseWord(v.Qualifiers));
                    _scopes[^1][dc.Name] = sym;
                    if (dc.Init is not null) AddDeclDefinition(dc, sym);
                }
                break;
            case ExprStmt e: CollectExpr(e.Expression); break;
            case IfStmt i: CollectExpr(i.Cond); CollectScoped(i.Then); CollectScoped(i.Else); break;
            case ForStmt f:
                Push();
                CollectStmt(f.Init);
                if (f.Cond is not null) CollectExpr(f.Cond);
                if (f.Update is not null) CollectExpr(f.Update);
                CollectScoped(f.Body);
                Pop();
                break;
            case WhileStmt w: CollectExpr(w.Cond); CollectScoped(w.Body); break;
            case DoWhileStmt d: CollectScoped(d.Body); CollectExpr(d.Cond); break;
            case SwitchStmt sw:
                CollectExpr(sw.Value);
                Push();
                foreach (var c in sw.Cases) foreach (var st in c.Body) CollectStmt(st);
                Pop();
                break;
            case ReturnStmt r:
                // A precise-returning function's return value is an initial precise object: seed the
                // work list with a synthetic definition so the returned expression's operations get marked.
                if (r.Value is not null) CollectExpr(r.Value);
                if (_currentPreciseReturn && r.Value is not null)
                {
                    var sym = new Symbol(NewId("$ret"), new TypeName("", 0), true);
                    AddDefinition(sym.Id, new Definition { Node = r, DeclSymbol = sym, Right = r.Value });
                    Enqueue(sym.Id);
                }
                break;
        }
    }

    void CollectScoped(Stmt? s)
    {
        if (s is null) return;
        Push(); CollectStmt(s); Pop();
    }

    void CollectExpr(Expr e)
    {
        switch (e)
        {
            case Ident id:
            {
                var sym = Resolve(id.Name);
                _chain[id] = sym.Id; _root[id] = sym; _types[id] = sym.Type;
                break;
            }
            case Member m:
            {
                CollectExpr(m.Target);
                string baseChain = _chain.GetValueOrDefault(m.Target, "");
                var t = _types.GetValueOrDefault(m.Target);
                if (t.ArrayDepth == 0 && t.Name is not null && _structs.TryGetValue(t.Name, out var fields) && Array.FindIndex(fields, f => f.Field == m.Name) is >= 0 and var fi)
                {
                    _chain[m] = baseChain + Delim + m.Name;
                    _types[m] = fields[fi].Type;
                }
                else
                {
                    _chain[m] = baseChain;   // a swizzle names the whole vector
                    _types[m] = new TypeName("", 0);
                }
                if (_root.TryGetValue(m.Target, out var r)) _root[m] = r;
                break;
            }
            case Index ix:
            {
                CollectExpr(ix.Target);
                // glslang never visits an index expression here, so assignments inside one define nothing.
                _inIndex++; CollectExpr(ix.Idx); _inIndex--;
                _chain[ix] = _chain.GetValueOrDefault(ix.Target, "");
                var t = _types.GetValueOrDefault(ix.Target);
                _types[ix] = t.ArrayDepth > 0 ? t with { ArrayDepth = t.ArrayDepth - 1 } : new TypeName("", 0);
                if (_root.TryGetValue(ix.Target, out var r)) _root[ix] = r;
                break;
            }
            case Binary b when b.Op == ",":
                CollectExpr(b.Left); CollectExpr(b.Right);
                break;
            case Binary b when IsAssign(b.Op):
            {
                CollectExpr(b.Left);
                CollectExpr(b.Right);
                if (_chain.TryGetValue(b.Left, out var lc))
                {
                    AddDefinition(Front(lc), new Definition { Node = b, Left = b.Left, Right = b.Right, ArithAssign = IsArithAssign(b.Op) });
                    if (IsPreciseNode(b.Left)) Enqueue(lc);
                }
                break;
            }
            case Binary b: CollectExpr(b.Left); CollectExpr(b.Right); break;
            case Unary u:
            {
                CollectExpr(u.Operand);
                if (u.Op is "++" or "--" && _chain.TryGetValue(u.Operand, out var oc))
                {
                    AddDefinition(Front(oc), new Definition { Node = u, Left = u.Operand, Right = u.Operand, ArithAssign = true });
                    if (IsPreciseNode(u.Operand)) Enqueue(oc);
                }
                break;
            }
            case Conditional c: CollectExpr(c.Cond); CollectExpr(c.Then); CollectExpr(c.Else); break;
            case Call call:
                foreach (var a in call.Args) CollectExpr(a);
                if (_structs.ContainsKey(call.Name) && !call.IsArray) _types[call] = new TypeName(call.Name, 0);
                else if (_functionReturns.TryGetValue(call.Name, out var rt)) _types[call] = rt;
                break;
            case Method meth:
                CollectExpr(meth.Target);
                foreach (var a in meth.Args) CollectExpr(a);
                break;
        }
    }

    static bool IsAssign(string op) => op is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or "<<=" or ">>=";
    static bool IsArithAssign(string op) => op is "+=" or "-=" or "*=" or "/=" or "%=";
    static bool IsArith(string op) => op is "+" or "-" or "*" or "/" or "%";

    static string Front(string chain) { int p = chain.IndexOf(Delim); return p < 0 ? chain : chain[..p]; }
    static string AfterFront(string chain) { int p = chain.IndexOf(Delim); return p < 0 ? "" : chain[(p + 1)..]; }

    // A node's type carries `precise` when its root symbol is precise (it propagates up member and index
    // dereferences), or when the checker or propagator marked it.
    bool IsPreciseNode(Expr e) =>
        _preciseNodes.Contains(e) || (e is Ident or Member or Index && _root.TryGetValue(e, out var r) && r.Precise);

    // ---- TNoContractionAssigneeCheckingTraverser ----

    (bool Contains, string Remained) CheckAssignee(Definition def, string precise)
    {
        string assignee;
        if (def.Left is null)
        {
            if (def.DeclSymbol!.Precise) return (true, "");
            assignee = def.DeclSymbol.Id;
        }
        else
        {
            MarkLeft(def.Left, precise);
            if (IsPreciseNode(def.Left)) return (true, "");
            assignee = _chain.GetValueOrDefault(def.Left, "");
        }
        if (assignee == precise || assignee.StartsWith(precise + Delim, StringComparison.Ordinal)) return (true, "");
        if (precise.StartsWith(assignee + Delim, StringComparison.Ordinal))
            return (true, precise.Length > assignee.Length ? precise[(assignee.Length + 1)..] : "");
        return (false, "");
    }

    // Post-order over the assignee's object nodes: a dereference of a precise object is precise, and a node
    // whose chain is the precise object itself becomes precise.
    void MarkLeft(Expr e, string precise)
    {
        switch (e)
        {
            case Member m:
                MarkLeft(m.Target, precise);
                if (IsPreciseNode(m.Target) || _chain.GetValueOrDefault(m) == precise) _preciseNodes.Add(m);
                break;
            case Index ix:
                MarkLeft(ix.Target, precise);
                if (IsPreciseNode(ix.Target) || _chain.GetValueOrDefault(ix) == precise) _preciseNodes.Add(ix);
                break;
            case Ident id:
                if (_chain.GetValueOrDefault(id) == precise) _preciseNodes.Add(id);
                break;
        }
    }

    // ---- TNoContractionPropagator ----

    string _remained = "";

    void PropagateInDefinition(Definition def, string remained)
    {
        _remained = remained;
        if (def.Node is Unary u)
        {
            Propagate(u.Operand);
            Marked[u] = false;
            return;
        }
        if (def.Right is not null) Propagate(def.Right);
        if (def.ArithAssign) Marked[def.Node] = false;
    }

    void AddPrecise(object node)
    {
        string chain = _chain.GetValueOrDefault(node, "");
        if (_remained.Length == 0) _preciseNodes.Add(node);
        else chain += Delim + _remained;
        if (_added.Add(chain)) Enqueue(chain);
    }

    void Propagate(Expr e)
    {
        switch (e)
        {
            case Ident or Member or Index:
                // An object node: it (or the nested object the remaining chain leads to) is precise. Only the
                // uppermost object is visited, so an index expression is not.
                AddPrecise(e);
                break;
            case Binary b:
                if (IsArith(b.Op)) Marked[b] = true;
                else if (IsArithAssign(b.Op)) Marked[b] = true;
                Propagate(b.Left); Propagate(b.Right);
                break;
            case Unary u:
                if (u.Op is "-" or "++" or "--") Marked[u] = false;
                Propagate(u.Operand);
                break;
            case Conditional c: Propagate(c.Cond); Propagate(c.Then); Propagate(c.Else); break;
            case Call call:
                if (_remained.Length > 0 && !call.IsArray && _structs.TryGetValue(call.Name, out var fields))
                {
                    // A struct constructor: follow the remaining chain into the one argument it names.
                    string front = Front(_remained);
                    int fi = Array.FindIndex(fields, f => f.Field == front);
                    if (fi >= 0 && fi < call.Args.Count)
                    {
                        string saved = _remained;
                        _remained = AfterFront(saved);
                        Propagate(call.Args[fi]);
                        _remained = saved;
                    }
                    break;
                }
                foreach (var a in call.Args) Propagate(a);
                break;
            case Method meth:
                Propagate(meth.Target);
                foreach (var a in meth.Args) Propagate(a);
                break;
        }
    }
}
