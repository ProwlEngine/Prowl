using System.Collections.Generic;

namespace Prowl.Graphite.Vk;

internal unsafe sealed partial class VkDescriptorBinder
{
    private readonly HashSet<PropertyID> _reportedSkippedUniforms = new();

    private void ReportSkippedUniforms(PropertyID block, UniformBlockField[]? fields)
    {
        if (!_gd.ValidationEnabled || fields == null)
            return;

        foreach (UniformBlockField field in fields)
        {
            if (!_cbOwner.ActiveProperties.Entries.TryGetValue(field.Name, out PropertyEntry? entry) || entry.Kind != PropertyEntryKind.Uniform)
                continue;

            if (_reportedSkippedUniforms.Add(field.Name))
            {
                string label = PropertyID.ToString(field.Name) ?? field.Name.ToString();
                string blockLabel = PropertyID.ToString(block) ?? block.ToString();
                _gd.OnWarning?.Invoke($"Uniform '{label}' targets manually specified uniform buffer '{blockLabel}'. Skipping.");
            }
        }
    }
}
