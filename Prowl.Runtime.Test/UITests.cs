// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;

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
}
