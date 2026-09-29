// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Prowl.Echo;

namespace Prowl.Editor.Migration.Releases;

internal sealed class Release_1_0_Preview5 : MigrationRelease
{
    public override string Name => "1.0-preview.5";

    public override MigrationStep[] Steps =>
    [
        new("Asset references to $asset", ctx => ctx.RewriteEcho(ConvertAssetReferences)),
        new("Fragment include renamed to ProwlCG", ctx => ctx.RewriteText(s => s_fragmentInclude.Replace(s, "#include \"ProwlCG\""), ".shader", ".glsl")),
    ];

    private static readonly Regex s_fragmentInclude = new(@"#include\s+""Fragment""");

    private static EchoObject? ConvertAssetReferences(EchoObject echo)
    {
        bool changed = false;
        EchoObject result = ConvertTag(echo, ref changed);
        return changed ? result : null;
    }

    // An asset reference used to be {"AssetID": guid}, with the asset inline under "Instance" when it had no guid.
    // It is now {"$asset": guid}. An asset copied inline used to carry its AssetID and AssetPath, which it no longer does.
    private static EchoObject ConvertTag(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            for (int i = 0; i < tag.List.Count; i++)
            {
                EchoObject item = tag.List[i];
                EchoObject converted = ConvertTag(item, ref changed);
                if (!ReferenceEquals(item, converted)) tag[i] = converted;
            }
            return tag;
        }

        if (tag.TagType != EchoType.Compound) return tag;

        foreach (string name in tag.GetNames().ToList())
        {
            EchoObject child = tag[name];
            EchoObject converted = ConvertTag(child, ref changed);
            if (!ReferenceEquals(child, converted)) tag[name] = converted;
        }

        if (!tag.TryGet("AssetID", out EchoObject? id)) return tag;
        changed = true;

        List<string> content = tag.GetNames().Where(n => n is not ("AssetID" or "$type" or "$id")).ToList();
        if (content.Count == 0 || (content.Count == 1 && content[0] == "Instance"))
        {
            if (Guid.TryParse(id!.StringValue, out Guid guid) && guid != Guid.Empty)
            {
                var stub = EchoObject.NewCompound();
                stub["$asset"] = new EchoObject(guid.ToString());
                return stub;
            }

            if (!tag.TryGet("Instance", out EchoObject? instance)) return new EchoObject(EchoType.Null, null);
            tag.Remove("Instance");
            return instance!;
        }

        tag.Remove("AssetID");
        tag.Remove("AssetPath");
        return tag;
    }
}
