// Based on: https://github.com/urholaukkarinen/transform-gizmo - Dual licensed under MIT and Apache 2.0.

using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

public interface ISubGizmo
{
    bool Pick(Ray ray, Float2 screenPos, out float t);
    GizmoResult? Update(Ray ray, Float2 screenPos);
    void Draw(Prowl.Quill.Canvas canvas);
    void SetFocused(bool focused);
}
