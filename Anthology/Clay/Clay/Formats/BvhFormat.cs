// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats;

/// <summary>
/// Biovision BVH motion capture format.
/// </summary>
/// <remarks>
/// This format supports a single animation clip, along with the corresponding skeleton.
/// </remarks>
internal sealed class BvhFormat : IModelFormat
{
    private enum BvhChannelName
    {
        Xposition,
        Yposition,
        Zposition,
        Zrotation,
        Yrotation,
        Xrotation
    }

    private sealed class Joint(IntermediateNode node)
    {
        public IntermediateNode Node { get; } = node;
        public List<BvhChannelName> Channels { get; } = [];
        public bool HasEndSite { get; set; }
        public Float3 EndSite { get; set; }
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


    /// <summary>Format token this importer handles (e.g. <c>"gltf"</c>).</summary>
    public string Token => "bvh";

    /// <summary>True when this importer can handle the supplied format token.</summary>
    public bool CanRead(string formatToken) => formatToken == "bvh";

    /// <summary>
    /// Reads the source into an <see cref="IntermediateScene"/>. The source may be either
    /// <see cref="ImportContext.SourcePath"/> (when not null) or <paramref name="stream"/>.
    /// </summary>
    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        List<Joint> joints = [];
        int rangeCount;

        // HIERARCHY
        string line = ExpectLine(reader);
        ExpectToken("HIERARCHY", line.Trim());

        // One or more skeletons.
        while (true)
        {
            // ROOT <name of skeleton>
            line = ExpectLine(reader);
            var rootParts = new Range[2];
            rangeCount = Split(line, rootParts);
            if (string.Equals(line[rootParts[0]], "MOTION", StringComparison.OrdinalIgnoreCase))
                break;
            ExpectToken("ROOT", line[rootParts[0]]);
            if (rangeCount == 1)
                throw new FormatException($"Expected name after 'ROOT'.");

            // Recursively parse the skeletal structure.
            ParseJoint(reader, joints, name: line[rootParts[1]]);
        }

        // Frames: 7840
        line = ExpectLine(reader);
        var framesParts = new Range[3];
        rangeCount = Split(line, framesParts);
        ExpectToken("Frames:", line[framesParts[0]]);
        if (rangeCount != 2)
            throw new FormatException($"Expected one value after 'Frames:'.");
        int frameCount = Parse<int>(line[framesParts[1]]);
        if (frameCount < 0)
            context.Log.Warning($"The number of animation frames is negative. Ignoring.", nameof(BvhFormat));

        // Frame Time: 0.033333
        line = ExpectLine(reader);
        var ftParts = new Range[4];
        rangeCount = Split(line, ftParts);
        ExpectToken("Frame", line[ftParts[0]]);
        if (rangeCount != 3)
            throw new FormatException($"Expected one value after 'Frame Time:'.");
        ExpectToken("Time:", line[ftParts[1]]);
        float timePerFrame = Parse<float>(line[ftParts[2]]);
        if (timePerFrame <= 0)
            throw new FormatException($"The reported frame time cannot be negative ({timePerFrame}).");

