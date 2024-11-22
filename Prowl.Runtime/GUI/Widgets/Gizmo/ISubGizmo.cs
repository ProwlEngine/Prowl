// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GUI;

public interface ISubGizmo
{
    bool Pick(Ray ray, Vector2 screenPos, out double t);
    GizmoResult? Update(Ray ray, Vector2 screenPos);
    void Draw();
    void SetFocused(bool focused);
}
