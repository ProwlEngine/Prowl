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
        new("Directional lights shine along Forward", ctx => ctx.RewriteEchoAssets(TurnDirectionalLights)),
        new("Joint motors and limits measured along their own body", ctx => ctx.RewriteEchoAssets(FlipJointDirections)),
    ];

    // Joint motors, hinge angles and slider distances used to be measured from the connected body's side, so a
    // positive motor moved this body backwards along its axis. They now mean this body along +axis, so each
    // saved range is mirrored and each motor speed negated to keep the same motion.
    private static readonly (string Type, string[] Ranges, string[] Speeds)[] s_jointFields =
    [
        ("Prowl.Runtime.HingeJoint,", ["minAngleDegrees", "maxAngleDegrees"], ["motorTargetVelocity"]),
        ("Prowl.Runtime.PrismaticJoint,", ["minDistance", "maxDistance"], ["motorTargetVelocity"]),
        ("Prowl.Runtime.HingeAngleConstraint,", ["minAngle", "maxAngle"], []),
        ("Prowl.Runtime.UniversalJoint,", [], ["motorTargetVelocity"]),
        ("Prowl.Runtime.LinearMotorConstraint,", [], ["targetVelocity"]),
        ("Prowl.Runtime.AngularMotorConstraint,", [], ["targetVelocity"]),
    ];

    private static EchoObject? FlipJointDirections(EchoObject echo)
    {
        bool changed = false;
        FlipJointDirections(echo, ref changed);
        return changed ? echo : null;
    }

    private static void FlipJointDirections(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                FlipJointDirections(item, ref changed);
            return;
        }

        if (tag.TagType != EchoType.Compound) return;

        if (tag.TryGet("$type", out EchoObject? type) && type!.TagType == EchoType.String)
        {
            foreach ((string name, string[] ranges, string[] speeds) in s_jointFields)
            {
                if (!type.StringValue.StartsWith(name, StringComparison.Ordinal)) continue;

                if (ranges.Length == 2 && tag.TryGet(ranges[0], out EchoObject? min) && tag.TryGet(ranges[1], out EchoObject? max))
                {
                    float low = min!.FloatValue, high = max!.FloatValue;
                    tag[ranges[0]] = new EchoObject(-high);
                    tag[ranges[1]] = new EchoObject(-low);
                    changed = true;
                }

                foreach (string speed in speeds)
                {
                    if (!tag.TryGet(speed, out EchoObject? value)) continue;
                    tag[speed] = new EchoObject(-value!.FloatValue);
                    changed = true;
                }
            }
        }

        foreach (string child in tag.GetNames().ToList())
            FlipJointDirections(tag[child], ref changed);
    }

    private static readonly Regex s_fragmentInclude = new(@"#include\s+""Fragment""");

    // A directional light used to shine along -Forward. It now shines along Forward like a spot light, so every
    // saved one is turned half way round its local up axis, which points it the other way and keeps it level.
    private static EchoObject? TurnDirectionalLights(EchoObject echo)
    {
        bool changed = false;
        TurnDirectionalLights(echo, ref changed);
        return changed ? echo : null;
    }

    private static void TurnDirectionalLights(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            foreach (EchoObject item in tag.List)
                TurnDirectionalLights(item, ref changed);
            return;
        }

        if (tag.TagType != EchoType.Compound) return;

        if (HasDirectionalLight(tag)
            && tag.TryGet("Transform", out EchoObject? transform)
            && transform!.TryGet("_localRotation", out EchoObject? rotation)
            && rotation!.TryGet("X", out EchoObject? x) && rotation.TryGet("Y", out EchoObject? y)
            && rotation.TryGet("Z", out EchoObject? z) && rotation.TryGet("W", out EchoObject? w))
        {
            // The rotation times a half turn about Y, written out for the quaternion (0, 1, 0, 0).
            float qx = x!.FloatValue, qy = y!.FloatValue, qz = z!.FloatValue, qw = w!.FloatValue;
            rotation["X"] = new EchoObject(-qz);
            rotation["Y"] = new EchoObject(qw);
            rotation["Z"] = new EchoObject(qx);
            rotation["W"] = new EchoObject(-qy);
            changed = true;
        }

        foreach (string name in tag.GetNames().ToList())
            TurnDirectionalLights(tag[name], ref changed);
    }

    private static bool HasDirectionalLight(EchoObject gameObject)
    {
        if (!gameObject.TryGet("Components", out EchoObject? components) || components!.TagType != EchoType.List) return false;
        foreach (EchoObject component in components.List)
            if (component.TagType == EchoType.Compound && component.TryGet("$type", out EchoObject? type)
                && type!.StringValue.StartsWith("Prowl.Runtime.DirectionalLight,", StringComparison.Ordinal))
                return true;
        return false;
    }

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
