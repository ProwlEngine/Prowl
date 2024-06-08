namespace Prowl.Runtime.GUI
{
    public interface ISubGizmo
    {
        bool Pick(Ray ray, Vector2 screenPos, out double t);
        GizmoResult? Update(Ray ray, Vector2 screenPos);
        void Draw();
        void SetFocused(bool focused);
    }
}