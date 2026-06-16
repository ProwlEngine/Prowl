using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


// Will use slang so no need for abstraction
public class VulkanCompiler : CompilerModule
{
    private TargetDescription _target;
    public TargetDescription Target => _target;

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;


    public VulkanCompiler(string profileString = "spirv_1_5")
    {
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Spirv
        };
    }

    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler) =>
        SlangReflector.BuildDescription(linkedComponent, layoutIndex, handler, entryPointNameOverride: "main");
}
