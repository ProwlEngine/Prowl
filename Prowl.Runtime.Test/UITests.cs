// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Runtime.Test;

public class UITests : RuntimeTestBase
{
    private sealed class ClickCounter : Component, IPointerClickHandler, IPointerDownHandler
    {
        public int Clicks;
        public int Presses;
        public void OnPointerClick(PointerEventData e) => Clicks++;
        public void OnPointerDown(PointerEventData e) => Presses++;
    }

    private GameObject CreateUIObject(string name, Scene scene, GameObject? parent = null)
    {
        var go = CreateGameObject(name);
        go.EnsureRectTransform();
        if (parent is null) scene.Add(go);
        else go.SetParent(parent, false);
        return go;
    }

    // A dropdown's items sit under the dropdown, so an item click must stop at the item's button.
    [Fact]
    public void ButtonClick_DoesNotBubbleToEnclosingClickable()
    {
        Scene scene = CreateScene(enable: true);
        var outer = CreateUIObject("Outer", scene);
        var counter = outer.AddComponent<ClickCounter>();
        var item = CreateUIObject("Item", scene, outer);
        item.AddComponent<UIImage>();
        var button = item.AddComponent<UIButton>();
        int buttonClicks = 0;
        button.OnClick += () => buttonClicks++;

        var e = new PointerEventData { Button = MouseButton.Left };
        EventSystem.Bubble<IPointerClickHandler>(item, e, static (h, ev) => h.OnPointerClick(ev));
        EventSystem.Bubble<IPointerDownHandler>(item, e, static (h, ev) => h.OnPointerDown(ev));

        Assert.Equal(1, buttonClicks);
        Assert.Equal(0, counter.Clicks);
        Assert.Equal(0, counter.Presses);
    }

    private sealed class DragCatcher : Component, IDragHandler
    {
        public void OnDrag(PointerEventData e) { }
    }

    // Press on `pressOn`, move past the drag threshold, release over `releaseOn`.
    private static void PressMoveRelease(GameObject pressOn, GameObject releaseOn)
    {
        var system = new EventSystem();
        var e = new PointerEventData { Button = MouseButton.Left };
        Float2 start = new(100, 100), moved = new(110, 100);
        system.UpdateButton(e, MouseButton.Left, true, false, true, pressOn, null, start, Float2.Zero, Float2.Zero, Float2.One, 0f);
        system.UpdateButton(e, MouseButton.Left, false, false, true, releaseOn, null, moved, moved - start, Float2.Zero, Float2.One, 0.1f);
        system.UpdateButton(e, MouseButton.Left, false, true, false, releaseOn, null, moved, Float2.Zero, Float2.Zero, Float2.One, 0.2f);
    }

    private (GameObject buttonGo, GameObject labelGo, Func<int> clicks) CreateButtonWithLabel(Scene scene, GameObject? parent)
    {
        var buttonGo = CreateUIObject("Button", scene, parent);
        buttonGo.AddComponent<UIImage>();
        var button = buttonGo.AddComponent<UIButton>();
        int clicks = 0;
        button.OnClick += () => clicks++;
        var labelGo = CreateUIObject("Label", scene, buttonGo);
        labelGo.AddComponent<TextComponent>();
        return (buttonGo, labelGo, () => clicks);
    }

    [Fact]
    public void Click_SurvivesSmallMoveWhenNothingDrags()
    {
        Scene scene = CreateScene(enable: true);
        var (buttonGo, _, clicks) = CreateButtonWithLabel(scene, null);

        PressMoveRelease(buttonGo, buttonGo);

        Assert.Equal(1, clicks());
    }

    [Fact]
    public void Click_FiresWhenPressedOnLabelAndReleasedOnButton()
    {
        Scene scene = CreateScene(enable: true);
        var (buttonGo, labelGo, clicks) = CreateButtonWithLabel(scene, null);

        PressMoveRelease(labelGo, buttonGo);

        Assert.Equal(1, clicks());
    }

    [Fact]
    public void LayoutIntrinsicSize_IgnoresSizeDeltaOnStretchedAxes()
    {
        Scene scene = CreateScene(enable: true);
        var fixedGo = CreateUIObject("Fixed", scene);
        fixedGo.RectTransform!.SizeDelta = new Float2(50f, 30f);

        var stretchedGo = CreateUIObject("Stretched", scene);
        RectTransform rt = stretchedGo.RectTransform!;
        rt.AnchorMin = new Float2(0f, 0.5f);
        rt.AnchorMax = new Float2(1f, 0.5f);
        rt.SizeDelta = new Float2(-16f, 30f);

        LayoutUtility.InvalidateCache();
        Assert.Equal(new Float2(50f, 30f), LayoutUtility.GetPreferredSize(fixedGo));
        Assert.Equal(new Float2(0f, 30f), LayoutUtility.GetPreferredSize(stretchedGo));
    }

