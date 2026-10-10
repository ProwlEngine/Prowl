---
name: prowl-ui
description: Game UI in Prowl. Use when building a HUD, crosshair, health or ammo display, pause or main menu, or any in game interface, and when choosing between Paper OnGui immediate mode and GameObject UI (GameCanvas, RectTransform, UIButton, EventSystem).
---

# Prowl game UI

Prowl has two unrelated UI systems. Do not mix them in one screen.

- **Paper in `OnGui`**: immediate mode, written in code every frame. Use it for HUDs, crosshairs and menus. This is the default choice.
- **GameObject UI**: `GameCanvas`, `RectTransform`, `TextComponent`, `UIImage`, `UIButton` objects in the scene. Use it for world space or VR panels, or UI laid out in the editor.

## Paper in OnGui

Paper is a large library with its own layout rules and traps. **Read the prowl-paper skill before writing more than a few boxes.**

Override `OnGui(Paper paper)` on any component. It runs every frame after the cameras render and needs no canvas. In the editor it draws only in the game view during play.

```csharp
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;

public sealed class Hud : Component
{
    public int Ammo = 30;
    public float Health = 0.75f;
    private bool _paused;

    public override void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) SetPaused(!_paused);
    }

    public override void OnGui(Paper paper)
    {
        FontFile? font = FontAsset.LoadDefault().FontFile;
        if (font == null) return;

        using (paper.Column("hud")
            .PositionType(PositionType.SelfDirected)
            .AnchorLeft(20).AnchorBottom(20).Width(240).Height(UnitValue.Auto)
            .Gap(6)
            .IsNotInteractable()
            .Enter())
        {
            paper.Box("ammo").Height(30)
                .Text($"{Ammo} / 90", font).FontSize(24).TextColor(Color.White);

            using (paper.Box("health track").Height(12).BackgroundColor(new Color(0f, 0f, 0f, 0.5f)).Rounded(6).Enter())
                paper.Box("health fill").Width(UnitValue.Percentage(Health * 100f)).Height(12)
                    .BackgroundColor(new Color(0.9f, 0.2f, 0.2f, 1f)).Rounded(6);
        }

        using (paper.Box("crosshair")
            .PositionType(PositionType.SelfDirected)
            .Width(8).Height(8).Margin(UnitValue.Stretch())   // stretch margins centre it
            .IsNotInteractable()
            .Enter())
            paper.Draw((canvas, rect) => canvas.CircleFilled(rect.Min.X + 4f, rect.Min.Y + 4f, 3f, Color.White));

        if (!_paused) return;

        using (paper.Column("pause")
            .PositionType(PositionType.SelfDirected)
            .Width(200).Height(UnitValue.Auto).Margin(UnitValue.Stretch())
            .BackgroundColor(new Color(0f, 0f, 0f, 0.8f)).Rounded(10).Padding(14).Gap(8)
            .Enter())
        {
            paper.Box("resume").Height(32)
                .BackgroundColor(new Color(0.2f, 0.2f, 0.25f, 1f)).Rounded(6)
                .Hovered.BackgroundColor(new Color(0.3f, 0.3f, 0.4f, 1f)).End()
                .Text("Resume", font).FontSize(16).TextColor(Color.White)
                .OnClick(_ => SetPaused(false));
        }
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        Time.TimeScale = paused ? 0f : 1f;
        if (paused) Input.UnlockCursor();
        else Input.LockCursor();
    }
}
```

Rules:

- Text needs a font object. `FontAsset.LoadDefault().FontFile` is the built in one.
- Every element needs an id that is unique among its siblings. In a loop pass the index too: `paper.Box("slot", i)`. A duplicate id throws.
- Width and height default to stretch. Use `UnitValue.Auto` to fit content, a number for pixels, or `UnitValue.Percentage(n)`.
- Place things on the screen with `PositionType(PositionType.SelfDirected)` and `AnchorLeft/Right/Top/Bottom`. Anchors take pixels or `UnitValue.Percentage(p, offset)`. Centre a fixed size element with `.Margin(UnitValue.Stretch())`.
- Every component's `OnGui` shares one Paper, so two instances of a component collide on ids. Wrap the body in `paper.PushID(InstanceID)` and `paper.PopID()`.
- `.Enter()` in a `using` makes the following elements children.
- `paper.Draw((canvas, rect) => ...)` gives a vector canvas for crosshairs, arcs and custom shapes.
- `.IsNotInteractable()` on display only elements, so they never block clicks.
- `OnClick` and the other callbacks run at the end of the frame, not inline.
- Paper never consumes game input. A click on a button also reaches `Input.GetMouseButtonDown`. In `OnGui` store `paper.WantsCapturePointer` in a field, and check that field in the next `Update` before shooting on click. Scripts only get Paper through `OnGui`.

## GameObject UI

- Create elements with `menu --path "GameObject/UI/Button" --context /Canvas` (also Text, Image, Panel, Slider, Toggle, Input Field, Dropdown, Scroll View). These add the `RectTransform` and create an `EventSystem` if the scene has none.
- Objects made in code or with `go create` must add the `RectTransform` and `EventSystem` themselves. **Without an `EventSystem` nothing receives clicks**, and it only runs in play mode.
- `RectTransform` positions are Y up from the bottom left.
- `UIButton.OnClick` is a C# event: `button.OnClick += Fire;`. `TextComponent.Text` sets the string and `Size` is an int.

## Cursor

`Input.LockCursor()` hides and locks the cursor for mouse look. Unlock it while a menu is open, as in the example.

Look up `Paper`, `GameCanvas`, `RectTransform`, `UIButton`, `TextComponent` and `EventSystem` with `api --type`.
