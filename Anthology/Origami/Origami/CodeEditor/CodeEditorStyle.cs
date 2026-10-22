// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>Font style of a token kind.</summary>
[Flags]
public enum CodeTokenStyle : byte
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Underline = 4,
}

/// <summary>Which keys accept the selected completion.</summary>
public enum CompletionAcceptKeys { TabAndEnter, Tab, Enter }

/// <summary>How the caret is drawn.</summary>
public enum CaretStyle { Line, Block, Underline }

/// <summary>The key held while clicking to add a caret. The other of the two goes to the definition under the pointer.</summary>
public enum MultiCaretModifier { Alt, Ctrl }

/// <summary>Behaviour and layout switches for a <see cref="CodeEditor"/>. Safe to change at any time.</summary>
public sealed class CodeEditorOptions
{
    /// <summary>Monospace font. Null uses the theme's mono font.</summary>
    public FontFile? Font;

    /// <summary>Faces for bold, italic and bold italic tokens. A missing face falls back to the closest one there is.</summary>
    public FontFile? BoldFont;
    public FontFile? ItalicFont;
    public FontFile? BoldItalicFont;
    public float FontSize = 15f;
    public float MinFontSize = 6f;
    public float MaxFontSize = 48f;

    /// <summary>Row height as a multiple of the font size.</summary>
    public float LineHeight = 1.5f;

    public int TabSize = 4;

    /// <summary>Use the font's ligatures and contextual alternates, such as joined arrows in coding fonts.</summary>
    public bool Ligatures = true;

    /// <summary>Tab key and auto indent insert spaces instead of tab characters.</summary>
    public bool InsertSpaces = true;

    public bool ReadOnly;
    public bool ShowLineNumbers = true;
    public bool ShowFolding = true;

    /// <summary>Strip left of the line numbers for breakpoints and markers. See <see cref="CodeEditor.GlyphMarginClicked"/>.</summary>
    public bool ShowGlyphMargin = true;
    public bool ShowMinimap = true;
    public bool ShowIndentGuides = true;
    public bool ShowWhitespace;
    public bool ShowStatusBar = true;
    public bool HighlightCurrentLine = true;
    public bool HighlightMatchingBrackets = true;

    /// <summary>Shows notes such as reference counts after declarations, when the language provides them.</summary>
    public bool ShowCodeLens = true;

    /// <summary>Marks other occurrences of the word under the caret.</summary>
    public bool HighlightOccurrences = true;
    public bool AutoClosePairs = true;
    public bool AutoIndent = true;

    /// <summary>Formats the statement or block a typed ';' or '}' closes, for languages that support it.</summary>
    public bool FormatOnType = true;
    public bool ScrollBeyondLastLine = true;
    public bool SmoothScrolling = true;
    public bool CaretBlink = true;

    /// <summary>Opens completion as soon as a word is typed, not only on '.' and Ctrl+Space.</summary>
    public bool CompletionOnTyping = true;

    /// <summary>Hover shows info and diagnostics after resting this long, in seconds. Zero or less disables it.</summary>
    public float HoverDelay = 0.5f;

    /// <summary>Seconds without edits before diagnostics and semantic colours are refreshed.</summary>
    public float AnalysisDelay = 0.35f;

    /// <summary>Lines kept visible above and below the caret when it moves.</summary>
    public int ScrollMargin = 3;

    public float MinimapWidth = 90f;

    /// <summary>Columns with a vertical ruler, such as 120.</summary>
    public int[] Rulers = Array.Empty<int>();

    public CaretStyle CaretStyle = CaretStyle.Line;

    /// <summary>Width of the line caret in pixels.</summary>
    public float CaretWidth = 2f;

    public MultiCaretModifier MultiCaretModifier = MultiCaretModifier.Alt;

    /// <summary>Selected text can be dragged to a new place, or copied there with Ctrl held.</summary>
    public bool DragAndDropText = true;

    /// <summary>Marks spaces and tabs at the end of lines.</summary>
    public bool ShowTrailingWhitespace;

    /// <summary>Colours brackets by how deeply they are nested.</summary>
    public bool BracketPairColors;

    /// <summary>Colours indent guides by their level.</summary>
    public bool RainbowIndentGuides;

    /// <summary>Shows the parameter list while typing a call. Ctrl+Shift+Space still opens it when off.</summary>
    public bool ParameterHints = true;

    public CompletionAcceptKeys AcceptCompletionWith = CompletionAcceptKeys.TabAndEnter;