    [Fact]
    public void TextPreferredSize_FollowsTextAndWrapsAtWidth()
    {
        Scene scene = CreateScene(enable: true);
        var go = CreateUIObject("Text", scene);
        var text = go.AddComponent<TextComponent>();
        text.Text = "Some words that wrap";

        LayoutUtility.InvalidateCache();
        Float2 unwrapped = LayoutUtility.GetPreferredSize(go);
        Assert.True(unwrapped.X > 0f && unwrapped.Y > 0f);

        // Narrower than the text, so the wrapped height is taller than one line.
        RectTransform rt = go.RectTransform!;
        rt.ComputedRect = new Rect(0f, 0f, unwrapped.X * 0.4f, unwrapped.Y);
        LayoutUtility.InvalidateCache();
        Assert.True(LayoutUtility.GetPreferredSize(go).Y > unwrapped.Y);
    }

    private sealed class Box : Graphic
    {
        public override void GenerateMesh(UIMeshBuilder b, in UIContext ctx) => b.AddQuad(GameObject.RectTransform!.Rect, Color, Float2.Zero, Float2.One);
    }

    // A child can sit inside a mask even when its parent's rect is outside it.
    [Fact]
    public void MaskCull_KeepsChildrenOfAnElementOutsideTheMask()
    {
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(1000f, 1000f);
        try
        {
            Scene scene = CreateScene(enable: true);
            var canvasGo = CreateGameObject("Canvas");
            scene.Add(canvasGo);
            var canvas = canvasGo.AddComponent<GameCanvas>();

            var mask = CreateUIObject("Mask", scene, canvasGo);
            mask.AddComponent<RectMask>();

            var holder = CreateUIObject("Holder", scene, mask);
            holder.RectTransform!.SizeDelta = Float2.Zero;
            holder.RectTransform.AnchoredPosition = new Float2(300f, 0f);

            var inside = CreateUIObject("Inside", scene, holder);
            inside.RectTransform!.SizeDelta = new Float2(50f, 50f);
            inside.RectTransform.AnchoredPosition = new Float2(-300f, 0f);
            var box = inside.AddComponent<Box>();

            canvas.RebuildIfDirty();

            Assert.Contains(canvas.Tree.Items, item => ReferenceEquals(item.Owner, box));
        }
        finally { GameCanvas.ScreenSizeOverride = prevOverride; }
    }

    [Fact]
    public void Canvases_AreListedByTheSceneTheyAreEnabledIn()
    {
        Scene first = CreateScene(enable: true);
        Scene second = CreateScene(enable: true);
        var canvasGo = CreateGameObject("Canvas");
        first.Add(canvasGo);
        var canvas = canvasGo.AddComponent<GameCanvas>();

        Assert.Equal([canvas], first.Canvases);
        Assert.Empty(second.Canvases);

        canvas.Enabled = false;
        Assert.Empty(first.Canvases);

        canvas.Enabled = true;
        canvasGo.Enabled = false;
        Assert.Empty(first.Canvases);

        canvasGo.Enabled = true;
        Assert.Equal([canvas], first.Canvases);

        first.Remove(canvasGo);
        Assert.Empty(first.Canvases);
    }

    // A mask rotated 45 degrees clips to a diamond; clicks must follow the diamond, not the unrotated rect.
    [Fact]
    public void Raycast_FollowsRotatedMask()
    {
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(1000f, 1000f);
        try
        {
            Scene scene = CreateScene(enable: true);
            var canvasGo = CreateGameObject("Canvas");
            scene.Add(canvasGo);
            canvasGo.AddComponent<GameCanvas>();

            var mask = CreateUIObject("Mask", scene, canvasGo);
            mask.AddComponent<RectMask>();
            mask.RectTransform!.LocalRotation = Quaternion.AxisAngle(Float3.UnitZ, MathF.PI / 4f);

            var content = CreateUIObject("Content", scene, mask);
            content.RectTransform!.SizeDelta = new Float2(300f, 300f);
            content.AddComponent<Box>();

            Float2 window = new(1000f, 1000f);
            // Inside the unrotated 100x100 rect around (500, 500) but outside the diamond.
            Assert.False(UIRaycaster.TryPick(scene, new Float2(545f, 455f), window, out _));
            // Outside the unrotated rect but inside the diamond.
            Assert.True(UIRaycaster.TryPick(scene, new Float2(565f, 500f), window, out var hit));
            Assert.Same(content, hit.GameObject);
        }
        finally { GameCanvas.ScreenSizeOverride = prevOverride; }
    }

