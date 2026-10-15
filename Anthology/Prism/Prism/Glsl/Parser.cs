namespace Prowl.Prism.Glsl;

/// <summary>
/// A recursive-descent parser for GLSL (Vulkan profile). Declarations and statements are
/// parsed top-down; expressions use precedence climbing (one method for the whole binary
/// operator ladder). GLSL is a frozen spec, so this targets exactly it, no more.
/// </summary>
public sealed class Parser
{
    static readonly HashSet<string> QualifierWords =
    [
        "const", "in", "out", "inout", "uniform", "buffer", "shared", "flat",
        "noperspective", "smooth", "centroid", "patch", "sample", "precise", "invariant",
        "coherent", "volatile", "restrict", "readonly", "writeonly", "highp", "mediump", "lowp",
        "perprimitiveEXT", "taskPayloadSharedEXT", "pervertexEXT",
        "nonprivate", "devicecoherent", "queuefamilycoherent", "workgroupcoherent", "subgroupcoherent", "shadercallcoherent",
        "rayPayloadEXT", "rayPayloadInEXT", "hitAttributeEXT", "callableDataEXT", "callableDataInEXT", "shaderRecordEXT",
        "rayPayloadNV", "rayPayloadInNV", "hitAttributeNV", "callableDataNV", "callableDataInNV",
    ];

    readonly List<Token> _t;
    readonly int _version;
    readonly bool _isEs;
    readonly IReadOnlyList<string> _extensions;
    readonly IReadOnlyList<string> _pragmas;
    // Struct definitions produced by an inline `struct S { .. } v;`, emitted ahead of the declaration
    // that carries them (a local one is hoisted to module scope, where SPIR-V types live anyway).
    readonly List<Decl> _pendingDecls = [];
    int _i;
    int _invariantAllFrom = -1;   // token index of `#pragma STDGL invariant(all)`: later outputs are invariant
    // Recursion depth guard. Expression/statement handling is recursive descent in the parser and recursive
    // again over the AST in the lowerer, so unbounded nesting would overflow the stack -- which in .NET is an
    // uncatchable process kill, not an exception. The limit is far above any real shader and is chosen with
    // margin below the shallowest measured overflow (parenthesis nesting on a 1MB stack).
    // ONE budget shared by statements, expressions and initializers: each nested construct consumes
    // depth from the same counter, and the per-site messages label which construct was being parsed
    // when the budget ran out ("statement nesting..." for blocks, "expression nesting..." for operands -
    // a chain of 500 `if`s runs out while parsing one of the nested conditions, so it reports the
    // expression site). The limit is stack-safety: nested lowering re-walks the AST in the lowerer,
    // so unbounded nesting would overflow the stack -- which in .NET is an
    // uncatchable process kill, not an exception. The limit is far above any real shader and is chosen with
    // margin below the shallowest measured overflow (parenthesis nesting on a 1MB stack).
    internal const int MaxNestingDepth = 8192;   // glslang's acceptance zone: ~5,000-deep paren nesting
                                             // compiles and ~10,000 dies; 256-deep ifs compile and
                                             // ~2,000 die. 8192 covers the corpus and the shared zone
                                             // on the compile lane's 64 MB stack (see GlslCompiler).
    int _depth;
    int _controlDepth;   // nested if/for/while/do/switch statements being parsed
    const int MaxControlNesting = 1023;

    Parser(List<Token> tokens, int version, bool isEs = false, IReadOnlyList<string>? extensions = null, IReadOnlyList<string>? pragmas = null)
    {
        _t = tokens;
        _version = version;
        _isEs = isEs;
        _extensions = extensions ?? [];
        _pragmas = pragmas ?? [];
    }

    public static TranslationUnit Parse(string source, IReadOnlyDictionary<string, string>? defines = null, IncludeCallback? includes = null) =>
        Parse(source, defines, includes, null);

    /// <summary>Parses for a known stage, which selects the stage macro glslang predefines (GL_FRAGMENT_SHADER, ...).</summary>
    public static TranslationUnit Parse(string source, IReadOnlyDictionary<string, string>? defines, IncludeCallback? includes, Prowl.Prism.Compiler.ShaderStage? stage)
    {
        var (tokens, version, isEs, extensions, pragmas, names, invariantAllFrom) = Preprocessor.ProcessPooled(source, defines, includes, stage);
        try
        {
            return new Parser(tokens, version, isEs, extensions, pragmas) { _invariantAllFrom = invariantAllFrom }.ParseTranslationUnit() with { SourceNames = names };
        }
        catch (GlslParseException e) when (!e.Located)
        {
            throw e.Resolve(names);
        }
    }

    // ---- top level ----

    TranslationUnit ParseTranslationUnit()
    {
        var decls = new List<Decl>();
        bool defaultFloatRelaxed = false;
        bool? intRelaxed = null;
        while (!IsKind(TokenKind.End))
        {
            if (TryPrecisionStatement(ref defaultFloatRelaxed, ref intRelaxed)) continue;
            int ln = Cur.Line;
            var d = ParseExternalDeclaration();
            if (d.Line == 0) d.Line = ln;
            // A struct defined inline with declarators yields its StructDef ahead of the variable.
            foreach (var p in _pendingDecls) { if (p.Line == 0) p.Line = ln; decls.Add(p); }
            _pendingDecls.Clear();
            decls.Add(d);
        }
        return new TranslationUnit(_version, decls, _extensions, _pragmas, defaultFloatRelaxed, _isEs, intRelaxed);
    }