    /// <summary>Characters that accept the selected completion and are then typed. Null uses each suggestion's own, empty turns them off.</summary>
    public string? CompletionCommitCharacters;

    /// <summary>Puts suggestions accepted recently first, in every editor.</summary>
    public bool SortCompletionsByRecentUse;
}

/// <summary>Colours of a <see cref="CodeEditor"/>. <see cref="Token"/> is indexed by <see cref="CodeTokenKind"/>.</summary>
public sealed class CodeEditorColors
{
    public Color32 Background;
    public Color32 GutterBackground;
    public Color32 LineNumber;
    public Color32 LineNumberActive;
    public Color32 CurrentLine;
    public Color32 Selection;
    public Color32 SelectionInactive;
    public Color32 Caret;
    public Color32 IndentGuide;
    public Color32 IndentGuideActive;
    public Color32 Ruler;
    public Color32 Whitespace;
    public Color32 BracketMatch;
    public Color32 OccurrenceHighlight;
    public Color32 FindMatch;
    public Color32 FindMatchCurrent;
    public Color32 Error;
    public Color32 Warning;
    public Color32 Info;
    public Color32 FoldPlaceholder;
    public Color32 FoldMarker;
    public Color32 Popup;
    public Color32 PopupBorder;
    public Color32 PopupSelection;
    public Color32 PopupText;
    public Color32 PopupTextDim;
    public Color32 PopupMatch;
    public Color32 MinimapSlider;
    public Color32 Scrollbar;
    public Color32 ScrollbarHover;
    public Color32 StatusBar;
    public Color32 StatusText;
    public Color32 Breakpoint;
    public Color32 TrailingWhitespace;

    /// <summary>Bracket colours by nesting depth, cycled.</summary>
    public Color32[] BracketPairs = Array.Empty<Color32>();

    /// <summary>Indent guide colours by level, cycled.</summary>
    public Color32[] RainbowGuides = Array.Empty<Color32>();

    public readonly Color32[] Token = new Color32[Enum.GetValues<CodeTokenKind>().Length];

    /// <summary>Font style per token kind, indexed by <see cref="CodeTokenKind"/>.</summary>
    public readonly CodeTokenStyle[] Styles = new CodeTokenStyle[Enum.GetValues<CodeTokenKind>().Length];

    public Color32 this[CodeTokenKind kind]
    {
        get => Token[(int)kind];
        set => Token[(int)kind] = value;
    }

    private static Color32 Hex(uint rgb, byte a = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);

    private static Color32 Lighten(Color32 c, int amount)
        => new((byte)Math.Min(255, c.R + amount), (byte)Math.Min(255, c.G + amount), (byte)Math.Min(255, c.B + amount), c.A);

    private static Color32 FromTheme(System.Drawing.Color c, byte? a = null) => new(c.R, c.G, c.B, a ?? c.A);

    /// <summary>A dark palette that takes its surfaces and accents from an Origami theme.</summary>
    public static CodeEditorColors FromTheme(OrigamiTheme theme)
    {
        var c = CreateDark();
        c.Background = FromTheme(theme.Neutral.C100, 255);
        c.GutterBackground = c.Background;
        c.Caret = FromTheme(theme.Primary.C700);
        c.Selection = FromTheme(theme.Primary.C500, 80);
        c.SelectionInactive = FromTheme(theme.Primary.C500, 40);
        c.LineNumber = FromTheme(theme.Ink.C200);
        c.LineNumberActive = FromTheme(theme.Ink.C500);
        c.Popup = FromTheme(theme.Popover, 252);
        c.PopupBorder = FromTheme(theme.BorderStrong);
        c.PopupSelection = FromTheme(theme.Primary.C500, 70);
        c.PopupText = FromTheme(theme.Ink.C500);
        c.PopupTextDim = FromTheme(theme.Ink.C300);
        c.PopupMatch = FromTheme(theme.Primary.C700);
        c.StatusBar = Lighten(c.Background, 8);
        c.StatusText = FromTheme(theme.Ink.C300);
        return c;
    }

