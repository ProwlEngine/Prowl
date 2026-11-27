<img src="Samples/RaylibTest/ProwlLogo.png" width="100%" alt="Scribe logo image">

![Github top languages](https://img.shields.io/github/languages/top/prowlengine/prowl.scribe)
[![GitHub version](https://img.shields.io/github/v/release/prowlengine/prowl.scribe?include_prereleases&style=flat-square)](https://github.com/prowlengine/prowl.scribe/releases)
[![GitHub license](https://img.shields.io/github/license/prowlengine/prowl.scribe?style=flat-square)](LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/prowlengine/prowl.scribe?style=flat-square)](https://github.com/prowlengine/prowl.scribe/issues)
[![GitHub stars](https://img.shields.io/github/stars/prowlengine/prowl.scribe?style=flat-square)](https://github.com/prowlengine/prowl.scribe/stargazers)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord)](https://discord.gg/BqnJ9Rn4sn)

> [!IMPORTANT]
> Scribe is under active development. APIs may change and it hasn't yet proven itself in production.

### [<p align="center">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn)
# <p align="center">A Fast & Flexible Font System for .NET</p>
## <p align="center">📚 Documentation: Coming Soon 📚</p>

<span id="readme-top"></span>

### <p align="center">Table of Contents</p>
1. [About The Project](#-about-the-project-)
2. [Features](#-features-)
3. [Getting Started](#-getting-started-)
   * [Installation](#installation)
   * [Basic Usage](#basic-usage)
   * [Customizing Glyphs](#customizing-glyphs)
4. [Contributing](#-contributing-)
5. [License](#-license-)

# <span align="center">📝 About The Project 📝</span>

Scribe is an open-source, **[MIT-licensed](LICENSE)** TrueType font parser and rasterizer for .NET. It powers text rendering in the Prowl game engine but is designed to work in any environment that can provide an `IFontRenderer`.

# <span align="center">✨ Features ✨</span>

- TrueType font parsing and glyph rasterization
- Font Families
- Dynamic atlas packing with optional expansion
- Flexible text layout engine with cursor hit testing
- Fonts, sizes, spacing, decoration and even substitution for each character, with kerning and wrapping intact
- Draw hooks on every glyph for animated text
- Pluggable rendering backend through `IFontRenderer`
- Optional layout caching with LRU eviction
- TTF Loader and Rasterizer Based Upon STBTrueType
 - Unicode codepoint mapping, full glyph metrics (advance, bearings, bounds, vertical metrics) and kerning pairs
 - Extracts glyph outlines and rasterizes to 8-bit alpha bitmaps

# <span align="center">🚀 Getting Started 🚀</span>

## Installation

Add the package via NuGet:

```bash
dotnet add package Prowl.Scribe
```

## Basic Usage

```csharp
var renderer = new MyFontRenderer(); // implements IFontRenderer
var scribe = new FontSystem(renderer);

// Load fonts, Will load all System Fonts with the passed FontFamilies as Priority
scribe.LoadSystemFonts("Arial");
// or: var font = scribe.AddFont(File.ReadAllBytes("path/to/font.ttf"));
// If you use SystemFonts its recommended to add priorities also for different platforms
// Not all operating systems have the same fonts. This is the priority list used in the Samples:
// "Segoe UI", "Arial", "Liberation Sans", "Consola", "Menlo", "Liberation Mono"

// Draw text will attempt to use preferredFont if available
var preferredFont = scribe.GetFont(fontFamily, FontStyle.Bold);
scribe.DrawText("Hello World!", position, FontColor.Blue, pixelSize, preferredFont)
```

## Customizing Glyphs

A `GlyphCustomizer` runs once per character while text is laid out. Whatever it
sets is what Scribe shapes, kerns, measures and wraps with, so a bold or larger
run is part of the layout rather than stretched afterwards.

```csharp
var settings = TextLayoutSettings.Default;
settings.Font = font;
settings.Customizer = (ref GlyphStyle g) =>
{
    if (g.CharIndex < 5) g.Font = bold;
    g.Underline = g.CharIndex >= 6;
    // g.PixelSize, g.LetterSpacing, g.WordSpacing and g.Codepoint can all change too.
};

var layout = new TextLayout();
scribe.UpdateLayout(layout, "Hello world", settings);
```

A `GlyphModifier` runs once per glyph while a layout is drawn, and can move each
of its four corners, recolour it or hide it. The layout itself is untouched, so
animating text costs nothing but the modifier.

```csharp
scribe.DrawLayout(layout, position, FontColor.White, (ref GlyphDraw g) =>
{
    if (g.IsDecoration) return;
    float lift = MathF.Sin(time + g.CharIndex) * 2f;
    g.SetCorners(g.TopLeft + new Float2(0, lift), g.TopRight + new Float2(0, lift),
                 g.BottomLeft + new Float2(0, lift), g.BottomRight + new Float2(0, lift));
});
```

# <span align="center">🤝 Contributing 🤝</span>

Contributions, issues and feature requests are welcome! Feel free to fork this repository and submit a pull request.

# <span align="center">📄 License 📄</span>

Distributed under the MIT License. See [LICENSE](LICENSE) for details.
