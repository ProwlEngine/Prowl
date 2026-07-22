# Origami Documentation

Origami is a component library for [Paper](https://github.com/ProwlEngine/Prowl), Prowl's
immediate-mode UI library. Widgets are static factory calls off `Origami` that return a fluent
builder; chain modifiers, then terminate with `.Show()` (or `.Body(...)` for container widgets).

```csharp
Origami.Button(paper, "save-btn", "Save", () => Save())
    .Primary()
    .Show();
```

## Where to start

Read [Core Concepts](CoreConcepts.md) first - it covers theming, metrics/variants, icons, drag
and drop, field drawers, and the per-frame `BeginFrame`/`EndFrame` lifecycle that every other doc
assumes you already know.

## Widget reference

- [Inputs](Inputs.md) - Button, ButtonGroup, IconToolbar, Toggle, RadioGroup, Slider, RangeSlider,
  NumericField, VectorField, TextField, ColorField, DatePicker, Dropdown, MultiDropdown
- [Layout & Navigation](Layout.md) - AppBar, MenuBar, Tabs, Breadcrumb, Accordion, Foldout, Header,
  Label, ScrollView, ContextMenu
- [Overlays & Feedback](Overlays.md) - Modal, Toasts, Tooltip, ProgressBar, Spinner, Skeleton,
  ChatBubble, FileDialog
- [Data Display](DataDisplay.md) - Table, Tree, PropertyGrid, Chart, FlameGraph, NodeGraph,
  ImageDiff
- [Docking & Floating Windows](Docking.md) - DockSpace, DockNode, DockPanel, FloatingWindow,
  serialization
- [3D Gizmos](Gizmos.md) - TransformGizmo, ViewManipulatorGizmo, sub-gizmos, GizmoDraw3D,
  GizmoUtils

## Screenshots

Each widget section has a placeholder image link, e.g. `images/inputs/button.png`. The
`docs/images/<category>/` folders already exist - drop a screenshot in with the matching filename
and it will render in place wherever this doc set is viewed.