    // `precision <qualifier> <type>;` sets a type's default precision. It has no required semantics on a
    // desktop Vulkan target, but glslang still emits RelaxedPrecision for a mediump/lowp default. The int
    // default is a nullable flag: unset until a statement (or, in the Lowerer, the ES-fragment default) applies.
    bool TryPrecisionStatement(ref bool defaultFloatRelaxed, ref bool? intRelaxed)
    {
        if (!IsText("precision")) return false;
        Advance();
        string qualifier = Expect(TokenKind.Identifier, "precision qualifier").Text;
        string type = Expect(TokenKind.Identifier, "type").Text;
        Expect(TokenKind.Semicolon);
        if (type == "float") defaultFloatRelaxed = qualifier is "mediump" or "lowp";
        if (type is "int" or "uint") intRelaxed = qualifier is "mediump" or "lowp";
        return true;
    }

    Decl ParseExternalDeclaration()
    {
        if (IsText("struct")) return ParseStructDef();

        int declStart = _i;
        var quals = ParseQualifiers();
        // After `#pragma STDGL invariant(all)`, glslang makes every user output declared from then on invariant.
        if (_invariantAllFrom >= 0 && declStart >= _invariantAllFrom && quals.Has("out") && !quals.Has("invariant"))
            quals = quals with { Words = [.. quals.Words, "invariant"] };
        if (AcceptKind(TokenKind.Semicolon)) return new LayoutDecl(quals);   // e.g. layout(...) in;
        // A qualified inline struct definition, e.g. `const struct S { .. } v = S(..);`.
        if (IsText("struct"))
        {
            var sd = ParseInlineStruct();
            _pendingDecls.Add(sd);
            var qt = new TypeRef(sd.Name);
            return new GlobalVar(quals, qt, ParseDeclaratorList(qt));
        }

        string name = Expect(TokenKind.Identifier, "declaration").Text;      // a type name or a block name
        if (IsKind(TokenKind.LBrace)) return ParseInterfaceBlock(quals, name);
        // A qualifier applied to an already-declared variable, e.g. `invariant gl_Position;`.
        if (AcceptKind(TokenKind.Semicolon)) return new QualifierRedecl(quals, name);

        var type = new TypeRef(name, ParseArrayDims());   // `float[4] f(...)` / `float[4] v;`
        var name2Tok = ExpectName();
        string name2 = name2Tok.Text;
        if (IsKind(TokenKind.LParen))
        {
            var parameters = ParseParams();
            if (AcceptKind(TokenKind.Semicolon)) return new FunctionProto(type, name2, parameters);   // prototype
            return new FunctionDef(type, name2, parameters, ParseBlock());
        }

        var declarators = new List<Declarator> { ParseDeclaratorTail(name2, type, name2Tok.Line) };
        while (AcceptKind(TokenKind.Comma))
        {
            var nd = ExpectName();
            declarators.Add(ParseDeclaratorTail(nd.Text, type, nd.Line));
        }
        Expect(TokenKind.Semicolon);
        return new GlobalVar(quals, type, declarators);
    }

    Decl ParseStructDef()
    {
        var def = ParseInlineStruct();
        // `struct S { .. } a, b;` declares variables of the new type as well as defining it.
        if (!AcceptKind(TokenKind.Semicolon))
        {
            _pendingDecls.Add(def);
            return new GlobalVar(Qualifiers.None, new TypeRef(def.Name), ParseDeclaratorList(new TypeRef(def.Name)));
        }
        return def;
    }

    // The `struct Name { fields }` part alone, leaving any declarators to the caller.
    StructDef ParseInlineStruct()
    {
        ExpectText("struct");
        string name = ExpectName("struct name").Text;
        return new StructDef(name, ParseFieldBlock());
    }

    List<Declarator> ParseDeclaratorList(TypeRef type)
    {
        var first = ExpectName();
        var declarators = new List<Declarator> { ParseDeclaratorTail(first.Text, type, first.Line) };
        while (AcceptKind(TokenKind.Comma))
        {
            var nd = ExpectName();
            declarators.Add(ParseDeclaratorTail(nd.Text, type, nd.Line));
        }
        Expect(TokenKind.Semicolon);
        return declarators;
    }

    InterfaceBlock ParseInterfaceBlock(Qualifiers quals, string blockName)
    {
        var fields = ParseFieldBlock();
        // An instance name must be a legal user identifier; a keyword here is simply not a name, so the
        // following ';' expectation produces the standard syntax error (glslang: "unexpected FLOAT").
        string? instance = IsKind(TokenKind.Identifier) && !ReservedWords.Contains(Cur.Text) ? Advance().Text : null;
        var instanceArray = ParseArrayDims();
        Expect(TokenKind.Semicolon);
        return new InterfaceBlock(quals, blockName, fields, instance, instanceArray);
    }

    List<Field> ParseFieldBlock()
    {
        Expect(TokenKind.LBrace);
        var fields = new List<Field>();
        while (!IsKind(TokenKind.RBrace))
        {
            if (IsKind(TokenKind.End)) throw new GlslParseException("unexpected end of input in field list", Cur.Line);
            var quals = ParseQualifiers();
            var type = ParseType();
            var nameTok = ExpectName("member name");
            var declarators = new List<Declarator> { ParseDeclaratorTail(nameTok.Text, null, nameTok.Line) };
            while (AcceptKind(TokenKind.Comma))
            {
                var nd = ExpectName("member name");
                declarators.Add(ParseDeclaratorTail(nd.Text, null, nd.Line));
            }
            Expect(TokenKind.Semicolon);
            fields.Add(new Field(quals, type, declarators));
        }
        Expect(TokenKind.RBrace);
        return fields;
    }

    List<Param> ParseParams()
    {
        Expect(TokenKind.LParen);
        var ps = new List<Param>();
        if (!IsKind(TokenKind.RParen))
        {
            if (IsText("void") && Peek().Kind == TokenKind.RParen) Advance();
            else do { ps.Add(ParseParam()); } while (AcceptKind(TokenKind.Comma));
        }
        Expect(TokenKind.RParen);
        return ps;
    }

