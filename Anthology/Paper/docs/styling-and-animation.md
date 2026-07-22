# Styling and Animation

## How styles are stored and resolved

Every stylable property is a `GuiProp` enum value:

- **Visual:** `BackgroundColor`, `BackgroundGradient`, `BackgroundImage`, `BorderColor`,
  `BorderWidth`, `Rounded`, `BoxShadow`, `BackdropBlur`
- **Layout sizing:** `AspectRatio`, `Width`, `Height`, `MinWidth`, `MaxWidth`, `MinHeight`, `MaxHeight`
- **Positioning:** `Left`, `Right`, `Top`, `Bottom`, `MinLeft`, `MaxLeft`, `MinRight`, `MaxRight`, `MinTop`, `MaxTop`, `MinBottom`, `MaxBottom`
- **Child layout:** `ChildLeft`, `ChildRight`, `ChildTop`, `ChildBottom`
- **Spacing:** `RowBetween`, `ColBetween`
- **Padding:** `PaddingLeft`, `PaddingRight`, `PaddingTop`, `PaddingBottom`
- **Transform:** `TranslateX`, `TranslateY`, `ScaleX`, `ScaleY`, `Rotate`, `OriginX`, `OriginY`, `SkewX`, `SkewY`, `Transform`
- **Text:** `TextColor`, `WordSpacing`, `LetterSpacing`, `LineHeight`, `TabSize`, `FontSize`, `TextQuality`

Each element has one persistent `ElementStyle` (keyed by element ID, so it
survives across frames) holding a `StyleValues` struct — an allocation-free
struct with one field per `GuiProp` plus a bitmask of which properties were
explicitly set *this frame*. Fields already hold their default even when
unset, so reads never need a separate default lookup.

**Every frame, before your code runs, `ElementStyle.BeginFrame()` resets the
element's values back to defaults.** Nothing persists from frame to frame unless
you (or a transition, see below) set it again this frame. This is the core thing
to internalize about immediate-mode styling: `.BackgroundColor(Color.Blue)` isn't
"set and forget," it's "assert this value for this frame."

## Fluent setters and state-driven styles

`ElementBuilder` (and `StyleTemplate`, see below) exposes every `GuiProp` as a
fluent method — `BackgroundColor`, `Rounded`, `Width`, `Padding`, `Transition`,
etc. — all funneling through `SetStyleProperty(GuiProp, object)`.

```csharp
paper.Box("Btn")
    .BackgroundColor(Color.Gray)
    .Hovered
        .BackgroundColor(Color.LightGray)
        .End()
    .Active
        .BackgroundColor(Color.DarkGray)
        .End()
    .If(someCondition)
        .BackgroundColor(Color.Red)
        .End();
```

`.Normal`, `.Hovered`, `.Active`, `.Focused`, and `.If(bool)` each return a
pooled `StateDrivenStyle` — a style-setter scope that only actually writes its
properties if the gating condition is true (`Hovered` precomputes
`Paper.IsElementHovered(id)`, etc.), then `.End()` returns you to the
`ElementBuilder` chain.

**There is no priority system beyond call order.** Every `.Set()` call — gated
or not — writes immediately if its condition is true; the *last* call for a
given `GuiProp` in the chain wins, full stop. This is why the snippet above
resolves `.If(condition)` over `.Hovered`/`.Active` when both are true: it's
declared last. Order your chains from least to most specific.

## Named, reusable styles

`Paper` has a small style-template registry so you don't have to repeat the
same builder chain everywhere:

```csharp
public StyleTemplate DefineStyle(string name)
public StyleTemplate DefineStyle(string name, params string[] inheritFrom) // copies properties/transitions from each parent
public void RegisterStyle(string name, StyleTemplate template)
public bool TryGetStyle(string name, out StyleTemplate? template)
public void ApplyStyleWithStates(ElementHandle element, string baseName)
public void RegisterStyleFamily(string baseName, StyleTemplate baseStyle,
    StyleTemplate normalStyle = null, StyleTemplate hoveredStyle = null,
    StyleTemplate focusedStyle = null, StyleTemplate activeStyle = null)
public StyleFamilyBuilder CreateStyleFamily(string baseName) // .Base(t).Hovered(t).Active(t).Focused(t).Register()
```

A `StyleTemplate` is a standalone object with the exact same fluent setters as
`ElementBuilder` (both derive from `StyleSetterBase<T>`); it just records
properties/transitions instead of writing them straight to an element.
`.ApplyTo(ElementHandle)` pushes it onto an element, `.ApplyTo(StyleTemplate)`
merges it into another template, `.Clone()` deep-copies it.

