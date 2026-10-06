using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Base for shader programs; holds resource layouts and disposal contract.
/// </summary>
public abstract class ShaderProgram : GraphicsResource
{
    private readonly ResourceLayoutDescription[] _resourceLayouts;
    private readonly SetBindingMetadata[] _bindingMetadata;

    internal ShaderProgram(ResourceLayoutDescription[] resourceLayouts)
    {
        _resourceLayouts = Util.ShallowClone(resourceLayouts);
        DeepCloneUniformFields(_resourceLayouts);
        _bindingMetadata = SetBindingMetadata.Build(_resourceLayouts);
        StorageWriteElements = FindStorageWriteElements(_resourceLayouts);
    }

    internal (PropertyID Name, ResourceKind Kind)[] StorageWriteElements { get; }

    private static (PropertyID Name, ResourceKind Kind)[] FindStorageWriteElements(ResourceLayoutDescription[] layouts)
    {
        List<(PropertyID, ResourceKind)> found = new();
        foreach (ResourceLayoutDescription layout in layouts)
        {
            if (layout.Elements == null) continue;
            foreach (ResourceLayoutElementDescription element in layout.Elements)
            {
                if (element.Kind is ResourceKind.StructuredBufferReadWrite or ResourceKind.TextureReadWrite)
                    found.Add((element.Name, element.Kind));
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// Resource layouts declared by this program.
    /// </summary>
    public IReadOnlyList<ResourceLayoutDescription> ResourceLayouts => _resourceLayouts;

    internal ResourceLayoutDescription[] ResourceLayoutsArray => _resourceLayouts;

    internal SetBindingMetadata[] BindingMetadata => _bindingMetadata;

    private protected static void DeepCloneUniformFields(ResourceLayoutDescription[] layouts)
    {
        for (int i = 0; i < layouts.Length; i++)
        {
            ResourceLayoutElementDescription[] elements = layouts[i].Elements;
            if (elements == null) continue;
            ResourceLayoutElementDescription[] clonedElements = new ResourceLayoutElementDescription[elements.Length];
            for (int j = 0; j < elements.Length; j++)
            {
                ResourceLayoutElementDescription elem = elements[j];
                if (elem.UniformFields != null)
                {
                    elem.UniformFields = (UniformBlockField[])elem.UniformFields.Clone();
                }
                clonedElements[j] = elem;
            }
            layouts[i].Elements = clonedElements;
        }
    }
}
