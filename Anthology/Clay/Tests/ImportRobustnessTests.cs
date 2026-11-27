// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// What a malformed file should cost. A joint index the exporter got wrong, one channel with the
/// wrong value count, a NaN in a curve: none of these are a reason to lose the model, and none of
/// them should reach a consumer as data it cannot index or evaluate.
/// </summary>
public sealed class ImportRobustnessTests
{
    private static Model Load(string body, PostProcessFlags flags = PostProcessFlags.None)
    {
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          {{body}}
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw with { PostProcess = flags });
    }

    private static string Buffer(params byte[][] segments)
    {
        var bytes = new List<byte>();
        foreach (var segment in segments)
        {
            while (bytes.Count % 4 != 0) bytes.Add(0);
            bytes.AddRange(segment);
        }
        return Convert.ToBase64String(bytes.ToArray());
    }

    private static byte[] Floats(params float[] v)
    {
        var b = new byte[v.Length * 4];
        System.Buffer.BlockCopy(v, 0, b, 0, b.Length);
        return b;
    }


    /// <summary>
    /// A skin naming a joint index the file does not have. The joint used to be replaced by a node
    /// that was never added to the scene, so its bake index stayed -1, that -1 landed in
    /// BoneNodeIndices, and the consumer indexed its node array with it.
    /// </summary>
    [Fact]
    public void SkinWithAMissingJoint_StillProducesIndexableBones()
    {
        var model = Load("""
          "nodes": [
            { "name": "Root", "children": [ 1, 2 ] },
            { "name": "Bone", "children": [] },
            { "name": "Mesh", "skin": 0 }
          ],
          "skins": [ { "joints": [ 1, 99 ] } ]
        """);

        var skin = Assert.Single(model.Skins);
        Assert.Equal(2, skin.BoneNodeIndices.Length);
        foreach (int index in skin.BoneNodeIndices)
            Assert.InRange(index, 0, model.Nodes.Count - 1);
    }

    /// <summary>
    /// A joint that exists but belongs to a scene other than the one being imported reaches the same
    /// dead end: a real node that no scene root leads to.
    /// </summary>
    [Fact]
    public void SkinWithAJointOutsideTheScene_StillProducesIndexableBones()
    {
        var model = Load("""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Mesh", "skin": 0 },
            { "name": "OtherSceneBone" }
          ],
          "skins": [ { "joints": [ 2 ] } ]
        """);

        int bone = Assert.Single(model.Skins[0].BoneNodeIndices);
        Assert.InRange(bone, 0, model.Nodes.Count - 1);
        Assert.Equal("OtherSceneBone", model.Nodes[bone].Name);
    }

    // The joint keeps the transform its own branch gave it, so attaching it does not move it.
    [Fact]
    public void AttachedJoint_KeepsItsTransform()
    {
        var model = Load("""
          "nodes": [
            { "name": "Root", "children": [ 1 ] },
            { "name": "Mesh", "skin": 0 },
            { "name": "Detached", "translation": [ 3, 4, 5 ] }
          ],
          "skins": [ { "joints": [ 2 ] } ]
        """);

        var bone = model.Nodes[model.Skins[0].BoneNodeIndices[0]];
        Assert.Equal(3f, bone.LocalPosition.X, 4);
        Assert.Equal(5f, bone.LocalPosition.Z, 4);
    }


    /// <summary>
    /// Every other malformed channel in the animation mapper warns and moves on. A value count
    /// mismatch threw, which lost the whole model over one channel.
    /// </summary>
    [Fact]
    public void ChannelWithTheWrongValueCount_IsSkippedNotFatal()
    {
        // Two keys of translation need six floats. This sampler supplies three.
        string body = $$"""
          "nodes": [ { "name": "Mover" } ],
          "animations": [ {
            "name": "Clip",
            "samplers": [ { "input": 0, "output": 1, "interpolation": "LINEAR" } ],
            "channels": [ { "sampler": 0, "target": { "node": 0, "path": "translation" } } ]
          } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR" },
            { "bufferView": 1, "componentType": 5126, "count": 1, "type": "VEC3" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
            { "buffer": 0, "byteOffset": 8, "byteLength": 12 }
          ],
          "buffers": [ { "byteLength": 20, "uri": "data:application/octet-stream;base64,{{Buffer(Floats(0f, 1f), Floats(1f, 2f, 3f))}}" } ]
        """;

        var model = Load(body);

        Assert.Single(model.Nodes, n => n.Name == "Mover");
        Assert.Contains(model.Log.Entries, e => e.Message.Contains("skipping the channel"));
    }


    /// <summary>
    /// Two textures over one image with different samplers each decoded their own private copy of
    /// the encoded bytes, doubling peak memory on a GLB with large embedded images.
    /// </summary>
    [Fact]
    public void TwoTexturesOverOneImage_ShareItsBytes()
    {
        var model = Load("""
          "nodes": [ { "name": "N" } ],
          "textures": [ { "source": 0, "sampler": 0 }, { "source": 0, "sampler": 1 } ],
          "samplers": [ { "wrapS": 10497 }, { "wrapS": 33071 } ],
          "images": [ { "mimeType": "image/png", "uri": "data:image/png;base64,iVBORw0KGgo=" } ]
        """);

        Assert.Equal(2, model.Textures.Count);
        Assert.NotNull(model.Textures[0].EncodedBytes);
        Assert.Same(model.Textures[0].EncodedBytes, model.Textures[1].EncodedBytes);
    }

    // Sharing the bytes must not share the sampler, which is the whole reason for two textures.
    [Fact]
    public void SharedImageTextures_KeepTheirOwnSamplers()
    {
        var model = Load("""
          "nodes": [ { "name": "N" } ],
          "textures": [ { "source": 0, "sampler": 0 }, { "source": 0, "sampler": 1 } ],
          "samplers": [ { "wrapS": 10497 }, { "wrapS": 33071 } ],
          "images": [ { "mimeType": "image/png", "uri": "data:image/png;base64,iVBORw0KGgo=" } ]
        """);

        Assert.NotEqual(model.Textures[0].Sampler.WrapU, model.Textures[1].Sampler.WrapU);
    }


    /// <summary>
    /// The step's documentation claimed it scanned every animation curve for NaN. It only collapsed
    /// redundant keys and never looked at a value.
    /// </summary>
    [Fact]
    public void NonFiniteRotationValues_BecomeIdentity()
    {
        string body = $$"""
          "nodes": [ { "name": "Spinner" } ],
          "animations": [ {
            "samplers": [ { "input": 0, "output": 1, "interpolation": "LINEAR" } ],
            "channels": [ { "sampler": 0, "target": { "node": 0, "path": "rotation" } } ]
          } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR" },
            { "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC4" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 8 },
            { "buffer": 0, "byteOffset": 8, "byteLength": 32 }
          ],
          "buffers": [ { "byteLength": 40, "uri": "data:application/octet-stream;base64,{{Buffer(
              Floats(0f, 1f),
              Floats(float.NaN, float.NaN, float.NaN, float.NaN, 0f, 0f, 0f, 1f))}}" } ]
        """;

        var model = Load(body, PostProcessFlags.FindInvalidData);

        var curve = model.AnimationClips.Single().Bindings.Single(b => b.Property == AnimatedProperty.Rotation).Curve;
        var rotation = curve.EvaluateQuaternion(0f);
        Assert.Equal(1f, rotation.W, 4);
        Assert.Equal(0f, rotation.X, 4);
    }

    [Fact]
    public void KeysAtNonFiniteTimes_AreDropped()
    {
        string body = $$"""
          "nodes": [ { "name": "Mover" } ],
          "animations": [ {
            "samplers": [ { "input": 0, "output": 1, "interpolation": "LINEAR" } ],
            "channels": [ { "sampler": 0, "target": { "node": 0, "path": "translation" } } ]
          } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "SCALAR" },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 12 },
            { "buffer": 0, "byteOffset": 12, "byteLength": 36 }
          ],
          "buffers": [ { "byteLength": 48, "uri": "data:application/octet-stream;base64,{{Buffer(
              Floats(0f, float.NaN, 2f),
              Floats(0f, 0f, 0f, 9f, 9f, 9f, 4f, 0f, 0f))}}" } ]
        """;

        var model = Load(body, PostProcessFlags.FindInvalidData);

        var curve = model.AnimationClips.Single().Bindings.Single(b => b.Property == AnimatedProperty.Position).Curve;
        Assert.Equal(2, curve.Count);
        // The surviving keys keep their own values rather than being shifted onto the dropped one's.
        Assert.Equal(0f, curve.EvaluateFloat3(0f).X, 4);
        Assert.Equal(4f, curve.EvaluateFloat3(2f).X, 4);
    }
}
