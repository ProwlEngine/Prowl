// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

public interface ISubGizmo
{
    bool Pick(Ray ray, Float2 screenPos, out float t);
    GizmoResult? Update(Ray ray, Float2 screenPos);
    void Draw(Prowl.Quill.Canvas canvas);
    void SetFocused(bool focused);
}
