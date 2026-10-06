using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe sealed partial class VkDescriptorBinder
{
    private void ValidateBackedUbo(PropertyID name, uint blockSize, DeviceBufferRange target)
    {
        if (!_gd.ValidationEnabled)
            return;

        string label = PropertyID.ToString(name) ?? name.ToString();
        if ((target.Buffer.Usage & BufferUsage.UniformBuffer) == 0)
            throw new RenderException($"Uniform block '{label}' is backed by a buffer without {nameof(BufferUsage)}.{nameof(BufferUsage.UniformBuffer)}.");
        if ((ulong)target.Offset + blockSize > target.Buffer.SizeInBytes || target.SizeInBytes < blockSize)
            throw new RenderException($"Uniform block '{label}' needs {blockSize} bytes at offset {target.Offset}, which does not fit the bound range or buffer.");
        if (target.Offset % _gd.UniformBufferMinOffsetAlignment != 0)
            throw new RenderException($"Uniform block '{label}' is backed at offset {target.Offset}, which is not a multiple of the device's uniform offset alignment {_gd.UniformBufferMinOffsetAlignment}.");
    }
}