    Param ParseParam()
    {
        var quals = ParseQualifiers();
        var type = ParseType();
        // Same rule as block instance names: a keyword cannot be a parameter name.
        string? name = IsKind(TokenKind.Identifier) && !ReservedWords.Contains(Cur.Text) ? Advance().Text : null;
        return new Param(quals, type, name, ParseArrayDims());
    }

    // ---- qualifiers & types ----

    Qualifiers ParseQualifiers()
    {
        var words = new List<string>();
        var layout = new List<LayoutItem>();
        while (true)
        {
            if (IsText("layout")) ParseLayout(layout);
            else if (IsKind(TokenKind.Identifier) && QualifierWords.Contains(Cur.Text)) words.Add(Advance().Text);
            else break;
        }
        return words.Count == 0 && layout.Count == 0 ? Qualifiers.None : new Qualifiers(words, layout);
    }

    void ParseLayout(List<LayoutItem> layout)
    {
        ExpectText("layout");
        Expect(TokenKind.LParen);
        do
        {
            string id = Expect(TokenKind.Identifier, "layout id").Text;
            Expr? value = AcceptOp("=") ? ParseAssignment() : null;
            if (!KnownLayoutQualifiers.Contains(id))
                // glslang: "'q77' : unrecognized layout identifier, or qualifier requires assignment
                // (e.g., binding = 4)" for bare ids, "'q77' : there is no such layout identifier for
                // this stage taking an assigned value" for assigned ones. The parser is context-free
                // (no stage knowledge), so the assigned variant drops glslang's "for this stage".
                throw Error(value is not null
                    ? $"'{id}' : unrecognized layout identifier taking an assigned value"
                    : $"'{id}' : unrecognized layout identifier, or qualifier requires assignment (e.g., binding = 4)");
            layout.Add(new LayoutItem(id, value));
        } while (AcceptKind(TokenKind.Comma));
        Expect(TokenKind.RParen);
    }

    // A type may carry prefix array dimensions: `float[4] x` means the same as `float x[4]`.
    // A cooperative-matrix type carries angle-bracketed template arguments: coopmat<T, scope, rows, cols, use>.
    TypeRef ParseType()
    {
        string name = Expect(TokenKind.Identifier, "type").Text;
        IReadOnlyList<Expr>? templateArgs = null;
        if (IsOp("<"))
        {
            AcceptOp("<");
            // Parse each argument above relational precedence so the closing '>' is not read as a compare.
            var args = new List<Expr> { ParseBinary(9) };
            while (AcceptKind(TokenKind.Comma)) args.Add(ParseBinary(9));
            if (!AcceptOp(">")) throw Error($"expected '>' to close template arguments but found '{Cur.Text}'");
            templateArgs = args;
        }
        return new TypeRef(name, ParseArrayDims(), templateArgs);
    }

    Declarator ParseDeclaratorTail(string name) => ParseDeclaratorTail(name, null);

    Declarator ParseDeclaratorTail(string name, TypeRef? type, int line = 0)
    {
        var dims = ParseArrayDims();
        Expr? init = null;
        if (AcceptOp("="))
        {
            if (IsKind(TokenKind.LBrace) && type is not null)
                init = ParseBraceInit(type, dims.Count + (type.Array?.Count ?? 0));
            else
                init = ParseAssignment();
        }
        return new Declarator(name, dims, init) { Line = line };
    }

    // A GLSL 4.20 brace initializer means the same as the type's constructor, so it parses straight into
    // one: `float a[] = {1, 2}` is `float[](1, 2)`, and a nested brace is the element's constructor.
    Expr ParseBraceInit(TypeRef type, int arrayDepth)
    {
        if (++_depth > MaxNestingDepth) { _depth--; throw new GlslParseException($"initializer nesting is too deep (limit {MaxNestingDepth})", Cur.Line); }
        try { return ParseBraceInitCore(type, arrayDepth); }
        finally { _depth--; }
    }

    // A nested brace initializes a member or element whose type only the semantic model knows, so it
    // parses into a nameless constructor that the lowerer resolves against the declared type. An array
    // element keeps the outer type, which is already correct for it.
    Expr ParseNestedBraceInit(TypeRef type, int arrayDepth)
    {
        var inner = ParseBraceInit(type, arrayDepth);
        return arrayDepth >= 0 ? inner : inner is Call c ? c with { Name = "" } : inner;
    }

    Expr ParseBraceInitCore(TypeRef type, int arrayDepth)
    {
        Expect(TokenKind.LBrace);
        var items = new List<Expr>();
        while (!IsKind(TokenKind.RBrace))
        {
            if (IsKind(TokenKind.End)) throw new GlslParseException("unexpected end of input in initializer", Cur.Line);
            items.Add(IsKind(TokenKind.LBrace) ? ParseNestedBraceInit(type, arrayDepth - 1) : ParseAssignment());
            if (!AcceptKind(TokenKind.Comma)) break;
        }
        Expect(TokenKind.RBrace);
        return new Call(type.Name, items, IsArray: arrayDepth > 0);
    }

    List<Expr?> ParseArrayDims()
    {
        var dims = new List<Expr?>();
        while (AcceptKind(TokenKind.LBracket))
        {
            dims.Add(IsKind(TokenKind.RBracket) ? null : ParseAssignment());
            Expect(TokenKind.RBracket);
        }
        return dims;
    }

    // ---- statements ----

    Block ParseBlock()
    {
        Expect(TokenKind.LBrace);
        var stmts = new List<Stmt>();
        while (!IsKind(TokenKind.RBrace))
        {
            if (IsKind(TokenKind.End)) throw new GlslParseException("unexpected end of input in block", Cur.Line);
            stmts.Add(ParseStatement());
        }
        Expect(TokenKind.RBrace);
        return new Block(stmts);
    }

