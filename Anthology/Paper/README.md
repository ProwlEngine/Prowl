<img src="https://i.imgur.com/wtDdydO.png" width="100%" alt="Paper logo image">

![Github top languages](https://img.shields.io/github/languages/top/prowlengine/prowl.paper)
[![GitHub version](https://img.shields.io/github/v/release/prowlengine/prowl.paper?include_prereleases&style=flat-square)](https://github.com/prowlengine/prowl.paper/releases)
[![GitHub license](https://img.shields.io/github/license/prowlengine/prowl.paper?style=flat-square)](https://github.com/prowlengine/prowl.paper/blob/main/LICENSE.txt)
[![GitHub issues](https://img.shields.io/github/issues/prowlengine/prowl.paper?style=flat-square)](https://github.com/prowlengine/prowl.paper/issues)
[![GitHub stars](https://img.shields.io/github/stars/prowlengine/prowl.paper?style=flat-square)](https://github.com/prowlengine/prowl.paper/stargazers)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)

# <p align="center">An Immediate-Mode UI Library</p>

<span id="readme-top"></span>

1. [About The Project](#-about-the-project-)
2. [Features](#-features-)
3. [Getting Started](#🚀-getting-started-🚀)
   * [Installation](#installation)
   * [Basic Usage](#basic-usage)
   * [Layouting](#layouting)
   * [Styling and Animation](#styling-and-animation)
   * [Event Handling](#event-handling)
5. [Contributing](#🤝-contributing-🤝)
6. [Contributors](#contributors-🌟)
7. [Dependencies](#dependencies-📦)
8. [License](#📜-license-📜)

# <span align="center">📝 About The Project 📝

Paper is an open-source, **[MIT-licensed](#span-aligncenter-license-span)** immediate-mode UI library built for the Prowl Game Engine. It provides a lightweight, flexible way to create highly reactive user interfaces with minimal effort.

### [<p align="center">Join our Discord server! 🎉</p>](https://discord.gg/BqnJ9Rn4sn)

Paper follows the immediate-mode GUI (IMGUI) paradigm, where UI elements are created and configured on every frame. Unlike retained-mode systems, it doesn't maintain persistent widget objects, making code simpler and more direct.
Here's a basic example:
```cs
// Create a simple button
using (Paper.Box("MyButton")
    .Size(100)
    .BackgroundColor(Color.ForestGreen)
    .Rounded(8)
    .Text(Text.Center("Click Me", myFont, Color.White))
    .OnClick((rect) => Console.WriteLine("Button clicked!"))
    .Enter())
{
    // Child elements would go here
}
```
Which produces this button you can click on:

<img src="https://i.imgur.com/4R3meKS.png" width="50%">

# <span align="center">✨ Features ✨</span>

-   **General:**
    - Cross-Platform! Windows, Linux & Mac!
    - Immediate Mode Architecture
    - 100% C#!
    - Highly Portable
        - Very easy to integrate
        - OpenTK Example
        - Raylib Example (Less than 500 loc including shaders!)
        - With more to come!
            - DirectX, Web, Unity
    - Fluent API
    - Flexible Layout System
        - Rows, Columns & Custom Positioning
        - Pixel, Percentage, Stretch or Auto for Positioning and Sizing
    - A Powerful Built-in Animation System
        - Many built-in Easing functions
        - Easily provide your own Easing functions
        - Automatic transitions for each individual Property of any node at any time
    - Rich Event Handling
        - Comprehensive Mouse & Keyboard support
        - Parent-child Event Bubbling
        - OnClick, OnDragStart, OnHover and many more
    - Transformations
        - Scale, Rotate, Translate & Skew any Element
    - Vector Graphics
        - Highly Performant
        - Hardware accelerated
        - Anti-Aliased
        - Scissoring
        - MoveTo, LineTo, CurveTo, Fill, Stroke, etc
        - Draw custom shapes at any time any where

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">🚀 Getting Started 🚀</span>

## Installation
```
dotnet add package Prowl.Paper
```

## Initialization
You will need a Renderer, Theres examples in the repository under Samples for OpenTK and Raylib, with more to come.
```cs
Paper.Initialize(yourRenderer, screenWidth, screenHeight);

// Load a font
var fontSystem = new FontSystem();
fontSystem.AddFont(File.ReadAllBytes("path/to/font.ttf"));
var myFont = fontSystem.GetFont(24);
```

## Basic Usage
Paper is very easy to use, Simply call BeginFrame, Do your UI then call EndFrame
```cs
// In your main loop:
void RenderUI()
{
    // Begin the UI frame
    Paper.BeginFrame(deltaTime);
    
    // Define your UI
    using (Paper.Column("MainContainer")
        .BackgroundColor(240, 240, 240)
        .Enter())
    {
        // A header
        using (Paper.Box("Header")
            .Height(60)
            .BackgroundColor(50, 120, 200)
            .Text(Text.Center("My Application", myFont, Color.White))
            .Enter()) { }
            
        // Content area
        using (Paper.Row("Content").Enter())
        {
            // Sidebar
            Paper.Box("Sidebar")
                .Width(200)
                .BackgroundColor(220, 220, 220);
                
            // Main content
            Paper.Box("MainContent");
        }
    }
    
    // End the UI frame
    Paper.EndFrame();
}
```
That should result in the following UI:

<img src="https://i.imgur.com/aWIu1kN.png" width="100%">


## Layouting

Paper provides a very powerful layouting engine based on the Morphorm library.

```cs
// Row container (horizontal layout)
using (Paper.Row("MyRow")
    .Enter())
{
    // Children will be arranged horizontally
}

// Column container (vertical layout)
using (Paper.Column("MyColumn")
    .Enter())
{
    // Children will be arranged vertically
}

// Custom positioning
using (Paper.Box("CustomPositionedElement")
    .PositionType(PositionType.SelfDirected) // Absolute positioning
    .Left(100)
    .Top(50)
    .Enter())
{
    // This element is positioned exactly at (100, 50)
}
```

## Styling and Animation
Paper provides a very powerful yet simple way to both draw and animate your UI
```cs
// Basic styling
using (Paper.Box("StyledElement")
    .BackgroundColor(Color.Blue)
    .BorderColor(Color.White)
    .BorderWidth(2)
    .Rounded(8)
    .Enter()) { }

// State-based styling
using (Paper.Box("InteractiveElement")
    .BackgroundColor(Color.Gray)
    .Hovered
        .BackgroundColor(Color.LightGray)
        .End()
    .Active
        .BackgroundColor(Color.DarkGray)
        .End()
    .Enter()) { }

// Transitions/animations
using (Paper.Box("AnimatedElement")
    .BackgroundColor(Color.Red)
    .Hovered
        .BackgroundColor(Color.Green)
        .End()
    .Transition(GuiProp.BackgroundColor, 0.3, Paper.Easing.SineInOut)
    .Enter()) { }

// Various built-in easing functions
Paper.Easing.Linear
Paper.Easing.EaseIn
Paper.Easing.EaseOut
Paper.Easing.EaseInOut
Paper.Easing.CubicIn
Paper.Easing.ElasticOut
// ... and many more
```

## Event Handling
Events will bubble upwards through their parents.
So hovering a child will also call OnHover for the parent.
```cs
Paper.Box("InteractiveElement")
    .OnClick((rect) => HandleClick())
    .OnHover((rect) => ShowTooltip())
    .OnEnter((rect) => PlayHoverSound())
    .OnLeave((rect) => HideTooltip())
    .OnDragStart((rect) => StartDragging())
    .OnDragging((start, rect) => UpdateDragPosition(start))
    .OnDragEnd((start, total, rect) => FinishDragging())
    .OnScroll((delta, rect) => Scroll(delta))
```

## So much more!
Theres so much more I couldn't possibly fit it all into this Readme file.
A more complete documentation & tutorial will be coming in the near future!

# <span align="center">🤝 Contributing 🤝</span>

Check our [Contributing guide](//CONTRIBUTING.md) to see how to be part of this team.

<p align="right">(<a href="#readme-top">back to top</a>)</p>


## Contributors 🌟

<a href="https://github.com/prowlengine/prowl.paper/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=prowlengine/prowl.paper" alt="contrib.rocks image" />
</a>


## Dependencies 📦

- [Prowl.Quill](https://github.com/ProwlEngine/Prowl.Quill)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

# <span align="center">📜 License 📜</span>

Distributed under the MIT License. See [LICENSE](//LICENSE) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

### [Join our Discord server! 🎉](https://discord.gg/BqnJ9Rn4sn)
[![Discord](https://img.shields.io/discord/1151582593519722668?logo=discord
)](https://discord.gg/BqnJ9Rn4sn)

