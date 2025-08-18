# Prowl.Quill
A simple but high-performance hardware-accelerated vector graphics library for the Prowl Game Engine ecosystem. Quill provides a lightweight yet powerful API for creating beautiful anti-aliased 2D graphics with minimal overhead.

![image](https://github.com/user-attachments/assets/31133a2c-d880-476e-b024-a1a054f9da8c)


## Features

- **High Performance**
  - Anti-aliased graphics
  - Efficient batch rendering
  - Minimal overhead with less than 1K lines of executable code
  - Built for speed without sacrificing quality

- **Drawing Operations**
  - Path-based drawing with automatic tessellation
  - Shape primitives (rectangles, circles, pies, rounded rectangles)
  - Curves (arcs, quadratic and cubic Bézier curves)
  - Polyline
	- Caps: Round, Bevel, Butt, Square
	- Joins: Miter, Round, Bevel
  - Filled and stroked rendering modes
  - Gradients
  - Transparency support
  - Font/Text Rendering
  - Concave Fill shapes

- **Canvas Controls**
  - State stack for save/restore operations
  - Transform management (translate, rotate, scale & skew)
  - Stroke styles (width, color, caps, joins, etc)
  - Fill styles (color, etc)
  - Scissor regions for clipping (Supports Transformation)
  - Texture Support

- **Portability**
  - OpenTK Backend
  - Raylib Backend
  - With more planned!

## Usage

### Basic Shapes

```csharp
// Create a canvas
Canvas canvas = new Canvas();

// Draw a filled rectangle
canvas.RectFilled(10, 10, 100, 50, Color.FromArgb(200, 255, 100, 100));

// Draw a circle with outline
canvas.SetStrokeColor(Color.FromArgb(255, 255, 255, 255));
canvas.SetStrokeWidth(2.0);
canvas.Circle(200, 100, 40);
canvas.Stroke();

// Draw a rounded rectangle with fill
canvas.SetFillColor(Color.FromArgb(200, 100, 255, 150));
canvas.RoundedRect(300, 100, 120, 60, 15, 15, 15, 15);
canvas.Fill();
```

### Path-Based Drawing

```csharp
// Start a new path
canvas.BeginPath();

// Draw a complex shape
canvas.MoveTo(50, 50);
canvas.LineTo(150, 50);
canvas.LineTo(100, 150);
canvas.ClosePath();

// Fill and stroke
canvas.SetFillColor(Color.FromArgb(180, 100, 100, 255));
canvas.SetStrokeColor(Color.FromArgb(255, 255, 255, 255));
canvas.SetStrokeWidth(3.0);
canvas.FillAndStroke();
```

### Curves

```csharp
// Bezier curve
canvas.BeginPath();
canvas.MoveTo(50, 200);
canvas.BezierCurveTo(100, 100, 200, 300, 250, 200);
canvas.SetStrokeColor(Color.FromArgb(255, 100, 200, 255));
canvas.Stroke();

// Arc
canvas.BeginPath();
canvas.Arc(150, 150, 50, 0, Math.PI);
canvas.SetStrokeColor(Color.FromArgb(255, 255, 255, 100));
canvas.Stroke();
```

### Transformations

```csharp
// Save current state
canvas.SaveState();

// Apply transformations
canvas.TransformBy(Transform2D.CreateTranslation(200, 200));
canvas.TransformBy(Transform2D.CreateRotate(45.0 * Math.PI / 180.0));
canvas.TransformBy(Transform2D.CreateScale(0.8, 0.8));

// Draw a rectangle at the transformed position
canvas.RectFilled(-40, -40, 80, 80, Color.FromArgb(200, 255, 150, 100));

// Restore previous state
canvas.RestoreState();
```

### Line Styles

```csharp
// Set line join style
canvas.SetStrokeJoint(JointStyle.Round);
canvas.SetStrokeWidth(8.0);
canvas.SetStrokeColor(Color.FromArgb(255, 100, 255, 100));

// Draw path with the specified style
canvas.BeginPath();
canvas.MoveTo(50, 50);
canvas.LineTo(150, 80);
canvas.LineTo(100, 150);
canvas.LineTo(200, 200);
canvas.Stroke();

// Different end caps
canvas.SetStrokeStartCap(EndCapStyle.Round);
canvas.SetStrokeEndCap(EndCapStyle.Square);
canvas.BeginPath();
canvas.MoveTo(250, 100);
canvas.LineTo(350, 200);
canvas.Stroke();
```

### Scissor Clipping

```csharp
// Save current state
canvas.SaveState();

// Set scissor region
canvas.Scissor(100, 100, 200, 150);

// Draw content that will be clipped by the scissor
canvas.CircleFilled(200, 180, 80, Color.FromArgb(180, 255, 100, 200));

// Reset scissor
canvas.ResetScissor();

// Restore previous state
canvas.RestoreState();
```

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