    // GL_EXT_control_flow_attributes: `[[a, b(N), ...]]` before an if/switch/loop, applied the way glslang's
    // handleSelectionAttributes / handleSwitchAttributes / handleLoopAttributes do. An unknown attribute, or one with
    // the wrong kind of argument, is only a warning there and is ignored here.
    Stmt ParseAttributedStatement()
    {
        Advance(); Advance();   // [[
        var attrs = new List<(string Name, bool HasArg, int? Arg, int Line)>();
        do
        {
            var nameTok = Expect(TokenKind.Identifier, "attribute name");
            bool hasArg = false;
            int? arg = null;
            if (AcceptKind(TokenKind.LParen))
            {
                hasArg = true;
                arg = ParseAttributeArg(nameTok.Text);
                Expect(TokenKind.RParen);
            }
            attrs.Add((nameTok.Text, hasArg, arg, nameTok.Line));
        } while (AcceptKind(TokenKind.Comma));
        Expect(TokenKind.RBracket);
        Expect(TokenKind.RBracket);   // ]]
        var inner = ParseStatement();

        if (inner is IfStmt or SwitchStmt)
        {
            bool flatten = false, dontFlatten = false;
            foreach (var a in attrs)
            {
                if (a.HasArg) continue;
                if (a.Name == "flatten") flatten = true;
                else if (a.Name is "branch" or "dont_flatten") dontFlatten = true;
            }
            uint sel = flatten ? 1u : dontFlatten ? 2u : 0u;
            return inner switch
            {
                IfStmt i => i with { SelControl = sel },
                SwitchStmt sw => sw with { SelControl = sel },
                _ => inner,
            };
        }
        if (inner is not (ForStmt or WhileStmt or DoWhileStmt)) return inner;

        bool unroll = false, dontUnroll = false;
        int dependency = 0;
        uint minIter = 0, maxIter = uint.MaxValue, multiple = 1, peel = 0, partial = 0;
        foreach (var a in attrs)
        {
            bool noArg = !a.HasArg;
            bool intArg = a.Arg is not null;
            uint u = unchecked((uint)(a.Arg ?? 0));
            switch (a.Name)
            {
                case "unroll": if (noArg) unroll = true; break;
                case "loop" or "dont_unroll": if (noArg) { dontUnroll = true; peel = partial = 0; } break;
                case "dependency_infinite": if (noArg) dependency = -1; break;
                case "dependency_length":
                    if (!intArg) break;
                    if (a.Arg <= 0) throw new GlslParseException("'dependency_length' : must be positive", a.Line);
                    dependency = a.Arg!.Value;
                    break;
                case "min_iterations": if (intArg) minIter = u; break;
                case "max_iterations": if (intArg) maxIter = u; break;
                case "iteration_multiple":
                    if (!intArg) break;
                    if (u == 0) throw new GlslParseException("'iteration_multiple' : must be greater than or equal to 1", a.Line);
                    multiple = u;
                    break;
                case "peel_count": if (intArg) { peel = u; dontUnroll = false; } break;
                case "partial_count": if (intArg) { partial = u; dontUnroll = false; } break;
            }
        }
        // glslang's TranslateLoopControl.
        uint control = 0, depLength = 0;
        var loopParams = new List<uint>();
        if (dontUnroll) control |= 2;
        if (unroll) control |= 1;
        if (dependency == -1) control |= 4;
        else if (dependency > 0) { control |= 8; depLength = (uint)dependency; }
        if (minIter > 0) { control |= 0x10; loopParams.Add(minIter); }
        if (maxIter < uint.MaxValue) { control |= 0x20; loopParams.Add(maxIter); }
        if (multiple > 1) { control |= 0x40; loopParams.Add(multiple); }
        if (peel > 0) { control |= 0x80; loopParams.Add(peel); }
        if (partial > 0) { control |= 0x100; loopParams.Add(partial); }
        IReadOnlyList<uint>? extra = loopParams.Count > 0 ? loopParams : null;
        return inner switch
        {
            ForStmt f => f with { LoopControl = control, DepLength = depLength, LoopParams = extra },
            WhileStmt w => w with { LoopControl = control, DepLength = depLength, LoopParams = extra },
            DoWhileStmt d => d with { LoopControl = control, DepLength = depLength, LoopParams = extra },
            _ => inner,
        };
    }

    // An attribute's argument. glslang takes it from a constant expression and uses it only when it is an int
    // constant; a literal of another type (2u, 1.0, true) is ignored. Other expressions are not supported.
    int? ParseAttributeArg(string attr)
    {
        bool negate = false;
        if (IsKind(TokenKind.Operator) && Cur.Text == "-") { Advance(); negate = true; }
        var argTok = Advance();
        if (argTok.Kind == TokenKind.IntConst && TryParseIntLiteral(argTok.Text, out uint value))
            return unchecked(negate ? -(int)value : (int)value);
        if (argTok.Kind is TokenKind.UintConst or TokenKind.FloatConst or TokenKind.DoubleConst or TokenKind.HalfConst
            or TokenKind.BoolConst or TokenKind.Int64Const or TokenKind.Uint64Const)
            return null;
        throw new GlslParseException($"attribute '{attr}' expects an integer argument, found '{argTok.Text}'", argTok.Line);
    }

    static bool TryParseIntLiteral(string text, out uint value)
    {
        value = 0;
        try
        {
            ulong v = text.StartsWith("0x") || text.StartsWith("0X") ? Convert.ToUInt64(text[2..], 16)
                : text.Length > 1 && text[0] == '0' ? Convert.ToUInt64(text, 8)
                : ulong.Parse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            if (v > uint.MaxValue) return false;
            value = (uint)v;
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) { return false; }
    }

