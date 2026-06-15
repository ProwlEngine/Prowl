# Prowl.Crumb

A tiny, high-performance, reusable tokenizer for modern C#, targeting .NET 10.

Crumb is a lexer engine for game engines, lightweight markup languages, DSLs, configuration
formats, and more. It tokenizes directly over a `ReadOnlySpan<char>`
with zero allocations, and it knows nothing about identifiers, keywords, numbers,
or strings until you tell it.

It is not a bloated parser framework with all the features under the sun. 
It is a small utility for that boilerplate tokenizer you've always just copy-pasted everywhere in your codebase,
but with all the little things you never put the energy into adding.

## Features

- Zero allocations during tokenization. Tokens never own text; they reference
  slices of the original source.
- `ref struct` tokenizer with explicit `Position`, `Line`, and `Column` tracking.
- Fixed tokens (keywords and symbols) compiled into first-character dispatch
  tables (`FrozenDictionary`) with longest-match semantics.
- Dynamic rules as delegates for identifiers, numbers, strings, comments, and any
  custom syntax. Registration order is precedence.
- First-class opaque blocks for embedding sublanguages (for example HLSL inside
  ShaderLab) as a single token.
- `Peek`, `Mark`/`Reset` backtracking, and `TryConsume`/`Expect` helpers.
- Configuration over inheritance. No virtual dispatch, no boxing, no regex, no LINQ
  on the hot path. This thing is fast.

## Installation

```bash
dotnet add package Prowl.Crumb
```

## Quick start

```csharp
using Prowl.Crumb;

enum TokenKind
{
    EndOfFile,
    Error,
    Identifier,
    Number,
    String,
    Properties,
    Shader,
    SubShader,
    Pass,
    OpenBrace,
    CloseBrace,
    HlslSource,
}

var rules = new TokenizerRules<TokenKind>()
    .EndOfFile(TokenKind.EndOfFile)
    .Error(TokenKind.Error)
    .Whitespace(c => c is ' ' or '\t' or '\r' or '\n')
    .Keyword("Properties", TokenKind.Properties)
    .Keyword("Shader", TokenKind.Shader)
    .Keyword("SubShader", TokenKind.SubShader)
    .Keyword("Pass", TokenKind.Pass)
    .Symbol("{", TokenKind.OpenBrace)
    .Symbol("}", TokenKind.CloseBrace)
    .Identifier(TokenKind.Identifier)
    .Number(TokenKind.Number)
    .String('"', TokenKind.String)
    .LineComment("//")
    .Comment("/*", "*/")
    .Block("HLSLPROGRAM", "ENDHLSL", TokenKind.HlslSource);

var tokenizer = new Tokenizer<TokenKind>(source, rules);

while (true)
{
    var token = tokenizer.Next();
    Console.WriteLine($"{token.Kind}: '{tokenizer.Slice(token)}'");

    if (token.Kind == TokenKind.EndOfFile)
        break;
}
```

## Concepts

### Token kinds

Token kinds are your own enum, constrained as `unmanaged, Enum`. Crumb needs to
know two of your values so it can report them:

- `EndOfFile(kind)` is emitted when the source is exhausted.
- `Error(kind)` is emitted for a single character that no rule could consume,
  so callers can recover instead of throwing.

