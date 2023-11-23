namespace Prowl.Runtime.ImGUI.ImGuizmo
{
    public enum OPERATION
    {
        TranslateX = unchecked((int)(1U << 0)),
        TranslateY = unchecked((int)(1U << 1)),
        TranslateZ = unchecked((int)(1U << 2)),
        RotateX = unchecked((int)(1U << 3)),
        RotateY = unchecked((int)(1U << 4)),
        RotateZ = unchecked((int)(1U << 5)),
        RotateScreen = unchecked((int)(1U << 6)),
        ScaleX = unchecked((int)(1U << 7)),
        ScaleY = unchecked((int)(1U << 8)),
        ScaleZ = unchecked((int)(1U << 9)),
        Bounds = unchecked((int)(1U << 10)),
        ScaleXu = unchecked((int)(1U << 11)),
        ScaleYu = unchecked((int)(1U << 12)),
        ScaleZu = unchecked((int)(1U << 13)),
        Translate = unchecked(7),
        Rotate = unchecked(120),
        Scale = unchecked(896),
        Scaleu = unchecked(14336),
        Universal = unchecked(14463),
    }
}
