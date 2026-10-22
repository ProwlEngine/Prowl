// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.OrigamiUI;

/// <summary>What a span of code is, for colouring. Lexers produce the lexical kinds, language services refine identifiers with the semantic ones.</summary>
public enum CodeTokenKind : byte
{
    Plain,
    Keyword,
    ControlKeyword,
    Preprocessor,
    PreprocessorText,
    Comment,
    DocComment,
    DocCommentTag,
    String,
    StringEscape,
    Char,
    Number,
    Operator,
    Punctuation,
    Identifier,

    Namespace,
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    Record,
    TypeParameter,
    Method,
    ExtensionMethod,
    Property,
    Field,
    Constant,
    Event,
    EnumMember,
    Local,
    Parameter,
    Label,
    ExcludedCode,
}

/// <summary>Kind of symbol a completion item or navigation result stands for, used to pick its icon.</summary>
public enum CodeSymbolKind : byte
{
    Text,
    Keyword,
    Snippet,
    Namespace,
    Class,
    Struct,
    Interface,
    Enum,
    EnumMember,
    Delegate,
    Record,
    Method,
    ExtensionMethod,
    Constructor,
    Property,
    Field,
    Event,
    Local,
    Parameter,
    Constant,
    TypeParameter,
    Operator,
    Label,
}

public enum CodeDiagnosticSeverity : byte { Hidden, Info, Warning, Error }

/// <summary>A run of a lexed line. <see cref="Start"/> is a column into the line.</summary>
public readonly struct CodeToken
{
    public readonly int Start;
    public readonly int Length;
    public readonly CodeTokenKind Kind;

    public CodeToken(int start, int length, CodeTokenKind kind)
    {
        Start = start;
        Length = length;
        Kind = kind;
    }

    public int End => Start + Length;
}

/// <summary>
/// Line at a time lexer. Line based state is what lets the editor highlight only the visible part of a huge file:
/// a line's colours depend only on its text and the state the previous line ended in.
/// </summary>
public interface ICodeLexer
{
    /// <summary>
    /// Lexes <paramref name="line"/> starting in <paramref name="state"/> and returns the state the next line starts in.
    /// Must be stateless and thread safe, since folding runs it on a background thread while the editor draws.
    /// <paramref name="tokens"/> is null when the editor only needs the end state, so the lexer can skip emitting.
    /// </summary>
    int Tokenize(string line, int state, List<CodeToken>? tokens);
}

/// <summary>A piece of display text tagged with how to colour it, for hover info and signatures.</summary>
public readonly struct CodeTaggedText
{
    public readonly string Text;
    public readonly CodeTokenKind Kind;

    public CodeTaggedText(string text, CodeTokenKind kind = CodeTokenKind.Plain)
    {
        Text = text;
        Kind = kind;
    }
}

/// <summary>A replacement of <see cref="Length"/> chars at <see cref="Start"/>. Offsets join lines with a single '\n'.</summary>
public readonly struct CodeTextEdit
{
    public readonly int Start;
    public readonly int Length;
    public readonly string NewText;

    public CodeTextEdit(int start, int length, string newText)
    {
        Start = start;
        Length = length;
        NewText = newText;
    }
}

public sealed class CodeCompletionItem
{
    public string Label { get; init; } = "";
    public CodeSymbolKind Kind { get; init; }

    /// <summary>Short text shown greyed after the label, such as a return type or namespace.</summary>
    public string? Detail { get; init; }

    /// <summary>Text matched against what was typed. Defaults to <see cref="Label"/>.</summary>
    public string? FilterText { get; init; }

    /// <summary>Text the list is ordered by before matching. Defaults to <see cref="Label"/>.</summary>
    public string? SortText { get; init; }

    /// <summary>Text inserted on commit when the language has no <see cref="CodeLanguageSession.ResolveCompletion"/> of its own.</summary>
    public string? InsertText { get; init; }

    /// <summary>Selected by default when the list opens, such as the type a 'new' expects.</summary>
    public bool Preselect { get; init; }

    /// <summary>Characters that commit this item as well as typing themselves, such as '(' or '.'.</summary>
    public string? CommitCharacters { get; init; }

    /// <summary>Anything the language wants to carry back to <see cref="CodeLanguageSession.ResolveCompletion"/>.</summary>
    public object? Data { get; init; }

