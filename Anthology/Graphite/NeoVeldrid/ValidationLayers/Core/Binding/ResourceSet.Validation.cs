using System.Diagnostics;

namespace NeoVeldrid;

public abstract partial class ResourceSet
{
#if VALIDATE_USAGE
    internal ResourceLayout Layout { get; private set; }
    internal BindableResource[] Resources { get; private set; }
#endif

    [Conditional("VALIDATE_USAGE")]
    private void ResourceSet_StoreLayoutAndResources(ref ResourceSetDescription description)
    {
#if VALIDATE_USAGE
        Layout = description.Layout;
        Resources = description.BoundResources;
#endif
    }
}
