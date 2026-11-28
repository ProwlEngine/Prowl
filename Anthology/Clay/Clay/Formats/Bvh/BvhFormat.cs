// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Bvh;

/// <summary>
/// Biovision BVH motion capture format.
/// </summary>
/// <remarks>
/// This format supports a single animation clip, along with the corresponding skeleton.
/// </remarks>
internal sealed class BvhFormat : IModelFormat
{
    /// <summary>
    /// Determines whether a file with the given file extension can be read by this importer.
    /// </summary>
    /// <param name="extension"> The lowercase file extension WITHOUT the leading period ('.'). </param>
    /// <returns><see langword="true"/> if the file extension is readable by this importer.</returns>
    public bool CanRead(string extension) => extension == "bvh";

    /// <summary>
    /// Determines via the initial file content whether the file can be read by this importer.
    /// </summary>
    /// <param name="initialBytes"> The initial 32 bytes of data from the file, if the file is at least that size. </param>
    /// <returns><see langword="true"/> if the file data is readable by this importer.</returns>
    public bool CanRead(ReadOnlySpan<byte> initialBytes)
    {
        // "HIERARCHY" a BVH file.
        return initialBytes.Length >= 9 &&
            initialBytes[0] == 0x48 && initialBytes[1] == 0x49 && initialBytes[2] == 0x45 && initialBytes[3] == 0x52 &&
            initialBytes[4] == 0x41 && initialBytes[5] == 0x52 && initialBytes[6] == 0x43 && initialBytes[7] == 0x48 &&
            initialBytes[8] == 0x59;
    }

    // BVH files have three or six animation channels.
    private enum BvhChannelName
    {
        PositionX,
        PositionY,
        PositionZ,
        RotationX,
        RotationY,
        RotationZ,
    }

