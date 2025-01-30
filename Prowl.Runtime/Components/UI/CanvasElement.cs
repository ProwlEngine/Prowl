// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Runtime.UI;

[RequireComponent(typeof(RectTransform))]
public abstract class CanvasElement : MonoBehaviour
{
    private RectTransform? _rectTransform;
    public RectTransform RectTransform => (_rectTransform == null) ?_rectTransform = GetComponent<RectTransform>() : _rectTransform;

    public abstract void Draw(UIDrawList drawList);

    public override void OnValidate() => RectTransform?.TargetCanvas.SetCanvasDirty();
}
