# Text and Input Fields

## Static text

```csharp
.Text(string text, FontFile font)
.Alignment(TextAlignment mode)   // Left/Center/Right, Middle{Left,Center,Right}, Bottom{Left,Center,Right}
.Wrap(TextWrapMode mode)         // NoWrap, Wrap
.TextTruncate(bool truncate = true) // single-line "..." ellipsis when wider than the element
.WordSpacing(...) .LetterSpacing(...) .LineHeight(...) .TabSize(int) .FontSize(float)
.TextColor(Color)
.TextQuality(FontQuality quality) // Low(16)/Normal(32, default)/High(64)/Ultra(128) — atlas rasterization resolution
.IsPassword(char mask = '*')     // draw every character as the mask, even on a rich text element, while the element keeps the real text
```

Text content-sizes an `Auto`-dimensioned element automatically (no
`ContentSizer` needed) — see [Layout Engine](layout-engine.md). Text layout is
memoized per frame (and cached across frames keyed by a content+metrics
fingerprint) so re-measuring unchanged text is cheap even though the layout
solver may query it multiple times per frame during stretch resolution.

## Rich text

```csharp
.Text("A <b>bold</> word and some <wave 2 8 #f80>waving text</>", font)
.RichText(FontFile bold = null, FontFile italic = null, FontFile boldItalic = null, FontFile mono = null)
```

`RichText` is a toggle on an ordinary text element. The text is still laid out
by Scribe, so kerning, wrapping, alignment and decorations behave exactly as
they do for plain text. The tags only decide how each glyph is styled and how
it moves.

Tags are `<name args>text</>`, and `</>` closes the most recent one. A named
close such as `</b>` also works.

| Tag | Does |
|-----|------|
| `b` `i` `u` `s` `mono` | bold, italic, underline, strikethrough, monospace |
| `size 1.5` | scales the run, and the line grows to fit it |
| `#f80` `#ff8800cc` `red` | colour |
| `link url` | a link run |

Effects each take an optional strength, speed and `#colour`, in that order:
`shake`, `wiggle`, `wave`, `sizewave`, `bounce`, `slide`, `dangle`,
`pendulum`, `swing`, `rotate`, `pulse`, `rainbow`. They are pure functions of
time, so nothing about them is stored and nothing needs resetting.

A style whose face is not supplied falls back to the regular font. Write `\<`
for a literal `<`.

## Markdown

```csharp
var markdown = new MarkdownBuilder(font)
    .Fonts(bold, italic, boldItalic, mono)
    .FontSize(16)
    .OnLink(href => OpenUrl(href))
    .Images(src => LoadTexture(src))
    .Source(readme);

// each frame, wherever it belongs
markdown.Build(paper, "readme");
```

`MarkdownBuilder` turns Markdown into ordinary elements: headings and
paragraphs are rich text elements, lists are rows and columns, tables are rows
of cells, and code blocks are clipped boxes. Because they are real elements,
they lay out, scroll and clip like anything else, and `Build` returns the
containing column so it can be styled further.

Keep the builder and call `Build` every frame. `Source` only parses again when
the text actually changes, and every string a block needs is made once per
parse. Links are clickable where they are drawn and show the pointer cursor when
hovered, and an image on a line of its
own is drawn at its natural size, no wider than the document. Parsing is done
by [Prowl.Quire](../../Quire).

## Text input controls

```csharp
ElementBuilder TextField(string value, TextInputSettings settings, Action<string> onChange = null, [CallerLineNumber] int intID = 0)
ElementBuilder TextField(string value, FontFile font, Action<string> onChange = null, Color? textColor = null, string placeholder = "", Color? placeholderColor = null, ...)
ElementBuilder TextArea(string value, TextInputSettings settings, Action<string> onChange = null, ...)   // multi-line, with scrolling
ElementBuilder TextArea(string value, FontFile font, Action<string> onChange = null, string placeholder = "", ...)
```

Both build a fully-featured editable field on the element: click-to-position
cursor, Shift+click to extend selection, double-click to select the word under
the cursor, drag-select (with edge auto-scroll), full keyboard editing (arrows
w/ Ctrl for word-jump and Shift for selection, Home/End, Backspace/Delete,
Ctrl+A/C/X/V, Tab inserts a literal tab in multi-line fields, Enter inserts a
newline in multi-line fields), a blinking caret, and selection-highlight
rendering. Internally these are ordinary `ElementBuilder` calls (`.Clip()`,
`OnPress`/`OnDragStart`/`OnDragging`/`OnKeyPressed`/`OnTextInput`/`OnFocusChange`,
`.OnPostLayout` + `Paper.Draw`) — nothing here is special-cased outside the
public API.

### `TextInputSettings`

```csharp
public struct TextInputSettings
{
    FontFile Font;
    Color TextColor;
    string Placeholder;
    Color PlaceholderColor;
    bool ReadOnly;
    int MaxLength;               // 0 = no limit
    bool DoWrap;                 // multi-line only
    Func<char, string, bool> CharFilter; // (char, currentValue) -> accept?
    bool SelectAllOnFocus;
    char? MaskChar;               // password-style masking
    string ForceValue;            // programmatic override, see below
    bool ForceSelectAll;          // select-all when ForceValue lands on a focused field

    public static TextInputSettings Default => ...;
}
```

### Value synchronization model

Because this is immediate mode, the field's editable state (cursor, selection,
scroll offset, current text) lives in per-element storage (see
[State and Storage](state-and-storage.md)) rather than in the `value` you pass
in — that parameter is only the *external* value your own code holds. Each
frame the field reconciles the two:

- **`ForceValue` set** → applied unconditionally, replacing internal state
  regardless of focus (optionally selecting all, if `ForceSelectAll` and the
  field is focused). Use this for explicit external pushes — autocomplete picks,
  undo/redo, code-side rewrites.
- **Not focused** → the external `value` is authoritative; internal state syncs
  to match if it diverged (so gizmos/undo/code writes propagate in normally).
- **Focused, no `ForceValue`** → internal state wins; the external value is
  ignored for that frame. This matters because a caller's setter chain often
  round-trips the value through filters/formatters that can strip in-progress
  characters (e.g. a numeric field's formatter stripping a trailing "." from
  "0." before the decimal point is typed) — without this rule, every keystroke
  would risk a spurious external-value overwrite mid-edit.

### Password / masked fields

Setting `MaskChar` replaces every non-newline character with that glyph for
display and hit-testing purposes only — the underlying value stays real for
`onChange`/cursor math. Clipboard copy and cut (Ctrl+C/Ctrl+X) are suppressed
entirely while a mask is set, so masked content can't be exfiltrated through the
clipboard.

### Character filtering

`CharFilter(char, currentValue) -> bool` is checked both for typed characters
and for each character of pasted clipboard text (paste additionally truncates to
whatever fits under `MaxLength`).
