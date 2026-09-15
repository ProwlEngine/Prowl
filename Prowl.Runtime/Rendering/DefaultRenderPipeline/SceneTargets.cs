// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;
using Texture = Prowl.Graphite.Texture;

namespace Prowl.Runtime.Rendering;

/// <summary>The scene MRT's attachments by role, resolved from the shared gbuffer graph texture.</summary>
public readonly struct SceneTargets
{
    public readonly Texture Color;
    public readonly Texture Normals;
    public readonly Texture Motion;
    public readonly Texture Depth;
    public readonly Framebuffer Framebuffer;

    public bool IsValid => Framebuffer != null;

    private SceneTargets(RenderTexture gbuffer)
    {
        Color = gbuffer.ColorTextures[0];
        Normals = gbuffer.ColorTextures[1];
        Motion = gbuffer.ColorTextures[2];
        Depth = gbuffer.DepthTexture!;
        Framebuffer = gbuffer.Framebuffer;
    }

    /// <summary>Resolves the gbuffer handle for this view and stores the result on <see cref="CameraView.Targets"/>.</summary>
    public static SceneTargets Resolve(RenderContext<CameraView> context, TextureHandle gbuffer)
    {
        var targets = new SceneTargets(context.GetRenderTexture(gbuffer));
        context.View.Targets = targets;
        return targets;
    }

    private static Sampler? s_pointClamp;

    /// <summary>Point-filtered clamp sampler for reading scene attachments (depth in particular) from shaders.</summary>
    public static Sampler PointClampSampler
    {
        get
        {
            if (s_pointClamp != null)
                return s_pointClamp;

            s_pointClamp = Graphics.Device.ResourceFactory.CreateSampler(new SamplerDescription
            {
                AddressModeU = SamplerAddressMode.Clamp,
                AddressModeV = SamplerAddressMode.Clamp,
                AddressModeW = SamplerAddressMode.Clamp,
                Filter = SamplerFilter.MinPoint_MagPoint_MipPoint,
            });
            s_pointClamp.Name = "SceneTargets Point Clamp Sampler";
            return s_pointClamp;
        }
    }
}
