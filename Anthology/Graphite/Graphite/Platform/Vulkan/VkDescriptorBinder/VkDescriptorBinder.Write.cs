using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe sealed partial class VkDescriptorBinder
{
    private void PrepareResolvedTextures(ResourceLayoutElementDescription[] elements, bool isGraphics)
    {
        for (int i = 0; i < elements.Length; i++)
        {
            ref ResolvedBinding r = ref _resolveScratch[i];
            if (r.Kind != ResourceKind.TextureReadOnly && r.Kind != ResourceKind.TextureReadWrite)
                continue;

            VkTexture tex = r.View.Target;
            ImageLayout current = VkBarriers.CurrentLayout(_cbOwner, tex);

            if (r.Kind == ResourceKind.TextureReadOnly)
            {
                if (current != ImageLayout.ShaderReadOnlyOptimal && current != ImageLayout.DepthStencilReadOnlyOptimal)
                {
                    throw new RenderException(
                        $"Texture '{tex.Name}' is bound for sampling while in layout {current}. " +
                        "Declare it as a Sampled input of the pass, or transition it to Sampled.");
                }
                r.Layout = current;
                continue;
            }

            r.Layout = ImageLayout.General;
            if (current == ImageLayout.General || _cbOwner.IsTemporaryStorage(tex))
                continue;

            if (isGraphics || _cbOwner.StateOf(tex) != null)
            {
                throw new RenderException(
                    $"Texture '{tex.Name}' is bound for storage while in layout {current}. " +
                    "Declare it as a Storage input or output of the pass.");
            }

            _cbOwner.BeginTemporaryStorage(tex);
        }
    }

    private void WriteDescriptorsFromScratch(
        int setIdx, ResourceLayoutElementDescription[] elements, DescriptorSet dstSet, ShaderProgram reportProgram)
    {
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[MaxSetElements];
        DescriptorBufferInfo* bufInfos = stackalloc DescriptorBufferInfo[MaxSetElements];
        DescriptorImageInfo* imgInfos = stackalloc DescriptorImageInfo[MaxSetElements];
        int writeCount = 0, bufIdx = 0, imgIdx = 0;

        for (int i = 0; i < elements.Length; i++)
        {
            ref ResolvedBinding r = ref _resolveScratch[i];
            ref ResourceLayoutElementDescription elem = ref elements[i];

            if (r.Missing)
                ReportMissing(in elem, (uint)setIdx, reportProgram);

            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = dstSet,
                DstBinding = (uint)elem.BindingIndex,
                DescriptorCount = 1,
            };

            bool described = r.Kind switch
            {
                ResourceKind.UniformBuffer
                or ResourceKind.StructuredBufferReadOnly
                or ResourceKind.StructuredBufferReadWrite => DescribeBuffer(in r, ref write, bufInfos, ref bufIdx),

                ResourceKind.TextureReadOnly
                or ResourceKind.TextureReadWrite
                or ResourceKind.Sampler => DescribeImage(in r, ref write, imgInfos, ref imgIdx),

                _ => false,
            };

            if (described)
                writes[writeCount++] = write;
        }

        if (writeCount > 0)
            _gd.Vk.UpdateDescriptorSets(_gd.Device, (uint)writeCount, writes, 0, null);
    }

    private static bool DescribeBuffer(
        in ResolvedBinding r, ref WriteDescriptorSet write, DescriptorBufferInfo* bufInfos, ref int bufIdx)
    {
        bufInfos[bufIdx] = new DescriptorBufferInfo
        {
            Buffer = r.Buffer.DeviceBuffer,
            Offset = r.DescOffset,
            Range = r.DescRange,
        };

        // A uniform buffer's per-draw offset travels as a dynamic offset, so its descriptor offset is 0.
        write.DescriptorType = r.Kind == ResourceKind.UniformBuffer
            ? DescriptorType.UniformBufferDynamic
            : DescriptorType.StorageBuffer;
        write.PBufferInfo = &bufInfos[bufIdx++];
        return true;
    }

    private static bool DescribeImage(
        in ResolvedBinding r, ref WriteDescriptorSet write, DescriptorImageInfo* imgInfos, ref int imgIdx)
    {
        switch (r.Kind)
        {
            case ResourceKind.TextureReadOnly:
                imgInfos[imgIdx] = new DescriptorImageInfo
                {
                    ImageView = r.View.ImageView,
                    ImageLayout = r.Layout,
                    Sampler = r.Combined ? r.Sampler.DeviceSampler : default,
                };
                write.DescriptorType = r.Combined ? DescriptorType.CombinedImageSampler : DescriptorType.SampledImage;
                break;

            case ResourceKind.TextureReadWrite:
                imgInfos[imgIdx] = new DescriptorImageInfo
                {
                    ImageView = r.View.ImageView,
                    ImageLayout = ImageLayout.General,
                };
                write.DescriptorType = DescriptorType.StorageImage;
                break;

            default:
                imgInfos[imgIdx] = new DescriptorImageInfo
                {
                    Sampler = r.Sampler.DeviceSampler,
                };
                write.DescriptorType = DescriptorType.Sampler;
                break;
        }

        write.PImageInfo = &imgInfos[imgIdx++];
        return true;
    }

    private void ReportMissing(in ResourceLayoutElementDescription elem, uint setIdx, ShaderProgram reportProgram)
    {
        _gd.OnMissingProperty?.Invoke(reportProgram, elem.Name, elem.Kind, setIdx, elem.BindingIndex);
    }
}
