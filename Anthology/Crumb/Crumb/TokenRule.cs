using System;

namespace Prowl.Crumb;

/// <summary>
/// A structural token rule. Invoked when no fixed token matched at the current
/// position. The rule should either consume characters and produce a token
/// (returning <c>true</c>), or leave the tokenizer untouched and return <c>false</c>.
/// </summary>
public delegate bool TokenRule<TKind>(ref Tokenizer<TKind> tokenizer, out Token<TKind> token)
    where TKind : unmanaged, Enum;

/// <summary>
/// A predicate over a single character. Used for whitespace, identifier starts,
/// and identifier continuations. Kept as a concrete delegate to avoid interface
/// dispatch on the hot path.
/// </summary>
public delegate bool CharPredicate(char c);