    // Children the group doesn't control keep their own width, and alignment centers the run.
    [Fact]
    public void HorizontalGroup_RespectsChildControlWidthAndAlignment()
    {
        Scene scene = CreateScene(enable: true);
        var row = CreateUIObject("Row", scene);
        var group = row.AddComponent<HorizontalLayoutGroup>();
        group.ChildControlWidth = false;
        group.ChildForceExpandWidth = true;
        group.ChildAlignment = TextAlignment.CenterMiddle;

        var a = CreateUIObject("A", scene, row);
        a.RectTransform!.SizeDelta = new Float2(50f, 20f);
        var b = CreateUIObject("B", scene, row);
        b.RectTransform!.SizeDelta = new Float2(50f, 20f);

        LayoutUtility.InvalidateCache();
        group.Arrange(new Rect(0f, 0f, 400f, 100f));

        Assert.Equal(new Rect(150f, 0f, 200f, 100f), a.RectTransform.ComputedRect);
        Assert.Equal(new Rect(200f, 0f, 250f, 100f), b.RectTransform.ComputedRect);
    }

    private sealed class ArrangeCounter : LayoutGroup
    {
        public int Arranges;
        public override void Arrange(Rect rect) => Arranges++;
        public override float MinWidth => 0f;
        public override float PreferredWidth => 0f;
        public override float MinHeight => 0f;
        public override float PreferredHeight => 0f;
    }

    // A tint fade sets Color every frame; that must re-bake the one mesh, not lay the canvas out again.
    [Fact]
    public void ColorChange_RebakesWithoutRelayout()
    {
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(1000f, 1000f);
        try
        {
            Scene scene = CreateScene(enable: true);
            var canvasGo = CreateGameObject("Canvas");
            scene.Add(canvasGo);
            var canvas = canvasGo.AddComponent<GameCanvas>();
            var panel = CreateUIObject("Panel", scene, canvasGo);
            var counter = panel.AddComponent<ArrangeCounter>();
            var boxGo = CreateUIObject("Box", scene, panel);
            boxGo.RectTransform!.SizeDelta = new Float2(50f, 50f);
            var box = boxGo.AddComponent<Box>();

            canvas.RebuildIfDirty();
            int arranges = counter.Arranges;

            box.Color = new Color(1f, 0f, 0f, 1f);
            canvas.RebuildIfDirty();

            Assert.Equal(arranges, counter.Arranges);
            Assert.Equal(new Color32(255, 0, 0, 255), box.CachedMesh!.Colors32[0]);
        }
        finally { GameCanvas.ScreenSizeOverride = prevOverride; }
    }

    // A pause menu runs at a time scale of 0, and its buttons still have to show hover.
    [Fact]
    public void SelectableTint_AdvancesWhileGameIsPaused()
    {
        Scene scene = CreateScene(enable: true);
        var go = CreateUIObject("Button", scene);
        var image = go.AddComponent<UIImage>();
        var button = go.AddComponent<UIButton>();
        button.HighlightedColor = new Color(0.5f, 0.6f, 0.7f, 1f);

        var paused = new TimeData { DeltaTime = 0f, UnscaledDeltaTime = 0.05f };
        Time.TimeStack.Push(paused);
        try
        {
            button.OnPointerEnter(new PointerEventData());
            for (int i = 0; i < 4; i++) button.Update();
        }
        finally { Time.TimeStack.Pop(); }

        Assert.Equal(button.HighlightedColor, image.Color);
    }

    // Inside something draggable (a scroll view) the move is a drag, which cancels the click.
    [Fact]
    public void Click_IsCancelledWhenAnAncestorDrags()
    {
        Scene scene = CreateScene(enable: true);
        var scroll = CreateUIObject("Scroll", scene);
        scroll.AddComponent<DragCatcher>();
        var (buttonGo, _, clicks) = CreateButtonWithLabel(scene, scroll);

        PressMoveRelease(buttonGo, buttonGo);

        Assert.Equal(0, clicks());
    }