    public string Filter => FilterText ?? Label;
    public string Sort => SortText ?? Label;
}

public sealed class CodeCompletionList
{
    public IReadOnlyList<CodeCompletionItem> Items { get; init; } = Array.Empty<CodeCompletionItem>();

    /// <summary>Offset where the word being completed starts. Typed text from here to the caret filters the list.</summary>
    public int SpanStart { get; init; }

    /// <summary>When true the list is only shown, not committed, by commit characters such as space.</summary>
    public bool SuggestionMode { get; init; }
}

public enum CodeCompletionTriggerKind : byte { Invoke, Character, Deletion }

public readonly struct CodeCompletionTrigger
{
    public readonly CodeCompletionTriggerKind Kind;
    public readonly char Character;

    public CodeCompletionTrigger(CodeCompletionTriggerKind kind, char character = '\0')
    {
        Kind = kind;
        Character = character;
    }

    public static CodeCompletionTrigger Invoke => new(CodeCompletionTriggerKind.Invoke);
}

/// <summary>What committing a completion does to the document.</summary>
public sealed class CodeCompletionCommit
{
    public IReadOnlyList<CodeTextEdit> Edits { get; init; } = Array.Empty<CodeTextEdit>();

    /// <summary>Where the caret goes, as an offset in the document after the edits. Null puts it after the last edit.</summary>
    public int? CaretOffset { get; init; }
}

public sealed class CodeQuickInfo
{
    public int SpanStart { get; init; }
    public int SpanLength { get; init; }

    /// <summary>The signature line, coloured.</summary>
    public IReadOnlyList<CodeTaggedText> Signature { get; init; } = Array.Empty<CodeTaggedText>();

    /// <summary>Plain documentation paragraphs shown under the signature.</summary>
    public IReadOnlyList<string> Documentation { get; init; } = Array.Empty<string>();
}

public sealed class CodeSignatureParameter
{
    public string Name { get; init; } = "";
    public string? Documentation { get; init; }

    /// <summary>Range of this parameter inside the signature's joined text, for bolding the active one.</summary>
    public int LabelStart { get; init; }
    public int LabelLength { get; init; }
}

public sealed class CodeSignature
{
    public IReadOnlyList<CodeTaggedText> Parts { get; init; } = Array.Empty<CodeTaggedText>();
    public IReadOnlyList<CodeSignatureParameter> Parameters { get; init; } = Array.Empty<CodeSignatureParameter>();
    public string? Documentation { get; init; }
}

public sealed class CodeSignatureHelp
{
    public IReadOnlyList<CodeSignature> Signatures { get; init; } = Array.Empty<CodeSignature>();
    public int ActiveSignature { get; init; }
    public int ActiveParameter { get; init; }

    /// <summary>Offset of the open paren the help belongs to. The popup closes when the caret leaves it.</summary>
    public int ArgumentListStart { get; init; }
}

public sealed class CodeDiagnostic
{
    public int Start { get; init; }
    public int Length { get; init; }
    public CodeDiagnosticSeverity Severity { get; init; }
    public string Id { get; init; } = "";
    public string Message { get; init; } = "";
}

public readonly struct CodeClassifiedSpan
{
    public readonly int Start;
    public readonly int Length;
    public readonly CodeTokenKind Kind;

    public CodeClassifiedSpan(int start, int length, CodeTokenKind kind)
    {
        Start = start;
        Length = length;
        Kind = kind;
    }
}

/// <summary>A place in some document. <see cref="IsCurrentDocument"/> results are navigated in place, others raise <see cref="CodeEditor.NavigateRequested"/>.</summary>
public sealed class CodeLocation
{
    public bool IsCurrentDocument { get; init; }

    /// <summary>File path or document name for other documents, null for metadata only symbols.</summary>
    public string? Path { get; init; }

    public int Start { get; init; }
    public int Length { get; init; }

    /// <summary>Zero based line and column, filled in for locations outside the current document.</summary>
    public int Line { get; init; }
    public int Column { get; init; }

    /// <summary>What to call it in a list: the symbol's full name, or for a reference the whole source line it is on.</summary>
    public string Display { get; init; } = "";
    public CodeSymbolKind Kind { get; init; }
}

/// <summary>A note drawn after a declaration, such as how many references it has. Clicking it lists them.</summary>
public sealed class CodeLens
{
    /// <summary>Offset of the declared name. The note sits on its line and clicking it finds references from here.</summary>
    public int Offset { get; init; }
    public string Text { get; init; } = "";
}