Both default to `default(TKind)` (the enum's zero value) if you do not set them.

### Tokens reference, never own

```csharp
public readonly struct Token<TKind>
{
    public readonly TKind Kind;
    public readonly int Start;
    public readonly int Length;
    public readonly int Line;    // one-based
    public readonly int Column;  // one-based
    public int End => Start + Length;
}
```

To read a token's text, ask the tokenizer for a zero-allocation view:

```csharp
ReadOnlySpan<char> text = tokenizer.Slice(token);
```

### Resolution order

For every token, Crumb resolves in this order:

```
skip whitespace and skipped comments
  -> first-character fixed-token lookup (longest match)
  -> dynamic rules, in registration order
  -> single-character error token
```

## Rules

`TokenizerRules<TKind>` is a fluent builder. It compiles once (lazily on first use,
or explicitly via `.Compile()`) into immutable dispatch tables and can be reused
across many tokenizers and many sources. It cannot be modified after compilation.

### Whitespace

```csharp
.Whitespace(char.IsWhiteSpace)
.Whitespace(c => c is ' ' or '\t' or '\r' or '\n')
```

Whitespace is skipped automatically before every token.

### Keywords and symbols

```csharp
.Keyword("Shader", TokenKind.Shader)   // requires a word boundary
.Symbol("{", TokenKind.OpenBrace)      // matched literally
.Symbol("=", TokenKind.Equals)
.Symbol("==", TokenKind.EqualsEquals)
.Symbol("===", TokenKind.EqualsEqualsEquals)
```

Keywords only match when not immediately followed by an identifier-continuation
character, so `Shader` does not match inside `Shaders`. Symbols match literally.
Both honor longest-match: `===` wins over `==` wins over `=`. Fixed tokens are
indexed by first character, so adding more never slows the common path.

### Identifiers

Default start is a letter or `_`; default continuation is a letter, digit, or `_`.
Override either predicate:

```csharp
.Identifier(TokenKind.Identifier)
.Identifier(TokenKind.Identifier, IsIdentifierStart, IsIdentifierPart)
```

The continuation predicate is also used for keyword word-boundary checks.

### Numbers

One rule covers integers, decimals, scientific notation, and hexadecimal:

```csharp
.Number(TokenKind.Number)
// 42  3.14  1e10  2.5E-3  0xFF  .5  10.
```

### Strings

```csharp
.String('"', TokenKind.String)            // default escape is backslash
.String('\'', TokenKind.String, escape: '\\')
```

Characters following the escape character are taken literally. An unterminated
string consumes to end of input.

### Comments

```csharp
.LineComment("//")                  // skip to end of line
.LineComment("//", TokenKind.Line)  // emit as a token (newline excluded)
.Comment("/*", "*/")                // skip a delimited block
.Comment("/*", "*/", TokenKind.Doc) // emit a delimited block as a token
```

Skipped comments are treated as trivia, like whitespace. The emitting overloads
produce a token instead.

### Opaque blocks

Blocks embed a sublanguage as a single token without tokenizing its contents:

```csharp
.Block("HLSLPROGRAM", "ENDHLSL", TokenKind.HlslSource)
.Block("GLSL", "ENDGLSL", TokenKind.GlslSource)
```

After the opening string matches, Crumb scans to the closing string and emits one
token spanning the content between the delimiters (the delimiters themselves are
consumed but not included). The contents are a span over the original source, so
there is no allocation and you can feed them to another tokenizer.

### Custom rules

Any structural token is a delegate. It either consumes characters and produces a
token (returns `true`) or leaves the cursor untouched (returns `false`):

```csharp
public delegate bool TokenRule<TKind>(
    ref Tokenizer<TKind> tokenizer,
    out Token<TKind> token);

rules.Rule((ref Tokenizer<TokenKind> t, out Token<TokenKind> token) =>
{
    var src = t.Source;
    int pos = t.Position;
    if (pos >= src.Length || src[pos] != '@')
    {
        token = default;
        return false;
    }

    int begin = pos, line = t.Line, col = t.Column;
    pos++;
    while (pos < src.Length && char.IsLetter(src[pos]))
        pos++;

    t.Advance(pos - begin);
    token = t.CreateToken(TokenKind.Directive, begin, line, col);
    return true;
});
```

Inside a rule you have `Source`, `Position`, `Line`, `Column`, `Advance(count)`,
and `CreateToken(kind, start, line, column)`.

## Driving the tokenizer

```csharp
var tokenizer = new Tokenizer<TokenKind>(source, rules);

Token<TokenKind> next = tokenizer.Next();   // consume
Token<TokenKind> ahead = tokenizer.Peek();  // look ahead, no advance

if (tokenizer.TryConsume(TokenKind.OpenBrace)) { /* ... */ }
if (tokenizer.TryConsume(TokenKind.Number, out var number)) { /* ... */ }

Token<TokenKind> brace = tokenizer.Expect(TokenKind.OpenBrace); // throws if mismatched

bool done = tokenizer.IsAtEnd;
ReadOnlySpan<char> rest = tokenizer.Remaining();
```

### Backtracking

`Mark` captures the current position; `Reset` restores it. This is what a hand
written recursive-descent parser uses to try alternatives:

```csharp
var mark = tokenizer.Mark();
if (!TryParseExpression(ref tokenizer))
    tokenizer.Reset(mark); // rewind and try something else
```

## Diagnostics

`Expect` throws `UnexpectedTokenException` with the expected kind, actual kind, and
one-based line and column. That is the whole diagnostics surface; Crumb stays out
of the business of building error-reporting systems.

```csharp
try
{
    tokenizer.Expect(TokenKind.OpenBrace);
}
catch (UnexpectedTokenException ex)
{
    Console.Error.WriteLine(
        $"{ex.Expected} expected, got {ex.Actual} at {ex.Line}:{ex.Column}");
}
```

## Performance notes

- The tokenizer is a `ref struct`, so it lives on the stack and cannot be boxed.
- Fixed tokens dispatch by first character through a `FrozenDictionary`, never by
  iterating every keyword.
- No substring creation, no regex, no LINQ, and no interface dispatch on the hot
  path. Token-kind comparisons use `EqualityComparer<TKind>.Default` to avoid
  boxing.
- Hot helpers are marked `AggressiveInlining`, with ASCII fast paths for digits.

Because the tokenizer is a `ref struct`, it cannot be stored in a field, captured
in a lambda, or used inside an `async` method. Pass it by `ref`.

## API surface

- `Token<TKind>`
- `Tokenizer<TKind>`
- `TokenizerRules<TKind>`
- `TokenizerMark`
- `TokenRule<TKind>` and `CharPredicate`
- `UnexpectedTokenException`

## License

See [LICENSE](LICENSE).