    Stmt ParseStatement()
    {
        int ln = Cur.Line;
        if (++_depth > MaxNestingDepth) { _depth--; throw new GlslParseException($"statement nesting is too deep (limit {MaxNestingDepth})", ln); }
        // SPIR-V's universal limit is 1023 nested control-flow constructs (spirv-val rejects deeper modules).
        bool control = Cur.Kind == TokenKind.Identifier && Cur.Text is "if" or "for" or "while" or "do" or "switch";
        if (control && ++_controlDepth > MaxControlNesting)
            throw new GlslParseException($"control flow is nested deeper than the SPIR-V limit of {MaxControlNesting}", ln);
        Stmt s;
        try { s = ParseStatementCore(); }
        finally { _depth--; if (control) _controlDepth--; }
        if (s.Line == 0) s.Line = ln;
        return s;
    }

    Stmt ParseStatementCore()
    {
        if (IsKind(TokenKind.LBrace)) return ParseBlock();
        if (AcceptKind(TokenKind.Semicolon)) return new Block([]);   // empty statement
        if (IsKind(TokenKind.LBracket) && Peek().Kind == TokenKind.LBracket) return ParseAttributedStatement();

        if (IsKind(TokenKind.Identifier))
            switch (Cur.Text)
            {
                case "if": return ParseIf();
                case "for": return ParseFor();
                case "while": return ParseWhile();
                case "do": return ParseDoWhile();
                case "switch": return ParseSwitch();
                case "return": return ParseReturn();
                case "break" or "continue" or "discard" or "demote" or "terminateInvocation"
                    or "ignoreIntersectionEXT" or "terminateRayEXT":
                    string kw = Advance().Text;
                    Expect(TokenKind.Semicolon);
                    return new JumpStmt(kw);
            }

        if (IsDeclarationStart()) return ParseVarDecl();

        var expr = ParseExpression();
        Expect(TokenKind.Semicolon);
        return new ExprStmt(expr);
    }

    VarDeclStmt ParseVarDecl()
    {
        var quals = ParseQualifiers();
        // A struct defined inline in a local declaration: the type is hoisted to module scope (SPIR-V types
        // are module-level anyway) and the statement declares variables of it.
        if (IsText("struct"))
        {
            var sd = ParseInlineStruct();
            _pendingDecls.Add(sd);
            var st = new TypeRef(sd.Name);
            return new VarDeclStmt(quals, st, ParseDeclaratorList(st));
        }
        var type = ParseType();
        return new VarDeclStmt(quals, type, ParseDeclaratorList(type));
    }

    bool IsDeclarationStart()
    {
        if (!IsKind(TokenKind.Identifier)) return false;
        if (QualifierWords.Contains(Cur.Text)) return true;
        if (Cur.Text == "struct") return true;   // an inline struct definition declaring variables
        // A cooperative-matrix type begins with `coopmat<`.
        if (Cur.Text == "coopmat" && Peek() is { Kind: TokenKind.Operator, Text: "<" }) return true;
        if (Peek().Kind == TokenKind.Identifier) return true;   // "type name" begins a declaration
        // A prefix-array type is ambiguous with an array constructor: `float[4] v;` declares, while
        // `float[4](...)` constructs. Whatever follows the dimensions decides.
        if (Peek().Kind != TokenKind.LBracket) return false;
        int i = _i + 1;
        for (int depth = 0; i < _t.Count; i++)
        {
            if (_t[i].Kind == TokenKind.LBracket) depth++;
            else if (_t[i].Kind == TokenKind.RBracket && --depth == 0) { i++; break; }
        }
        return i < _t.Count && _t[i].Kind == TokenKind.Identifier;
    }

    Stmt ParseIf()
    {
        ExpectText("if");
        Expect(TokenKind.LParen);
        var cond = ParseExpression();
        Expect(TokenKind.RParen);
        var then = ParseStatement();
        Stmt? els = AcceptText("else") ? ParseStatement() : null;
        return new IfStmt(cond, then, els);
    }

    Stmt ParseFor()
    {
        ExpectText("for");
        Expect(TokenKind.LParen);
        Stmt? init;
        if (AcceptKind(TokenKind.Semicolon)) init = null;
        else if (IsDeclarationStart()) init = ParseVarDecl();       // consumes its ';'
        else { var e = ParseExpression(); Expect(TokenKind.Semicolon); init = new ExprStmt(e); }
        Expr? cond = IsKind(TokenKind.Semicolon) ? null : ParseExpression();
        Expect(TokenKind.Semicolon);
        Expr? update = IsKind(TokenKind.RParen) ? null : ParseExpression();
        Expect(TokenKind.RParen);
        return new ForStmt(init, cond, update, ParseStatement());
    }

    Stmt ParseWhile()
    {
        ExpectText("while");
        Expect(TokenKind.LParen);
        var cond = ParseExpression();
        Expect(TokenKind.RParen);
        return new WhileStmt(cond, ParseStatement());
    }

    Stmt ParseDoWhile()
    {
        ExpectText("do");
        var body = ParseStatement();
        ExpectText("while");
        Expect(TokenKind.LParen);
        var cond = ParseExpression();
        Expect(TokenKind.RParen);
        Expect(TokenKind.Semicolon);
        return new DoWhileStmt(body, cond);
    }

    Stmt ParseSwitch()
    {
        ExpectText("switch");
        Expect(TokenKind.LParen);
        var value = ParseExpression();
        Expect(TokenKind.RParen);
        Expect(TokenKind.LBrace);
        var cases = new List<SwitchCase>();
        while (!IsKind(TokenKind.RBrace))
        {
            if (IsKind(TokenKind.End)) throw new GlslParseException("unexpected end of input in switch body", Cur.Line);
            int caseLine = Cur.Line;   // the 'case'/'default' token's line - glslang reports case errors here
            Expr? label = AcceptText("case") ? ParseExpression() : null;
            if (label is null) ExpectText("default");
            Expect(TokenKind.Colon);
            var body = new List<Stmt>();
            while (!IsKind(TokenKind.RBrace) && !IsText("case") && !IsText("default"))
            {
                if (IsKind(TokenKind.End)) throw new GlslParseException("unexpected end of input in switch body", Cur.Line);
                body.Add(ParseStatement());
            }
            cases.Add(new SwitchCase(label, body) { Line = caseLine });
        }
        Expect(TokenKind.RBrace);
        return new SwitchStmt(value, cases);
    }

