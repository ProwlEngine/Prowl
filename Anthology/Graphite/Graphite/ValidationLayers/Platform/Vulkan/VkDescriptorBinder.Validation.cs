using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

internal sealed unsafe partial class VkDescriptorBinder
{
    private readonly HashSet<PropertyID> _reportedSkippedUniforms = [];

    private void ReportSkippedUniform(PropertyID block, PropertyID uniform)
    {
        if (!_gd.ValidationEnabled || !uniform.IsValid || !_reportedSkippedUniforms.Add(uniform))
            return;

        string label = PropertyID.ToString(uniform) ?? uniform.ToString();
        string blockLabel = PropertyID.ToString(block) ?? block.ToString();
        _gd.OnWarning?.Invoke($"Uniform '{label}' targets manually specified uniform buffer '{blockLabel}'. Skipping.");
    }
}