public sealed class CodeAction
{
    public string Title { get; init; } = "";

    /// <summary>True for fixes to a diagnostic, shown first and marked with a bulb.</summary>
    public bool IsQuickFix { get; init; }

    public IReadOnlyList<CodeTextEdit> Edits { get; init; } = Array.Empty<CodeTextEdit>();

    /// <summary>
    /// Computes the action's edits when it is picked, for actions too costly to compute for every entry in the
    /// list or that change other documents. When set it is used instead of <see cref="Edits"/>.
    /// </summary>
    public Func<CancellationToken, Task<CodeRenameResult?>>? Resolve { get; init; }
}

/// <summary>Edits to the current document and to other documents, keyed by their path.</summary>
public sealed class CodeRenameResult
{
    public IReadOnlyList<CodeTextEdit> Edits { get; init; } = Array.Empty<CodeTextEdit>();

    /// <summary>Edits to other documents, keyed by their path. The host applies them.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<CodeTextEdit>> OtherDocuments { get; init; }
        = new Dictionary<string, IReadOnlyList<CodeTextEdit>>();
}

public readonly struct CodeFoldRange
{
    public readonly int StartLine;
    public readonly int EndLine;

    /// <summary>Text shown in place of the hidden lines, such as "#region Fields".</summary>
    public readonly string? Label;

    public CodeFoldRange(int startLine, int endLine, string? label = null)
    {
        StartLine = startLine;
        EndLine = endLine;
        Label = label;
    }
}

/// <summary>
/// Everything an editor knows about a language without a compiler: its lexer, comment syntax, brackets
/// and indentation rules. Subclass and override <see cref="CreateSession"/> to add completions,
/// diagnostics and the rest through a <see cref="CodeLanguageSession"/>.
/// </summary>
public class CodeLanguage
{
    public virtual string Name => "Plain Text";
    public virtual ICodeLexer? Lexer => null;

    public virtual string? LineComment => null;
    public virtual string? BlockCommentStart => null;
    public virtual string? BlockCommentEnd => null;

    /// <summary>Pairs matched for highlighting and folding.</summary>
    public virtual IReadOnlyList<(char Open, char Close)> Brackets { get; } = new[] { ('(', ')'), ('[', ']'), ('{', '}') };

    /// <summary>Pairs closed automatically when the opener is typed.</summary>
    public virtual IReadOnlyList<(char Open, char Close)> AutoClosingPairs { get; } = new[] { ('(', ')'), ('[', ']'), ('{', '}'), ('"', '"'), ('\'', '\'') };

    /// <summary>Characters that open a completion list while typing.</summary>
    public virtual bool IsCompletionTrigger(char c) => false;

    /// <summary>Characters that open signature help while typing.</summary>
    public virtual bool IsSignatureHelpTrigger(char c) => false;

    /// <summary>Characters that format what they close as they are typed, see <see cref="CodeLanguageSession.FormatOnTypeAsync"/>.</summary>
    public virtual bool IsFormatOnTypeTrigger(char c) => false;

    public virtual bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>True when a new line after <paramref name="lineBeforeCaret"/> should be indented one level deeper.</summary>
    public virtual bool IndentsNextLine(string lineBeforeCaret)
    {
        string t = lineBeforeCaret.TrimEnd();
        return t.Length > 0 && (t[^1] == '{' || t[^1] == '(' || t[^1] == '[');
    }

    /// <summary>True when typing <paramref name="c"/> as the first non blank char of a line should outdent it.</summary>
    public virtual bool OutdentsOn(char c) => c == '}' || c == ')' || c == ']';

    /// <summary>Folds computed from the lexed text. The default folds matching braces and #region blocks. Runs off the UI thread.</summary>
    public virtual List<CodeFoldRange> ComputeFolds(string[] lines, CancellationToken ct)
        => CodeFolding.FromBraces(lines, Lexer, ct);

    /// <summary>Starts the smart side of the language for one editor. Null keeps the editor lexical only.</summary>
    public virtual CodeLanguageSession? CreateSession(CodeDocument document) => null;
}