    public static CodeEditorColors CreateDark()
    {
        var c = new CodeEditorColors
        {
            Background = Hex(0x14111C),
            GutterBackground = Hex(0x14111C),
            LineNumber = Hex(0x5C566E),
            LineNumberActive = Hex(0xCFC8E6),
            CurrentLine = Hex(0xFFFFFF, 10),
            Selection = Hex(0x8A5CF6, 80),
            SelectionInactive = Hex(0x8A5CF6, 40),
            Caret = Hex(0xD9C7FF),
            IndentGuide = Hex(0xFFFFFF, 18),
            IndentGuideActive = Hex(0xFFFFFF, 50),
            Ruler = Hex(0xFFFFFF, 16),
            Whitespace = Hex(0xFFFFFF, 40),
            BracketMatch = Hex(0xB98AFF, 70),
            OccurrenceHighlight = Hex(0xFFFFFF, 26),
            FindMatch = Hex(0xF2B482, 70),
            FindMatchCurrent = Hex(0xF2B482, 150),
            Error = Hex(0xFF5370),
            Warning = Hex(0xFFCB6B),
            Info = Hex(0x82AAFF),
            FoldPlaceholder = Hex(0xFFFFFF, 26),
            FoldMarker = Hex(0x8F88A6),
            Popup = Hex(0x1D1928, 252),
            PopupBorder = Hex(0x3B3450),
            PopupSelection = Hex(0x8A5CF6, 70),
            PopupText = Hex(0xE4E0F0),
            PopupTextDim = Hex(0x8F88A6),
            PopupMatch = Hex(0xC9A8FF),
            MinimapSlider = Hex(0xFFFFFF, 22),
            Scrollbar = Hex(0xFFFFFF, 40),
            ScrollbarHover = Hex(0xFFFFFF, 80),
            StatusBar = Hex(0x1A1624),
            StatusText = Hex(0x8F88A6),
            Breakpoint = Hex(0xE5484D),
            TrailingWhitespace = Hex(0xFF5370, 45),
            BracketPairs = [Hex(0xFFD479), Hex(0xDA70D6), Hex(0x4FB3FF)],
            RainbowGuides = [Hex(0xFFD479, 60), Hex(0x9CC9FF, 60), Hex(0xB98AFF, 60), Hex(0x7EE8A7, 60), Hex(0xFF7AC6, 60), Hex(0xF2B482, 60)],
        };

        c[CodeTokenKind.Plain] = Hex(0xD6D2E4);
        c[CodeTokenKind.Identifier] = Hex(0xD6D2E4);
        c[CodeTokenKind.Keyword] = Hex(0xB98AFF);
        c[CodeTokenKind.ControlKeyword] = Hex(0xFF7AC6);
        c[CodeTokenKind.Preprocessor] = Hex(0x9A94AE);
        c[CodeTokenKind.PreprocessorText] = Hex(0x9A94AE);
        c[CodeTokenKind.Comment] = Hex(0x6B6684);
        c[CodeTokenKind.DocComment] = Hex(0x6E8A74);
        c[CodeTokenKind.DocCommentTag] = Hex(0x56705C);
        c[CodeTokenKind.String] = Hex(0xF2B482);
        c[CodeTokenKind.StringEscape] = Hex(0xFFD580);
        c[CodeTokenKind.Char] = Hex(0xF2B482);
        c[CodeTokenKind.Number] = Hex(0x8FE3C8);
        c[CodeTokenKind.Operator] = Hex(0xC9C5D8);
        c[CodeTokenKind.Punctuation] = Hex(0xA7A2BA);
        c[CodeTokenKind.Namespace] = Hex(0xD6D2E4);
        c[CodeTokenKind.Class] = Hex(0x5EE6D0);
        c[CodeTokenKind.Record] = Hex(0x5EE6D0);
        c[CodeTokenKind.Delegate] = Hex(0x5EE6D0);
        c[CodeTokenKind.Struct] = Hex(0x7EE8A7);
        c[CodeTokenKind.Interface] = Hex(0xB5E58C);
        c[CodeTokenKind.Enum] = Hex(0x9FE0B0);
        c[CodeTokenKind.TypeParameter] = Hex(0x8ED6C8);
        c[CodeTokenKind.Method] = Hex(0xFFD479);
        c[CodeTokenKind.ExtensionMethod] = Hex(0xFFD479);
        c[CodeTokenKind.Property] = Hex(0x9CC9FF);
        c[CodeTokenKind.Field] = Hex(0x9CC9FF);
        c[CodeTokenKind.Event] = Hex(0xFFB86C);
        c[CodeTokenKind.EnumMember] = Hex(0xC0A9FF);
        c[CodeTokenKind.Constant] = Hex(0xC0A9FF);
        c[CodeTokenKind.Local] = Hex(0xBFE0FF);
        c[CodeTokenKind.Parameter] = Hex(0xBFE0FF);
        c[CodeTokenKind.Label] = Hex(0xD6D2E4);
        c[CodeTokenKind.ExcludedCode] = Hex(0x5A566B);
        return c;
    }
}
