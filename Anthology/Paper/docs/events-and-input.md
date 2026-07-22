# Events and Input

## Feeding input into Paper

Paper never polls a windowing API itself — the host forwards input each frame
(or as OS events arrive):

```csharp
void SetKeyState(PaperKey key, bool isKeyDown)
void SetPointerPosition(float x, float y)
void SetPointerState(PaperMouseBtn btn, float x, float y, bool isPointerBtnDown, bool isPointerMove)
void SetPointerWheel(float wheel)
void AddInputCharacter(string text)   // one or more characters; normalizes \r to \n
void PushInputText(char character)    // lower-level single-char enqueue
void ClearInput()                     // resets all keyboard/mouse state (e.g. on host window losing focus)
void SetClipboardHandler(IClipboardHandler handler) // GetClipboardText()/SetClipboardText(string); without one, copy/paste no-ops
```

Pass `isPointerMove: true` to `SetPointerState` to update position without
touching button state; pass `false` with the button flag to register an actual
press/release.

Paper drives its own frame-boundary bookkeeping (`StartInputFrame`/`EndInputFrame`)
from `BeginFrame`/`EndFrame` — you don't call those yourself.

## Reading input state

```csharp
bool IsKeyDown/IsKeyUp/IsKeyPressed/IsKeyReleased(PaperKey key)
bool IsKeyHeld(PaperKey key, float holdDuration = 0.5f)
bool IsKeyRepeating(PaperKey key)  // auto-repeat is on by default
bool IsKeyPressedOrRepeating(PaperKey key)
PaperKey LastKeyPressed

bool IsPointerDown/IsPointerUp/IsPointerPressed/IsPointerReleased(PaperMouseBtn btn)
bool IsPointerHeld(PaperMouseBtn btn, float holdDuration = 0.5f)
bool IsPointerDoubleClick(PaperMouseBtn btn)  // window: 0.25s, ~1.4px
Float2 GetPointerClickPos(PaperMouseBtn btn)
bool IsPointerOverRect(float x, float y, float w, float h)
Float2 PointerPos { get; set; }  // setting fires OnPointerPosSet
Float2 PreviousPointerPos, PointerDelta
bool IsPointerMoving
float PointerWheel

float DeltaTime, Time
bool KeyAutoRepeatEnabled, float AutoRepeatDelay (default 0.8s, min 0.1), float AutoRepeatRate (default 0.05s, min 0.01)

void SetCursorVisibility(bool visible)  // fires OnCursorVisibilitySet — wire to your window's cursor API
event Action<Float2> OnPointerPosSet
event Action<bool> OnCursorVisibilitySet
```

## Interaction state queries

```csharp
bool IsElementHovered(int id)
bool IsElementActive(int id)   // pressed
bool IsElementFocused(int id)
bool IsElementDragging(int id)
bool IsParentHovered / IsParentActive / IsParentFocused / IsParentDragging  // scoped to CurrentParent

bool IsParentFocusWithin
bool IsElementFocusWithin(int id)  // CSS :focus-within — one frame stale (see gotchas)

int HoveredElementId, ActiveElementId, FocusedElementId  // 0 = none

PaperCursor CurrentCursor
event Action<PaperCursor> OnCursorChange  // fires only when it changes

void SetFocus(ElementHandle? element = null)  // defaults to CurrentParent; does NOT fire OnFocusChange
void ClearFocus()                              // fires OnFocusChange(false) on the previously focused element

bool WantsCapturePointer  // true if anything is hovered/active — useful to suppress input to e.g. a 3D scene underneath
bool SkipKeyboardNavigation  // auto-resets each frame; set by a focused multi-line TextArea so Tab inserts a tab char
```

Each of `IsElementHovered`/`Active`/`Focused`/`Dragging` also returns true for
elements `.HookToParent()`-linked to an ancestor currently in that state (walks
up multi-level hook chains).

## Event bubbling model

Each frame, `HandleInteractions()`:

1. **Hit-tests** for the topmost interactable element under the pointer —
   non-`Layer.Base` layers are checked first, highest layer to lowest, then the
   base tree. Children are tested in reverse order (front-most drawn wins ties).
   `IsNotInteractable()` elements are skipped but their children can still be
   hit; `.Clip()` prunes children hit-testing when the pointer is outside the
   clipped parent's bounds.
2. Builds the **bubble path** (hit element → root) for hover tracking. This is
   independent of `StopPropagation` — hover state always reflects the full
   ancestor chain regardless of propagation settings; only *event dispatch* is
   affected by `StopPropagation`.
3. **Hover/Enter/Leave**: computed from the set difference between this frame's
   and last frame's bubble path. `OnEnter`/`OnLeave` fire only on the transition
   edge; `OnHover` fires every frame the pointer is within the path.
4. **Cursor resolution** — see below.
5. **Scroll** dispatches to the hovered element and bubbles.
6. **Mouse buttons**: press sets the active element, starts drag tracking, fires
   `OnPress` (and, if focusable, updates focus, firing `OnFocusChange(false)`/
   `OnFocusChange(true)` on the old/new focused element). Movement past a 5px
   threshold while held promotes to a drag — `OnDragStart` (and the first
   `OnDragging` after crossing the threshold) report the **entire accumulated
   delta since the press**, not just the current frame's motion, so consumers
   applying `Delta` incrementally don't "lose" the first 5 pixels. Release fires
   `OnDragEnd` (if it was dragging), then `OnClick` (if released over the same
   element without having dragged), then always `OnRelease`. Right-click fires
   `OnRightClick` directly on press (not gated by release) and separately clears
   focus if the target isn't already focused. Double-click fires `OnDoubleClick`
   using its own timing state in the input layer, independent of and in addition
   to the normal click phases. `OnHeld` fires every frame while pressed.