    Stmt ParseReturn()
    {
        ExpectText("return");
        Expr? value = IsKind(TokenKind.Semicolon) ? null : ParseExpression();
        Expect(TokenKind.Semicolon);
        return new ReturnStmt(value);
    }

    // ---- expressions (precedence climbing) ----

    // A full expression may be a comma-sequence (`a, b`): the lowest-precedence operator. Argument
    // lists and initializers use ParseAssignment, so their commas stay separators, not this operator.
    Expr ParseExpression()
    {
        var e = ParseAssignment();
        while (AcceptKind(TokenKind.Comma))
            e = new Binary(",", e, ParseAssignment());
        return e;
    }

    // Stamps a node with the source line it started on (used to attribute lowering errors).
    static E At<E>(E node, int line) where E : Expr { if (node.Line == 0) node.Line = line; return node; }

    Expr ParseAssignment()
    {
        if (++_depth > MaxNestingDepth) { _depth--; throw new GlslParseException($"expression nesting is too deep (limit {MaxNestingDepth})", Cur.Line); }
        try { return ParseAssignmentCore(); }
        finally { _depth--; }
    }

    Expr ParseAssignmentCore()
    {
        int ln = Cur.Line;
        var left = ParseConditional();
        if (Cur.Kind == TokenKind.Operator && IsAssignOp(Cur.Text))
        {
            string op = Advance().Text;
            return At(new Binary(op, left, ParseAssignment()), ln);   // right-associative
        }
        return left;
    }

    Expr ParseConditional()
    {
        int ln = Cur.Line;
        var cond = ParseBinary(1);
        if (!AcceptKind(TokenKind.Question)) return cond;
        var then = ParseExpression();
        Expect(TokenKind.Colon);
        return At(new Conditional(cond, then, ParseAssignment()), ln);
    }

    Expr ParseBinary(int minPrec)
    {
        int ln = Cur.Line;
        var left = ParseUnary();
        // The chain is built iteratively, so it does not grow the parser's stack -- but it does grow the AST
        // left spine, which every later recursive walk (type inference, lowering, reachability) descends. Cap
        // its length so the produced tree stays inside the depth every such walk can handle.
        for (int chain = 0; Cur.Kind == TokenKind.Operator; chain++)
        {
            int prec = BinPrec(Cur.Text);
            if (prec < minPrec || prec == 0) break;
            if (_depth + chain >= MaxNestingDepth) throw new GlslParseException($"expression nesting is too deep (limit {MaxNestingDepth})", ln);
            string op = Advance().Text;
            left = At(new Binary(op, left, ParseBinary(prec + 1)), ln);   // left-associative
        }
        return left;
    }

    Expr ParseUnary()
    {
        int ln = Cur.Line;
        if (Cur.Kind == TokenKind.Operator && IsPrefixOp(Cur.Text))
        {
            // Prefix operators chain directly into this method, so they need their own depth accounting.
            if (++_depth > MaxNestingDepth) { _depth--; throw new GlslParseException($"expression nesting is too deep (limit {MaxNestingDepth})", ln); }
            try
            {
                string op = Advance().Text;
                return At(new Unary(op, ParseUnary(), Prefix: true), ln);
            }
            finally { _depth--; }
        }
        return ParsePostfix();
    }

    Expr ParsePostfix()
    {
        int ln = Cur.Line;
        var e = At(ParsePrimary(), ln);
        // Postfix suffixes also chain iteratively into a deep left spine; bound it like the binary chain.
        for (int chain = 0; ; chain++)
        {
            if (_depth + chain >= MaxNestingDepth) throw new GlslParseException($"expression nesting is too deep (limit {MaxNestingDepth})", ln);
            if (AcceptKind(TokenKind.LBracket))
            {
                var idx = ParseExpression();
                Expect(TokenKind.RBracket);
                e = new Index(e, idx);
            }
            else if (AcceptKind(TokenKind.Dot))
            {
                string member = Expect(TokenKind.Identifier, "member").Text;
                e = IsKind(TokenKind.LParen) ? new Method(e, member, ParseArgs()) : new Member(e, member);
            }
            else if (Cur.Kind == TokenKind.Operator && Cur.Text is "++" or "--")
            {
                e = new Unary(Advance().Text, e, Prefix: false);
            }
            else return At(e, ln);
            e = At(e, ln);
        }
    }