/// <summary>
/// Compiler backed features for one open document. Every async method runs off the UI thread and may be
/// cancelled when the user moves on. Offsets are into the document's text with lines joined by '\n'.
/// </summary>
public abstract class CodeLanguageSession : IDisposable
{
    /// <summary>Called on the UI thread for every edit, in order, before any request that sees it.</summary>
    public virtual void OnTextChanged(TextChange change) { }

    /// <summary>Called on the UI thread when the whole text was replaced.</summary>
    public virtual void OnTextReset(string text) { }

    public virtual Task<CodeCompletionList?> GetCompletionsAsync(int offset, CodeCompletionTrigger trigger, CancellationToken ct)
        => Task.FromResult<CodeCompletionList?>(null);

    /// <summary>Description for the selected completion item, shown beside the list.</summary>
    public virtual Task<CodeQuickInfo?> GetCompletionDescriptionAsync(CodeCompletionItem item, CancellationToken ct)
        => Task.FromResult<CodeQuickInfo?>(null);

    /// <summary>Called on the UI thread when an item is committed. Null replaces the typed word with the item's insert text.</summary>
    public virtual CodeCompletionCommit? ResolveCompletion(CodeCompletionList list, CodeCompletionItem item, int caretOffset, char commitChar)
        => null;

    public virtual Task<CodeQuickInfo?> GetQuickInfoAsync(int offset, CancellationToken ct)
        => Task.FromResult<CodeQuickInfo?>(null);

    public virtual Task<CodeSignatureHelp?> GetSignatureHelpAsync(int offset, CancellationToken ct)
        => Task.FromResult<CodeSignatureHelp?>(null);

    public virtual Task<IReadOnlyList<CodeDiagnostic>> GetDiagnosticsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeDiagnostic>>(Array.Empty<CodeDiagnostic>());

    public virtual Task<IReadOnlyList<CodeClassifiedSpan>> GetClassificationsAsync(int start, int length, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeClassifiedSpan>>(Array.Empty<CodeClassifiedSpan>());

    public virtual Task<IReadOnlyList<CodeLocation>> GetDefinitionAsync(int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>());

    public virtual Task<IReadOnlyList<CodeLocation>> FindReferencesAsync(int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>());

    /// <summary>Types implementing or deriving from the symbol, or members implementing or overriding it.</summary>
    public virtual Task<IReadOnlyList<CodeLocation>> GetImplementationsAsync(int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>());

    /// <summary>The base types and interfaces of a type, or the members a member overrides or implements.</summary>
    public virtual Task<IReadOnlyList<CodeLocation>> GetBaseDefinitionsAsync(int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>());

    /// <summary>The declaration of the type of the symbol, such as the class of a variable.</summary>
    public virtual Task<IReadOnlyList<CodeLocation>> GetTypeDefinitionAsync(int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeLocation>>(Array.Empty<CodeLocation>());

    /// <summary>Notes for the declarations within the span, such as reference counts.</summary>
    public virtual Task<IReadOnlyList<CodeLens>> GetCodeLensesAsync(int start, int length, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeLens>>(Array.Empty<CodeLens>());

    /// <summary>Spans of the symbol under the caret and its other uses in this document.</summary>
    public virtual Task<IReadOnlyList<CodeClassifiedSpan>> GetHighlightsAsync(int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeClassifiedSpan>>(Array.Empty<CodeClassifiedSpan>());

    public virtual Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(int start, int length, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeAction>>(Array.Empty<CodeAction>());

    /// <summary>Edits that format the given span, or the whole document when <paramref name="length"/> is negative.</summary>
    public virtual Task<IReadOnlyList<CodeTextEdit>> FormatAsync(int start, int length, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeTextEdit>>(Array.Empty<CodeTextEdit>());

    /// <summary>Edits that remove unused using directives and sort the rest.</summary>
    public virtual Task<IReadOnlyList<CodeTextEdit>> OrganizeUsingsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeTextEdit>>(Array.Empty<CodeTextEdit>());

    /// <summary>Edits that format the statement or block closed by <paramref name="typed"/>, which was just typed before <paramref name="offset"/>.</summary>
    public virtual Task<IReadOnlyList<CodeTextEdit>> FormatOnTypeAsync(int offset, char typed, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<CodeTextEdit>>(Array.Empty<CodeTextEdit>());

    public virtual Task<CodeRenameResult?> RenameAsync(int offset, string newName, CancellationToken ct)
        => Task.FromResult<CodeRenameResult?>(null);

    public virtual void Dispose() { }
}