        // The next sections consists of a line per frame.
        int frame = 0;
        int totalChannelCount = joints.Sum(m => m.Channels.Count);
        var channelParts = new Range[totalChannelCount + 1];
        while (true)
        {
            // <channel value from joint 1> <channel value from joint 1> ... <channel value from joint 2> ...
            string? dataLine = reader.ReadLine();
            if (dataLine is null)
                break;
            // Skip blank lines.
            if (string.IsNullOrWhiteSpace(dataLine))
                continue;
            rangeCount = Split(dataLine, channelParts);
            if (rangeCount != totalChannelCount)
                throw new FormatException($"Expected {totalChannelCount} values per MOTION line.");

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
                    float channelValue = Parse<float>(dataLine[channelParts[valueIndex]]);
                    switch (joint.Channels[channelIndex])
                    {
                        case BvhChannelName.Xposition:
                            translation.X = channelValue;
                            hasTranslation = true;
                            break;
                        case BvhChannelName.Yposition:
                            translation.Y = channelValue;
                            hasTranslation = true;
                            break;
                        case BvhChannelName.Zposition:
                            translation.Z = channelValue;
                            hasTranslation = true;
                            break;

                        case BvhChannelName.Xrotation:
                            rotation = rotation * Quaternion.AxisAngle(Float3.UnitX, channelValue * Maths.Deg2Rad);
                            hasRotation = true;
                            break;
                        case BvhChannelName.Yrotation:
                            rotation = rotation * Quaternion.AxisAngle(Float3.UnitY, channelValue * Maths.Deg2Rad);
                            hasRotation = true;
                            break;
                        case BvhChannelName.Zrotation:
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
        if (frame != frameCount)
            context.Log.Warning($"The frame count in the file ({frameCount}) doesn't match the number of frames that were read ({frame}). The file might be truncated.", nameof(BvhFormat));

        // Add every non-empty binding to an animation.
        var animation = new IntermediateAnimation { Name = "Animation" };
        foreach (Joint joint in joints)
        {
            if (joint.PositionBinding.Times.Count > 0)
                animation.Bindings.Add(joint.PositionBinding);
            if (joint.RotationBinding.Times.Count > 0)
                animation.Bindings.Add(joint.RotationBinding);
        }

        // Return a scene.
        var result = new IntermediateScene { Animations = { animation } };
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
    private static void ParseJoint(StreamReader reader, List<Joint> joints, string name, Joint? parent = null)
    {
        var joint = new Joint(new IntermediateNode
        {
            Parent = parent?.Node,
            Name = name
        });
        if (parent is not null)
            parent.Node.Children.Add(joint.Node);
        joints.Add(joint);  // Do this before parsing children.

        // {
        string line = ExpectLine(reader);
        ExpectToken("{", line.Trim());

        // OFFSET <X> <Y> <Z>
        line = ExpectLine(reader);
        var offsetParts = new Range[5];
        int rangeCount = Split(line, offsetParts);
        if (rangeCount != 4)
            throw new FormatException($"Expected three values after OFFSET.");
        ExpectToken("OFFSET", line[offsetParts[0]]);
        float x = Parse<float>(line[offsetParts[1]]);
        float y = Parse<float>(line[offsetParts[2]]);
        float z = Parse<float>(line[offsetParts[3]]);
        joint.Node.LocalPosition = new Float3(x, y, z);

        // CHANNELS 6 Xposition Yposition Zposition Zrotation Yrotation Xrotation
        line = ExpectLine(reader);
        var channelsParts = new Range[9];
        rangeCount = Split(line, channelsParts);
        if (rangeCount < 2 || rangeCount > 8)
            throw new FormatException($"Expected 1-7 values after CHANNELS.");
        ExpectToken("CHANNELS", line[channelsParts[0]]);
        int channelCount = Parse<int>(line[channelsParts[1]]);
        if (channelCount != rangeCount - 2)
            throw new FormatException($"Incorrect channel count after CHANNELS.");
        for (int i = 0; i < channelCount; i ++)
        {
            if (!Enum.TryParse(line[channelsParts[2 + i]], ignoreCase: false, out BvhChannelName channelName))
                throw new FormatException($"Couldn't parse '{line[channelsParts[2 + i]]}' as a valid channel name.");
            joint.Channels.Add(channelName);
        }

        bool hasChildren = false;
        do
        {

            // JOINT LeftLeg | End Site
            line = ExpectLine(reader);
            var jointParts = new Range[2];
            rangeCount = Split(line, jointParts);
            if (string.Equals(line[jointParts[0]], "JOINT", StringComparison.OrdinalIgnoreCase))
            {
                if (rangeCount < 2)
                    throw new FormatException($"Expected joint name after JOINT.");

                // Recursively parse the skeletal structure.
                ParseJoint(reader, joints, name: line[jointParts[1]], parent: joint);
                hasChildren = true;
            }
            else if (rangeCount >= 2 &&
                string.Equals(line[jointParts[0]], "End", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(line[jointParts[1]], "Site", StringComparison.OrdinalIgnoreCase))
            {
                if (hasChildren)
                    throw new FormatException($"Unexpected 'End Site' after 'JOINT'.");
                ParseEndSite(reader, joint);
            }
            else if (string.Equals(line[jointParts[0]], "}", StringComparison.OrdinalIgnoreCase))
                break;
            else
                throw new FormatException($"Expected JOINT or 'End Site' but was '{line[jointParts[0]]}'.");

        } while (true);
    }


    /// <summary>
    /// Parses the inner lines of "End Site".
    /// </summary>
    /// <param name="reader">The stream reader.</param>
    /// <param name="joint">The parent joint.</param>
    private static void ParseEndSite(StreamReader reader, Joint joint)
    {
        var parts = new Range[5];   // Maximum number of parts is 4 + 1 to detect overflow.

        // {
        string line = ExpectLine(reader);
        ExpectToken("{", line.Trim());

        // OFFSET <X> <Y> <Z>
        line = ExpectLine(reader);
        int rangeCount = Split(line, parts);
        if (rangeCount != 4)
            throw new FormatException($"Expected three values after OFFSET.");
        ExpectToken("OFFSET", line[parts[0]]);
        float x = Parse<float>(line[parts[1]]);
        float y = Parse<float>(line[parts[2]]);
        float z = Parse<float>(line[parts[3]]);
        joint.HasEndSite = true;
        joint.EndSite = new Float3(x, y, z);

        // }
        line = ExpectLine(reader);
        ExpectToken("}", line.Trim());
    }


    /// <summary>Reads a non-empty line from the stream reader (throws if it's empty).</summary>
    private static string ExpectLine(StreamReader reader)
    {
        do
        {
            string? line = reader.ReadLine() ?? throw new FormatException("Unexpected EOF while parsing.");
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        } while (true);
    }

    /// <summary>Compares two string and throws an error if they are different.</summary>
    private static void ExpectToken(string expected, string token)
    {
        if (!string.Equals(token, expected, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Expected '{expected}'.");
    }

    /// <summary>Splits a line of text using BVH parsing rules.</summary>
    private static int Split(ReadOnlySpan<char> source, Span<Range> destination)
    {
        return MemoryExtensions.SplitAny(source, destination, " \t", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Parses a string using BVH parsing rules (throws if parsing fails).</summary>
    private static T Parse<T>(string text) where T : IParsable<T>
    {
        if (!T.TryParse(text, CultureInfo.InvariantCulture, out T? value))
            throw new FormatException($"Couldn't parse '{text}' as a {typeof(T)}.");
        return value;
    }
}