    Expr ParsePrimary()
    {
        switch (Cur.Kind)
        {
            case TokenKind.IntConst or TokenKind.UintConst or TokenKind.Int64Const or TokenKind.Uint64Const or TokenKind.Int16Const or TokenKind.Uint16Const
                or TokenKind.FloatConst or TokenKind.DoubleConst or TokenKind.HalfConst or TokenKind.BoolConst or TokenKind.StringConst:
                var lit = Advance();
                return new Literal(lit.Kind, lit.Text);

            case TokenKind.LParen:
                Advance();
                var inner = ParseExpression();
                Expect(TokenKind.RParen);
                return inner;

            case TokenKind.Identifier:
                string name = Advance().Text;
                if (IsKind(TokenKind.LParen)) return new Call(name, ParseArgs());
                if (IsKind(TokenKind.LBracket))
                {
                    var dims = new List<Expr?>();
                    // This consumes the whole `[..][..]` run to tell an array constructor from indexing, so
                    // it builds the Index spine itself and needs the same depth bound as the postfix chain.
                    while (AcceptKind(TokenKind.LBracket))
                    {
                        if (_depth + dims.Count >= MaxNestingDepth)
                            throw new GlslParseException($"expression nesting is too deep (limit {MaxNestingDepth})", Cur.Line);
                        dims.Add(IsKind(TokenKind.RBracket) ? null : ParseExpression());   // T[](...) has no size
                        Expect(TokenKind.RBracket);
                    }
                    // T[N](...) / T[](...) / T[A][B](...) is an array constructor; otherwise it is indexing.
                    if (IsKind(TokenKind.LParen)) return new Call(name, ParseArgs(), dims[0], IsArray: true, ArrayDims: dims);
                    Expr indexed = new Ident(name);
                    foreach (var d in dims) indexed = new Index(indexed, d ?? throw Error("array index expected"));
                    return indexed;
                }
                return new Ident(name);

            default:
                if (Cur.Kind == TokenKind.End)
                    throw new GlslParseException("unexpected end of file", _i > 0 ? _t[_i - 1].Line : 0);
                throw Error($"unexpected token '{Cur.Text}'");
        }
    }

    List<Expr> ParseArgs()
    {
        Expect(TokenKind.LParen);
        var args = new List<Expr>();
        if (!IsKind(TokenKind.RParen))
        {
            if (IsText("void") && Peek().Kind == TokenKind.RParen) Advance();
            else do { args.Add(ParseAssignment()); } while (AcceptKind(TokenKind.Comma));
        }
        Expect(TokenKind.RParen);
        return args;
    }

    static int BinPrec(string op) => op switch
    {
        "||" => 1, "^^" => 2, "&&" => 3, "|" => 4, "^" => 5, "&" => 6,
        "==" or "!=" => 7,
        "<" or ">" or "<=" or ">=" => 8,
        "<<" or ">>" => 9,
        "+" or "-" => 10,
        "*" or "/" or "%" => 11,
        _ => 0,
    };

    static bool IsAssignOp(string op) =>
        op is "=" or "+=" or "-=" or "*=" or "/=" or "%=" or "<<=" or ">>=" or "&=" or "|=" or "^=";

    static bool IsPrefixOp(string op) => op is "+" or "-" or "!" or "~" or "++" or "--";

    // GLSL keywords that may never be used as a user identifier: qualifiers, flow keywords, literals,
    // AND every type name (glslang lexes type names as keyword tokens - `int float = 1;` is a syntax
    // error - while Prism lexes them as plain identifiers and classifies types contextually, so the
    // rejection happens at ExpectName). Type positions parse through raw Expect(Identifier), which is
    // why `float x;` still works.
    static readonly HashSet<string> ReservedWords =
    [
        "const", "uniform", "buffer", "shared", "attribute", "varying", "coherent", "volatile", "restrict",
        "readonly", "writeonly", "layout", "centroid", "flat", "smooth", "noperspective", "patch", "sample",
        "invariant", "precise", "in", "out", "inout", "precision", "highp", "mediump", "lowp",
        "break", "continue", "do", "for", "while", "switch", "case", "default", "if", "else", "discard", "return",
        "void", "struct", "true", "false", "bool", "int", "uint", "float", "double",
        "vec2", "vec3", "vec4", "ivec2", "ivec3", "ivec4", "bvec2", "bvec3", "bvec4",
        "uvec2", "uvec3", "uvec4", "dvec2", "dvec3", "dvec4",
        "mat2", "mat3", "mat4", "mat2x2", "mat2x3", "mat2x4", "mat3x2", "mat3x3", "mat3x4",
        "mat4x2", "mat4x3", "mat4x4", "dmat2", "dmat3", "dmat4",
        "dmat2x2", "dmat2x3", "dmat2x4", "dmat3x2", "dmat3x3", "dmat3x4", "dmat4x2", "dmat4x3", "dmat4x4",
        "sampler1D", "sampler2D", "sampler3D", "samplerCube",
        "sampler1DShadow", "sampler2DShadow", "samplerCubeShadow",
        "sampler1DArray", "sampler2DArray", "sampler1DArrayShadow", "sampler2DArrayShadow",
        "samplerCubeArray", "samplerCubeArrayShadow", "sampler2DRect", "sampler2DRectShadow",
        "samplerBuffer", "sampler2DMS", "sampler2DMSArray",
        "isampler1D", "isampler2D", "isampler3D", "isamplerCube", "isampler1DArray", "isampler2DArray",
        "isamplerCubeArray", "isampler2DRect", "isamplerBuffer", "isampler2DMS", "isampler2DMSArray",
        "usampler1D", "usampler2D", "usampler3D", "usamplerCube", "usampler1DArray", "usampler2DArray",
        "usamplerCubeArray", "usampler2DRect", "usamplerBuffer", "usampler2DMS", "usampler2DMSArray",
        "image1D", "image2D", "image3D", "imageCube", "image1DArray", "image2DArray", "imageCubeArray",
        "image2DRect", "imageBuffer", "image2DMS", "image2DMSArray",
        "iimage1D", "iimage2D", "iimage3D", "iimageCube", "iimage1DArray", "iimage2DArray",
        "iimageCubeArray", "iimage2DRect", "iimageBuffer", "iimage2DMS", "iimage2DMSArray",
        "uimage1D", "uimage2D", "uimage3D", "uimageCube", "uimage1DArray", "uimage2DArray",
        "uimageCubeArray", "uimage2DRect", "uimageBuffer", "uimage2DMS", "uimage2DMSArray",
        "atomic_uint",
    ];

