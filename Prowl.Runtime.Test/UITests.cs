// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class UITests : RuntimeTestBase
{
    private sealed class ClickCounter : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
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

    private sealed class DragCatcher : MonoBehaviour, IDragHandler
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
}
