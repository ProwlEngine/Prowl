namespace Prowl.Prism.Glsl;

// A source-attributed compile error. Line is the 1-based source line (0 = unknown); the message is
// prefixed with it so callers get "shader.frag:12: ..." style diagnostics.
public sealed class GlslParseException : Exception
{
    public int Line { get; }
    public GlslParseException(string message, int line = 0) : base(line > 0 ? $"line {line}: {message}" : message) => Line = line;
    // Re-attach a line to an error raised deeper down without one (the message is re-prefixed).
    public GlslParseException WithLine(int line) => Line > 0 || line <= 0 ? this : new GlslParseException(Message, line);
}

// Array sizes are IReadOnlyList<Expr?>: one entry per dimension, null for an unsized
// dimension ("[]", e.g. a storage-buffer runtime array).

// ---- expressions ----
// Line is the 1-based source line the node starts on (0 = unset), stamped by the parser and used to
// attribute lowering errors. It rides along through `with` copies.
public abstract record Expr
{
    public int Line { get; set; }
    // Per-node memo of the lowerer's constant-fold result (an opaque CVal, or null = "folds to nothing").
    // The lowerer speculatively const-evaluates every operator node, so without this a nested expression is
    // re-folded once per ancestor -- O(n^2), and each literal re-parsed. Fresh per compile (the AST is
    // rebuilt each parse), and a node sits at one program point so its folded value is stable.
    internal bool ConstEvaluated;
    internal object? ConstResult;
}
public sealed record Literal(TokenKind Kind, string Text) : Expr;
public sealed record Ident(string Name) : Expr;
public sealed record Unary(string Op, Expr Operand, bool Prefix) : Expr;
public sealed record Binary(string Op, Expr Left, Expr Right) : Expr;    // includes assignment ops
public sealed record Conditional(Expr Cond, Expr Then, Expr Else) : Expr;
public sealed record Call(string Name, IReadOnlyList<Expr> Args, Expr? ArraySize = null, bool IsArray = false, IReadOnlyList<Expr?>? ArrayDims = null) : Expr;  // f(...), T(...), T[N](...), T[](...), T[A][B](...)
public sealed record Index(Expr Target, Expr Idx) : Expr;
public sealed record Member(Expr Target, string Name) : Expr;           // ".field" / swizzle
public sealed record Method(Expr Target, string Name, IReadOnlyList<Expr> Args) : Expr;  // e.g. arr.length()

// ---- types & qualifiers ----
// Array carries the "prefix" dimensions of `float[4] x` (equivalent to the postfix `float x[4]`).
public sealed record TypeRef(string Name, IReadOnlyList<Expr?>? Array = null, IReadOnlyList<Expr>? TemplateArgs = null);
public sealed record LayoutItem(string Name, Expr? Value);              // "binding = 0" or bare "std430"
public sealed record Qualifiers(IReadOnlyList<string> Words, IReadOnlyList<LayoutItem> Layout)
{
    public static readonly Qualifiers None = new([], []);
    public bool Has(string word) => Words.Contains(word);
}
public sealed record Declarator(string Name, IReadOnlyList<Expr?> Array, Expr? Init)
{
    public int Line { get; set; }   // 1-based source line of the declarator's name (0 = unset)
}

// ---- statements ----
public abstract record Stmt { public int Line { get; set; } }
public sealed record Block(IReadOnlyList<Stmt> Statements) : Stmt;
public sealed record VarDeclStmt(Qualifiers Qualifiers, TypeRef Type, IReadOnlyList<Declarator> Declarators) : Stmt;
public sealed record ExprStmt(Expr Expression) : Stmt;
public sealed record IfStmt(Expr Cond, Stmt Then, Stmt? Else, uint SelControl = 0) : Stmt;
public sealed record ForStmt(Stmt? Init, Expr? Cond, Expr? Update, Stmt Body, uint LoopControl = 0, uint DepLength = 0) : Stmt;
public sealed record WhileStmt(Expr Cond, Stmt Body, uint LoopControl = 0, uint DepLength = 0) : Stmt;
public sealed record DoWhileStmt(Stmt Body, Expr Cond, uint LoopControl = 0, uint DepLength = 0) : Stmt;
public sealed record SwitchStmt(Expr Value, IReadOnlyList<SwitchCase> Cases) : Stmt;
public sealed record SwitchCase(Expr? Label, IReadOnlyList<Stmt> Body)
{
    public int Line { get; set; }   // the 'case'/'default' token's line (0 = unset)
}   // Label null => default
public sealed record ReturnStmt(Expr? Value) : Stmt;
public sealed record JumpStmt(string Keyword) : Stmt;                   // break, continue, discard

// ---- top-level declarations ----
public sealed record TranslationUnit(int Version, IReadOnlyList<Decl> Declarations, IReadOnlyList<string>? Extensions = null, IReadOnlyList<string>? Pragmas = null, bool DefaultFloatRelaxed = false, bool IsEs = false, bool? IntPrecisionRelaxed = null);
public abstract record Decl { public int Line { get; set; } }
public sealed record Param(Qualifiers Qualifiers, TypeRef Type, string? Name, IReadOnlyList<Expr?> Array);
public sealed record Field(Qualifiers Qualifiers, TypeRef Type, IReadOnlyList<Declarator> Declarators);
public sealed record FunctionDef(TypeRef ReturnType, string Name, IReadOnlyList<Param> Parameters, Block Body) : Decl;
public sealed record FunctionProto(TypeRef ReturnType, string Name, IReadOnlyList<Param> Parameters) : Decl;
public sealed record GlobalVar(Qualifiers Qualifiers, TypeRef Type, IReadOnlyList<Declarator> Declarators) : Decl;
public sealed record StructDef(string Name, IReadOnlyList<Field> Members) : Decl;
public sealed record InterfaceBlock(Qualifiers Qualifiers, string BlockName, IReadOnlyList<Field> Members, string? InstanceName, IReadOnlyList<Expr?> InstanceArray) : Decl;
public sealed record LayoutDecl(Qualifiers Qualifiers) : Decl;         // e.g. layout(local_size_x = 8) in;
public sealed record QualifierRedecl(Qualifiers Qualifiers, string Name) : Decl;   // e.g. invariant gl_Position;
