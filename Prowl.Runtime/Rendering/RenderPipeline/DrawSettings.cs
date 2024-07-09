namespace Prowl.Runtime.RenderPipelines
{
    public enum SortMode { FrontToBack, BackToFront }

    public struct DrawSettings(string? renderOrder, Material? overrideMaterial = null, Material? fallback = null)
    {
        public readonly string? RenderOrder = renderOrder;
        public readonly Material? Fallback = fallback;
        public readonly Material? OverrideMaterial = overrideMaterial;
    }
}