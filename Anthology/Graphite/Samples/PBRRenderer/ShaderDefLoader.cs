using System.IO;

using Prowl.Graphite.Samples;
using Prowl.Graphite.ShaderDef;


namespace Prowl.Graphite.Samples.PBRRenderer;


public static class ShaderDefLoader
{
    public static GraphicsProgram Load(GraphicsDevice device, string shaderDefPath, int passIndex = 0)
    {
        string source = File.ReadAllText(shaderDefPath);
        ShaderDefinition def = ShaderParser.Parse(source);

        // This sample compiles synchronously before ever drawing, so there's nothing to fall back to -
        // an empty (uncompiled) Variant just means "throw if resolution ever fails", same as before.
        def.Create(device, ShaderLoader.Compiler, new Variant());

        ShaderPass pass = def.Passes![passIndex];
        pass.GetVariant(0).TryGetDescription(device.BackendType, out ShaderDescription description);

        description.BlendState = pass.State.ToBlendState(BlendStateDescription.SingleDisabled);
        description.DepthStencilState = pass.State.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyLessEqual);
        description.RasterizerState = pass.State.ToRasterizerState(new(FaceCullMode.Back, FrontFace.Clockwise, true, false));

        return device.ResourceFactory.CreateGraphicsProgram(description);
    }
}
