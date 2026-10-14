using System.Text;

namespace Prowl.Prism.Glsl;

/// <summary>
/// Prints an AST back to GLSL. Expressions are fully parenthesized so precedence is
/// explicit and unambiguous. Used to verify the parser round-trips (parse -> print ->
/// compile) and as the basis for GLSL re-emission.
/// </summary>
public static class AstPrinter
{
    public static string Print(TranslationUnit unit)
    {
        var sb = new StringBuilder();
        sb.Append("#version ").Append(unit.Version).Append('\n');
        foreach (var decl in unit.Declarations) sb.Append(Decl(decl)).Append('\n');
        return sb.ToString();
    }

    static string Decl(Decl d) => d switch
    {
        FunctionDef f => $"{f.ReturnType.Name} {f.Name}({Join(f.Parameters, Param)}) {Stmt(f.Body)}",
        FunctionProto p => $"{p.ReturnType.Name} {p.Name}({Join(p.Parameters, Param)});",
        GlobalVar g => $"{Quals(g.Qualifiers)}{g.Type.Name} {Join(g.Declarators, Declarator)};",
        StructDef s => $"struct {s.Name} {{ {Concat(s.Members, m => Field(m) + " ")}}};",
        InterfaceBlock b =>
            $"{Quals(b.Qualifiers)}{b.BlockName} {{ {Concat(b.Members, m => Field(m) + " ")}}}" +
            $"{(b.InstanceName is null ? "" : " " + b.InstanceName)}{Dims(b.InstanceArray)};",
        LayoutDecl l => $"{Quals(l.Qualifiers).TrimEnd()};",
        _ => throw new GlslParseException($"cannot print {d.GetType().Name}"),
    };

    static string Stmt(Stmt s) => s switch
    {
        Block b => "{ " + Concat(b.Statements, x => Stmt(x) + " ") + "}",
        VarDeclStmt v => $"{Quals(v.Qualifiers)}{v.Type.Name} {Join(v.Declarators, Declarator)};",
        ExprStmt e => $"{Expr(e.Expression)};",
        IfStmt i => $"if ({Expr(i.Cond)}) {Stmt(i.Then)}" + (i.Else is null ? "" : $" else {Stmt(i.Else)}"),
        ForStmt f => $"for ({(f.Init is null ? ";" : Stmt(f.Init))} {(f.Cond is null ? "" : Expr(f.Cond))}; " +
                     $"{(f.Update is null ? "" : Expr(f.Update))}) {Stmt(f.Body)}",
        WhileStmt w => $"while ({Expr(w.Cond)}) {Stmt(w.Body)}",
        DoWhileStmt d => $"do {Stmt(d.Body)} while ({Expr(d.Cond)});",
        SwitchStmt sw => $"switch ({Expr(sw.Value)}) {{ {Concat(sw.Cases, Case)}}}",
        ReturnStmt r => r.Value is null ? "return;" : $"return {Expr(r.Value)};",
        JumpStmt j => $"{j.Keyword};",
        _ => throw new GlslParseException($"cannot print {s.GetType().Name}"),
    };

    static string Case(SwitchCase c) =>
        (c.Label is null ? "default: " : $"case {Expr(c.Label)}: ") + Concat(c.Body, x => Stmt(x) + " ");

    static string Expr(Expr e) => e switch
    {
        Literal l => l.Text,
        Ident id => id.Name,
        Unary u => u.Prefix ? $"({u.Op}{Expr(u.Operand)})" : $"({Expr(u.Operand)}{u.Op})",
        Binary b => $"({Expr(b.Left)} {b.Op} {Expr(b.Right)})",
        Conditional c => $"({Expr(c.Cond)} ? {Expr(c.Then)} : {Expr(c.Else)})",
        Call { IsArray: true } c => $"{c.Name}[{(c.ArraySize is null ? "" : Expr(c.ArraySize))}]({Join(c.Args, Expr)})",
        Call c => $"{c.Name}({Join(c.Args, Expr)})",
        Index x => $"{Expr(x.Target)}[{Expr(x.Idx)}]",
        Member m => $"{Expr(m.Target)}.{m.Name}",
        Method me => $"{Expr(me.Target)}.{me.Name}({Join(me.Args, Expr)})",
        _ => throw new GlslParseException($"cannot print {e.GetType().Name}"),
    };

    static string Param(Param p) =>
        $"{Quals(p.Qualifiers)}{p.Type.Name}{(p.Name is null ? "" : " " + p.Name)}{Dims(p.Array)}";

    static string Field(Field f) => $"{Quals(f.Qualifiers)}{f.Type.Name} {Join(f.Declarators, Declarator)};";

    static string Declarator(Declarator d) =>
        $"{d.Name}{Dims(d.Array)}{(d.Init is null ? "" : " = " + Expr(d.Init))}";

    static string Dims(IReadOnlyList<Expr?> dims) =>
        Concat(dims, d => d is null ? "[]" : $"[{Expr(d)}]");

    static string Quals(Qualifiers q)
    {
        var sb = new StringBuilder();
        if (q.Layout.Count > 0)
            sb.Append("layout(")
              .Append(Join(q.Layout, li => li.Value is null ? li.Name : $"{li.Name} = {Expr(li.Value)}"))
              .Append(") ");
        foreach (var w in q.Words) sb.Append(w).Append(' ');
        return sb.ToString();
    }

    static string Join<T>(IReadOnlyList<T> items, Func<T, string> f) => string.Join(", ", items.Select(f));
    static string Concat<T>(IReadOnlyList<T> items, Func<T, string> f) => string.Concat(items.Select(f));
}
