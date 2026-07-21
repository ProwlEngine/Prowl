using System;
using System.Collections.Generic;

namespace Prowl.Graphite;

/// <summary>
/// Base device resource shared by every shader program kind. Holds the resource layouts and the
/// disposal/identity contract common to <see cref="GraphicsProgram"/> and <see cref="ComputeProgram"/>.
/// </summary>
public abstract class ShaderProgram : DeviceResource, IDisposable
{
    private readonly ResourceLayoutDescription[] _resourceLayouts;
    private readonly SetBindingMetadata[] _bindingMetadata;

    internal ShaderProgram(ResourceLayoutDescription[] resourceLayouts)
    {
        _resourceLayouts = Util.ShallowClone(resourceLayouts) ?? Array.Empty<ResourceLayoutDescription>();
        DeepCloneUniformFields(_resourceLayouts);
        _bindingMetadata = SetBindingMetadata.Build(_resourceLayouts);
    }

    /// <summary>
    /// The resource layouts declared by this program.
    /// </summary>
    public IReadOnlyList<ResourceLayoutDescription> ResourceLayouts => _resourceLayouts;

    internal ResourceLayoutDescription[] ResourceLayoutsArray => _resourceLayouts;

    /// <summary>
    /// Precomputed per-set binding metadata, indexed parallel to <see cref="ResourceLayoutsArray"/>.
    /// Built once at construction from the resource layouts.
    /// </summary>
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

    /// <summary>
    /// A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
    /// tools.
    /// </summary>
    public abstract string Name { get; set; }

    /// <summary>
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();
}