`CreateStyleFamily` is the ergonomic way to define a component's full state set
at once:

```csharp
paper.CreateStyleFamily("button")
    .Base(new StyleTemplate()
        .Height(40).Rounded(8).BackgroundColor(Color.FromArgb(50, 0, 0, 0))
        .Transition(GuiProp.BackgroundColor, 0.2f)
        .Transition(GuiProp.Rounded, 0.2f))
    .Hovered(new StyleTemplate().BackgroundColor(Color.FromArgb(100, primary)).Rounded(12))
    .Active(new StyleTemplate().Scale(0.95f).BackgroundColor(Color.FromArgb(150, primary)))
    .Register();

// later, per element:
paper.Box("Save").Style("button").OnClick(e => Save());
```

`ElementBuilder.Style(params string[] names)` / `StyleIf(bool, params string[] names)`
call `ApplyStyleWithStates` for each name, which applies `"{name}"` (the base
template) then conditionally applies `"{name}:hovered"`, `"{name}:focused"`,
`"{name}:active"` (in that order) if the element is currently in that state.

> **Caveat:** `ApplyStyleWithStates` never looks up `"{name}:normal"` — only
> `:hovered`/`:focused`/`:active`. A `normalStyle` passed to
> `RegisterStyleFamily`/`.Normal(...)` is stored but currently never applied by
> the state-resolution path. Don't rely on it.

## Inheritance

```csharp
ElementBuilder InheritStyle(ElementHandle? element = null) // defaults to the layout parent
```

