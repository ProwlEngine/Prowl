namespace Prowl.Runtime.RenderPipelines
{
    public enum SortMode { FrontToBack, BackToFront }

    public struct DrawSettings(string shaderTagId, SortMode sortingMode, Material? overrideMaterial = null, Material? fallback = null)
    {
        public readonly string ShaderTagId = shaderTagId;
        public readonly Material? Fallback = fallback;
        public readonly Material? OverrideMaterial = overrideMaterial;
        public readonly SortMode SortingMode = sortingMode;
    }
}