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
   * [Markdown Rendering](#markdown-rendering)
4. [Contributing](#-contributing-)
5. [License](#-license-)

# <span align="center">📝 About The Project 📝</span>

Scribe is an open-source, **[MIT-licensed](LICENSE)** TrueType font parser and rasterizer for .NET. It powers text rendering in the Prowl game engine but is designed to work in any environment that can provide an `IFontRenderer`.

# <span align="center">✨ Features ✨</span>

- TrueType font parsing and glyph rasterization
- Font Families
- Dynamic atlas packing with optional expansion
- Flexible text layout engine with cursor hit testing
- Lightweight Markdown parser and layout engine
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

## Markdown Rendering

```csharp
var imageProvider = new MyImageProvider(); // implements IMarkdownImageProvider

// Get the fonts you would like the Markdown rendering to use
var font = fs.GetFont("Arial", FontStyle.Regular);
var mono = fs.GetFont("Consola", FontStyle.Regular);
var bold = fs.GetFont("Arial", FontStyle.Bold);
var italic = fs.GetFont("Arial", FontStyle.Italic);
var boldItalic = fs.GetFont("Arial", FontStyle.BoldItalic);

// Create the markdown Settings
var settings = MarkdownLayoutSettings.Default(font, mono, bold, italic, boldItalic, width: 400);

// Initialize the Markdown Layout Engine
var engine = new MarkdownLayoutEngine(scribe, renderer, settings, imageProvider);

// Parse your Markdown
var document = Markdown.Parse(YourMarkdown);

// Pass your Parsed Markdown into the Engine to calculate its Layout at the specified location
var markdownLayout = engine.Layout(document, position);

// Draw your Markdown
engine.Render(markdownLayout);

// You can also check if the mouse cursor is hovering over a Clickable Link!
bool isMouseOverLink = engine.TryGetLinkAt(markdownLayout, mouse, out var href);
```

<img width="736" height="1106" alt="MarkdownShowcase" src="https://github.com/user-attachments/assets/c4ef6229-ef1c-41a2-bbf9-51a806ff4252" />

# <span align="center">🤝 Contributing 🤝</span>

Contributions, issues and feature requests are welcome! Feel free to fork this repository and submit a pull request.

# <span align="center">📄 License 📄</span>

Distributed under the MIT License. See [LICENSE](LICENSE) for details.