    private static readonly Dictionary<string, BvhChannelName> _channelNameToEnum = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Xposition", BvhChannelName.PositionX },
        { "Yposition", BvhChannelName.PositionY },
        { "Zposition", BvhChannelName.PositionZ },
        { "Xrotation", BvhChannelName.RotationX },
        { "Yrotation", BvhChannelName.RotationY },
        { "Zrotation", BvhChannelName.RotationZ },
    };

    private sealed class Joint(IntermediateNode node)
    {
        public IntermediateNode Node { get; } = node;
        public List<BvhChannelName> Channels { get; } = [];
        public bool HasEndSite { get; set; }
        public IntermediateAnimationBinding PositionBinding { get; } = new IntermediateAnimationBinding
        {
            TargetNode = node,
            Property = AnimatedProperty.Position,
            Dimension = 3
        };
        public IntermediateAnimationBinding RotationBinding { get; } = new IntermediateAnimationBinding
        {
            TargetNode = node,
            Property = AnimatedProperty.Rotation,
            Dimension = 4
        };
    }

    /// <summary>
    /// Reads the source into an <see cref="IntermediateScene"/>. The source may be either
    /// <see cref="ImportContext.SourcePath"/> (when not null) or <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="context">Importer context.</param>
    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        using var reader = new BvhStreamReader(stream);
        List<Joint> joints = [];
        int rangeCount;

        // HIERARCHY
        string line = reader.ExpectLine();
        reader.ExpectToken("HIERARCHY", line.Trim());

        // One or more skeletons.
        while (true)
        {
            // ROOT <name of skeleton>
            line = reader.ExpectLine();
            var rootParts = new Range[2];
            rangeCount = BvhStreamReader.Split(line, rootParts);
            if (string.Equals(line[rootParts[0]], "MOTION", StringComparison.OrdinalIgnoreCase))
                break;
            reader.ExpectToken("ROOT", line[rootParts[0]]);
            if (rangeCount == 1)
                throw new InvalidDataException($"Expected name after 'ROOT' (line {reader.Line}).");

            // Recursively parse the skeletal structure.
            ParseJoint(reader, joints, name: line[rootParts[1]]);
        }
        if (joints.Count == 0)
            throw new InvalidDataException($"At least one joint must be defined (line {reader.Line}).");

        // Frames: 7840
        line = reader.ExpectLine();
        var framesParts = new Range[3];
        rangeCount = BvhStreamReader.Split(line, framesParts);
        reader.ExpectToken("Frames:", line[framesParts[0]]);
        if (rangeCount != 2)
            throw new InvalidDataException($"Expected one value after 'Frames:' (line {reader.Line}).");
        int frameCount = reader.ParseInt(line[framesParts[1]]);
        if (frameCount < 0)
            throw new InvalidDataException("The number of animation frames cannot be negative (line {reader.Line}).");

        // Preallocate binding lists instead of resizing them multiple times.
        foreach (Joint joint in joints)
        {
            joint.PositionBinding.Times.EnsureCapacity(frameCount);
            joint.PositionBinding.Values.EnsureCapacity(frameCount * 3);
            joint.RotationBinding.Times.EnsureCapacity(frameCount);
            joint.RotationBinding.Values.EnsureCapacity(frameCount * 4);
        }

        // Frame Time: 0.033333
        line = reader.ExpectLine();
        var ftParts = new Range[4];
        rangeCount = BvhStreamReader.Split(line, ftParts);
        reader.ExpectToken("Frame", line[ftParts[0]]);
        if (rangeCount != 3)
            throw new InvalidDataException($"Expected one value after 'Frame Time:' (line {reader.Line}).");
        reader.ExpectToken("Time:", line[ftParts[1]]);
        float timePerFrame = reader.ParseFloat(line[ftParts[2]]);
        if (timePerFrame <= 0)
            throw new InvalidDataException($"The reported frame time ({timePerFrame}) cannot be zero or negative (line {reader.Line}).");

        // The next section consists of a line per frame.
        int frame = 0;
        int totalChannelCount = joints.Sum(m => m.Channels.Count);
        var channelParts = new Range[totalChannelCount + 1];
        while (true)
        {
            // <channel value from joint 1> <channel value from joint 1> ... <channel value from joint 2> ...
            string? dataLine = reader.ReadLine();
            if (dataLine is null)
                break;
            rangeCount = BvhStreamReader.Split(dataLine, channelParts);
            if (rangeCount != totalChannelCount)
                throw new InvalidDataException($"Expected {totalChannelCount} values per MOTION line (line {reader.Line}).");

            int valueIndex = 0;
            for (int jointIndex = 0; jointIndex < joints.Count; jointIndex++)
            {
                Joint joint = joints[jointIndex];

                // Read translation and rotation values.
                bool hasTranslation = false, hasRotation = false;
                Float3 translation = joint.Node.LocalPosition;
                Quaternion rotation = Quaternion.Identity;
                for (int channelIndex = 0; channelIndex < joint.Channels.Count; channelIndex++, valueIndex++)
                {
                    float channelValue = reader.ParseFloat(dataLine.AsSpan()[channelParts[valueIndex]]);
                    switch (joint.Channels[channelIndex])
                    {
                        case BvhChannelName.PositionX:
                            translation.X = channelValue;
                            hasTranslation = true;
                            break;
                        case BvhChannelName.PositionY:
                            translation.Y = channelValue;
                            hasTranslation = true;
                            break;
                        case BvhChannelName.PositionZ:
                            translation.Z = channelValue;
                            hasTranslation = true;
                            break;

                        case BvhChannelName.RotationX:
                            rotation = rotation * Quaternion.AxisAngle(Float3.UnitX, channelValue * Maths.Deg2Rad);
                            hasRotation = true;
                            break;
                        case BvhChannelName.RotationY:
                            rotation = rotation * Quaternion.AxisAngle(Float3.UnitY, channelValue * Maths.Deg2Rad);
                            hasRotation = true;
                            break;
                        case BvhChannelName.RotationZ:
                            rotation = rotation * Quaternion.AxisAngle(Float3.UnitZ, channelValue * Maths.Deg2Rad);
                            hasRotation = true;
                            break;
                    }
                }

                // Add to the bindings.
                if (hasTranslation)
                {
                    joint.PositionBinding.Times.Add(frame * timePerFrame);
                    joint.PositionBinding.Values.Add(translation.X);
                    joint.PositionBinding.Values.Add(translation.Y);
                    joint.PositionBinding.Values.Add(translation.Z);
                }
                if (hasRotation)
                {
                    joint.RotationBinding.Times.Add(frame * timePerFrame);
                    joint.RotationBinding.Values.Add(rotation.X);
                    joint.RotationBinding.Values.Add(rotation.Y);
                    joint.RotationBinding.Values.Add(rotation.Z);
                    joint.RotationBinding.Values.Add(rotation.W);
                }
            }

            // Increment the frame count.
            frame++;
        }
        if (frameCount >= 0 && frame != frameCount)
            context.Log.Warning($"The frame count in the file ({frameCount}) doesn't match the number of frames that were read ({frame}). The file might be truncated.", nameof(BvhFormat));
        else if (frame == 0)
            context.Log.Warning("Zero animation frames were found in the file. Only the hierarchy will be imported.", nameof(BvhFormat));

        // Add every non-empty binding to an animation.
        var animation = new IntermediateAnimation { Name = Path.GetFileNameWithoutExtension(context.SourcePath) ?? "Animation" };
        foreach (Joint joint in joints)
        {
            if (joint.PositionBinding.Times.Count > 0)
                animation.Bindings.Add(joint.PositionBinding);
            if (joint.RotationBinding.Times.Count > 0)
                animation.Bindings.Add(joint.RotationBinding);
        }

        // Build a root node to match FBX and GLTF.
        var root = new IntermediateNode { Name = "<RootNode>" };
        foreach (Joint joint in joints)
        {
            if (joint.Node.Parent is null)
            {
                joint.Node.Parent = root;
                root.Children.Add(joint.Node);
            }
        }

        // Return a scene.
        var result = new IntermediateScene()
        {
            // BVH is specified to have a right-handed Y-up coordinate system.
            SourceCoordinateSystem = CoordinateSystem.RightHandedYUp,

            // Units are *not* specified, but centimeters are most common, so default to that.
            SourceUnitToMeters = 0.01f,
        };
        if (animation.Bindings.Count > 0)
            result.Animations.Add(animation);
        result.Nodes.Add(root);
        result.Nodes.AddRange(joints.Select(j => j.Node));
        return result;
    }

    /// <summary>
    /// Parses the inner lines of a JOINT.
    /// </summary>
    /// <param name="reader">The stream reader.</param>
    /// <param name="joints">A global list of joints that this method should add to.</param>
    /// <param name="name">The joint name.</param>
    /// <param name="parent">The parent joint, or null if this is a root.</param>
    /// <param name="depth">The number of ancestors.</param>
    private static void ParseJoint(BvhStreamReader reader, List<Joint> joints, string name, Joint? parent = null, int depth = 0)
    {
        if (depth >= 1024)  // Don't overflow the stack.
            throw new InvalidDataException($"Joint hierarchy too deep (line {reader.Line}).");
        var joint = new Joint(new IntermediateNode
        {
            Parent = parent?.Node,
            Name = name
        });
        if (parent is not null)
            parent.Node.Children.Add(joint.Node);
        joints.Add(joint);  // Do this before parsing children.

        // {
        string line = reader.ExpectLine();
        reader.ExpectToken("{", line.Trim());

        // OFFSET <X> <Y> <Z>
        line = reader.ExpectLine();
        var offsetParts = new Range[5];
        int rangeCount = BvhStreamReader.Split(line, offsetParts);
        if (rangeCount != 4)
            throw new InvalidDataException($"Expected three values after OFFSET (line {reader.Line}).");
        reader.ExpectToken("OFFSET", line[offsetParts[0]]);
        float x = reader.ParseFloat(line[offsetParts[1]]);
        float y = reader.ParseFloat(line[offsetParts[2]]);
        float z = reader.ParseFloat(line[offsetParts[3]]);
        joint.Node.LocalPosition = new Float3(x, y, z);

        // CHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation
        line = reader.ExpectLine();
        var channelsParts = new Range[9];
        rangeCount = BvhStreamReader.Split(line, channelsParts);
        if (rangeCount < 2 || rangeCount > 8)
            throw new InvalidDataException($"Expected 1-7 values after CHANNELS (line {reader.Line}).");
        reader.ExpectToken("CHANNELS", line[channelsParts[0]]);
        int channelCount = reader.ParseInt(line[channelsParts[1]]);
        if (channelCount != rangeCount - 2)
            throw new InvalidDataException($"Incorrect channel count after CHANNELS (line {reader.Line}).");
        for (int i = 0; i < channelCount; i ++)
        {
            if (!_channelNameToEnum.TryGetValue(line[channelsParts[2 + i]], out BvhChannelName channelName))
                throw new InvalidDataException($"Couldn't parse '{line[channelsParts[2 + i]]}' as a valid channel name (line {reader.Line}).");
            if (joint.Channels.Contains(channelName))
                throw new InvalidDataException($"Duplicate channel '{line[channelsParts[2 + i]]}' (line {reader.Line}).");
            joint.Channels.Add(channelName);
        }

        bool hasChildren = false;
        do
        {

            // JOINT LeftLeg | End Site
            line = reader.ExpectLine();
            var jointParts = new Range[2];
            rangeCount = BvhStreamReader.Split(line, jointParts);
            if (string.Equals(line[jointParts[0]], "JOINT", StringComparison.OrdinalIgnoreCase))
            {
                if (rangeCount < 2)
                    throw new InvalidDataException($"Expected joint name after 'JOINT' (line {reader.Line}).");
                if (joint.HasEndSite)
                    throw new InvalidDataException($"Unexpected 'JOINT' after 'End Site' (line {reader.Line}).");

                // Recursively parse the skeletal structure.
                ParseJoint(reader, joints, name: line[jointParts[1]], parent: joint, depth: depth + 1);
                hasChildren = true;
            }
            else if (rangeCount >= 2 &&
                string.Equals(line[jointParts[0]], "End", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(line[jointParts[1]], "Site", StringComparison.OrdinalIgnoreCase))
            {
                if (hasChildren || joint.HasEndSite)
                    throw new InvalidDataException($"Unexpected 'End Site' (line {reader.Line}).");
                ParseEndSite(reader, joint);
            }
            else if (string.Equals(line[jointParts[0]], "}", StringComparison.OrdinalIgnoreCase))
            {
                if (rangeCount > 1)
                    throw new InvalidDataException($"Unexpected token after 'JOINT' (line {reader.Line}).");
                break;
            }
            else
                throw new InvalidDataException($"Expected 'JOINT <Name>' or 'End Site' but was '{line}' (line {reader.Line}).");

        } while (true);
    }


    /// <summary>
    /// Parses the inner lines of "End Site".
    /// </summary>
    /// <param name="reader">The stream reader.</param>
    /// <param name="joint">The parent joint.</param>
    private static void ParseEndSite(BvhStreamReader reader, Joint joint)
    {
        var parts = new Range[5];   // Maximum number of parts is 4 + 1 to detect overflow.

        // {
        string line = reader.ExpectLine();
        reader.ExpectToken("{", line.Trim());

        // OFFSET <X> <Y> <Z>
        line = reader.ExpectLine();
        int rangeCount = BvhStreamReader.Split(line, parts);
        if (rangeCount != 4)
            throw new InvalidDataException($"Expected three values after OFFSET (line {reader.Line}).");
        reader.ExpectToken("OFFSET", line[parts[0]]);
        float x = reader.ParseFloat(line[parts[1]]);
        float y = reader.ParseFloat(line[parts[2]]);
        float z = reader.ParseFloat(line[parts[3]]);
        joint.HasEndSite = true;
        joint.Node.Metadata.Add("EndSite", new Float3(x, y, z));

        // }
        line = reader.ExpectLine();
        reader.ExpectToken("}", line.Trim());
    }
}
