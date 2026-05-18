namespace Prowl.Veldrid.OpenGL;

internal class OpenGLResourceLayout : ResourceLayout
{
    private bool _disposed;

    public ResourceLayoutElementDescription[] Elements { get; }

    public override string Name { get; set; }

    public override bool IsDisposed => _disposed;

    public OpenGLResourceLayout(ref ResourceLayoutDescription description)
        : base(ref description)
    {
        Elements = Util.ShallowClone(description.Elements);
    }

    /// <summary>
    /// Finds the index of a Sampler element in this layout whose BindingIndex and Name
    /// match the supplied texture element. Returns -1 if no match exists.
    /// </summary>
    public int FindCombinedSamplerIndex(int textureBindingIndex, string textureName)
    {
        for (int i = 0; i < Elements.Length; i++)
        {
            ref ResourceLayoutElementDescription e = ref Elements[i];
            if (e.Kind == ResourceKind.Sampler
                && e.BindingIndex == textureBindingIndex
                && e.Name == textureName)
            {
                return i;
            }
        }
        return -1;
    }

    public override void Dispose()
    {
        _disposed = true;
    }
}