    // Layout qualifier identifiers the parser accepts inside layout(...). Everything the corpus uses plus
    // the spec's other stage primitives, image formats, XFB and depth/stencil modes, the ray-tracing
    // payload/attribute/callable names, and the fragment-ordering/blend families. Unknown ids reject at
    // parse time with a glslang-style message ('q77' : unrecognized layout identifier) - Prism lexes
    // layout ids as plain identifiers, so without this check a typo like q77 = 64 silently changes
    // nothing. Stage-scoped misuse (local_size_x in a fragment shader) is NOT checked here - the parser
    // is context-free; glslang rejects that separately ("for this stage").
    static readonly HashSet<string> KnownLayoutQualifiers =
    [
        // resource + interface
        "set", "binding", "location", "component", "index", "offset", "align", "input_attachment_index",
        "std140", "std430", "scalar", "push_constant", "buffer_reference", "buffer_reference_align",
        "constant_id", "stream", "xfb_buffer", "xfb_offset", "xfb_stride",
        // compute
        "local_size_x", "local_size_y", "local_size_z",
        "local_size_x_id", "local_size_y_id", "local_size_z_id",
        // geometry / tessellation / mesh primitive and sizing
        "triangles", "lines", "points", "lines_adjacency", "triangles_adjacency", "line_strip",
        "triangle_strip", "points", "quads", "isolines", "point_mode",
        "equal_spacing", "fractional_even_spacing", "fractional_odd_spacing", "cw", "ccw",
        "input_points", "input_lines", "input_lines_adjacency", "input_triangles", "input_triangles_adjacency",
        "input_quads", "input_quads_adjacency", "input_isolines",
        "max_vertices", "max_primitives", "vertices", "invocations",
        "passthrough", "derivative_group_linearNV", "derivative_group_quadsNV",
        // fragment / depth / stencil / ordering
        "origin_upper_left", "pixel_center_integer",
        "early_fragment_tests", "post_depth_coverage", "early_and_late_fragment_tests_amd",
        "depth_any", "depth_greater", "depth_less", "depth_unchanged",
        "pixel_interlock_ordered", "pixel_interlock_unordered", "sample_interlock_ordered",
        "sample_interlock_unordered", "pixel_interlock_aggregate", "sample_interlock_aggregate",
        // row/column_major
        "row_major", "column_major",
        // ray tracing / mesh shading payload qualifiers
        "rayPayloadEXT", "rayPayloadInEXT", "callableDataEXT", "callableDataInEXT", "hitAttributeEXT",
        "rayPayloadNV", "rayPayloadInNV", "callableDataNV", "callableDataInNV", "hitAttributeNV",
        "shaderRecordEXT", "pervertexNV", "perprimitiveNV", "pertaskNV",
        // image formats (GLSL spec layout image-format ids)
        "rgba32f", "rgba16f", "rg32f", "rg16f", "r32f", "r16f",
        "rgba32ui", "rgba16ui", "rgba8ui", "rg32ui", "rg16ui", "rg8ui", "r32ui", "r16ui", "r8ui",
        "rgba32i", "rgba16i", "rgba8i", "rg32i", "rg16i", "rg8i", "r32i", "r16i", "r8i",
        "rgba8_snorm", "rg8_snorm", "r8_snorm",
        "rgba16", "rg16", "r16",
        "rgb10_a2", "rgb10_a2ui", "r11f_g11f_b10f",
        "rgba8", "rg8", "r8",
        "rgba16_snorm", "rg16_snorm", "r16_snorm", "r64ui", "r64i",
    ];

    // Expects an identifier that is not a reserved keyword (used for user-declared names).
    Token ExpectName(string what = "name")
    {
        var t = Expect(TokenKind.Identifier, what);
        if (ReservedWords.Contains(t.Text)) throw Error($"'{t.Text}' is a reserved keyword and cannot be a name");
        return t;
    }

    // ---- token helpers ----

    // Token access is clamped to the terminating End token so that a malformed source which runs the parser
    // past the end produces a normal "expected X" diagnostic instead of an IndexOutOfRange escaping the API.
    Token Cur => _t[Math.Min(_i, _t.Count - 1)];
    Token Peek() => _t[Math.Min(_i + 1, _t.Count - 1)];
    Token Advance() { var t = Cur; if (_i < _t.Count - 1) _i++; return t; }
    bool IsKind(TokenKind kind) => _t[_i].Kind == kind;
    bool IsText(string text) => _t[_i].Kind == TokenKind.Identifier && _t[_i].Text == text;
    bool IsOp(string op) => _t[_i].Kind == TokenKind.Operator && _t[_i].Text == op;
    bool AcceptKind(TokenKind kind) { if (IsKind(kind)) { _i++; return true; } return false; }
    bool AcceptText(string text) { if (IsText(text)) { _i++; return true; } return false; }
    bool AcceptOp(string op) { if (IsOp(op)) { _i++; return true; } return false; }

    Token Expect(TokenKind kind, string what = "")
    {
        if (IsKind(kind)) return Advance();
        throw Error($"expected {(what.Length > 0 ? what : TokenSpelling(kind))} but found '{Cur.Text}'");
    }

    // A human-readable spelling of a token kind for diagnostics, so a message reads "expected ';'" rather
    // than leaking the internal enum name "Semicolon".
    static string TokenSpelling(TokenKind kind) => kind switch
    {
        TokenKind.Semicolon => "';'",
        TokenKind.Comma => "','",
        TokenKind.LParen => "'('",
        TokenKind.RParen => "')'",
        TokenKind.LBracket => "'['",
        TokenKind.RBracket => "']'",
        TokenKind.LBrace => "'{'",
        TokenKind.RBrace => "'}'",
        TokenKind.Colon => "':'",
        TokenKind.Identifier => "a name",
        _ => kind.ToString(),
    };

    void ExpectText(string text)
    {
        if (!AcceptText(text)) throw Error($"expected '{text}' but found '{Cur.Text}'");
    }

    GlslParseException Error(string message) => new(message, Cur.Line);
}