Marks this element's style to fall back to another element's style for any
`GuiProp` *not* explicitly set on itself this frame. Resolution happens at read
time (each `GetXxx()` getter checks "did I set this? if not and I have a parent
style, ask it"), so it's opt-in per element and only affects properties you
leave unset.

## Transitions (declarative, tied to `GuiProp`)

```csharp
.Transition(GuiProp property, float duration, Func<float, float> easing = null)
```

Configuring a transition on a property doesn't change what you write — you still
call `.BackgroundColor(...)` etc. as normal — but from then on, whatever value
you declare for that property each frame becomes the *target* of a tween instead
of taking effect immediately. Every frame, Paper compares the newly-declared
target against the previous target: if it changed (e.g. hover state flipped),
the animation restarts *from the value it was actually at* (not from the old
target), so redirecting mid-animation is smooth rather than snapping. The
tweened value unconditionally overrides whatever you declared that frame.

Supported interpolated types: `float`, `double`, `int`, `Color` (see below),
`Float2`/`Float3`/`Float4`, `UnitValue`, `Transform2D`, `Gradient`, `BoxShadow`,
and `string` (which just flips discretely at `t > 0.5`, not really "interpolated").
Any other boxed type snaps to the end value with no animation.

**Color transitions interpolate in HSV space** (via `HSV.FromColor`/`.Lerp`/`.ToColor()`),
not straight RGB — this avoids fading through a muddy gray when animating
between saturated hues. If either endpoint color is fully transparent, its RGB
is first replaced with the *other* endpoint's RGB so a fade to/from transparent
doesn't visibly sweep through white/black.

Omitting the `.Transition(...)` call for a property on a given frame stops it
from being configured that frame (the config dictionary is cleared every
`BeginFrame`), but the tweened value already reached stays in the interpolation
state and simply stops advancing.

## Imperative animation primitives (`Paper.Animation.cs`)

These are separate from the `GuiProp`/`Transition` system — plain methods on
`Paper` you call each frame with a target value; they return the *current*
animated value for you to feed into any style setter or custom draw code. State
is keyed by call site (`[CallerLineNumber]`) and persisted via per-element
storage, so give them an explicit `id` if you call one from inside a loop or a
shared helper function — otherwise every iteration collides on the same slot.

```csharp
float AnimateBool(bool target, float duration = 0.2f, Func<float,float>? easing = null, string? id = null, ...)
float AnimateFloat(float target, float speed = 8f, string? id = null, ...)
float AnimateSpring(float target, float frequency = 6f, float damping = 0.7f, ...)
float OneShot(bool trigger, float duration = 0.4f, Func<float,float>? easing = null, ...)
float Pulse(float period = 1.5f)
Color AnimateColor(Color target, float speed = 8f, ...)
Float2 AnimateVec2(Float2 target, float speed = 8f, ...)
float AnimateAngle(float targetDegrees, float speed = 8f, ...)
float StableFor(bool current, string? id = null, ...)
Float2 Shake(bool trigger, float intensity = 4f, float decay = 6f, float frequency = 30f, ...)
```

- `AnimateBool` — linear 0..1 progress chased at `1/duration` per second toward
  1 (true) or 0 (false); easing is applied only when reading the value, so
  reversing direction mid-flight stays smooth regardless of the easing shape.
  `duration <= 0` snaps instantly.
- `AnimateFloat`/`AnimateColor`/`AnimateVec2` — frame-rate-independent
  exponential smoothing (`t = 1 - exp(-speed*dt)`) toward a target that can keep
  changing every frame (cursor position, live scroll offset). `AnimateColor`
  interpolates RGBA channels directly — **not** HSV like style transitions, so
  the same start/end colors can look visually different between the two systems.
- `AnimateSpring` — a real spring-damper integrator; can overshoot/oscillate.
  `damping`: `0` rings forever, `1` is critically damped (no overshoot).
- `OneShot` — ramps 0→1 over `duration` on the rising edge of `trigger`, holds
  at 1 while true, resets to 0 the instant it goes false. For fire-once
  flash/pulse effects.
- `Pulse` — stateless cosine oscillator 0..1 driven by `Paper.Time`, for
  breathing/blinking idle effects.
- `AnimateAngle` — exponential chase along the *shortest* signed path around the
  circle; the returned value is an unwrapped running angle (can go outside
  `[0,360)` by design, since it's meant to feed directly into `.Rotate(...)`) —
  `% 360f` it yourself if you need a normalized angle.
- `StableFor` — returns how many seconds a boolean has held its current value,
  resetting to 0 the frame it flips. Useful for long-press detection, hover-delay
  tooltips, staggered list entrance.
- `Shake` — a rising edge on `trigger` kicks off an exponentially-decaying 2D
  jitter offset (`decay` ≈ 1/seconds-to-settle); add the result directly to a
  translate/position. Must be called every frame to keep animating.

## Easing functions (`Easing` static class)

All are `static float F(float t)` matching the `Func<float,float>` shape
`Transition`/`AnimateBool`/`OneShot` expect, so any static method, lambda, or
method group works as a custom easing function.

| Family | Members | Shape |
|---|---|---|
| `Linear` | — | no easing |
| Quad | `EaseIn`/`EaseOut`/`EaseInOut` | gentle accelerate/decelerate/both |
| Cubic/Quart/Quint | `CubicIn/Out/InOut`, `QuartIn/Out/InOut`, `QuintIn/Out/InOut` | increasingly pronounced power curves |
| Sine | `SineIn/Out/InOut` | gentle sine-shaped accelerate/decelerate |
| Expo | `ExpoIn/Out/InOut` | near-flat start/end, sharp middle |
| Circ | `CircIn/Out/InOut` | quarter/semicircle acceleration |
| Back | `BackIn/Out/InOut` | slight overshoot before/after/on both ends |
| Elastic | `ElasticIn/Out/InOut` | spring-like oscillation settling at the end |
| Bounce | `BounceOut/In/InOut` | decaying bounces |
| `Step(t)` | — | instantaneous jump at `t=0.5` |
| `SmoothStep(t)` / `SmootherStep(t)` | — | Hermite smoothing, `SmootherStep` has smoother derivatives |
| `Spring(t, dampingRatio=0.5f, angularFrequency=20f)` | — | real damped oscillator; **not** a plain `Func<float,float>` — wrap it (`t => Easing.Spring(t, 0.3f)`) to use as an easing callback; can exceed `[0,1]` while oscillating |

## Gradients

```csharp
.BackgroundLinearGradient(x1, y1, x2, y2, Color color1, Color color2)
.BackgroundRadialGradient(centerX, centerY, innerRadius, outerRadius, Color inner, Color outer)
.BackgroundBoxGradient(centerX, centerY, width, height, radius, feather, Color inner, Color outer)
.ClearBackgroundGradient()
```

Backed by `struct Gradient` (`Gradient.Linear/.Radial/.Box(...)`, `Gradient.None`,
`Gradient.Lerp`). All positional/size parameters are relative to the element's
own rect (0..1-ish, not pixels) except radii, which are relative to
`min(width, height)`. Interpolating between two gradients of *different* types
holds at the start gradient until `t` reaches 1, then snaps — shapes don't morph
into each other.

## Box shadow

```csharp
.BoxShadow(offsetX, offsetY, blur, spread, Color color)
.BoxShadow(BoxShadow shadow)
```

`struct BoxShadow { float OffsetX, OffsetY, Blur, Spread; Color Color; }` (all
pixel units). `BoxShadow.None` is all-zero/transparent. `IsVisible => Color.A > 0`
is exactly the check the renderer uses to skip drawing it entirely. `BoxShadow.Lerp`
interpolates offsets/blur/spread linearly and color via straight RGBA (not HSV).