7. **Keyboard**: Tab navigation is processed first (unless `SkipKeyboardNavigation`)
   — it scans every focusable, visible element with `TabIndex >= 0`, sorts by
   `TabIndex`, and wraps around. Otherwise, `OnKeyPressed` fires for each
   currently-pressed key and `OnTextInput` drains typed characters — both only
   to the currently focused element (and its hooked children).
8. The focus-within ancestor cache is rebuilt last.

**Bubbling** (`BubbleEventToParents`, used by click/drag/scroll — not by hover/
enter/leave/focus/key/text): after the origin element's handler runs, the same
event is retargeted (`Source`/`ElementRect`/derived positions recomputed) and
invoked on each ancestor in turn, stopping if the origin or any ancestor along
the way has `.StopEventPropagation()` set, or if the event's own
`evt.StopPropagation()` was called.

**`HookToParent`** is a *separate* mechanism from bubbling: it's downward
fan-out of the *same* event from the element that actually received the input
to any direct children flagged `.HookToParent()` — for compound widgets where a
wrapper is what's really interactive but an inner label/icon should observe the
same events (and, via the state queries above, read the same hover/active/
focused/dragging state). Don't confuse this with bubbling, which goes the
opposite direction (toward ancestors).

## Event types

- **`ElementEvent`** (base for Click/Drag/Scroll; also used directly for
  Hover/Enter/Leave): `ElementHandle Source`, `Rect ElementRect`,
  `Float2 PointerPosition` (raw screen coords), `Float2 RelativePosition`
  (pointer minus `ElementRect.Min`, recomputed on bubble-retarget),
  `Float2 NormalizedPosition` (0..1 within the element), `StopPropagation()`.
- **`ClickEvent : ElementEvent`**: `PaperMouseBtn Button`,
  `ClickPhase Phase` (`Click`, `Press`, `Release`, `DoubleClick`, `RightClick`,
  `Held`) — one type backs all of `OnPress`/`OnClick`/`OnRelease`/
  `OnDoubleClick`/`OnRightClick`/`OnHeld`.
- **`DragEvent : ElementEvent`**: `Float2 StartPosition` (press position),
  `Float2 Delta` (this frame's movement, except the first Start/Dragging frame
  which carries the full accumulated delta), `Float2 TotalDelta` (cumulative
  since press), `DragPhase Phase` (`Start`, `Dragging`, `End`).
- **`ScrollEvent : ElementEvent`**: `float Delta`.
- **`FocusEvent`** (not an `ElementEvent` — no position/bubbling): `ElementHandle Source`, `bool IsFocused`.
- **`KeyEvent`** (not an `ElementEvent`): `ElementHandle Source`, `PaperKey Key`, `bool IsRepeat`.
- **`TextInputEvent`** (not an `ElementEvent`): `ElementHandle Source`, `char Character`.

Focus/Key/TextInput events aren't spatial or bubbled — they're dispatched only
to the focused element and its hooked children.

## Cursors

```csharp
.Cursor(PaperCursor cursor)          // requested while hovered
.CursorDragging(PaperCursor cursor)  // requested while pressed/dragging; falls back to Cursor if left Inherit
```

`PaperCursor`: `Inherit` (0, resolves to `Default` at the root), `Default`,
`Pointer`, `Grab`, `Grabbing`, `Text`, `Crosshair`, `ResizeHorizontal`,
`ResizeVertical`, `ResizeNWSE`, `ResizeNESW`, `ResizeAll`, `NotAllowed`, `Wait`,
`Help`. Paper only *resolves* which shape currently applies
(`Paper.CurrentCursor` / `OnCursorChange`) — the host maps it to an actual OS
cursor (the names mirror the common GLFW/SDL cross-platform set; fall back to
`Default` for anything your windowing library doesn't provide). Resolution
walks up the ancestor chain from the active (if dragging) or hovered element for
the nearest non-`Inherit` value; while dragging, `CursorDragging` is preferred
over `Cursor` at each step.

## Gotchas

- Hover state (via the bubble path) ignores `StopPropagation` entirely — only
  event *dispatch* is blocked by it, not `IsElementHovered` for ancestors.
- `IsElementFocusWithin`/`IsParentFocusWithin` read a cache rebuilt at the very
  end of the previous frame's interaction handling, so they're one frame stale
  by design — this lets immediate-mode code check "does my not-yet-built
  subtree contain focus" without a chicken-and-egg problem.
- `SetFocus()` does **not** fire `OnFocusChange` (unlike the press-driven and
  Tab-driven paths, and unlike `ClearFocus()`, which does fire
  `OnFocusChange(false)`).
- Masked/password text fields suppress Ctrl+C/Ctrl+X regardless of these
  mechanics — see [Text and Input Fields](text-and-input-fields.md).