    [Fact]
    public void Slider_SetValueWithoutNotify_ChangesValueSilently()
    {
        Scene scene = CreateScene(enable: true);
        var slider = CreateUIObject("Slider", scene).AddComponent<UISlider>();
        int changes = 0;
        slider.OnValueChanged += _ => changes++;

        slider.SetValueWithoutNotify(0.75f);
        Assert.Equal(0.75f, slider.Value);
        Assert.Equal(0, changes);

        slider.Value = 0.25f;
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Dropdown_SetValueWithoutNotify_ChangesValueSilently()
    {
        Scene scene = CreateScene(enable: true);
        var dropdown = CreateUIObject("Dropdown", scene).AddComponent<UIDropdown>();
        dropdown.SetOptions(["A", "B", "C"]);
        int changes = 0;
        dropdown.OnValueChanged += _ => changes++;

        dropdown.SetValueWithoutNotify(2);

        Assert.Equal(2, dropdown.Value);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Toggle_Click_FlipsAndNotifies_AndDrivesTheCheckmark()
    {
        Scene scene = CreateScene(enable: true);
        var go = CreateUIObject("Toggle", scene);
        var toggle = go.AddComponent<UIToggle>();
        var check = CreateUIObject("Checkmark", scene, go).AddComponent<UIImage>();
        toggle.Checkmark = check;
        bool? last = null;
        toggle.OnValueChanged += v => last = v;

        toggle.OnPointerClick(new PointerEventData { Button = MouseButton.Left });

        Assert.True(toggle.IsOn);
        Assert.True(last);
        Assert.True(check.Enabled);

        toggle.SetIsOnWithoutNotify(false);
        Assert.False(check.Enabled);
        Assert.True(last);
    }

    [Fact]
    public void Toggle_NotInteractable_IgnoresClicks()
    {
        Scene scene = CreateScene(enable: true);
        var toggle = CreateUIObject("Toggle", scene).AddComponent<UIToggle>();
        toggle.Interactable = false;

        toggle.OnPointerClick(new PointerEventData { Button = MouseButton.Left });

        Assert.False(toggle.IsOn);
    }
    // A laser from a VR controller drives world space UI: pressing and letting go on a button clicks it.
    [Fact]
    public void WorldPointer_ClicksAWorldSpaceButtonAlongItsRay()
    {
        Float2? prevOverride = GameCanvas.ScreenSizeOverride;
        GameCanvas.ScreenSizeOverride = new Float2(1000f, 1000f);
        try
        {
            Scene scene = CreateScene(enable: true);
            var canvasGo = CreateGameObject("Canvas");
            canvasGo.Transform.Position = new Float3(0f, 0f, 2f);
            canvasGo.Transform.LocalScale = new Float3(0.01f);
            scene.Add(canvasGo);
            var canvas = canvasGo.AddComponent<GameCanvas>();
            canvas.RenderMode = RenderMode.WorldSpace;
            canvas.ReferenceResolution = new Float2(200f, 100f);

            var button = CreateUIObject("Button", scene, canvasGo);
            RectTransform rect = button.RectTransform!;
            rect.AnchorMin = rect.AnchorMax = rect.Pivot = Float2.Zero;
            rect.AnchoredPosition = new Float2(50f, 25f);
            rect.SizeDelta = new Float2(100f, 50f);
            button.AddComponent<Box>();
            var counter = button.AddComponent<ClickCounter>();

            var systemGo = CreateGameObject("Event System");
            scene.Add(systemGo);
            var system = systemGo.AddComponent<EventSystem>();

            void Point(Float3 from, bool pressed)
            {
                system.WorldPointer = new EventSystem.RayPointer { Ray = new Ray(from, Float3.UnitZ), Pressed = pressed };
                system.Update();
            }

            // Beside the button the canvas has nothing to hit.
            Point(new Float3(0.1f, 0.5f, 0f), false);
            Assert.Null(system.WorldPointerDistance);

            Point(new Float3(1f, 0.5f, 0f), false);
            Point(new Float3(1f, 0.5f, 0f), true);
            Point(new Float3(1f, 0.5f, 0f), false);

            Assert.Equal(1, counter.Presses);
            Assert.Equal(1, counter.Clicks);
            Assert.Same(button, system.Hovered);
            Assert.Equal(2f, system.WorldPointerDistance!.Value, 3);
        }
        finally { GameCanvas.ScreenSizeOverride = prevOverride; }
    }
}
