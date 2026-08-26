using Prowl.Runtime.Audio;

namespace Prowl.Editor.Importers;

/// <summary>
/// Imports .audiomixer files, Echo-serialized <see cref="AudioMixer"/> assets.
/// </summary>
/// <remarks>
/// Groups are registered as sub-assets so an AudioSource can reference one directly. They are keyed on
/// the identity persisted with each group rather than on its name or position, so renaming a group or
/// inserting one above it leaves every existing reference pointing at the same bus.
/// </remarks>
[ImporterFor(".audiomixer")]
public class AudioMixerImporter : AssetImporter
{
    public override int Version => 1;

    public override bool Import(ImportContext ctx)
    {
        if (!ImportHelper.ImportEcho<AudioMixer>(ctx, "audio mixer"))
            return false;

        if (ctx.MainAsset is not AudioMixer mixer)
            return false;

        for (int i = 0; i < mixer.Groups.Count; i++)
        {
            AudioMixerGroup group = mixer.Groups[i];

            if (group == null) continue;

            // Recorded before the sub-asset is registered, so it is part of what gets cached for the
            // group. Without it a group loaded on its own cannot tell what it feeds into.
            group.SetOwningMixer(ctx.AssetGuid);
            ctx.AddSubAsset(group.GroupName, group, SubAssetIdentity.Key(group.Identity));
        }

        return true;
    }
}
