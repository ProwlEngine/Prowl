// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;
using Prowl.Vector;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// What a sampler declares has to survive into the curve. The old bridge resampled every channel
/// into scalar points at its key times, which silently turned STEP into LINEAR and discarded
/// CUBICSPLINE tangents, so a stepped channel ramped and a cubic one lost its shape.
/// </summary>
public sealed class AnimationFidelityTests
{
    /// <summary>
    /// One node with a single translation channel, so the sampler under test is the only thing in
    /// the clip. Cubic output is three times as long, being in-tangent, value, out-tangent per key.
    /// </summary>
    private static Model LoadTranslationClip(string interpolation, float[] times, float[] output)
    {
        var bytes = new byte[times.Length * 4 + output.Length * 4];
        Buffer.BlockCopy(times, 0, bytes, 0, times.Length * 4);
        Buffer.BlockCopy(output, 0, bytes, times.Length * 4, output.Length * 4);

        int timesBytes = times.Length * 4;
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "Mover" } ],
          "animations": [ {
            "name": "Clip",
            "samplers": [ { "input": 0, "output": 1, "interpolation": "{{interpolation}}" } ],
            "channels": [ { "sampler": 0, "target": { "node": 0, "path": "translation" } } ]
          } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": {{times.Length}}, "type": "SCALAR" },
            { "bufferView": 1, "componentType": 5126, "count": {{output.Length / 3}}, "type": "VEC3" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": {{timesBytes}} },
            { "buffer": 0, "byteOffset": {{timesBytes}}, "byteLength": {{output.Length * 4}} }
          ],
          "buffers": [ { "byteLength": {{bytes.Length}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(bytes)}}" } ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        // Raw so no post-process step reshapes the keys under the assertions.
        return ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw);
    }

    private static AnimationCurve TranslationCurve(Model model) =>
        model.AnimationClips.Single().Bindings.Single(b => b.Property == AnimatedProperty.Position).Curve;


    [Fact]
    public void StepSampler_KeepsItsInterpolation()
    {
        var curve = TranslationCurve(LoadTranslationClip("STEP",
            [0f, 1f],
            [0f, 0f, 0f, 10f, 0f, 0f]));

        Assert.All(curve.GetInterpolations().ToArray(), m => Assert.Equal(CurveInterpolation.Step, m));
    }

    // The regression this pins: a stepped channel that ramps is the visible symptom.
    [Fact]
    public void StepSampler_HoldsItsValueRatherThanRamping()
    {
        var curve = TranslationCurve(LoadTranslationClip("STEP",
            [0f, 1f],
            [0f, 0f, 0f, 10f, 0f, 0f]));

        Assert.Equal(0f, curve.EvaluateFloat3(0.5f).X, 4);
        Assert.Equal(0f, curve.EvaluateFloat3(0.99f).X, 4);
        Assert.Equal(10f, curve.EvaluateFloat3(1f).X, 4);
    }


    [Fact]
    public void CubicSampler_KeepsItsTangents()
    {
        // Two keys, value 0 to 0, with a large out-tangent leaving the first. A curve that dropped
        // the tangents would read flat across the whole segment.
        var curve = TranslationCurve(LoadTranslationClip("CUBICSPLINE",
            [0f, 1f],
            [
                0f, 0f, 0f,   0f, 0f, 0f,   4f, 0f, 0f,
                0f, 0f, 0f,   0f, 0f, 0f,   0f, 0f, 0f,
            ]));

        Assert.All(curve.GetInterpolations().ToArray(), m => Assert.Equal(CurveInterpolation.CubicSpline, m));
        Assert.NotEqual(0f, curve.EvaluateFloat3(0.5f).X, 3);
    }

    [Fact]
    public void CubicSampler_UnpacksValuesFromTheInterleavedOutput()
    {
        // The middle triple of each key is the value; picking the wrong one silently shifts the
        // whole channel onto its tangents.
        var curve = TranslationCurve(LoadTranslationClip("CUBICSPLINE",
            [0f, 1f],
            [
                -1f, 0f, 0f,   7f, 0f, 0f,   -1f, 0f, 0f,
                -2f, 0f, 0f,   9f, 0f, 0f,   -2f, 0f, 0f,
            ]));

        Assert.Equal(7f, curve.EvaluateFloat3(0f).X, 3);
        Assert.Equal(9f, curve.EvaluateFloat3(1f).X, 3);
    }

    [Fact]
    public void LinearSampler_StaysLinear()
    {
        var curve = TranslationCurve(LoadTranslationClip("LINEAR",
            [0f, 1f],
            [0f, 0f, 0f, 10f, 0f, 0f]));

        Assert.All(curve.GetInterpolations().ToArray(), m => Assert.Equal(CurveInterpolation.Linear, m));
        Assert.Equal(5f, curve.EvaluateFloat3(0.5f).X, 3);
    }


    /// <summary>
    /// A clip cut from a shared timeline can start after zero. Reporting its length as the last key
    /// time makes a player that runs a playhead from zero sit on the first pose for the gap.
    /// </summary>
    [Fact]
    public void ClipStartingLate_ReportsItsRangeNotJustItsEnd()
    {
        var model = LoadTranslationClip("LINEAR",
            [2f, 5f],
            [0f, 0f, 0f, 10f, 0f, 0f]);

        var clip = model.AnimationClips.Single();
        Assert.Equal(2f, clip.StartTime, 4);
        Assert.Equal(5f, clip.EndTime, 4);
        Assert.Equal(3f, clip.Duration, 4);
    }

    [Fact]
    public void ClipStartingAtZero_HasZeroStartTime()
    {
        var model = LoadTranslationClip("LINEAR",
            [0f, 4f],
            [0f, 0f, 0f, 10f, 0f, 0f]);

        var clip = model.AnimationClips.Single();
        Assert.Equal(0f, clip.StartTime, 4);
        Assert.Equal(4f, clip.Duration, 4);
    }

    // A backfilled channel is one key holding forever, so its time must not widen the clip.
    [Fact]
    public void BackfilledChannelsDoNotStretchTheRange()
    {
        var model = LoadTranslationClip("LINEAR",
            [2f, 5f],
            [0f, 0f, 0f, 10f, 0f, 0f]);

        var clip = model.AnimationClips.Single();
        Assert.Equal(3, clip.Bindings.Length); // translation plus backfilled rotation and scale
        Assert.Equal(2f, clip.StartTime, 4);
    }
}
