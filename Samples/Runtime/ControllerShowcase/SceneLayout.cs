// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Samples;
using Prowl.Vector;

using static Prowl.Samples.Sample;

namespace ControllerShowcase;

/// <summary>One placed piece of the world, so the HUD can say what you are standing next to.</summary>
public sealed record Landmark(string Name, string About, Float3 Position, float Radius);

/// <summary>The controller playground, every object at the spot it was placed by hand.</summary>
public static class SceneLayout
{
    /// <summary>Adds every object of the playground to <paramref name="scene"/> and returns the pieces the HUD names.</summary>
    public static Landmark[] Spawn(Scene scene)
    {
        Material stairs = GridMaterial(new Color(0.455f, 0.325f, 0.104f, 1f));
        Material tower = GridMaterial(new Color(0.156f, 0.325f, 0.52f, 1f));
        Material ceiling = GridMaterial(new Color(0.39f, 0.156f, 0.39f, 1f));
        Material trap = GridMaterial(new Color(0.52f, 0.156f, 0.104f, 1f));
        Material curve = GridMaterial(new Color(0.104f, 0.416f, 0.338f, 1f));
        Material rough = GridMaterial(new Color(0.208f, 0.312f, 0.104f, 1f));
        Material moving = GridMaterial(new Color(0.52f, 0.39f, 0.052f, 1f));
        Material edge = GridMaterial(new Color(0.364f, 0.364f, 0.442f, 1f));
        Material plain = GridMaterial(new Color(0.39f, 0.416f, 0.468f, 1f));
        Material dark = Lit(new Color(0.03f, 0.032f, 0.04f, 1f), 0f, 0.7f);
        Material travel = GridMaterial(new Color(0.286f, 0.156f, 0.52f, 1f));
        Teleporter gate0, gate1, gate2, gate3;

        scene.Add(Box("Step", new Float3(2.6f, 0.1f, 0.2f), stairs, new Float3(10.600972f, 0.05f, 34.194122f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.2f, 0.2f), stairs, new Float3(10.609207f, 0.1f, 33.99429f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.3f, 0.2f), stairs, new Float3(10.617443f, 0.15f, 33.79446f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.4f, 0.2f), stairs, new Float3(10.625678f, 0.2f, 33.59463f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.5f, 0.2f), stairs, new Float3(10.633914f, 0.25f, 33.3948f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.6f, 0.2f), stairs, new Float3(10.642149f, 0.3f, 33.19497f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.7f, 0.2f), stairs, new Float3(10.650385f, 0.35f, 32.99514f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.8f, 0.2f), stairs, new Float3(10.65862f, 0.4f, 32.79531f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.90000004f, 0.2f), stairs, new Float3(10.666856f, 0.45000002f, 32.595478f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1f, 0.2f), stairs, new Float3(10.675091f, 0.5f, 32.39565f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.1f, 0.2f), stairs, new Float3(10.683327f, 0.55f, 32.195816f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.2f, 0.2f), stairs, new Float3(10.691563f, 0.6f, 31.995987f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.3000001f, 0.2f), stairs, new Float3(10.699798f, 0.65000004f, 31.796158f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.4f, 0.2f), stairs, new Float3(10.708034f, 0.7f, 31.596327f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.5f, 0.2f), stairs, new Float3(10.716269f, 0.75f, 31.396496f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.6f, 0.2f), stairs, new Float3(10.724504f, 0.8f, 31.196667f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.7f, 0.2f), stairs, new Float3(10.732739f, 0.85f, 30.996836f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.8000001f, 0.2f), stairs, new Float3(10.740975f, 0.90000004f, 30.797005f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.9f, 0.2f), stairs, new Float3(10.74921f, 0.95f, 30.597176f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2f, 0.2f), stairs, new Float3(10.757446f, 1f, 30.397345f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.1000001f, 0.2f), stairs, new Float3(10.765681f, 1.0500001f, 30.197514f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.2f, 0.2f), stairs, new Float3(10.773917f, 1.1f, 29.997684f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.3f, 0.2f), stairs, new Float3(10.782152f, 1.15f, 29.797853f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.4f, 0.2f), stairs, new Float3(10.790388f, 1.2f, 29.598022f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.5f, 0.2f), stairs, new Float3(10.798624f, 1.25f, 29.398193f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.6000001f, 0.2f), stairs, new Float3(10.806859f, 1.3000001f, 29.198362f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.7f, 0.2f), stairs, new Float3(10.815095f, 1.35f, 28.998531f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.8f, 0.2f), stairs, new Float3(10.82333f, 1.4f, 28.798702f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.9f, 0.2f), stairs, new Float3(10.831566f, 1.45f, 28.598871f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 0.2f), stairs, new Float3(10.839801f, 1.5f, 28.39904f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.1000001f, 0.2f), stairs, new Float3(10.848037f, 1.5500001f, 28.199211f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.2f, 0.2f), stairs, new Float3(10.856272f, 1.6f, 27.99938f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.3f, 0.2f), stairs, new Float3(10.864508f, 1.65f, 27.799551f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.4f, 0.2f), stairs, new Float3(10.872743f, 1.7f, 27.59972f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.5f, 0.2f), stairs, new Float3(10.880979f, 1.75f, 27.399889f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.6000001f, 0.2f), stairs, new Float3(10.889214f, 1.8000001f, 27.20006f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.7f, 0.2f), stairs, new Float3(10.8974495f, 1.85f, 27.000229f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.8f, 0.2f), stairs, new Float3(10.905685f, 1.9f, 26.800398f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.9f, 0.2f), stairs, new Float3(10.91392f, 1.95f, 26.600567f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 4f, 0.2f), stairs, new Float3(10.922156f, 2f, 26.400738f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Hidden("Hidden Ramp", new Float3(2.6f, 0.05f, 8.944272f), new Float3(10.761564f, 2f, 30.29743f), new Quaternion(-0.004731324f, 0.9730426f, 0.22970417f, 0.020042213f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.1f, 0.2f), stairs, new Float3(7.6035166f, 0.05f, 34.07059f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.2f, 0.2f), stairs, new Float3(7.6117516f, 0.1f, 33.870758f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.3f, 0.2f), stairs, new Float3(7.6199875f, 0.15f, 33.67093f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.4f, 0.2f), stairs, new Float3(7.6282225f, 0.2f, 33.4711f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.5f, 0.2f), stairs, new Float3(7.6364584f, 0.25f, 33.271267f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.6f, 0.2f), stairs, new Float3(7.6446934f, 0.3f, 33.071438f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.7f, 0.2f), stairs, new Float3(7.6529293f, 0.35f, 32.87161f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.8f, 0.2f), stairs, new Float3(7.6611643f, 0.4f, 32.671776f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.90000004f, 0.2f), stairs, new Float3(7.6694f, 0.45000002f, 32.471947f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1f, 0.2f), stairs, new Float3(7.677635f, 0.5f, 32.272118f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.1f, 0.2f), stairs, new Float3(7.685871f, 0.55f, 32.072285f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.2f, 0.2f), stairs, new Float3(7.694107f, 0.6f, 31.872456f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.3000001f, 0.2f), stairs, new Float3(7.702342f, 0.65000004f, 31.672625f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.4f, 0.2f), stairs, new Float3(7.710578f, 0.7f, 31.472794f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.5f, 0.2f), stairs, new Float3(7.718813f, 0.75f, 31.272964f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.6f, 0.2f), stairs, new Float3(7.727049f, 0.8f, 31.073133f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.7f, 0.2f), stairs, new Float3(7.735284f, 0.85f, 30.873302f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.8000001f, 0.2f), stairs, new Float3(7.74352f, 0.90000004f, 30.673473f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.9f, 0.2f), stairs, new Float3(7.7517548f, 0.95f, 30.473642f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2f, 0.2f), stairs, new Float3(7.7599907f, 1f, 30.273811f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.1000001f, 0.2f), stairs, new Float3(7.7682257f, 1.0500001f, 30.073982f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.2f, 0.2f), stairs, new Float3(7.7764616f, 1.1f, 29.874151f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.3f, 0.2f), stairs, new Float3(7.7846966f, 1.15f, 29.674322f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.4f, 0.2f), stairs, new Float3(7.7929325f, 1.2f, 29.474491f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.5f, 0.2f), stairs, new Float3(7.8011684f, 1.25f, 29.27466f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.6000001f, 0.2f), stairs, new Float3(7.8094034f, 1.3000001f, 29.074831f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.7f, 0.2f), stairs, new Float3(7.8176394f, 1.35f, 28.875f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.8f, 0.2f), stairs, new Float3(7.8258743f, 1.4f, 28.675169f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.9f, 0.2f), stairs, new Float3(7.8341103f, 1.45f, 28.475338f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 0.2f), stairs, new Float3(7.842345f, 1.5f, 28.275509f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.1000001f, 0.2f), stairs, new Float3(7.850581f, 1.5500001f, 28.075678f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.2f, 0.2f), stairs, new Float3(7.858816f, 1.6f, 27.875849f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.3f, 0.2f), stairs, new Float3(7.867052f, 1.65f, 27.676018f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.4f, 0.2f), stairs, new Float3(7.875287f, 1.7f, 27.476187f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.5f, 0.2f), stairs, new Float3(7.883523f, 1.75f, 27.276358f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.6000001f, 0.2f), stairs, new Float3(7.891758f, 1.8000001f, 27.076527f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.7f, 0.2f), stairs, new Float3(7.899994f, 1.85f, 26.876696f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.8f, 0.2f), stairs, new Float3(7.90823f, 1.9f, 26.676867f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.9f, 0.2f), stairs, new Float3(7.916465f, 1.95f, 26.477036f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 4f, 0.2f), stairs, new Float3(7.9247007f, 2f, 26.277205f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.2f, 0.4f), stairs, new Float3(4.6101785f, 0.1f, 33.84714f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.4f, 0.4f), stairs, new Float3(4.6266494f, 0.2f, 33.44748f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.6f, 0.4f), stairs, new Float3(4.6431203f, 0.3f, 33.04782f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.8f, 0.4f), stairs, new Float3(4.659591f, 0.4f, 32.64816f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1f, 0.4f), stairs, new Float3(4.676062f, 0.5f, 32.248497f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.2f, 0.4f), stairs, new Float3(4.6925335f, 0.6f, 31.848839f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.4f, 0.4f), stairs, new Float3(4.7090044f, 0.7f, 31.449177f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.6f, 0.4f), stairs, new Float3(4.7254753f, 0.8f, 31.049517f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.8000001f, 0.4f), stairs, new Float3(4.741946f, 0.90000004f, 30.649857f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2f, 0.4f), stairs, new Float3(4.758417f, 1f, 30.250195f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.2f, 0.4f), stairs, new Float3(4.774888f, 1.1f, 29.850534f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.4f, 0.4f), stairs, new Float3(4.791359f, 1.2f, 29.450874f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.6000001f, 0.4f), stairs, new Float3(4.8078303f, 1.3000001f, 29.051212f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.8f, 0.4f), stairs, new Float3(4.8243012f, 1.4f, 28.651552f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 0.4f), stairs, new Float3(4.840772f, 1.5f, 28.251892f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.2f, 0.4f), stairs, new Float3(4.857243f, 1.6f, 27.85223f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.4f, 0.4f), stairs, new Float3(4.873714f, 1.7f, 27.45257f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.6000001f, 0.4f), stairs, new Float3(4.890185f, 1.8000001f, 27.05291f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.8f, 0.4f), stairs, new Float3(4.9066563f, 1.9f, 26.653248f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 4f, 0.4f), stairs, new Float3(4.923127f, 2f, 26.253588f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Hidden("Hidden Ramp", new Float3(2.6f, 0.05f, 8.944272f), new Float3(4.7666526f, 2f, 30.050365f), new Quaternion(-0.004731324f, 0.9730426f, 0.22970417f, 0.020042213f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.2f, 0.4f), stairs, new Float3(1.6127226f, 0.1f, 33.72361f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.4f, 0.4f), stairs, new Float3(1.6291938f, 0.2f, 33.323948f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.6f, 0.4f), stairs, new Float3(1.6456647f, 0.3f, 32.924286f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.8f, 0.4f), stairs, new Float3(1.6621356f, 0.4f, 32.524628f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1f, 0.4f), stairs, new Float3(1.6786067f, 0.5f, 32.124966f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.2f, 0.4f), stairs, new Float3(1.6950777f, 0.6f, 31.725306f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.4f, 0.4f), stairs, new Float3(1.7115486f, 0.7f, 31.325645f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.6f, 0.4f), stairs, new Float3(1.7280197f, 0.8f, 30.925983f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.8000001f, 0.4f), stairs, new Float3(1.7444906f, 0.90000004f, 30.526323f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2f, 0.4f), stairs, new Float3(1.7609615f, 1f, 30.126663f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.2f, 0.4f), stairs, new Float3(1.7774327f, 1.1f, 29.727001f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.4f, 0.4f), stairs, new Float3(1.7939036f, 1.2f, 29.327341f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.6000001f, 0.4f), stairs, new Float3(1.8103745f, 1.3000001f, 28.927681f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.8f, 0.4f), stairs, new Float3(1.8268456f, 1.4f, 28.528019f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 0.4f), stairs, new Float3(1.8433166f, 1.5f, 28.128359f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.2f, 0.4f), stairs, new Float3(1.8597875f, 1.6f, 27.728699f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.4f, 0.4f), stairs, new Float3(1.8762584f, 1.7f, 27.329039f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.6000001f, 0.4f), stairs, new Float3(1.8927295f, 1.8000001f, 26.929377f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.8f, 0.4f), stairs, new Float3(1.9092004f, 1.9f, 26.529716f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 4f, 0.4f), stairs, new Float3(1.9256713f, 2f, 26.130056f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.25f, 0.5f), stairs, new Float3(-1.382674f, 0.125f, 33.550117f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.5f, 0.5f), stairs, new Float3(-1.3620852f, 0.25f, 33.050545f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.75f, 0.5f), stairs, new Float3(-1.3414965f, 0.375f, 32.55097f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1f, 0.5f), stairs, new Float3(-1.3209078f, 0.5f, 32.05139f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.25f, 0.5f), stairs, new Float3(-1.3003191f, 0.625f, 31.551815f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.5f, 0.5f), stairs, new Float3(-1.2797303f, 0.75f, 31.05224f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.75f, 0.5f), stairs, new Float3(-1.2591416f, 0.875f, 30.552664f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2f, 0.5f), stairs, new Float3(-1.2385529f, 1f, 30.05309f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.25f, 0.5f), stairs, new Float3(-1.2179642f, 1.125f, 29.553513f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.5f, 0.5f), stairs, new Float3(-1.1973754f, 1.25f, 29.053936f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.75f, 0.5f), stairs, new Float3(-1.1767867f, 1.375f, 28.55436f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 0.5f), stairs, new Float3(-1.1561979f, 1.5f, 28.054785f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.25f, 0.5f), stairs, new Float3(-1.1356093f, 1.625f, 27.555208f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.5f, 0.5f), stairs, new Float3(-1.1150205f, 1.75f, 27.055634f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.75f, 0.5f), stairs, new Float3(-1.0944318f, 1.875f, 26.556057f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 4f, 0.5f), stairs, new Float3(-1.073843f, 2f, 26.05648f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Hidden("Hidden Ramp", new Float3(2.6f, 0.05f, 8.944272f), new Float3(-1.2282585f, 2f, 29.8033f), new Quaternion(-0.004731324f, 0.9730426f, 0.22970417f, 0.020042213f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.3f, 0.61538464f), stairs, new Float3(-4.377754f, 0.15f, 33.368942f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.6f, 0.61538464f), stairs, new Float3(-4.352414f, 0.3f, 32.75408f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.90000004f, 0.61538464f), stairs, new Float3(-4.327074f, 0.45000002f, 32.139217f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.2f, 0.61538464f), stairs, new Float3(-4.301734f, 0.6f, 31.524357f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.5f, 0.61538464f), stairs, new Float3(-4.276394f, 0.75f, 30.909492f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.8000001f, 0.61538464f), stairs, new Float3(-4.251054f, 0.90000004f, 30.29463f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.1000001f, 0.61538464f), stairs, new Float3(-4.225714f, 1.0500001f, 29.679768f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.4f, 0.61538464f), stairs, new Float3(-4.200374f, 1.2f, 29.064905f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.7f, 0.61538464f), stairs, new Float3(-4.175034f, 1.35f, 28.450043f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 0.61538464f), stairs, new Float3(-4.1496944f, 1.5f, 27.83518f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.3000002f, 0.61538464f), stairs, new Float3(-4.1243544f, 1.6500001f, 27.220316f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.6000001f, 0.61538464f), stairs, new Float3(-4.0990143f, 1.8000001f, 26.605453f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.9f, 0.61538464f), stairs, new Float3(-4.073674f, 1.95f, 25.990591f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.35f, 0.72727275f), stairs, new Float3(-7.3729057f, 0.175f, 33.189514f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.7f, 0.72727275f), stairs, new Float3(-7.3429585f, 0.35f, 32.46286f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.05f, 0.72727275f), stairs, new Float3(-7.3130116f, 0.525f, 31.736202f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.4f, 0.72727275f), stairs, new Float3(-7.2830644f, 0.7f, 31.009546f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.75f, 0.72727275f), stairs, new Float3(-7.253117f, 0.875f, 30.28289f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.1f, 0.72727275f), stairs, new Float3(-7.22317f, 1.05f, 29.556236f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.45f, 0.72727275f), stairs, new Float3(-7.1932225f, 1.225f, 28.829578f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.8f, 0.72727275f), stairs, new Float3(-7.1632752f, 1.4f, 28.102924f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.1499999f, 0.72727275f), stairs, new Float3(-7.133328f, 1.5749999f, 27.376268f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.5f, 0.72727275f), stairs, new Float3(-7.1033807f, 1.75f, 26.649612f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.85f, 0.72727275f), stairs, new Float3(-7.0734334f, 1.925f, 25.922955f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 0.5f, 1f), stairs, new Float3(-10.364746f, 0.25f, 32.929733f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1f, 1f), stairs, new Float3(-10.323569f, 0.5f, 31.930582f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 1.5f, 1f), stairs, new Float3(-10.282392f, 0.75f, 30.93143f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2f, 1f), stairs, new Float3(-10.241214f, 1f, 29.93228f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 2.5f, 1f), stairs, new Float3(-10.200037f, 1.25f, 28.933126f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3f, 1f), stairs, new Float3(-10.158859f, 1.5f, 27.933975f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 3.5f, 1f), stairs, new Float3(-10.1176815f, 1.75f, 26.934824f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(2.6f, 4f, 1f), stairs, new Float3(-10.076504f, 2f, 25.93567f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Ramp", new Float3(2.6f, 0.3f, 8.944272f), stairs, new Float3(-13.214f, 1.873f, 29.218f), new Quaternion(-0.004730017f, 0.97304344f, 0.22970082f, 0.02004007f)));
        scene.Add(Box("Top Landing", new Float3(27f, 4f, 4f), stairs, new Float3(-0.9811938f, 2f, 23.80839f), new Quaternion(0f, 0.9997879f, 0f, 0.020593097f)));
        scene.Add(Box("Step", new Float3(6f, 0.25f, 0.5f), stairs, new Float3(-7.242f, 0.125f, 11.796f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 0.5f, 0.5f), stairs, new Float3(-7.262f, 0.25f, 12.295f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 0.75f, 0.5f), stairs, new Float3(-7.283f, 0.375f, 12.795f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 1f, 0.5f), stairs, new Float3(-7.304f, 0.5f, 13.294f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 1.25f, 0.5f), stairs, new Float3(-7.324f, 0.625f, 13.794f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 1.5f, 0.5f), stairs, new Float3(-7.345f, 0.75f, 14.293f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 1.75f, 0.5f), stairs, new Float3(-7.365f, 0.875f, 14.793f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 2f, 0.5f), stairs, new Float3(-7.386f, 1f, 15.293f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 2.25f, 0.5f), stairs, new Float3(-7.407f, 1.125f, 15.792f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 2.5f, 0.5f), stairs, new Float3(-7.427f, 1.25f, 16.292f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 2.75f, 0.5f), stairs, new Float3(-7.448f, 1.375f, 16.791f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 3f, 0.5f), stairs, new Float3(-7.468f, 1.5f, 17.291f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 3.25f, 0.5f), stairs, new Float3(-7.489f, 1.625f, 17.79f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 3.5f, 0.5f), stairs, new Float3(-7.509f, 1.75f, 18.29f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 3.75f, 0.5f), stairs, new Float3(-7.53f, 1.875f, 18.79f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Step", new Float3(6f, 4f, 0.5f), stairs, new Float3(-7.551f, 2f, 19.289f), new Quaternion(0f, 0.020589959f, 0f, -0.999788f)));
        scene.Add(Box("Back Landing", new Float3(6f, 4f, 2f), stairs, new Float3(-7.602f, 2f, 20.538f), new Quaternion(0f, 0.999788f, 0f, 0.020589959f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), stairs, new Float3(-1.141f, 0.361f, 13.946f), new Quaternion(-0.079819836f, -0.4000992f, -0.03499993f, 0.91231817f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), edge, new Float3(0.427f, 0.866f, 14.975f), new Quaternion(-0.16871996f, -0.23290995f, -0.04106999f, 0.9568698f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), stairs, new Float3(2.218f, 1.34f, 15.307f), new Quaternion(-0.2582907f, -0.06200017f, -0.016610045f, 0.96393263f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), edge, new Float3(3.921f, 1.768f, 14.933f), new Quaternion(-0.33994088f, 0.10344026f, 0.037650093f, 0.9339823f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), stairs, new Float3(5.26f, 2.138f, 13.98f), new Quaternion(-0.4055903f, 0.25467017f, 0.11876008f, 0.8697906f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), edge, new Float3(6.05f, 2.438f, 12.678f), new Quaternion(-0.44816834f, 0.38397858f, 0.22168918f, 0.77624714f)));
        scene.Add(Box("Ramp", new Float3(2f, 0.3f, 6f), stairs, new Float3(6.237f, 2.659f, 11.303f), new Quaternion(-0.4621394f, 0.48517936f, 0.33972955f, 0.6600091f)));
        scene.Add(Box("Ramp", new Float3(4f, 0.3f, 4f), stairs, new Float3(41.845f, 0.605f, 3.244f), new Quaternion(-0.12717985f, -0.7899591f, -0.17512979f, 0.57368934f), new Float3(1f, 1f, 1.282f)));
        scene.Add(Box("Crest Down", new Float3(4f, 0.3f, 4f), edge, new Float3(38.031f, 0.846f, 2.004f), new Quaternion(0.12717985f, -0.7899591f, 0.17512979f, 0.57368934f)));
        scene.Add(Box("Step", new Float3(4f, 0.25f, 0.5f), edge, new Float3(40.353f, 0.125f, -8.923f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 0.5f, 0.5f), edge, new Float3(40.134f, 0.25f, -8.473f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 0.75f, 0.5f), edge, new Float3(39.916f, 0.375f, -8.024f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 1f, 0.5f), edge, new Float3(39.698f, 0.5f, -7.574f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 1.25f, 0.5f), edge, new Float3(39.479f, 0.625f, -7.124f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 1.5f, 0.5f), edge, new Float3(39.261f, 0.75f, -6.674f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 1.75f, 0.5f), edge, new Float3(39.042f, 0.875f, -6.225f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Step", new Float3(4f, 2f, 0.5f), edge, new Float3(38.824f, 1f, -5.775f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Trough Landing", new Float3(4f, 2f, 3f), edge, new Float3(38.059f, 1f, -4.201f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Trough Down", new Float3(4f, 0.3f, 4.7324033f), stairs, new Float3(36.496f, 0.866f, -0.982f), new Quaternion(0.21092923f, -0.2188292f, 0.04850982f, 0.9514565f)));
        scene.Add(Box("Trough Up", new Float3(4f, 0.3f, 4.7324033f), edge, new Float3(34.564f, 0.863f, 2.997f), new Quaternion(-0.21092923f, -0.2188292f, -0.04850982f, 0.9514565f)));
        scene.Add(Box("Trough Landing", new Float3(4f, 2f, 3f), edge, new Float3(33.001f, 1f, 6.214f), new Quaternion(0f, -0.22415102f, 0f, 0.9745544f)));
        scene.Add(Box("Valley Side", new Float3(4f, 0.3f, 8f), trap, new Float3(28.754f, 1.766f, 12.801f), new Quaternion(0.29509822f, -0.491127f, -0.42211744f, 0.7025157f)));
        scene.Add(Box("Valley Side", new Float3(4f, 0.3f, 8f), trap, new Float3(29.399f, 1.766f, 14.564f), new Quaternion(-0.29509822f, -0.491127f, 0.42211744f, 0.7025157f)));
        scene.Add(Box("Column", new Float3(2f, 8f, 2f), dark, new Float3(17.76591f, 4f, -10.987799f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.502699f, 0.125f, -12.231205f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.632477f, 0.375f, -11.748341f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.762255f, 0.625f, -11.265476f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.892033f, 0.875f, -10.782612f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(20.216476f, 0.875f, -9.575452f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(19.009315f, 1.125f, -9.251008f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(18.526451f, 1.375f, -9.121231f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(18.043587f, 1.625f, -8.991453f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.560722f, 1.875f, -8.861676f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(16.353561f, 1.875f, -8.537232f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(16.02912f, 2.125f, -9.744392f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(15.899342f, 2.375f, -10.227257f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(15.769564f, 2.625f, -10.710121f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(15.639787f, 2.875f, -11.192986f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(15.315343f, 2.875f, -12.400146f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(16.522503f, 3.125f, -12.724589f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.005367f, 3.375f, -12.854366f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.488232f, 3.625f, -12.984144f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.971096f, 3.875f, -13.113921f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(19.178257f, 3.875f, -13.438365f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.502699f, 4.125f, -12.231205f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.632477f, 4.375f, -11.748341f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.762255f, 4.625f, -11.265476f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(19.892033f, 4.875f, -10.782612f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(20.216476f, 4.875f, -9.575452f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(19.009315f, 5.125f, -9.251008f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(18.526451f, 5.375f, -9.121231f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(18.043587f, 5.625f, -8.991453f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.560722f, 5.875f, -8.861676f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(16.353561f, 5.875f, -8.537232f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(16.02912f, 6.125f, -9.744392f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(15.899342f, 6.375f, -10.227257f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(15.769564f, 6.625f, -10.710121f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(0.5f, 0.25f, 2f), tower, new Float3(15.639787f, 6.875f, -11.192986f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(15.315343f, 6.875f, -12.400146f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(16.522503f, 7.125f, -12.724589f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.005367f, 7.375f, -12.854366f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.488232f, 7.625f, -12.984144f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Step", new Float3(2f, 0.25f, 0.5f), tower, new Float3(17.971096f, 7.875f, -13.113921f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Tower Landing", new Float3(2f, 0.25f, 2f), edge, new Float3(19.178257f, 7.875f, -13.438365f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Thin Plate", new Float3(5f, 0.05f, 5f), Lit(new Color(1f, 0.32f, 0.04f, 1f), 0f, 0.5f), new Float3(19.323238f, 0.5f, -5.1934285f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Plate Post", new Float3(0.2f, 0.5f, 0.2f), dark, new Float3(19.323238f, 0.25f, -5.1934285f), new Quaternion(0f, -0.6084592f, 0f, 0.7935852f)));
        scene.Add(Box("Wall", new Float3(1f, 3f, 0.4f), ceiling, new Float3(4.576f, 1.5f, -27.5f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Lintel", new Float3(1.4f, 1.7f, 0.4f), dark, new Float3(5.632f, 2.15f, -26.928f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Wall", new Float3(1.6f, 3f, 0.4f), ceiling, new Float3(6.951f, 1.5f, -26.215f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Lintel", new Float3(1.4f, 1.15f, 0.4f), dark, new Float3(8.27f, 2.289f, -25.501f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.244f, 1f)));
        scene.Add(Box("Wall", new Float3(1.6f, 3f, 0.4f), ceiling, new Float3(9.589f, 1.5f, -24.787f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Lintel", new Float3(1.4f, 0.79999995f, 0.4f), dark, new Float3(10.908f, 2.383f, -24.073f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.546f, 1f)));
        scene.Add(Box("Wall", new Float3(1.6f, 3f, 0.4f), ceiling, new Float3(12.228f, 1.5f, -23.359f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Lintel", new Float3(0.7f, 0.5999999f, 0.4f), dark, new Float3(13.239f, 2.7f, -22.812f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Wall", new Float3(1.6f, 3f, 0.4f), ceiling, new Float3(14.25f, 1.5f, -22.264f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Lintel", new Float3(0.9f, 0.5999999f, 0.4f), dark, new Float3(15.35f, 2.7f, -21.669f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Wall", new Float3(1.6f, 3f, 0.4f), ceiling, new Float3(16.449f, 1.5f, -21.074f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Lintel", new Float3(1.2f, 0.5999999f, 0.4f), dark, new Float3(17.68f, 2.7f, -20.408f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Wall", new Float3(1.6f, 3f, 0.4f), ceiling, new Float3(18.912f, 1.5f, -19.742f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Bar", new Float3(3f, 0.08f, 0.08f), Lit(new Color(0.7f, 0.5f, 0.02f, 1f), 0f, 0.4f), new Float3(11.667f, 0.15f, -17.623f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Bar", new Float3(3f, 0.08f, 0.08f), Lit(new Color(0.7f, 0.5f, 0.02f, 1f), 0f, 0.4f), new Float3(10.477f, 0.35f, -15.425f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Bar", new Float3(3f, 0.08f, 0.08f), Lit(new Color(0.7f, 0.5f, 0.02f, 1f), 0f, 0.4f), new Float3(9.287f, 1.6f, -13.226f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Bar Post", new Float3(0.15f, 1.8f, 7f), dark, new Float3(9.158f, 0.9f, -16.138f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Bar Post", new Float3(0.15f, 1.8f, 7f), dark, new Float3(11.796f, 0.9f, -14.711f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(8.157f, 0.65f, -20.439f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(10.619f, 0.65f, -19.107f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Roof", new Float3(3.2f, 0.3f, 1.5f), ceiling, new Float3(9.388f, 1.554f, -19.773f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.673f, 1f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(7.443f, 0.65f, -19.12f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(9.905f, 0.65f, -17.787f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Roof", new Float3(3.2f, 0.3f, 1.5f), ceiling, new Float3(8.674f, 1.554f, -18.454f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.673f, 1f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(6.729f, 0.65f, -17.801f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(9.191f, 0.65f, -16.468f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Roof", new Float3(3.2f, 0.3f, 1.5f), ceiling, new Float3(7.96f, 1.554f, -17.135f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.673f, 1f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(6.015f, 0.65f, -16.482f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(8.478f, 0.65f, -15.149f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Roof", new Float3(3.2f, 0.3f, 1.5f), ceiling, new Float3(7.246f, 1.554f, -15.815f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.673f, 1f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(5.301f, 0.65f, -15.162f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(7.764f, 0.65f, -13.83f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Roof", new Float3(3.2f, 0.3f, 1.5f), ceiling, new Float3(6.532f, 1.554f, -14.496f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.673f, 1f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(4.587f, 0.65f, -13.843f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Wall", new Float3(0.4f, 1.3f, 1.5f), dark, new Float3(7.05f, 0.65f, -12.511f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f)));
        scene.Add(Box("Tunnel Roof", new Float3(3.2f, 0.3f, 1.5f), ceiling, new Float3(5.818f, 1.554f, -13.177f), new Quaternion(0f, -0.2454804f, 0f, 0.9694016f), new Float3(1f, 1.673f, 1f)));
        scene.Add(Box("Box Roof", new Float3(4f, 0.2f, 4f), trap, new Float3(22.439f, 3.9f, 8.774f), new Quaternion(0f, -0.54799926f, 0f, -0.83647895f)));
        scene.Add(Box("Box Wall", new Float3(0.2f, 4f, 4f), trap, new Float3(21.64f, 2f, 10.608f), new Quaternion(0f, -0.54799926f, 0f, -0.83647895f)));
        scene.Add(Box("Box Wall", new Float3(0.2f, 4f, 4f), trap, new Float3(23.237f, 2f, 6.941f), new Quaternion(0f, -0.54799926f, 0f, -0.83647895f)));
        scene.Add(Box("Box Wall", new Float3(4f, 4f, 0.2f), trap, new Float3(24.272f, 2f, 9.573f), new Quaternion(0f, -0.54799926f, 0f, -0.83647895f)));
        scene.Add(Box("V Wall", new Float3(0.2f, 3f, 5f), trap, new Float3(24.435f, 1.5f, 4.19f), new Quaternion(0f, -0.69754297f, 0f, -0.7165431f)));
        scene.Add(Box("V Wall", new Float3(0.2f, 3f, 5f), trap, new Float3(25.234f, 1.5f, 2.357f), new Quaternion(0f, -0.37833026f, 0f, -0.9256707f)));
        scene.Add(Box("Wedge Roof", new Float3(3f, 0.2f, 6f), trap, new Float3(27.231f, 1.3f, -2.227f), new Quaternion(-0.18104999f, -0.5350099f, 0.11860999f, -0.81664985f)));
        scene.Add(Box("Wedge Side", new Float3(0.2f, 2.6f, 6f), dark, new Float3(26.592f, 1.3f, -0.76f), new Quaternion(0f, -0.54799926f, 0f, -0.83647895f)));
        scene.Add(Box("Wedge Side", new Float3(0.2f, 2.6f, 6f), dark, new Float3(27.87f, 1.3f, -3.694f), new Quaternion(0f, -0.54799926f, 0f, -0.83647895f)));
        scene.Add(Box("Pocket", new Float3(0.2f, 4f, 4f), trap, new Float3(29.148f, 2f, -6.628f), new Quaternion(0.12950969f, -0.73449826f, 0.11566973f, -0.65601844f)));
        scene.Add(Box("Pocket", new Float3(0.2f, 4f, 4f), trap, new Float3(30.107f, 2f, -8.828f), new Quaternion(-0.054319963f, -0.30807978f, -0.16492988f, -0.9353793f)));
        scene.Add(Box("Funnel", new Float3(0.2f, 3f, 8.2f), trap, new Float3(31.564f, 1.5f, -12.174f), new Quaternion(0f, -0.6324397f, 0f, -0.7746096f)));
        scene.Add(Box("Funnel", new Float3(0.2f, 3f, 8.2f), trap, new Float3(32.483f, 1.5f, -14.283f), new Quaternion(0f, -0.45756742f, 0f, -0.889175f)));
        scene.Add(Piece("Quarter Pipe", QuarterPipe(1f, 3f, 16), curve, new Float3(-1.954f, 0f, -17.169f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Box("Pipe Back", new Float3(3f, 1.5f, 0.3f), dark, new Float3(-2.678f, 0.75f, -18.062f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Piece("Quarter Pipe", QuarterPipe(2f, 3f, 16), curve, new Float3(-4.673f, 0f, -14.965f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Box("Pipe Back", new Float3(3f, 2.5f, 0.3f), dark, new Float3(-6.026f, 1.25f, -16.635f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Piece("Quarter Pipe", QuarterPipe(3f, 3f, 16), curve, new Float3(-7.392f, 0f, -12.761f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Box("Pipe Back", new Float3(3f, 3.5f, 0.3f), dark, new Float3(-9.375f, 1.75f, -15.209f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Piece("Quarter Pipe", QuarterPipe(4.5f, 3f, 16), curve, new Float3(-10.111f, 0f, -10.558f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Box("Pipe Back", new Float3(3f, 5f, 0.3f), dark, new Float3(-13.039f, 2.5f, -14.17f), new Quaternion(0f, 0.94257706f, 0f, -0.33398896f)));
        scene.Add(Piece("Arch", ArchTunnel(1.6f, 6f, 20), curve, new Float3(-12.11f, 0f, -25.794f), new Quaternion(0f, -0.43033063f, 0f, 0.90267134f)));
        scene.Add(Piece("Arch", ArchTunnel(2.6f, 6f, 20), curve, new Float3(-8.662f, 0f, -22.509f), new Quaternion(0f, -0.43033063f, 0f, 0.90267134f)));

        {
            GameObject go = Pose(Model("Log", Mesh.CreateCylinder(0.3f, 6f, 24), curve, Float3.Zero), new Float3(-6.759f, 0.18f, -30.875f), new Quaternion(0.23617037f, 0.23617037f, 0.666501f, 0.666501f));
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 0.3f;
            cylinder.Height = 6f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Log", Mesh.CreateCylinder(0.6f, 6f, 24), curve, Float3.Zero), new Float3(-5.437f, 0.36f, -29.243f), new Quaternion(0.23617037f, 0.23617037f, 0.666501f, 0.666501f));
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 0.6f;
            cylinder.Height = 6f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Log", Mesh.CreateCylinder(1f, 6f, 24), curve, Float3.Zero), new Float3(-3.674f, 0.6f, -27.068f), new Quaternion(0.23617037f, 0.23617037f, 0.666501f, 0.666501f));
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 1f;
            cylinder.Height = 6f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Log", Mesh.CreateCylinder(1.5f, 6f, 24), curve, Float3.Zero), new Float3(-1.344f, 0.9f, -24.194f), new Quaternion(0.23617037f, 0.23617037f, 0.666501f, 0.666501f));
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 1.5f;
            cylinder.Height = 6f;
            scene.Add(go);
        }

        scene.Add(Piece("Dome", Dome(1.2f, 18), curve, new Float3(-10.215f, -0.42f, -34.303f), new Quaternion(0f, 0.33398896f, 0f, 0.94257706f)));
        scene.Add(Piece("Dome", Dome(2.2f, 18), curve, new Float3(-12.983f, -0.77f, -32.194f), new Quaternion(0f, 0.33398896f, 0f, 0.94257706f)));
        scene.Add(Piece("Dome", Dome(3.2f, 18), curve, new Float3(-17.398f, -1.12f, -28.915f), new Quaternion(0f, 0.33398896f, 0f, 0.94257706f)));
        scene.Add(Piece("Jagged Field", Heightfield(12f, 12, [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.39027068f, 0.25727388f, 0.44019875f, 0.30279452f, 0.26378825f, 0.025031522f, 0.2195143f, 0.56455076f, 0.117807366f, 0.4664926f, 0.4452581f, 0f, 0f, 0.15556775f, 0.5400708f, 0.47847128f, 0.415651f, 0.4537507f, 0.72749305f, 0.31846815f, 1.0780615f, 0.29325113f, 0.82911426f, 0.43102667f, 0f, 0f, 0.37403154f, 1.0864884f, 0.7839182f, 1.0386518f, 0.33905643f, 0.9972001f, 0.03188936f, 1.0484315f, 0.50832415f, 0.6537806f, 0.54152673f, 0f, 0f, 0.58823776f, 0.48878813f, 0.6330292f, 0.60193145f, 0.4026115f, 0.3317937f, 0.14799955f, 1.1728612f, 0.7395925f, 0.07885966f, 0.12003844f, 0f, 0f, 0.31108037f, 0.9653505f, 0.5056245f, 0.48729023f, 0.8854335f, 0.01834406f, 0.45653668f, 1.1015064f, 0.502f, 0.55254024f, 0.17259161f, 0f, 0f, 0.5466582f, 0.8141356f, 0.6962292f, 1.0028114f, 1.0862902f, 0.5954177f, 0.12673488f, 0.05169729f, 0.75101763f, 1.02968f, 0.22251604f, 0f, 0f, 0.2651299f, 0.0010527922f, 0.6532091f, 0.00847064f, 1.0076287f, 0.54384124f, 0.48987243f, 0.15209681f, 0.37503937f, 0.26653892f, 0.27140352f, 0f, 0f, 0.5323548f, 1.0323795f, 0.5005472f, 0.67447555f, 1.136248f, 0.28784588f, 0.20496856f, 1.0086828f, 0.43847737f, 0.533864f, 0.23831588f, 0f, 0f, 0.42232963f, 0.42204458f, 0.13526234f, 1.0711432f, 1.1356704f, 0.060592245f, 0.7959706f, 0.4589307f, 0.68808436f, 0.45548388f, 0.22414856f, 0f, 0f, 0.5006711f, 1.1070906f, 0.44183195f, 0.7182771f, 0.54759663f, 0.15342213f, 0.9817775f, 0.11794302f, 0.7307081f, 0.29435542f, 0.375575f, 0f, 0f, 0.07859314f, 0.34317547f, 0.30890995f, 0.0458912f, 0.3335943f, 0.08828858f, 0.13584475f, 0.40752953f, 0.4495909f, 0.0339139f, 0.008417235f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]), rough, new Float3(-31.305f, 0.02f, 14.909f), new Quaternion(0f, 0.72399503f, 0f, 0.68980527f)));
        scene.Add(Piece("Rolling Bumps", Heightfield(12f, 32, RollingBumpHeight), rough, new Float3(-31.933f, 0.02f, 1.924f), new Quaternion(0f, 0.72399503f, 0f, 0.68980527f)));
        scene.Add(Box("Pebble", new Float3(0.34691012f, 0.15773658f, 0.26260248f), dark, new Float3(-16.06f, 0.172f, 9.442f), new Quaternion(-0.012359967f, 0.9880374f, 0.0134399645f, 0.15312959f)));
        scene.Add(Box("Pebble", new Float3(0.08950361f, 0.41780198f, 0.08933811f), dark, new Float3(-15.668f, 0.09f, 8.105f), new Quaternion(0.023220051f, 0.95757204f, 0.059720125f, 0.2809806f)));
        scene.Add(Box("Pebble", new Float3(0.33421677f, 0.08405338f, 0.26977658f), dark, new Float3(-15.288f, 0.06f, 14.772f), new Quaternion(-1E-05f, 0.77901f, -0.08839f, 0.62075f)));
        scene.Add(Box("Pebble", new Float3(0.26191846f, 0.16802762f, 0.3268213f), dark, new Float3(-22.878f, 0.224f, 17.212f), new Quaternion(0.0093599865f, 0.81011885f, 0.0015099978f, 0.58618915f)));
        scene.Add(Box("Pebble", new Float3(0.091468066f, 0.1738607f, 0.07851097f), dark, new Float3(-12.623f, 0.13f, 13.755f), new Quaternion(-0.05398992f, 0.96984863f, 0.012579982f, 0.23731966f)));
        scene.Add(Box("Pebble", new Float3(0.2223625f, 0.2864317f, 0.1907252f), dark, new Float3(-13.593f, 0.087f, 11.174f), new Quaternion(-0.010349952f, 0.99383533f, 0.00426998f, 0.11029948f)));
        scene.Add(Box("Pebble", new Float3(0.30061918f, 0.43636882f, 0.27936772f), dark, new Float3(-17.241f, 0.049f, 20.148f), new Quaternion(0.009510028f, 0.81807244f, -0.0053600157f, 0.57501173f)));
        scene.Add(Box("Pebble", new Float3(0.22670054f, 0.24942984f, 0.20518999f), dark, new Float3(-23.958f, 0.036f, 16.493f), new Quaternion(-0.016090062f, 0.9402736f, 0.056980215f, 0.33523127f)));
        scene.Add(Box("Pebble", new Float3(0.13236275f, 0.35704878f, 0.11299634f), dark, new Float3(-13.686f, 0.146f, 15.685f), new Quaternion(0.067249924f, 0.8274791f, 0.0024299975f, 0.5574494f)));
        scene.Add(Box("Pebble", new Float3(0.19630909f, 0.41637468f, 0.2481443f), dark, new Float3(-15.705f, 0.186f, 12.716f), new Quaternion(0.008259968f, 0.98304623f, 0.024099909f, 0.18157932f)));
        scene.Add(Box("Pebble", new Float3(0.20909588f, 0.09295308f, 0.17152256f), dark, new Float3(-16.148f, 0.059f, 8.707f), new Quaternion(-0.028429912f, 0.78695756f, 0.0324299f, 0.61549807f)));
        scene.Add(Box("Pebble", new Float3(0.27479658f, 0.35379404f, 0.23005044f), dark, new Float3(-17.961f, 0.117f, 9.275f), new Quaternion(-0.021840077f, 0.88397306f, -0.06261022f, 0.46281162f)));
        scene.Add(Box("Pebble", new Float3(0.2623152f, 0.36616254f, 0.2987986f), dark, new Float3(-22.747f, 0.133f, 17.732f), new Quaternion(0.009479983f, 0.85973847f, 0.058199897f, 0.5073191f)));
        scene.Add(Box("Pebble", new Float3(0.09213934f, 0.07402155f, 0.08060249f), dark, new Float3(-23.726f, 0.198f, 13.125f), new Quaternion(-0.010529993f, 0.95849943f, -0.06962995f, 0.2762598f)));
        scene.Add(Box("Pebble", new Float3(0.16032937f, 0.1586264f, 0.12560536f), dark, new Float3(-20.68f, 0.22f, 18.562f), new Quaternion(0.06935009f, 0.98526144f, -0.026060037f, 0.15418023f)));
        scene.Add(Box("Pebble", new Float3(0.3030399f, 0.41034186f, 0.21470189f), dark, new Float3(-21.087f, 0.116f, 9.107f), new Quaternion(0.038299877f, 0.8011174f, -0.007919975f, 0.5972281f)));
        scene.Add(Box("Pebble", new Float3(0.16307332f, 0.13158824f, 0.20761783f), dark, new Float3(-22.521f, 0.205f, 12.931f), new Quaternion(0.09487044f, 0.7513235f, -0.0058600274f, 0.65305305f)));
        scene.Add(Box("Pebble", new Float3(0.27071726f, 0.168731f, 0.27327886f), dark, new Float3(-14.288f, 0.105f, 18.199f), new Quaternion(-0.017350016f, 0.999841f, 0.0010200009f, 0.0040200036f)));
        scene.Add(Box("Pebble", new Float3(0.32073665f, 0.06392026f, 0.3270317f), dark, new Float3(-16.667f, 0.211f, 8.459f), new Quaternion(-0.03997998f, 0.84213954f, -0.063109964f, 0.5340597f)));
        scene.Add(Box("Pebble", new Float3(0.22601523f, 0.24990036f, 0.1804332f), dark, new Float3(-18.994f, 0.155f, 12.154f), new Quaternion(-0.027400114f, 0.8385535f, -0.03578015f, 0.54295224f)));
        scene.Add(Box("Pebble", new Float3(0.34032f, 0.17528251f, 0.25625038f), dark, new Float3(-16.659f, 0.182f, 9.135f), new Quaternion(-0.03274001f, 0.94740033f, 0.029200012f, 0.31703013f)));
        scene.Add(Box("Pebble", new Float3(0.31936014f, 0.2393176f, 0.23632318f), dark, new Float3(-18.559f, 0.162f, 18.48f), new Quaternion(0.086540215f, 0.8357321f, -0.008340021f, 0.54221135f)));
        scene.Add(Box("Pebble", new Float3(0.15233482f, 0.3479761f, 0.16950148f), dark, new Float3(-16.024f, 0.214f, 18.193f), new Quaternion(0.023700044f, 0.94281167f, 0.026920049f, 0.3313906f)));
        scene.Add(Box("Pebble", new Float3(0.27442157f, 0.1323406f, 0.19812733f), dark, new Float3(-19.93f, 0.113f, 15.141f), new Quaternion(0.016760003f, 0.7905502f, -0.073660016f, 0.60772014f)));
        scene.Add(Box("Pebble", new Float3(0.30747575f, 0.055113863f, 0.27366725f), dark, new Float3(-16.065f, 0.159f, 8.101f), new Quaternion(0.015720064f, 0.99676406f, 0.022840094f, 0.07545031f)));
        scene.Add(Box("Pebble", new Float3(0.26640558f, 0.40044323f, 0.32165828f), dark, new Float3(-15.468f, 0.194f, 16.956f), new Quaternion(0.062550105f, 0.7836913f, 0.0063000107f, 0.617961f)));
        scene.Add(Box("Pebble", new Float3(0.14125428f, 0.17736237f, 0.12424617f), dark, new Float3(-23.836f, 0.082f, 17.225f), new Quaternion(-0.017799925f, 0.97286594f, -0.023449901f, 0.22948904f)));
        scene.Add(Box("Pebble", new Float3(0.15279528f, 0.13986696f, 0.11697835f), dark, new Float3(-17.001f, 0.116f, 14.132f), new Quaternion(0.055980094f, 0.9660116f, 0.01760003f, 0.2517504f)));
        scene.Add(Box("Pebble", new Float3(0.29294974f, 0.34044695f, 0.36903095f), dark, new Float3(-20.406f, 0.119f, 11.376f), new Quaternion(-0.05467024f, 0.97202426f, 0.015530069f, 0.227901f)));
        scene.Add(Box("Pebble", new Float3(0.11033841f, 0.22264698f, 0.0817609f), dark, new Float3(-18.195f, 0.066f, 8.523f), new Quaternion(0.045850087f, 0.98117185f, 0.068150125f, 0.17480032f)));
        scene.Add(Box("Pebble", new Float3(0.23282607f, 0.1208937f, 0.3014244f), dark, new Float3(-20.389f, 0.172f, 17.633f), new Quaternion(-0.010630011f, 0.92980087f, 0.038110036f, 0.36593035f)));
        scene.Add(Box("Pebble", new Float3(0.2990436f, 0.19040804f, 0.24132912f), dark, new Float3(-20.43f, 0.193f, 19.594f), new Quaternion(0.06954021f, 0.72726214f, 0.005950018f, 0.682802f)));
        scene.Add(Box("Pebble", new Float3(0.1666803f, 0.3452417f, 0.1808794f), dark, new Float3(-14.641f, 0.215f, 16.281f), new Quaternion(-0.011860003f, 0.80862015f, -0.07730001f, 0.5831101f)));
        scene.Add(Box("Pebble", new Float3(0.15678239f, 0.3862955f, 0.12724501f), dark, new Float3(-15.678f, 0.113f, 17.833f), new Quaternion(-0.03853986f, 0.9910465f, -0.07362974f, 0.10449963f)));
        scene.Add(Box("Pebble", new Float3(0.1168385f, 0.1655474f, 0.12807307f), dark, new Float3(-18.538f, 0.036f, 19.951f), new Quaternion(-0.034350112f, 0.934973f, 0.026960088f, 0.35202113f)));
        scene.Add(Box("Pebble", new Float3(0.14032891f, 0.30286825f, 0.109796174f), dark, new Float3(-17.948f, 0.188f, 7.827f), new Quaternion(-0.0257201f, 0.98573387f, -0.049590193f, 0.15877062f)));
        scene.Add(Box("Pebble", new Float3(0.17922693f, 0.24498929f, 0.20218179f), dark, new Float3(-23.447f, 0.092f, 13.458f), new Quaternion(-0.023779968f, 0.93520874f, 0.03150996f, 0.35188955f)));
        scene.Add(Box("Pebble", new Float3(0.2631778f, 0.07883729f, 0.33567148f), dark, new Float3(-16.265f, 0.138f, 19.812f), new Quaternion(-0.019120043f, 0.7873418f, -0.0881602f, 0.60988134f)));
        scene.Add(Box("Pebble", new Float3(0.2403984f, 0.3090065f, 0.23084083f), dark, new Float3(-16.409f, 0.13f, 10.878f), new Quaternion(0.017490065f, 0.76870286f, 0.0807103f, 0.6342523f)));
        scene.Add(Box("Pebble", new Float3(0.18711942f, 0.34906846f, 0.16953458f), dark, new Float3(-23.443f, 0.139f, 15.066f), new Quaternion(0.0272599f, 0.9310265f, 0.040879846f, 0.36162865f)));
        scene.Add(Box("Pebble", new Float3(0.16012892f, 0.42773935f, 0.19847646f), dark, new Float3(-19.942f, 0.142f, 12.813f), new Quaternion(-0.001220001f, 0.9992908f, 0.031590026f, -0.020460017f)));
        scene.Add(Box("Pebble", new Float3(0.31016192f, 0.18453978f, 0.29210845f), dark, new Float3(-19.985f, 0.09f, 10.988f), new Quaternion(-0.060539715f, 0.9981352f, -0.0063399696f, 0.004579978f)));
        scene.Add(Box("Pebble", new Float3(0.20167318f, 0.37844896f, 0.26051944f), dark, new Float3(-17.306f, 0.07f, 15.211f), new Quaternion(-0.05569017f, 0.81913245f, -0.028320083f, 0.57019174f)));
        scene.Add(Box("Pebble", new Float3(0.17348474f, 0.05258901f, 0.12899297f), dark, new Float3(-15.116f, 0.127f, 10.21f), new Quaternion(-0.021679927f, 0.9827367f, -0.06396978f, 0.17223942f)));
        scene.Add(Box("Pebble", new Float3(0.0942861f, 0.08656959f, 0.07362488f), dark, new Float3(-20.415f, 0.028f, 8.365f), new Quaternion(0.007330016f, 0.927292f, 0.06943015f, 0.36777076f)));
        scene.Add(Box("Pebble", new Float3(0.10039647f, 0.36514977f, 0.12318965f), dark, new Float3(-17.219f, 0.156f, 19.28f), new Quaternion(0.067810215f, 0.939283f, -0.027250087f, 0.33527106f)));
        scene.Add(Box("Pebble", new Float3(0.1296043f, 0.17305735f, 0.1629811f), dark, new Float3(-14.969f, 0.118f, 16.27f), new Quaternion(-0.036270108f, 0.7791023f, -0.01346004f, 0.62570184f)));
        scene.Add(Box("Pebble", new Float3(0.08428419f, 0.15588747f, 0.08394172f), dark, new Float3(-18.426f, 0.091f, 14.218f), new Quaternion(0.014220015f, 0.9897911f, 0.017430019f, 0.14074016f)));
        scene.Add(Box("Pebble", new Float3(0.26890916f, 0.24687175f, 0.24269138f), dark, new Float3(-16.746f, 0.034f, 10.1f), new Quaternion(0.001930001f, 0.94222045f, 0.04937002f, 0.33133015f)));
        scene.Add(Box("Pebble", new Float3(0.16067189f, 0.37816396f, 0.17199698f), dark, new Float3(-19.577f, 0.051f, 14.249f), new Quaternion(-0.0147899585f, 0.8382976f, -0.002069994f, 0.5450084f)));
        scene.Add(Box("Pebble", new Float3(0.32982105f, 0.341121f, 0.40253487f), dark, new Float3(-15.481f, 0.07f, 13.714f), new Quaternion(0.018910076f, 0.99372405f, 0.030870125f, 0.10584043f)));
        scene.Add(Box("Pebble", new Float3(0.27853483f, 0.13450561f, 0.21681814f), dark, new Float3(-21.082f, 0.036f, 12.35f), new Quaternion(-0.048719846f, 0.95247704f, -0.012739961f, 0.30041906f)));
        scene.Add(Box("Pebble", new Float3(0.267024f, 0.29446995f, 0.18977113f), dark, new Float3(-15.86f, 0.058f, 8.845f), new Quaternion(-0.02240001f, 0.96579045f, 0.04908002f, 0.25365013f)));
        scene.Add(Box("Pebble", new Float3(0.31056774f, 0.2866094f, 0.23883085f), dark, new Float3(-19.233f, 0.079f, 17.38f), new Quaternion(0.039959967f, 0.9514792f, 0.07354994f, 0.29610977f)));
        scene.Add(Box("Pebble", new Float3(0.3250594f, 0.2736482f, 0.38827285f), dark, new Float3(-12.509f, 0.108f, 12.489f), new Quaternion(-0.026549974f, 0.9563091f, -0.016359985f, 0.29068974f)));
        scene.Add(Box("Pebble", new Float3(0.25818092f, 0.054669097f, 0.26597774f), dark, new Float3(-21.458f, 0.127f, 14.208f), new Quaternion(-0.04061008f, 0.89784175f, 0.00520001f, 0.43841085f)));
        scene.Add(Box("Pebble", new Float3(0.11949841f, 0.42797953f, 0.11472094f), dark, new Float3(-16.362f, 0.108f, 9.892f), new Quaternion(0.04791003f, 0.9805506f, 0.014840009f, 0.18975012f)));
        scene.Add(Box("Pebble", new Float3(0.31209683f, 0.109023735f, 0.3853093f), dark, new Float3(-20.77f, 0.138f, 8.635f), new Quaternion(0.045219902f, 0.99001783f, 0.0017299963f, 0.13347971f)));
        scene.Add(Box("Pebble", new Float3(0.13034458f, 0.3756406f, 0.1430361f), dark, new Float3(-14.11f, 0.12f, 10.047f), new Quaternion(-0.022579944f, 0.98454756f, -0.025859935f, 0.17171957f)));
        scene.Add(Box("Pebble", new Float3(0.09424851f, 0.37499887f, 0.07691019f), dark, new Float3(-21.022f, 0.201f, 14.973f), new Quaternion(0.010330042f, 0.97204393f, 0.00995004f, 0.23436095f)));
        scene.Add(Box("Pebble", new Float3(0.2837383f, 0.27174333f, 0.3095992f), dark, new Float3(-18.355f, 0.117f, 18.194f), new Quaternion(0.0032999967f, 0.9652791f, 0.060319945f, 0.25413975f)));
        scene.Add(Box("Pebble", new Float3(0.29161656f, 0.37471104f, 0.27749616f), dark, new Float3(-22.045f, 0.059f, 9.058f), new Quaternion(0.034599975f, 0.97671926f, -0.04066997f, 0.20776986f)));
        scene.Add(Box("Pebble", new Float3(0.2428411f, 0.4494413f, 0.26050016f), dark, new Float3(-19.113f, 0.163f, 17.345f), new Quaternion(0.056030177f, 0.98957306f, -0.0020800065f, 0.13267042f)));
        scene.Add(Box("Pebble", new Float3(0.16091946f, 0.10733912f, 0.13626084f), dark, new Float3(-14.834f, 0.195f, 15.653f), new Quaternion(-0.03846994f, 0.8436687f, -0.034069948f, 0.53439915f)));
        scene.Add(Box("Pebble", new Float3(0.097112454f, 0.30625796f, 0.11231627f), dark, new Float3(-16.894f, 0.168f, 15.323f), new Quaternion(-0.016839948f, 0.9981869f, 0.019659938f, 0.054339834f)));
        scene.Add(Box("Pebble", new Float3(0.09692754f, 0.060275372f, 0.06801715f), dark, new Float3(-16.435f, 0.119f, 11.163f), new Quaternion(0.06342004f, 0.7509505f, -0.023090016f, 0.65690047f)));
        scene.Add(Box("Pebble", new Float3(0.111924954f, 0.36591735f, 0.13221669f), dark, new Float3(-24.67f, 0.201f, 12.413f), new Quaternion(-0.03178003f, 0.88476086f, 0.003080003f, 0.46495044f)));
        scene.Add(Box("Pebble", new Float3(0.22783402f, 0.33996704f, 0.1683655f), dark, new Float3(-18.193f, 0.196f, 15.539f), new Quaternion(-0.05760974f, 0.98055553f, 0.028029874f, 0.18548916f)));
        scene.Add(Box("Pebble", new Float3(0.30937916f, 0.365253f, 0.3578566f), dark, new Float3(-14.247f, 0.066f, 15.758f), new Quaternion(-0.019050064f, 0.9984033f, -0.012660041f, 0.05165017f)));
        scene.Add(Box("Pebble", new Float3(0.099672705f, 0.35872787f, 0.09832897f), dark, new Float3(-24.181f, 0.091f, 12.736f), new Quaternion(0.032810077f, 0.95365226f, 0.007970018f, 0.29901072f)));
        scene.Add(Box("Pebble", new Float3(0.13406283f, 0.12329695f, 0.12329904f), dark, new Float3(-18.591f, 0.062f, 12.916f), new Quaternion(-0.007099999f, 0.90416986f, 0.022709997f, 0.42650995f)));
        scene.Add(Box("Pebble", new Float3(0.34770977f, 0.062838465f, 0.32019436f), dark, new Float3(-24.736f, 0.139f, 12.634f), new Quaternion(0.037839983f, 0.8308396f, -0.030059986f, 0.55440974f)));
        scene.Add(Box("Pebble", new Float3(0.17005911f, 0.3593072f, 0.15531713f), dark, new Float3(-20.095f, 0.026f, 12.311f), new Quaternion(0.013010044f, 0.9886333f, -0.00094000314f, 0.14978051f)));
        scene.Add(Box("Pebble", new Float3(0.24584506f, 0.06535839f, 0.27157235f), dark, new Float3(-15.958f, 0.044f, 10.745f), new Quaternion(-0.04866981f, 0.99599606f, -0.016139938f, 0.07322971f)));
        scene.Add(Box("Pebble", new Float3(0.21235612f, 0.26574594f, 0.22865312f), dark, new Float3(-19.958f, 0.033f, 7.573f), new Quaternion(0.029199881f, 0.73798704f, -0.013519945f, 0.67404723f)));
        scene.Add(Box("Pebble", new Float3(0.20760757f, 0.40909737f, 0.21614832f), dark, new Float3(-16.925f, 0.106f, 10.196f), new Quaternion(0.0727497f, 0.9849559f, -0.056989763f, 0.14601938f)));
        scene.Add(Box("Pebble", new Float3(0.2605844f, 0.34655097f, 0.20670116f), dark, new Float3(-14.449f, 0.196f, 12.934f), new Quaternion(-0.031670097f, 0.98399305f, -0.0018800058f, 0.17536053f)));
        scene.Add(Box("Pebble", new Float3(0.12700967f, 0.34681338f, 0.1380981f), dark, new Float3(-18.071f, 0.155f, 13.673f), new Quaternion(0.017490081f, 0.9581345f, 0.07597035f, 0.27550128f)));
        scene.Add(Box("Pebble", new Float3(0.085505895f, 0.20914659f, 0.065438926f), dark, new Float3(-21.199f, 0.088f, 17.833f), new Quaternion(-0.07356998f, 0.95899975f, 0.016559996f, 0.27318993f)));
        scene.Add(Box("Pebble", new Float3(0.11372301f, 0.4175336f, 0.105352506f), dark, new Float3(-20.741f, 0.18f, 9.332f), new Quaternion(0.058190048f, 0.9217408f, 0.040320035f, 0.38129032f)));
        scene.Add(Box("Pebble", new Float3(0.09130637f, 0.23649997f, 0.10728466f), dark, new Float3(-19.029f, 0.059f, 12.329f), new Quaternion(0.041960165f, 0.9989339f, 0.0069900276f, -0.01793007f)));
        scene.Add(Box("Pebble", new Float3(0.23224714f, 0.15902178f, 0.21785939f), dark, new Float3(-21.966f, 0.025f, 15.617f), new Quaternion(0.0061100014f, 0.8960902f, 0.077620015f, 0.43699008f)));
        scene.Add(Box("Pebble", new Float3(0.31131637f, 0.38969424f, 0.32882464f), dark, new Float3(-20.539f, 0.105f, 19.366f), new Quaternion(0.03193015f, 0.9025342f, 0.052900247f, 0.42616197f)));
        scene.Add(Box("Pebble", new Float3(0.19802389f, 0.11629088f, 0.171626f), dark, new Float3(-22.491f, 0.026f, 12.917f), new Quaternion(-0.06887022f, 0.9959031f, 0.04624014f, 0.03600011f)));
        scene.Add(Box("Pebble", new Float3(0.2285005f, 0.1963481f, 0.2865839f), dark, new Float3(-18.913f, 0.035f, 14.946f), new Quaternion(-0.02478013f, 0.82717437f, -0.025200132f, 0.5608329f)));
        scene.Add(Box("Pebble", new Float3(0.12374621f, 0.080221534f, 0.1247589f), dark, new Float3(-18.452f, 0.195f, 9.034f), new Quaternion(0.04224998f, 0.88546956f, 0.056669973f, 0.4592898f)));
        scene.Add(Box("Pebble", new Float3(0.21020015f, 0.27187255f, 0.21920612f), dark, new Float3(-16.416f, 0.13f, 19.789f), new Quaternion(-0.055220015f, 0.9701402f, -0.03674001f, 0.23330006f)));
        scene.Add(Box("Pebble", new Float3(0.32824597f, 0.41557592f, 0.37316546f), dark, new Float3(-16.216f, 0.108f, 18.093f), new Quaternion(-0.0440498f, 0.96533567f, -0.028189871f, 0.25571883f)));
        scene.Add(Box("Pebble", new Float3(0.24580963f, 0.3754335f, 0.17717944f), dark, new Float3(-16.087f, 0.135f, 9.42f), new Quaternion(-0.039910205f, 0.75359386f, -0.008450043f, 0.65607333f)));
        scene.Add(Box("Pebble", new Float3(0.26980898f, 0.44828594f, 0.3350336f), dark, new Float3(-23.093f, 0.112f, 13.51f), new Quaternion(0.080250196f, 0.816902f, -0.00416001f, 0.5711514f)));
        scene.Add(Box("Pebble", new Float3(0.20588666f, 0.36001673f, 0.25735736f), dark, new Float3(-13.436f, 0.125f, 13.906f), new Quaternion(0.035099987f, 0.8680196f, -0.057319973f, 0.49195975f)));
        scene.Add(Box("Pebble", new Float3(0.21092309f, 0.21472481f, 0.2251058f), dark, new Float3(-19.11f, 0.154f, 10.267f), new Quaternion(0.036139812f, 0.84858567f, -0.0022899883f, 0.5278173f)));
        scene.Add(Box("Pebble", new Float3(0.31844348f, 0.13747947f, 0.23867495f), dark, new Float3(-20.862f, 0.044f, 13.657f), new Quaternion(-0.05486998f, 0.96388966f, -0.04930998f, 0.2558799f)));
        scene.Add(Box("Pebble", new Float3(0.15853918f, 0.3909209f, 0.11218147f), dark, new Float3(-18.338f, 0.136f, 17.209f), new Quaternion(0.013659923f, 0.74246585f, 0.0707596f, 0.66599625f)));
        scene.Add(Box("Pebble", new Float3(0.34460187f, 0.2408658f, 0.291877f), dark, new Float3(-14.585f, 0.199f, 16.004f), new Quaternion(-0.06583014f, 0.9855821f, 0.06037013f, 0.14370032f)));
        scene.Add(Box("Pebble", new Float3(0.21255815f, 0.37288085f, 0.2760103f), dark, new Float3(-18.361f, 0.154f, 8.528f), new Quaternion(0.017160058f, 0.9374831f, -0.0303401f, 0.34628117f)));
        scene.Add(Box("Pebble", new Float3(0.08292736f, 0.38838434f, 0.08234635f), dark, new Float3(-19.548f, 0.179f, 7.683f), new Quaternion(0.05000005f, 0.991591f, 0.06471006f, 0.100300096f)));
        scene.Add(Box("Pebble", new Float3(0.08145441f, 0.26177764f, 0.071936935f), dark, new Float3(-15.889f, 0.217f, 8.728f), new Quaternion(-0.06774015f, 0.87395185f, 0.035180073f, 0.47998103f)));
        scene.Add(Box("Pebble", new Float3(0.19896656f, 0.070365585f, 0.18232733f), dark, new Float3(-13.746f, 0.095f, 10.317f), new Quaternion(-0.0074399672f, 0.7696566f, -0.05492976f, 0.6360472f)));
        scene.Add(Box("Pebble", new Float3(0.10408086f, 0.3453752f, 0.08051415f), dark, new Float3(-15.167f, 0.102f, 16.73f), new Quaternion(0.032760065f, 0.9970219f, -0.06348012f, 0.029060056f)));
        scene.Add(Box("Pebble", new Float3(0.27321824f, 0.3069616f, 0.3105513f), dark, new Float3(-15.217f, 0.068f, 11.412f), new Quaternion(-0.06487991f, 0.93956864f, 0.03461995f, 0.3343695f)));
        scene.Add(Box("Pebble", new Float3(0.2260501f, 0.19481611f, 0.16325793f), dark, new Float3(-22.025f, 0.027f, 17.894f), new Quaternion(-0.037030097f, 0.99373263f, -0.07166018f, 0.0773902f)));
        scene.Add(Box("Pebble", new Float3(0.15862161f, 0.2195479f, 0.13238898f), dark, new Float3(-13.568f, 0.033f, 11.427f), new Quaternion(-0.010060042f, 0.99128413f, -0.024080101f, 0.12913054f)));
        scene.Add(Box("Pebble", new Float3(0.33062547f, 0.13848765f, 0.23635308f), dark, new Float3(-23.104f, 0.037f, 18.512f), new Quaternion(0.042000044f, 0.90064096f, 0.041640043f, 0.43052045f)));
        scene.Add(Box("Pebble", new Float3(0.16156805f, 0.20817916f, 0.13074894f), dark, new Float3(-18.52f, 0.113f, 14.538f), new Quaternion(0.03039995f, 0.85228854f, -0.04988992f, 0.5197991f)));
        scene.Add(Box("Pebble", new Float3(0.16918746f, 0.16343193f, 0.12697722f), dark, new Float3(-15.843f, 0.215f, 18.662f), new Quaternion(-0.027870003f, 0.89002013f, -0.0028000001f, 0.45506006f)));
        scene.Add(Box("Pebble", new Float3(0.23118432f, 0.23603114f, 0.29562435f), dark, new Float3(-16.548f, 0.163f, 9.94f), new Quaternion(-0.04128003f, 0.7588505f, 0.021170015f, 0.64961046f)));
        scene.Add(Box("Pebble", new Float3(0.30148995f, 0.28433818f, 0.33621588f), dark, new Float3(-18.931f, 0.162f, 19.892f), new Quaternion(0.023949951f, 0.999578f, -0.0062399874f, 0.015209969f)));
        scene.Add(Box("Pebble", new Float3(0.2654943f, 0.085625745f, 0.29996926f), dark, new Float3(-21.207f, 0.171f, 17.034f), new Quaternion(0.0142199695f, 0.8394882f, -0.049859893f, 0.54089886f)));
        scene.Add(Box("Pebble", new Float3(0.08796421f, 0.06144303f, 0.08309787f), dark, new Float3(-13.594f, 0.138f, 16.011f), new Quaternion(-0.06013989f, 0.84889853f, 0.03229994f, 0.52412903f)));
        scene.Add(Box("Pebble", new Float3(0.30799264f, 0.2783937f, 0.34066767f), dark, new Float3(-23.192f, 0.086f, 13.256f), new Quaternion(0.07567971f, 0.72146726f, -0.011919955f, 0.6881974f)));
        scene.Add(Box("Pebble", new Float3(0.23741263f, 0.30395404f, 0.18198055f), dark, new Float3(-21.239f, 0.103f, 15.966f), new Quaternion(-0.03246016f, 0.7281436f, -0.012940063f, 0.68453336f)));
        scene.Add(Box("Pebble", new Float3(0.23180139f, 0.3355935f, 0.27195653f), dark, new Float3(-15.684f, 0.056f, 10.911f), new Quaternion(0.059399724f, 0.7658664f, -0.03582983f, 0.639247f)));
        scene.Add(Box("Pebble", new Float3(0.32384765f, 0.066895075f, 0.35579905f), dark, new Float3(-19.697f, 0.18f, 17.454f), new Quaternion(-0.028139958f, 0.94246864f, 0.0102699855f, 0.33294952f)));
        scene.Add(Box("Pebble", new Float3(0.26657712f, 0.25495118f, 0.311484f), dark, new Float3(-17.43f, 0.121f, 11.336f), new Quaternion(0.019490039f, 0.92543185f, 0.016310034f, 0.3780608f)));
        scene.Add(Box("Pebble", new Float3(0.13266176f, 0.20682822f, 0.14644144f), dark, new Float3(-12.784f, 0.185f, 12.659f), new Quaternion(0.020969933f, 0.82488734f, -0.065239795f, 0.5611282f)));
        scene.Add(Box("Pebble", new Float3(0.23307563f, 0.15266854f, 0.23474377f), dark, new Float3(-19.811f, 0.157f, 19.091f), new Quaternion(0.04788025f, 0.91885483f, -0.034110177f, 0.39019206f)));
        scene.Add(Box("Pebble", new Float3(0.15110976f, 0.10825657f, 0.13361283f), dark, new Float3(-14.727f, 0.187f, 12.202f), new Quaternion(-0.057800103f, 0.94939166f, -0.043500077f, 0.30565053f)));
        scene.Add(Box("Pebble", new Float3(0.1687851f, 0.38827237f, 0.14101204f), dark, new Float3(-15.184f, 0.078f, 18.106f), new Quaternion(0.032219887f, 0.8179772f, -0.032839887f, 0.573408f)));
        scene.Add(Box("Pebble", new Float3(0.21791786f, 0.29032782f, 0.2410137f), dark, new Float3(-21.029f, 0.114f, 19.88f), new Quaternion(-0.017839909f, 0.9036754f, 0.030499844f, 0.4267578f)));
        scene.Add(Box("Pebble", new Float3(0.13702948f, 0.26104528f, 0.13646114f), dark, new Float3(-17.468f, 0.092f, 18.781f), new Quaternion(0.029700004f, 0.8918601f, -0.040660005f, 0.44950005f)));
        scene.Add(Box("Pebble", new Float3(0.23525529f, 0.27115968f, 0.23597413f), dark, new Float3(-15.307f, 0.115f, 14.487f), new Quaternion(-0.081650324f, 0.9076537f, 0.031670127f, 0.41048166f)));
        scene.Add(Box("Pebble", new Float3(0.08948491f, 0.18888603f, 0.06588715f), dark, new Float3(-12.149f, 0.028f, 14.15f), new Quaternion(0.025480134f, 0.7876141f, 0.019490102f, 0.6153332f)));
        scene.Add(Box("Pebble", new Float3(0.26301008f, 0.44119483f, 0.19982736f), dark, new Float3(-13.957f, 0.055f, 13.577f), new Quaternion(0.06455016f, 0.98636246f, 0.014960038f, 0.15066037f)));
        scene.Add(Box("Pebble", new Float3(0.2891037f, 0.33764833f, 0.30455115f), dark, new Float3(-14.563f, 0.141f, 18.535f), new Quaternion(0.09143022f, 0.8788821f, -0.02530006f, 0.46751112f)));
        scene.Add(Box("Pebble", new Float3(0.17740735f, 0.09555261f, 0.17560436f), dark, new Float3(-20.659f, 0.083f, 19.239f), new Quaternion(0.049109783f, 0.8053464f, -0.037309837f, 0.5895874f)));
        scene.Add(Box("Pebble", new Float3(0.1332152f, 0.2752396f, 0.13108736f), dark, new Float3(-17.58f, 0.15f, 20.044f), new Quaternion(-0.059260055f, 0.8846008f, -0.017940016f, 0.46222046f)));
        scene.Add(Box("Pebble", new Float3(0.24516623f, 0.10709583f, 0.23475625f), dark, new Float3(-15.71f, 0.035f, 10.267f), new Quaternion(0.029449984f, 0.89407957f, -0.041589983f, 0.44499978f)));
        scene.Add(Box("Pebble", new Float3(0.112491444f, 0.37813002f, 0.08854681f), dark, new Float3(-14.482f, 0.223f, 18.001f), new Quaternion(-0.024669955f, 0.80765855f, -0.02289996f, 0.5886889f)));
        scene.Add(Box("Pebble", new Float3(0.11481424f, 0.064442284f, 0.09477273f), dark, new Float3(-13.995f, 0.223f, 10.154f), new Quaternion(0.0046299975f, 0.9965295f, 0.02266999f, 0.079959966f)));
        scene.Add(Box("Pebble", new Float3(0.102276415f, 0.1463186f, 0.082589805f), dark, new Float3(-23.436f, 0.1f, 14.198f), new Quaternion(0.07564035f, 0.9754545f, -0.033560157f, 0.20406096f)));
        scene.Add(Box("Pebble", new Float3(0.3073861f, 0.22786984f, 0.25560257f), dark, new Float3(-20.372f, 0.183f, 19.559f), new Quaternion(-0.022979913f, 0.79586697f, -0.078219704f, 0.59995776f)));
        scene.Add(Box("Pebble", new Float3(0.19398575f, 0.4456614f, 0.14545892f), dark, new Float3(-14.074f, 0.051f, 9.819f), new Quaternion(-0.01044003f, 0.91131264f, 0.017250048f, 0.41122118f)));
        scene.Add(Box("Pebble", new Float3(0.32709667f, 0.16022605f, 0.24409467f), dark, new Float3(-19.153f, 0.127f, 10.501f), new Quaternion(0.04555012f, 0.84175223f, 0.024490064f, 0.5373814f)));
        scene.Add(Box("Pebble", new Float3(0.18787327f, 0.448348f, 0.22843005f), dark, new Float3(-23.047f, 0.143f, 14.596f), new Quaternion(0.04977018f, 0.99410355f, -0.04165015f, 0.08687031f)));
        scene.Add(Box("Pebble", new Float3(0.14923176f, 0.3019804f, 0.14534976f), dark, new Float3(-22.029f, 0.202f, 16.191f), new Quaternion(0.02995995f, 0.9103185f, 0.040319934f, 0.4108493f)));
        scene.Add(Box("Pebble", new Float3(0.28216034f, 0.32188198f, 0.31441343f), dark, new Float3(-13.762f, 0.039f, 16.235f), new Quaternion(0.042120155f, 0.9270834f, -0.057340212f, 0.36804137f)));
        scene.Add(Box("Pebble", new Float3(0.19983259f, 0.10649768f, 0.2481103f), dark, new Float3(-16.386f, 0.029f, 12.139f), new Quaternion(0.024249908f, 0.9713763f, 0.014989943f, 0.2358291f)));
        scene.Add(Box("Pebble", new Float3(0.2622671f, 0.3695923f, 0.22616526f), dark, new Float3(-20.197f, 0.19f, 16.655f), new Quaternion(-0.05150016f, 0.97126305f, -0.03577011f, 0.22960071f)));
        scene.Add(Box("Pebble", new Float3(0.11619018f, 0.26412556f, 0.09288537f), dark, new Float3(-20.761f, 0.081f, 15.493f), new Quaternion(-0.066220075f, 0.964241f, 0.051400054f, 0.25142026f)));
        scene.Add(Box("Pebble", new Float3(0.19038643f, 0.35988617f, 0.17963773f), dark, new Float3(-21.582f, 0.116f, 12.595f), new Quaternion(-0.039989986f, 0.9652597f, -0.018789992f, 0.2575299f)));
        scene.Add(Box("Pebble", new Float3(0.17311284f, 0.23383984f, 0.17662849f), dark, new Float3(-22.261f, 0.203f, 14.808f), new Quaternion(0.05320995f, 0.8438892f, 0.0049499953f, 0.5338495f)));
        scene.Add(Box("Pebble", new Float3(0.20860541f, 0.06278917f, 0.1654026f), dark, new Float3(-18.203f, 0.204f, 14.312f), new Quaternion(-0.057569876f, 0.926158f, 0.024509948f, 0.3719092f)));
        scene.Add(Box("Pebble", new Float3(0.28318286f, 0.1529847f, 0.3127726f), dark, new Float3(-24.365f, 0.193f, 11.171f), new Quaternion(0.05639996f, 0.99444926f, 0.030399978f, 0.08345994f)));
        scene.Add(Box("Pebble", new Float3(0.08678172f, 0.41311392f, 0.08355903f), dark, new Float3(-16.889f, 0.218f, 12.342f), new Quaternion(0.04531004f, 0.76773065f, 0.052360043f, 0.6370205f)));
        scene.Add(Box("Pebble", new Float3(0.11261521f, 0.3410901f, 0.10277157f), dark, new Float3(-16.155f, 0.119f, 10.314f), new Quaternion(0.0008200017f, 0.8841518f, 0.026430054f, 0.46645096f)));
        scene.Add(Box("Pebble", new Float3(0.19360566f, 0.16061878f, 0.14376102f), dark, new Float3(-12.702f, 0.176f, 15.303f), new Quaternion(0.058550082f, 0.9210013f, 0.0032600046f, 0.38512054f)));
        scene.Add(Box("Pebble", new Float3(0.24727188f, 0.065486886f, 0.2419025f), dark, new Float3(-15.093f, 0.074f, 18.348f), new Quaternion(0.0424198f, 0.9889653f, -0.019639907f, 0.14057933f)));
        scene.Add(Box("Pebble", new Float3(0.25612456f, 0.20887816f, 0.2494703f), dark, new Float3(-13.011f, 0.147f, 14.842f), new Quaternion(0.035960175f, 0.9923048f, 0.056060277f, 0.1043805f)));
        scene.Add(Box("Pebble", new Float3(0.22203329f, 0.4100511f, 0.25732005f), dark, new Float3(-17.263f, 0.199f, 16.381f), new Quaternion(-0.08751008f, 0.7452107f, 0.007400007f, 0.66102064f)));
        scene.Add(Box("Pebble", new Float3(0.22156982f, 0.41133302f, 0.18448986f), dark, new Float3(-13.796f, 0.191f, 15.862f), new Quaternion(0.043099876f, 0.9829172f, -0.061309826f, 0.16809952f)));
        scene.Add(Box("Pebble", new Float3(0.17424408f, 0.34172815f, 0.13361935f), dark, new Float3(-15.721f, 0.067f, 8.978f), new Quaternion(-0.0013800043f, 0.9957431f, 0.02555008f, 0.08855028f)));
        scene.Add(Box("Pebble", new Float3(0.10236059f, 0.34260717f, 0.103294425f), dark, new Float3(-22.722f, 0.098f, 11.607f), new Quaternion(0.04901987f, 0.7370081f, 0.028569926f, 0.6734982f)));
        scene.Add(Box("Pebble", new Float3(0.23791727f, 0.15270036f, 0.29810026f), dark, new Float3(-15.477f, 0.056f, 12.23f), new Quaternion(0.05595982f, 0.9941568f, 0.03662988f, 0.08472972f)));
        scene.Add(Box("Pebble", new Float3(0.2806697f, 0.090378165f, 0.3292616f), dark, new Float3(-19.495f, 0.182f, 10.386f), new Quaternion(-0.07842022f, 0.90360254f, 0.017920053f, 0.42075118f)));
        scene.Add(Box("Pebble", new Float3(0.11258608f, 0.100708805f, 0.11111385f), dark, new Float3(-19.177f, 0.072f, 10.028f), new Quaternion(-0.034029897f, 0.83405745f, 0.049269848f, 0.5484183f)));
        scene.Add(Box("Pebble", new Float3(0.14441255f, 0.42562175f, 0.11716585f), dark, new Float3(-23.902f, 0.088f, 14.529f), new Quaternion(0.055680245f, 0.97492427f, 0.007980036f, 0.21531096f)));
        scene.Add(Box("Pebble", new Float3(0.12792014f, 0.17011671f, 0.09459928f), dark, new Float3(-18.981f, 0.031f, 16.804f), new Quaternion(-0.038389973f, 0.9982493f, 0.0037399975f, 0.044839967f)));
        scene.Add(Box("Pebble", new Float3(0.27675647f, 0.14323838f, 0.3155086f), dark, new Float3(-23.77f, 0.072f, 16.428f), new Quaternion(0.005880018f, 0.98571306f, -0.0063600196f, 0.16821052f)));
        scene.Add(Box("Pebble", new Float3(0.29751825f, 0.1219611f, 0.35643694f), dark, new Float3(-19.735f, 0.099f, 15.996f), new Quaternion(0.04961992f, 0.99792844f, -0.022379965f, 0.03428995f)));
        scene.Add(Box("Pebble", new Float3(0.2549914f, 0.2256473f, 0.2989535f), dark, new Float3(-19.751f, 0.141f, 9.383f), new Quaternion(-0.0023799874f, 0.86050546f, 0.04957974f, 0.5070173f)));
        scene.Add(Box("Pebble", new Float3(0.09950825f, 0.13273759f, 0.09560242f), dark, new Float3(-24.116f, 0.101f, 14.213f), new Quaternion(-0.03653004f, 0.78784084f, 0.022830024f, 0.61437064f)));
        scene.Add(Box("Pebble", new Float3(0.21006262f, 0.07383514f, 0.22721739f), dark, new Float3(-13.047f, 0.033f, 13.497f), new Quaternion(-0.008089982f, 0.919578f, 0.0052399887f, 0.39278916f)));
        scene.Add(Box("Pebble", new Float3(0.11504318f, 0.25350973f, 0.11517717f), dark, new Float3(-18.932f, 0.221f, 17.179f), new Quaternion(-0.047019966f, 0.95290935f, -0.022789983f, 0.2987198f)));
        scene.Add(Box("Pebble", new Float3(0.17297506f, 0.2580492f, 0.1390901f), dark, new Float3(-19.549f, 0.199f, 10.201f), new Quaternion(-0.04788023f, 0.9215244f, -0.0059200283f, 0.38531184f)));
        scene.Add(Box("Pebble", new Float3(0.11869833f, 0.15052888f, 0.13423058f), dark, new Float3(-20.182f, 0.205f, 18.88f), new Quaternion(-0.014980014f, 0.7796607f, 0.07129007f, 0.62195057f)));
        scene.Add(Box("Pebble", new Float3(0.15209103f, 0.3785874f, 0.197702f), dark, new Float3(-17.047f, 0.079f, 16.929f), new Quaternion(-0.022360021f, 0.8813008f, 0.036920033f, 0.47058046f)));
        scene.Add(Box("Pebble", new Float3(0.08105861f, 0.36789504f, 0.092706725f), dark, new Float3(-13.11f, 0.189f, 15.532f), new Quaternion(-0.0035800163f, 0.9891645f, 0.0661003f, 0.1310406f)));
        scene.Add(Box("Pebble", new Float3(0.24730122f, 0.13159384f, 0.2688684f), dark, new Float3(-22.074f, 0.1f, 17.229f), new Quaternion(0.07030992f, 0.8262891f, -0.0011999988f, 0.5588394f)));
        scene.Add(Box("Pebble", new Float3(0.24996744f, 0.14486319f, 0.27395523f), dark, new Float3(-17.087f, 0.192f, 18.683f), new Quaternion(0.021089928f, 0.9048669f, -0.036429875f, 0.42360854f)));
        scene.Add(Box("Pebble", new Float3(0.10911786f, 0.2034702f, 0.124407895f), dark, new Float3(-13.679f, 0.223f, 12.986f), new Quaternion(0.04239011f, 0.9198524f, 0.048010126f, 0.387001f)));
        scene.Add(Box("Pebble", new Float3(0.31318024f, 0.15650326f, 0.37138754f), dark, new Float3(-17.938f, 0.185f, 10.028f), new Quaternion(0.062260192f, 0.97683305f, -0.013190041f, 0.20432062f)));
        scene.Add(Box("Pebble", new Float3(0.17407243f, 0.25249124f, 0.14034821f), dark, new Float3(-15.09f, 0.056f, 17.884f), new Quaternion(-0.005379988f, 0.99196774f, -0.00865998f, 0.12607972f)));
        scene.Add(Box("Pebble", new Float3(0.34460336f, 0.08500916f, 0.36715016f), dark, new Float3(-19.905f, 0.206f, 19.653f), new Quaternion(-0.018190067f, 0.98236364f, -0.037380137f, 0.18230067f)));
        scene.Add(Box("Pebble", new Float3(0.18134457f, 0.06758865f, 0.16453253f), dark, new Float3(-14.706f, 0.109f, 15.985f), new Quaternion(-0.005849997f, 0.9902895f, 0.040439982f, 0.13287994f)));
        scene.Add(Box("Pebble", new Float3(0.08669481f, 0.25415254f, 0.09519065f), dark, new Float3(-16.326f, 0.165f, 13.384f), new Quaternion(0.06957992f, 0.91504896f, -0.024179973f, 0.39655954f)));
        scene.Add(Box("Pebble", new Float3(0.13809457f, 0.06703261f, 0.09681353f), dark, new Float3(-23.59f, 0.2f, 14.317f), new Quaternion(0.00018000041f, 0.7794218f, -0.041980095f, 0.62509143f)));
        scene.Add(Box("Pebble", new Float3(0.12994094f, 0.35311538f, 0.1436457f), dark, new Float3(-24.307f, 0.138f, 15.529f), new Quaternion(0.043820057f, 0.9355413f, -0.0024200033f, 0.35048044f)));
        scene.Add(Box("Pebble", new Float3(0.3165852f, 0.26102987f, 0.40791065f), dark, new Float3(-17.319f, 0.203f, 14.214f), new Quaternion(0.052869745f, 0.9969051f, 0.03855981f, 0.04356979f)));
        scene.Add(Box("Pebble", new Float3(0.2980993f, 0.09159985f, 0.2865887f), dark, new Float3(-17.866f, 0.097f, 19.316f), new Quaternion(-0.040070176f, 0.9964144f, -0.027890123f, 0.069100305f)));
        scene.Add(Box("Pebble", new Float3(0.10108461f, 0.3151839f, 0.085032545f), dark, new Float3(-19.654f, 0.188f, 18.462f), new Quaternion(-0.0006600013f, 0.8936417f, 0.055380106f, 0.44535086f)));
        scene.Add(Box("Pebble", new Float3(0.1913201f, 0.26974145f, 0.1938424f), dark, new Float3(-19.752f, 0.145f, 9.701f), new Quaternion(-0.02634011f, 0.9962042f, -0.06638028f, 0.04977021f)));
        scene.Add(Box("Pebble", new Float3(0.21119466f, 0.23356125f, 0.20603283f), dark, new Float3(-13.784f, 0.072f, 11.509f), new Quaternion(0.034509815f, 0.78469574f, 0.043249767f, 0.61740667f)));
        scene.Add(Box("Pebble", new Float3(0.1687942f, 0.4141546f, 0.16028382f), dark, new Float3(-18.355f, 0.148f, 8.9f), new Quaternion(-0.001800004f, 0.98897225f, 0.015280034f, 0.14730033f)));
        scene.Add(Box("Pebble", new Float3(0.198872f, 0.123253316f, 0.24290559f), dark, new Float3(-23.019f, 0.093f, 10.203f), new Quaternion(0.07726986f, 0.94255835f, -0.04550992f, 0.32177943f)));
        scene.Add(Box("Pebble", new Float3(0.1972077f, 0.26334015f, 0.19665277f), dark, new Float3(-18.805f, 0.08f, 16.534f), new Quaternion(-0.014819959f, 0.83239776f, -0.0049399864f, 0.55395854f)));
        scene.Add(Box("Pebble", new Float3(0.3437599f, 0.40241665f, 0.30596694f), dark, new Float3(-22.04f, 0.12f, 14.691f), new Quaternion(-0.07637993f, 0.8582093f, 0.003999997f, 0.5075696f)));
        scene.Add(Box("Pebble", new Float3(0.12475091f, 0.44940042f, 0.15397027f), dark, new Float3(-23.196f, 0.189f, 16.107f), new Quaternion(0.06607972f, 0.97501594f, -0.027949883f, 0.21022911f)));
        scene.Add(Box("Pebble", new Float3(0.1781791f, 0.37220347f, 0.17652303f), dark, new Float3(-18.34f, 0.181f, 17.593f), new Quaternion(-0.06086027f, 0.88726395f, 0.027330121f, 0.45641202f)));
        scene.Add(Box("Pebble", new Float3(0.107624985f, 0.39516562f, 0.13281839f), dark, new Float3(-16.33f, 0.054f, 9.231f), new Quaternion(0.00074999896f, 0.9975586f, -0.06272991f, 0.030679956f)));
        scene.Add(Box("Pebble", new Float3(0.16272143f, 0.28623846f, 0.1904621f), dark, new Float3(-15.593f, 0.193f, 10.431f), new Quaternion(-0.0016300015f, 0.99799097f, 0.0018100018f, 0.06331006f)));
        scene.Add(Box("Pebble", new Float3(0.113769196f, 0.34338942f, 0.09468406f), dark, new Float3(-16.863f, 0.093f, 12.806f), new Quaternion(0.06392015f, 0.72794175f, -0.023980057f, 0.6822316f)));
        scene.Add(Box("Pebble", new Float3(0.20939733f, 0.4124602f, 0.2695258f), dark, new Float3(-20.319f, 0.187f, 11.75f), new Quaternion(-0.01669007f, 0.9904641f, -0.056980237f, 0.12432052f)));
        scene.Add(Box("Pebble", new Float3(0.11828597f, 0.11116856f, 0.11687481f), dark, new Float3(-21.852f, 0.091f, 16.932f), new Quaternion(0.041169938f, 0.88782865f, -0.0042499937f, 0.4583093f)));
        scene.Add(Box("Pebble", new Float3(0.33509982f, 0.063898504f, 0.28982183f), dark, new Float3(-18.41f, 0.04f, 12.194f), new Quaternion(-0.044259984f, 0.7552797f, 0.015619994f, 0.6537198f)));
        scene.Add(Box("Pebble", new Float3(0.28437f, 0.40557316f, 0.24465108f), dark, new Float3(-19.481f, 0.078f, 10.773f), new Quaternion(0.028740102f, 0.98790354f, 0.037010133f, 0.14782052f)));
        scene.Add(Box("Pebble", new Float3(0.110603765f, 0.23529686f, 0.1436509f), dark, new Float3(-13.026f, 0.115f, 11.505f), new Quaternion(0.005940001f, 0.98772013f, -0.035160005f, 0.15211001f)));
        scene.Add(Box("Pebble", new Float3(0.16633675f, 0.07331617f, 0.14982542f), dark, new Float3(-22.283f, 0.185f, 10.875f), new Quaternion(-0.019539924f, 0.92120636f, 0.0043999827f, 0.38855848f)));
        scene.Add(Box("Pebble", new Float3(0.29307398f, 0.34884763f, 0.20744918f), dark, new Float3(-17.786f, 0.091f, 10.112f), new Quaternion(0.027169852f, 0.74984586f, -0.058999676f, 0.6584164f)));
        scene.Add(Box("Pebble", new Float3(0.14559305f, 0.41526547f, 0.106405504f), dark, new Float3(-19.678f, 0.028f, 9.622f), new Quaternion(0.04559015f, 0.8565329f, 0.016760055f, 0.51380175f)));
        scene.Add(Box("Pebble", new Float3(0.17326777f, 0.37150022f, 0.19255064f), dark, new Float3(-21.503f, 0.199f, 9.863f), new Quaternion(-0.041219916f, 0.7986884f, -0.02984994f, 0.5995888f)));
        scene.Add(Box("Pebble", new Float3(0.29882908f, 0.44094786f, 0.23178014f), dark, new Float3(-12.748f, 0.169f, 12.318f), new Quaternion(-0.001309993f, 0.921015f, -0.005369971f, 0.38948792f)));
        scene.Add(Box("Pebble", new Float3(0.10984865f, 0.13833793f, 0.13904656f), dark, new Float3(-19.47f, 0.206f, 8.422f), new Quaternion(-0.03917999f, 0.7379598f, -0.022519995f, 0.67332983f)));
        scene.Add(Box("Pebble", new Float3(0.2149019f, 0.24463576f, 0.20600623f), dark, new Float3(-15.228f, 0.163f, 10.238f), new Quaternion(-0.005740023f, 0.998184f, 0.024500098f, 0.054730225f)));
        scene.Add(Box("Pebble", new Float3(0.20498352f, 0.09801693f, 0.17475557f), dark, new Float3(-18.228f, 0.133f, 13.891f), new Quaternion(0.0056899907f, 0.98719835f, -0.05346991f, 0.15015975f)));
        scene.Add(Box("Pebble", new Float3(0.25418812f, 0.18791345f, 0.2495801f), dark, new Float3(-12.546f, 0.039f, 11.56f), new Quaternion(-0.052169807f, 0.94205654f, 0.029029893f, 0.33009878f)));
        scene.Add(Box("Pebble", new Float3(0.10513236f, 0.3582717f, 0.10878603f), dark, new Float3(-21.631f, 0.186f, 18.39f), new Quaternion(-0.051039964f, 0.9877693f, -0.016639989f, 0.1463899f)));
        scene.Add(Box("Pebble", new Float3(0.28145763f, 0.32078066f, 0.23404859f), dark, new Float3(-16.056f, 0.057f, 8.184f), new Quaternion(0.06675967f, 0.96860516f, 0.0067099663f, 0.23937881f)));
        scene.Add(Box("Pebble", new Float3(0.34664357f, 0.12201461f, 0.3978553f), dark, new Float3(-20.843f, 0.138f, 11.224f), new Quaternion(0.050069842f, 0.9009372f, 0.030669905f, 0.42995867f)));
        scene.Add(Box("Pebble", new Float3(0.3266191f, 0.10321161f, 0.2769664f), dark, new Float3(-24.824f, 0.182f, 15.633f), new Quaternion(-0.06715001f, 0.84958017f, 0.035380006f, 0.5219701f)));
        scene.Add(Box("Pebble", new Float3(0.2561912f, 0.2620476f, 0.20323913f), dark, new Float3(-13.399f, 0.206f, 12.817f), new Quaternion(0.026310088f, 0.80897266f, 0.070980236f, 0.58295196f)));
        scene.Add(Box("Pebble", new Float3(0.29328907f, 0.27486357f, 0.24828745f), dark, new Float3(-15.592f, 0.197f, 8.42f), new Quaternion(0.043640036f, 0.78005064f, -0.029390024f, 0.6235005f)));
        scene.Add(Box("Pebble", new Float3(0.13261345f, 0.32973057f, 0.1718788f), dark, new Float3(-17.054f, 0.04f, 10.356f), new Quaternion(-0.061980274f, 0.9687143f, 0.019270085f, 0.23954105f)));
        scene.Add(Box("Pebble", new Float3(0.31293106f, 0.30202308f, 0.2566684f), dark, new Float3(-22.646f, 0.171f, 16.184f), new Quaternion(0.04976014f, 0.9818728f, -0.032510094f, 0.17998052f)));
        scene.Add(Box("Pebble", new Float3(0.21045963f, 0.3558773f, 0.1505324f), dark, new Float3(-17.836f, 0.216f, 18.217f), new Quaternion(0.049059886f, 0.863138f, -0.051059883f, 0.49997887f)));
        scene.Add(Box("Pebble", new Float3(0.33325654f, 0.19640231f, 0.33417687f), dark, new Float3(-23.929f, 0.105f, 14.866f), new Quaternion(-0.028130017f, 0.82632047f, -0.01667001f, 0.5622504f)));
        scene.Add(Box("Pebble", new Float3(0.2213315f, 0.14878792f, 0.28425023f), dark, new Float3(-21.707f, 0.21f, 15.691f), new Quaternion(0.07421992f, 0.89566904f, -0.029519968f, 0.4374895f)));
        scene.Add(Box("Pebble", new Float3(0.09766088f, 0.12516953f, 0.082548134f), dark, new Float3(-19.522f, 0.209f, 7.612f), new Quaternion(0.06593981f, 0.996947f, -0.019639943f, 0.036919888f)));
        scene.Add(Box("Pebble", new Float3(0.10299745f, 0.07417829f, 0.106842205f), dark, new Float3(-13.112f, 0.095f, 12.558f), new Quaternion(0.0047400105f, 0.76023173f, -0.023170052f, 0.6492215f)));
        scene.Add(Box("Pebble", new Float3(0.12832546f, 0.13792005f, 0.13694745f), dark, new Float3(-14.501f, 0.136f, 14.866f), new Quaternion(0.06604972f, 0.98756576f, -0.057449754f, 0.13057943f)));
        scene.Add(Box("Pebble", new Float3(0.22182715f, 0.09491913f, 0.1990025f), dark, new Float3(-17.4f, 0.087f, 15.575f), new Quaternion(0.06465021f, 0.99778324f, 0.009960032f, 0.012240039f)));
        scene.Add(Box("Pebble", new Float3(0.08152013f, 0.33871937f, 0.09090955f), dark, new Float3(-17.857f, 0.202f, 8.791f), new Quaternion(0.03342983f, 0.998975f, 0.018969905f, -0.023909882f)));
        scene.Add(Box("Pebble", new Float3(0.12826148f, 0.113179654f, 0.10799176f), dark, new Float3(-24.082f, 0.136f, 17.005f), new Quaternion(-0.057729978f, 0.8104897f, -0.033289988f, 0.58194983f)));
        scene.Add(Box("Pebble", new Float3(0.25399196f, 0.329231f, 0.26195556f), dark, new Float3(-22.061f, 0.057f, 14.796f), new Quaternion(0.014389928f, 0.798816f, -0.071339644f, 0.597157f)));
        scene.Add(Box("Pebble", new Float3(0.31995323f, 0.15624551f, 0.39975855f), dark, new Float3(-21.902f, 0.094f, 12.306f), new Quaternion(0.03282987f, 0.9839261f, -0.039589845f, 0.17100933f)));
        scene.Add(Box("Pebble", new Float3(0.16370311f, 0.2559694f, 0.14373441f), dark, new Float3(-17.777f, 0.038f, 11.031f), new Quaternion(-0.013429952f, 0.9760265f, 0.0045599835f, 0.21718922f)));
        scene.Add(Box("Pebble", new Float3(0.25106174f, 0.14237246f, 0.20312123f), dark, new Float3(-15.951f, 0.069f, 14.674f), new Quaternion(-0.03567012f, 0.9200031f, -0.07008023f, 0.3839413f)));
        scene.Add(Box("Pebble", new Float3(0.31480703f, 0.16937032f, 0.32639572f), dark, new Float3(-18.684f, 0.188f, 12.316f), new Quaternion(0.011869957f, 0.9994964f, -0.021239923f, -0.020369926f)));
        scene.Add(Box("Pebble", new Float3(0.21749605f, 0.38558596f, 0.2820545f), dark, new Float3(-22.603f, 0.214f, 14.206f), new Quaternion(0.023200046f, 0.99861205f, -0.02496005f, 0.040160082f)));
        scene.Add(Box("Pebble", new Float3(0.19565831f, 0.42735723f, 0.15471654f), dark, new Float3(-22.47f, 0.203f, 15.134f), new Quaternion(0.006629979f, 0.9552869f, -0.06662978f, 0.28799906f)));
        scene.Add(Box("Pebble", new Float3(0.26488692f, 0.12368953f, 0.29394904f), dark, new Float3(-18.437f, 0.049f, 19.491f), new Quaternion(-0.030049896f, 0.99504656f, -0.06723977f, 0.06676977f)));
        scene.Add(Box("Pebble", new Float3(0.34195775f, 0.09003311f, 0.42914099f), dark, new Float3(-18.499f, 0.096f, 13.585f), new Quaternion(0.03832992f, 0.988288f, -0.021869956f, 0.1460797f)));
        scene.Add(Box("Pebble", new Float3(0.21176378f, 0.2899619f, 0.2519481f), dark, new Float3(-23.104f, 0.147f, 13.284f), new Quaternion(0.037829977f, 0.92505944f, 0.043949973f, 0.37536976f)));
        scene.Add(Box("Pebble", new Float3(0.086969726f, 0.25154656f, 0.09849698f), dark, new Float3(-22.118f, 0.082f, 10.873f), new Quaternion(-0.054130167f, 0.98925304f, 0.05358017f, 0.12481039f)));
        scene.Add(Box("Pebble", new Float3(0.34786886f, 0.22183993f, 0.42635173f), dark, new Float3(-23.344f, 0.194f, 17.102f), new Quaternion(0.02428f, 0.94848f, 0.04558f, 0.3126f)));
        scene.Add(Box("Pebble", new Float3(0.1437046f, 0.41604862f, 0.11751989f), dark, new Float3(-23.575f, 0.076f, 13.935f), new Quaternion(-0.005060015f, 0.7701023f, -0.07954024f, 0.63292193f)));
        scene.Add(Box("Pebble", new Float3(0.1742968f, 0.3782315f, 0.12943852f), dark, new Float3(-21.104f, 0.184f, 9.263f), new Quaternion(-0.064910285f, 0.91947407f, 0.031890143f, 0.3864417f)));
        scene.Add(Box("Pebble", new Float3(0.34860522f, 0.42732525f, 0.39730975f), dark, new Float3(-24.023f, 0.2f, 12.456f), new Quaternion(0.043350205f, 0.9984448f, 0.018280087f, 0.029910143f)));
        scene.Add(Box("Pebble", new Float3(0.17483462f, 0.36708194f, 0.13848501f), dark, new Float3(-17.244f, 0.029f, 19.232f), new Quaternion(0.038370166f, 0.9922143f, 0.07307032f, 0.093270406f)));
        scene.Add(Box("Pebble", new Float3(0.19035473f, 0.28217947f, 0.14818989f), dark, new Float3(-23.495f, 0.166f, 11.591f), new Quaternion(0.010449962f, 0.8887767f, -0.056309793f, 0.45474833f)));
        scene.Add(Box("Pebble", new Float3(0.21066001f, 0.3285823f, 0.19044195f), dark, new Float3(-22.465f, 0.079f, 11.136f), new Quaternion(-0.06384005f, 0.9680407f, 0.032310024f, 0.24037018f)));
        scene.Add(Box("Pebble", new Float3(0.17895645f, 0.38914382f, 0.16930068f), dark, new Float3(-17.954f, 0.111f, 13.047f), new Quaternion(0.046989966f, 0.8130194f, -0.021749984f, 0.5799296f)));
        scene.Add(Box("Pebble", new Float3(0.2426379f, 0.07385489f, 0.30999154f), dark, new Float3(-14.318f, 0.161f, 12.434f), new Quaternion(-0.05334005f, 0.7225607f, -0.024230022f, 0.68882066f)));
        scene.Add(Box("Pebble", new Float3(0.18341804f, 0.23049691f, 0.19737199f), dark, new Float3(-23.106f, 0.124f, 17.518f), new Quaternion(0.04932009f, 0.87944156f, -0.017780032f, 0.47311082f)));
        scene.Add(Box("Pebble", new Float3(0.33987826f, 0.39301077f, 0.33489412f), dark, new Float3(-22.071f, 0.039f, 15.004f), new Quaternion(-0.022140032f, 0.75785106f, -0.0011500017f, 0.652051f)));
        scene.Add(Box("Pebble", new Float3(0.18700907f, 0.24266388f, 0.22456653f), dark, new Float3(-21.967f, 0.159f, 9.694f), new Quaternion(0.0014399992f, 0.7411496f, -0.058899965f, 0.6687496f)));
        scene.Add(Box("Pebble", new Float3(0.23189665f, 0.19020562f, 0.2668724f), dark, new Float3(-20.246f, 0.173f, 12.114f), new Quaternion(0.06791037f, 0.84022456f, -0.019850109f, 0.5376029f)));
        scene.Add(Box("Pebble", new Float3(0.22059697f, 0.3527505f, 0.16168156f), dark, new Float3(-21.407f, 0.083f, 15.558f), new Quaternion(0.033660028f, 0.90817076f, -0.04105003f, 0.41522035f)));
        scene.Add(Box("Pebble", new Float3(0.31783623f, 0.32288945f, 0.2829616f), dark, new Float3(-24.084f, 0.102f, 14.632f), new Quaternion(0.06819986f, 0.974098f, -0.037729923f, 0.21226957f)));
        scene.Add(Box("Pebble", new Float3(0.080858305f, 0.20585956f, 0.0758031f), dark, new Float3(-15.68f, 0.159f, 10.546f), new Quaternion(-0.05688968f, 0.8352353f, -0.0077599566f, 0.546887f)));
        scene.Add(Box("Pebble", new Float3(0.10129923f, 0.3484366f, 0.12337231f), dark, new Float3(-19.806f, 0.1f, 7.648f), new Quaternion(0.015889928f, 0.99762547f, 0.04178981f, 0.052389763f)));
        scene.Add(Box("Pebble", new Float3(0.18319166f, 0.3443034f, 0.22662288f), dark, new Float3(-13.475f, 0.167f, 12.155f), new Quaternion(0.003979983f, 0.9912358f, -0.043339815f, 0.12472946f)));
        scene.Add(Box("Pebble", new Float3(0.34053224f, 0.41970423f, 0.34761232f), dark, new Float3(-22.314f, 0.176f, 16.487f), new Quaternion(0.021080043f, 0.999242f, 0.027040053f, 0.018440038f)));
        scene.Add(Box("Pebble", new Float3(0.3377952f, 0.09354083f, 0.3519651f), dark, new Float3(-21.333f, 0.113f, 14.343f), new Quaternion(0.06652004f, 0.9903707f, -0.032180022f, 0.11707008f)));
        scene.Add(Box("Pebble", new Float3(0.20402873f, 0.30564466f, 0.2625127f), dark, new Float3(-17.792f, 0.115f, 16.556f), new Quaternion(0.04128f, 0.81296f, 0.03346f, 0.57989f)));
        scene.Add(Box("Pebble", new Float3(0.27805215f, 0.28394893f, 0.27234665f), dark, new Float3(-14.481f, 0.202f, 16.516f), new Quaternion(-0.010700044f, 0.77414316f, 0.080010325f, 0.62784255f)));
        scene.Add(Box("Pebble", new Float3(0.15851304f, 0.25668705f, 0.15331526f), dark, new Float3(-18.067f, 0.153f, 10.596f), new Quaternion(-0.043909896f, 0.99184763f, -0.039459907f, 0.11292973f)));
        scene.Add(Box("Pebble", new Float3(0.2600053f, 0.18876614f, 0.31114218f), dark, new Float3(-13.059f, 0.149f, 17.094f), new Quaternion(-0.062059835f, 0.9948674f, -0.035409905f, 0.07164981f)));
        scene.Add(Box("Pebble", new Float3(0.12650798f, 0.3960158f, 0.11571016f), dark, new Float3(-21.091f, 0.044f, 16.959f), new Quaternion(0.03871016f, 0.99737406f, -0.01477006f, 0.05940024f)));
        scene.Add(Box("Pebble", new Float3(0.23737507f, 0.22587045f, 0.25866115f), dark, new Float3(-14.412f, 0.167f, 13.488f), new Quaternion(-0.066749744f, 0.9916763f, 0.010309961f, 0.10961958f)));
        scene.Add(Box("Pebble", new Float3(0.29979515f, 0.1429943f, 0.29589033f), dark, new Float3(-19.674f, 0.168f, 11.908f), new Quaternion(-0.016669994f, 0.8291897f, -0.021889992f, 0.5582898f)));
        scene.Add(Box("Pebble", new Float3(0.15980613f, 0.27033496f, 0.1999775f), dark, new Float3(-18.062f, 0.152f, 20.263f), new Quaternion(0.006130009f, 0.8195712f, 0.05477008f, 0.57032084f)));
        scene.Add(Box("Pebble", new Float3(0.28775585f, 0.36300412f, 0.27354935f), dark, new Float3(-20.574f, 0.198f, 19.049f), new Quaternion(-0.03164006f, 0.9881419f, -0.03645007f, 0.14576028f)));
        scene.Add(Box("Pebble", new Float3(0.08105295f, 0.3666258f, 0.09204581f), dark, new Float3(-18.946f, 0.114f, 14.777f), new Quaternion(-0.06316009f, 0.9826914f, 0.002700004f, 0.17413025f)));
        scene.Add(Box("Pebble", new Float3(0.14253354f, 0.067488424f, 0.1439048f), dark, new Float3(-22.629f, 0.2f, 18.846f), new Quaternion(-0.009079991f, 0.923339f, -0.042759955f, 0.38148957f)));
        scene.Add(Box("Pebble", new Float3(0.25813478f, 0.4279859f, 0.20361224f), dark, new Float3(-18.551f, 0.089f, 17.909f), new Quaternion(0.017970026f, 0.8231312f, -0.058240082f, 0.56457084f)));
        scene.Add(Box("Pebble", new Float3(0.1562466f, 0.15896606f, 0.11770908f), dark, new Float3(-22.79f, 0.074f, 11.511f), new Quaternion(0.0025800013f, 0.96935046f, 0.057810027f, 0.23877011f)));
        scene.Add(Box("Pebble", new Float3(0.28377187f, 0.27808797f, 0.23325415f), dark, new Float3(-20.285f, 0.042f, 12.315f), new Quaternion(-0.04950994f, 0.95030886f, 0.029269965f, 0.30594963f)));
        scene.Add(Box("Pebble", new Float3(0.33412832f, 0.23938146f, 0.3635619f), dark, new Float3(-16.866f, 0.165f, 8.742f), new Quaternion(-0.048529852f, 0.88657725f, 0.018429942f, 0.45965856f)));
        scene.Add(Box("Pebble", new Float3(0.31569847f, 0.18153153f, 0.2358257f), dark, new Float3(-17.721f, 0.097f, 15.016f), new Quaternion(0.033880103f, 0.89098275f, 0.033890106f, 0.4515014f)));
        scene.Add(Box("Pebble", new Float3(0.34622908f, 0.40715f, 0.28669986f), dark, new Float3(-23.16f, 0.088f, 14.232f), new Quaternion(0.004149997f, 0.8899893f, 0.048649967f, 0.45335966f)));
        scene.Add(Box("Pebble", new Float3(0.12959214f, 0.115989774f, 0.14614731f), dark, new Float3(-15.096f, 0.028f, 12.895f), new Quaternion(0.01569004f, 0.9163523f, 0.030240076f, 0.398921f)));
        scene.Add(Box("Pebble", new Float3(0.17629692f, 0.07344813f, 0.17447415f), dark, new Float3(-21.269f, 0.129f, 13.998f), new Quaternion(-0.028280128f, 0.9908445f, -0.005190023f, 0.13191059f)));
        scene.Add(Box("Pebble", new Float3(0.25331143f, 0.11922297f, 0.26448935f), dark, new Float3(-16.559f, 0.057f, 10.758f), new Quaternion(0.009630025f, 0.96601254f, -0.013350035f, 0.2579707f)));
        scene.Add(Box("Pebble", new Float3(0.13871807f, 0.42095459f, 0.13674806f), dark, new Float3(-14.91f, 0.134f, 17.017f), new Quaternion(-0.010300016f, 0.8371713f, 0.0629301f, 0.54321086f)));
        scene.Add(Box("Pebble", new Float3(0.18630719f, 0.32286578f, 0.21105357f), dark, new Float3(-23.418f, 0.126f, 13.393f), new Quaternion(-0.06774973f, 0.9924961f, 0.05776977f, 0.083809674f)));
        scene.Add(Box("Pebble", new Float3(0.20199662f, 0.24234109f, 0.23898326f), dark, new Float3(-20.269f, 0.056f, 9.767f), new Quaternion(-0.0009200031f, 0.77536255f, 0.052590176f, 0.6293221f)));
        scene.Add(Box("Pebble", new Float3(0.22685242f, 0.051142428f, 0.26070192f), dark, new Float3(-23.007f, 0.067f, 17.84f), new Quaternion(-0.012989998f, 0.90995985f, 0.064459994f, 0.40944993f)));
        scene.Add(Box("Pebble", new Float3(0.206365f, 0.35270151f, 0.20910537f), dark, new Float3(-23.22f, 0.124f, 17.619f), new Quaternion(0.053739857f, 0.7235181f, -0.023229938f, 0.6878182f)));
        scene.Add(Box("Pebble", new Float3(0.17996366f, 0.316861f, 0.15175201f), dark, new Float3(-16.888f, 0.188f, 16.393f), new Quaternion(0.014550013f, 0.77869064f, 0.05671005f, 0.62467057f)));
        scene.Add(Box("Pebble", new Float3(0.31725305f, 0.18754211f, 0.27370015f), dark, new Float3(-20.388f, 0.045f, 12.864f), new Quaternion(-0.009940013f, 0.96760124f, 0.04423006f, 0.24838033f)));
        scene.Add(Box("Pebble", new Float3(0.15782607f, 0.3079808f, 0.14106005f), dark, new Float3(-14.358f, 0.054f, 12.442f), new Quaternion(-0.031900086f, 0.96425265f, 0.005460015f, 0.26300073f)));
        scene.Add(Box("Pebble", new Float3(0.22543614f, 0.40807354f, 0.287165f), dark, new Float3(-16.302f, 0.199f, 8.958f), new Quaternion(-0.081960365f, 0.9487042f, 0.04288019f, 0.30233133f)));
        scene.Add(Box("Pebble", new Float3(0.21419904f, 0.4076493f, 0.20101823f), dark, new Float3(-17.898f, 0.153f, 12.208f), new Quaternion(-0.020599902f, 0.97615534f, -0.05826972f, 0.20808901f)));
        scene.Add(Box("Pebble", new Float3(0.081028834f, 0.39181826f, 0.057273056f), dark, new Float3(-24.672f, 0.18f, 15.736f), new Quaternion(-0.047480185f, 0.8630034f, -0.00040000156f, 0.502962f)));
        scene.Add(Box("Pebble", new Float3(0.20908315f, 0.12563759f, 0.24672377f), dark, new Float3(-21.542f, 0.043f, 11.646f), new Quaternion(0.04589016f, 0.8184528f, 0.027410096f, 0.572082f)));
        scene.Add(Box("Pebble", new Float3(0.13867182f, 0.13267387f, 0.16806822f), dark, new Float3(-17.135f, 0.135f, 11.704f), new Quaternion(-0.035230007f, 0.89752024f, -0.003460001f, 0.43955013f)));
        scene.Add(Box("Pebble", new Float3(0.13828096f, 0.21919397f, 0.099101275f), dark, new Float3(-13.539f, 0.101f, 15.444f), new Quaternion(0.013969917f, 0.7969452f, -0.002129987f, 0.6038864f)));
        scene.Add(Box("Pebble", new Float3(0.30354342f, 0.0859037f, 0.3010545f), dark, new Float3(-22.541f, 0.18f, 10.107f), new Quaternion(0.039010093f, 0.9696223f, -0.015930038f, 0.24095058f)));
        scene.Add(Box("Pebble", new Float3(0.17990044f, 0.13922182f, 0.12717512f), dark, new Float3(-16.164f, 0.219f, 16.862f), new Quaternion(-0.004609982f, 0.8175168f, 0.037289854f, 0.5746777f)));
        scene.Add(Box("Pebble", new Float3(0.34867656f, 0.420895f, 0.36469907f), dark, new Float3(-13.877f, 0.084f, 17.349f), new Quaternion(0.025159964f, 0.81870884f, 0.011139984f, 0.57354915f)));
        scene.Add(Box("Pebble", new Float3(0.3105055f, 0.36670703f, 0.3021517f), dark, new Float3(-21.638f, 0.081f, 12.691f), new Quaternion(-0.016109994f, 0.8256597f, -0.033039987f, 0.5629698f)));
        scene.Add(Box("Pebble", new Float3(0.092768006f, 0.069030456f, 0.0672019f), dark, new Float3(-17.392f, 0.189f, 15.01f), new Quaternion(0.00016000036f, 0.73467165f, 0.001360003f, 0.67842156f)));
        scene.Add(Box("Pebble", new Float3(0.118867666f, 0.35964718f, 0.12228299f), dark, new Float3(-20.373f, 0.106f, 15.078f), new Quaternion(-0.05013008f, 0.9598416f, 0.05331009f, 0.27083045f)));
        scene.Add(Box("Pebble", new Float3(0.17119491f, 0.29358804f, 0.14242646f), dark, new Float3(-17.931f, 0.088f, 8.455f), new Quaternion(0.05435022f, 0.99307406f, 0.03003012f, 0.0997404f)));
        scene.Add(Box("Pebble", new Float3(0.15190214f, 0.25011083f, 0.18435171f), dark, new Float3(-20.826f, 0.161f, 9.412f), new Quaternion(0.049000155f, 0.88509285f, 0.039590128f, 0.46113148f)));
        scene.Add(Box("Pebble", new Float3(0.20421651f, 0.10963823f, 0.16765097f), dark, new Float3(-23.682f, 0.141f, 10.861f), new Quaternion(0.038119998f, 0.9223699f, 0.07272999f, 0.37747994f)));
        scene.Add(Box("Pebble", new Float3(0.26666117f, 0.34937277f, 0.26664937f), dark, new Float3(-15.161f, 0.102f, 15.255f), new Quaternion(-0.05228f, 0.7898f, 0.0332f, 0.61023f)));
        scene.Add(Box("Pebble", new Float3(0.19701801f, 0.13957736f, 0.22847764f), dark, new Float3(-17.034f, 0.06f, 13.178f), new Quaternion(0.016510034f, 0.9558119f, 0.004480009f, 0.2934806f)));
        scene.Add(Box("Pebble", new Float3(0.12710336f, 0.3279086f, 0.15119784f), dark, new Float3(-23.099f, 0.218f, 13.568f), new Quaternion(-0.0075399633f, 0.85003585f, 0.005939971f, 0.52663743f)));
        scene.Add(Box("Pebble", new Float3(0.28019017f, 0.35065952f, 0.28247073f), dark, new Float3(-22.553f, 0.154f, 8.774f), new Quaternion(-0.0563499f, 0.9942482f, -0.046539918f, 0.07828986f)));
        scene.Add(Box("Pebble", new Float3(0.2589996f, 0.37699986f, 0.32181942f), dark, new Float3(-21.861f, 0.079f, 12.444f), new Quaternion(0.00025000036f, 0.99997145f, -0.0017200024f, 0.007360011f)));
        scene.Add(Box("Pebble", new Float3(0.10126977f, 0.44780135f, 0.084482186f), dark, new Float3(-17.057f, 0.218f, 14.517f), new Quaternion(-0.04187f, 0.94099f, 0.01668f, 0.33542f)));
        scene.Add(Box("Pebble", new Float3(0.08852656f, 0.29949725f, 0.11141734f), dark, new Float3(-16.098f, 0.182f, 17.291f), new Quaternion(0.057259835f, 0.9843772f, -0.060479827f, 0.15512955f)));
        scene.Add(Box("Pebble", new Float3(0.1966886f, 0.20087638f, 0.14039718f), dark, new Float3(-19.483f, 0.116f, 10.387f), new Quaternion(0.03924993f, 0.9791083f, 0.057069898f, 0.19117966f)));
        scene.Add(Box("Pebble", new Float3(0.20433137f, 0.16512373f, 0.17408042f), dark, new Float3(-18.317f, 0.222f, 11.465f), new Quaternion(-0.019840002f, 0.8350501f, 0.021640003f, 0.5493901f)));
        scene.Add(Box("Pebble", new Float3(0.30686158f, 0.1983511f, 0.26638654f), dark, new Float3(-15.825f, 0.221f, 14.92f), new Quaternion(0.0022899902f, 0.7868666f, 0.05572976f, 0.6145974f)));
        scene.Add(Box("Pebble", new Float3(0.15988936f, 0.19903445f, 0.16995391f), dark, new Float3(-12.644f, 0.043f, 13.939f), new Quaternion(0.041670084f, 0.9107118f, 0.028280057f, 0.40996084f)));
        scene.Add(Box("Pebble", new Float3(0.098620996f, 0.15367885f, 0.0696043f), dark, new Float3(-16.089f, 0.142f, 8.088f), new Quaternion(-0.011509957f, 0.9870164f, -0.023929913f, 0.15840942f)));
        scene.Add(Box("Pebble", new Float3(0.3197313f, 0.138871f, 0.2244362f), dark, new Float3(-18.266f, 0.04f, 17.779f), new Quaternion(0.014080007f, 0.8138504f, 0.027760014f, 0.5802403f)));
        scene.Add(Box("Pebble", new Float3(0.19657236f, 0.32427707f, 0.2489368f), dark, new Float3(-23.43f, 0.1f, 11.703f), new Quaternion(0.056350008f, 0.8760801f, 0.020560002f, 0.47842005f)));
        scene.Add(Box("Pebble", new Float3(0.3314497f, 0.25242463f, 0.36237338f), dark, new Float3(-16.997f, 0.129f, 8.894f), new Quaternion(0.054850124f, 0.99556226f, -0.04968011f, 0.05813013f)));
        scene.Add(Box("Pebble", new Float3(0.1491063f, 0.4203491f, 0.15865292f), dark, new Float3(-21.294f, 0.098f, 17.537f), new Quaternion(-0.03398998f, 0.8376395f, 0.013009992f, 0.5450097f)));
        scene.Add(Box("Pebble", new Float3(0.1666109f, 0.2122232f, 0.16786577f), dark, new Float3(-14.401f, 0.134f, 14.635f), new Quaternion(0.03689997f, 0.97566915f, -0.028599976f, 0.21421982f)));
        scene.Add(Box("Pebble", new Float3(0.30362248f, 0.44266167f, 0.32003096f), dark, new Float3(-19.97f, 0.084f, 9.097f), new Quaternion(0.06470996f, 0.9881994f, -0.029279983f, 0.13570993f)));
        scene.Add(Box("Pebble", new Float3(0.18512705f, 0.16651684f, 0.2022189f), dark, new Float3(-22.708f, 0.075f, 17.489f), new Quaternion(-0.044329904f, 0.8337082f, 0.034209926f, 0.5493588f)));
        scene.Add(Box("Pebble", new Float3(0.29278705f, 0.43710768f, 0.31517908f), dark, new Float3(-16.399f, 0.202f, 18.814f), new Quaternion(-0.023069931f, 0.95352715f, -0.034979895f, 0.2983791f)));
        scene.Add(Box("Pebble", new Float3(0.23970821f, 0.4230468f, 0.31069252f), dark, new Float3(-23.073f, 0.041f, 12.516f), new Quaternion(0.030389905f, 0.9270371f, -0.047939852f, 0.37064883f)));
        scene.Add(Box("Pebble", new Float3(0.3376754f, 0.3154539f, 0.3900922f), dark, new Float3(-18.862f, 0.121f, 19.919f), new Quaternion(0.03770991f, 0.9953276f, 0.028749932f, 0.0841098f)));
        scene.Add(Box("Pebble", new Float3(0.24537398f, 0.34511662f, 0.18194643f), dark, new Float3(-18.416f, 0.167f, 7.822f), new Quaternion(-0.056349993f, 0.9960899f, -0.007569999f, 0.067619994f)));

        {
            GameObject go = Pose(Model("Sliding Platform", Mesh.CreateCube(new Float3(2.5f, 0.2f, 2.5f)), moving, Float3.Zero), new Float3(-19f, 0.498f, 31.62f), new Quaternion(0f, 0.999979f, 0f, -0.006479994f));
            Kinematic(go);
            go.AddComponent<BoxCollider>().Size = new Float3(2.5f, 0.2f, 2.5f);
            PingPongMover mover = go.AddComponent<PingPongMover>();
            mover.From = new Float3(-19f, 0.498f, 31.62f);
            mover.To = new Float3(-19.103678f, 0.498f, 23.620672f);
            mover.Speed = 3f;
            scene.Add(go);
        }

        scene.Add(Box("Ledge", new Float3(3f, 4f, 3f), dark, new Float3(-27.551f, 2f, 27.731f), new Quaternion(0f, 0.999979f, 0f, -0.006479994f)));

        {
            GameObject go = Pose(Model("Lift", Mesh.CreateCube(new Float3(2.5f, 0.2f, 2.5f)), moving, Float3.Zero), new Float3(-24.801f, 0.1f, 27.695f), new Quaternion(0f, 0.999979f, 0f, -0.006479994f));
            Kinematic(go);
            go.AddComponent<BoxCollider>().Size = new Float3(2.5f, 0.2f, 2.5f);
            PingPongMover mover = go.AddComponent<PingPongMover>();
            mover.From = new Float3(-24.801f, 0.1f, 27.695f);
            mover.To = new Float3(-24.801f, 3.9f, 27.695f);
            mover.Speed = 1.5f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Spinning Disc", Mesh.CreateCylinder(3.5f, 0.25f, 32), moving, Float3.Zero), new Float3(-17.393f, 0.13f, -7.782f), new Quaternion(0f, 0.32446876f, 0f, -0.9458964f));
            Kinematic(go);
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 3.5f;
            cylinder.Height = 0.25f;
            go.AddComponent<Spinner>().DegreesPerSecond = 60f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Tilted Disc", Mesh.CreateCylinder(3.5f, 0.25f, 32), moving, Float3.Zero), new Float3(-23.499f, 0.8f, -13.695f), new Quaternion(0.08710013f, 0.669281f, 0.0028400044f, -0.7378811f));
            Kinematic(go);
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 3.5f;
            cylinder.Height = 0.25f;
            go.AddComponent<Spinner>().DegreesPerSecond = 45f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Sweeper", Mesh.CreateCube(new Float3(0.15f, 1.6f, 5f)), Lit(new Color(0.55f, 0.03f, 0.03f, 1f), 0f, 0.5f), Float3.Zero), new Float3(-32.762f, 0.85f, -7.777f), new Quaternion(0f, 0.102600284f, 0f, 0.9947227f));
            Kinematic(go);
            go.AddComponent<BoxCollider>().Size = new Float3(0.15f, 1.6f, 5f);
            go.AddComponent<Spinner>().DegreesPerSecond = -40f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Sweeper", Mesh.CreateCube(new Float3(0.15f, 1.6f, 5f)), Lit(new Color(0.55f, 0.03f, 0.03f, 1f), 0f, 0.5f), Float3.Zero), new Float3(-29.144f, 0.85f, -11.513f), new Quaternion(0f, 0.76569897f, 0f, -0.64319915f));
            Kinematic(go);
            go.AddComponent<BoxCollider>().Size = new Float3(0.15f, 1.6f, 5f);
            go.AddComponent<Spinner>().DegreesPerSecond = 40f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Log Roller", Mesh.CreateCylinder(1.2f, 6f, 32), moving, Float3.Zero), new Float3(-20.53f, -0.462f, 2.451f), new Quaternion(-0.118279606f, 0.39999866f, 0.6971677f, -0.583068f));
            Kinematic(go);
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 1.2f;
            cylinder.Height = 6f;
            go.AddComponent<Spinner>().DegreesPerSecond = 50f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Log Roller", Mesh.CreateCylinder(1.2f, 6f, 32), moving, Float3.Zero), new Float3(-18.91f, -1.153f, 0.775f), new Quaternion(-0.118279606f, 0.39999866f, 0.6971677f, -0.583068f), new Float3(1.572f, 1.572f, 1.572f));
            Kinematic(go);
            CylinderCollider cylinder = go.AddComponent<CylinderCollider>();
            cylinder.Radius = 1.2f;
            cylinder.Height = 6f;
            go.AddComponent<Spinner>().DegreesPerSecond = 50f;
            scene.Add(go);
        }

        scene.Add(Box("Hop", new Float3(2f, 0.5f, 2f), plain, new Float3(-11.954f, 0.25f, -3.936f), new Quaternion(0f, 0.7108717f, 0f, -0.7033217f)));
        scene.Add(Box("Hop", new Float3(2f, 1f, 2f), edge, new Float3(-11.982f, 0.5f, -1.337f), new Quaternion(0f, 0.7108717f, 0f, -0.7033217f)));
        scene.Add(Box("Hop", new Float3(2f, 1.5f, 2f), plain, new Float3(-12.01f, 0.75f, 1.263f), new Quaternion(0f, 0.7108717f, 0f, -0.7033217f)));
        scene.Add(Box("Hop", new Float3(2f, 2f, 2f), edge, new Float3(-12.038f, 1f, 3.863f), new Quaternion(0f, 0.7108717f, 0f, -0.7033217f)));
        scene.Add(Box("Hop", new Float3(2f, 2.5f, 2f), plain, new Float3(-12.065f, 1.25f, 6.463f), new Quaternion(0f, 0.7108717f, 0f, -0.7033217f)));
        scene.Add(Box("Beam Start", new Float3(3f, 2f, 3f), edge, new Float3(11.612f, 1f, 9.442f), new Quaternion(0f, 0.4689281f, 0f, 0.8832364f)));
        scene.Add(Box("Ramp", new Float3(3f, 0.3f, 4.6f), edge, new Float3(8.717f, 0.86f, 7.484f), new Quaternion(-0.19867991f, 0.4569098f, 0.10548995f, 0.8605996f)));
        scene.Add(Box("Beam End", new Float3(3f, 2f, 3f), edge, new Float3(22.38f, 1f, 16.725f), new Quaternion(0f, 0.4689281f, 0f, 0.8832364f)));
        scene.Add(Box("Beam", new Float3(1f, 0.2f, 10f), dark, new Float3(16.436f, 1.9f, 13.911f), new Quaternion(0f, 0.4689281f, 0f, 0.8832364f)));
        scene.Add(Box("Beam", new Float3(0.5f, 0.2f, 10f), dark, new Float3(17.027f, 1.9f, 13.037f), new Quaternion(0f, 0.4689281f, 0f, 0.8832364f)));
        scene.Add(Box("Beam", new Float3(0.25f, 0.2f, 10f), dark, new Float3(17.534f, 1.9f, 12.288f), new Quaternion(0f, 0.4689281f, 0f, 0.8832364f)));
        scene.Add(Box("Pad Tower", new Float3(3f, 2f, 3f), travel, new Float3(-17f, 1f, 50f), new Quaternion(0f, 0f, 0f, 1f)));

        {
            GameObject go = Pose(Model("Jump Pad", Mesh.CreateCylinder(1.2f, 0.08f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.2f, 1f, 0.5f, 1f), 2f), Float3.Zero), new Float3(-17f, 0.04f, 45f), new Quaternion(0f, 0f, 0f, 1f));
            go.AddComponent<JumpPad>().Launch = new Float3(0f, 12f, 6.339746f);
            scene.Add(go);
        }

        scene.Add(Box("Pad Tower", new Float3(3f, 4f, 3f), travel, new Float3(-12f, 2f, 50f), new Quaternion(0f, 0f, 0f, 1f)));

        {
            GameObject go = Pose(Model("Jump Pad", Mesh.CreateCylinder(1.2f, 0.08f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.2f, 1f, 0.5f, 1f), 2f), Float3.Zero), new Float3(-12f, 0.04f, 45f), new Quaternion(0f, 0f, 0f, 1f));
            go.AddComponent<JumpPad>().Launch = new Float3(0f, 15.491934f, 5.352331f);
            scene.Add(go);
        }

        scene.Add(Box("Pad Tower", new Float3(3f, 7f, 3f), travel, new Float3(-7f, 3.5f, 50f), new Quaternion(0f, 0f, 0f, 1f)));

        {
            GameObject go = Pose(Model("Jump Pad", Mesh.CreateCylinder(1.2f, 0.08f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.2f, 1f, 0.5f, 1f), 2f), Float3.Zero), new Float3(-7f, 0.04f, 45f), new Quaternion(0f, 0f, 0f, 1f));
            go.AddComponent<JumpPad>().Launch = new Float3(0f, 19.595919f, 4.5241838f);
            scene.Add(go);
        }

        scene.Add(Box("Gate Column", new Float3(2.6f, 9f, 2.6f), travel, new Float3(2f, 4.5f, 50f), new Quaternion(0f, 0f, 0f, 1f)));

        {
            GameObject go = Pose(Model("Teleporter", Mesh.CreateCylinder(1f, 0.06f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.55f, 0.2f, 1f, 1f), 2f), Float3.Zero), new Float3(2f, 0.03f, 44f), new Quaternion(0f, 0f, 0f, 1f));
            gate0 = go.AddComponent<Teleporter>();
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Teleporter", Mesh.CreateCylinder(1f, 0.06f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.55f, 0.2f, 1f, 1f), 2f), Float3.Zero), new Float3(2f, 9.03f, 50f), new Quaternion(0f, 0f, 0f, 1f));
            gate1 = go.AddComponent<Teleporter>();
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Planet", Mesh.CreateSphere(6f, 32, 48), travel, Float3.Zero), new Float3(14f, 20f, 54f), new Quaternion(0f, 0f, 0f, 1f));
            go.AddComponent<SphereCollider>().Radius = 6f;
            scene.Add(go);
        }

        {
            GameObject go = Pose(new GameObject("Gravity Zone"), new Float3(14f, 20f, 54f), new Quaternion(0f, 0f, 0f, 1f));
            GravityZone zone = go.AddComponent<GravityZone>();
            zone.TowardCenter = true;
            zone.Radius = 14f;
            zone.Priority = 1;
            scene.Add(go);
        }

        scene.Add(Box("Planet Block", new Float3(1.4f, 0.35090876f, 1.4f), edge, new Float3(8.552435f, 18.374914f, 56.14343f), new Quaternion(0.29147846f, 0f, 0.7407984f, 0.6051926f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 0.838824f, 1.4f), plain, new Float3(18.27599f, 23.2361f, 57.343433f), new Quaternion(0.30423734f, 0f, -0.38909563f, 0.869508f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 1.3583453f, 1.4f), edge, new Float3(18.870522f, 19.378296f, 49.620846f), new Quaternion(-0.49460542f, 0f, -0.55010307f, 0.67286855f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 1.3400087f, 1.4f), plain, new Float3(12.212207f, 25.56268f, 50.99578f), new Quaternion(-0.23793346f, 0f, 0.14159271f, 0.9609055f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 0.5099247f, 1.4f), edge, new Float3(8.579591f, 22.818787f, 54.746445f), new Quaternion(0.07102033f, 0f, 0.51572406f, 0.8538061f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 1.4428482f, 1.4f), plain, new Float3(13.700603f, 18.445055f, 47.57071f), new Quaternion(-0.7849087f, 0f, 0.03655141f, 0.6185324f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 1.2256426f, 1.4f), edge, new Float3(8.087685f, 19.754938f, 56.720535f), new Quaternion(0.30109173f, 0f, 0.6543375f, 0.69367594f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 0.4043374f, 1.4f), plain, new Float3(11.757168f, 15.709949f, 50.284946f), new Quaternion(-0.7899778f, 0f, 0.47692108f, 0.3853328f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 0.9502863f, 1.4f), edge, new Float3(9.722027f, 23.243862f, 50.562157f), new Quaternion(-0.31042796f, 0f, 0.38628963f, 0.86857057f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 0.44878155f, 1.4f), plain, new Float3(18.434f, 23.889332f, 55.649517f), new Quaternion(0.1489405f, 0f, -0.40036064f, 0.9041726f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 1.1624517f, 1.4f), edge, new Float3(9.598524f, 20.990429f, 58.65321f), new Quaternion(0.47282514f, 0f, 0.44724584f, 0.75921524f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 0.5679946f, 1.4f), plain, new Float3(14.84625f, 24.184816f, 49.52641f), new Quaternion(-0.39504075f, 0f, -0.07472818f, 0.9156192f)));
        scene.Add(Box("Planet Block", new Float3(1.4f, 1.0158253f, 1.4f), edge, new Float3(18.024294f, 22.230988f, 58.45972f), new Quaternion(0.42384297f, 0f, -0.382461f, 0.8210242f)));

        {
            GameObject go = Pose(Model("Jump Pad", Mesh.CreateCylinder(1.2f, 0.08f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(1f, 0.6f, 0.1f, 1f), 2f), Float3.Zero), new Float3(14f, 0.04f, 54f), new Quaternion(0f, 0f, 0f, 1f));
            go.AddComponent<JumpPad>().Launch = new Float3(0f, 23f, 0f);
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Teleporter", Mesh.CreateCylinder(1f, 0.06f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.55f, 0.2f, 1f, 1f), 2f), Float3.Zero), new Float3(14f, 26.02f, 54f), new Quaternion(0f, 0f, 0f, 1f));
            gate2 = go.AddComponent<Teleporter>();
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Teleporter", Mesh.CreateCylinder(1f, 0.06f, 32), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.55f, 0.2f, 1f, 1f), 2f), Float3.Zero), new Float3(17.5f, 0.03f, 50.5f), new Quaternion(0f, 0f, 0f, 1f));
            gate3 = go.AddComponent<Teleporter>();
            scene.Add(go);
        }

        scene.Add(Box("Flip Ceiling", new Float3(8.8f, 0.4f, 8.8f), travel, new Float3(-2f, 5.2f, 59f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Flip Wall", new Float3(8.8f, 5f, 0.4f), travel, new Float3(-2f, 2.5f, 63.2f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Flip Wall", new Float3(0.4f, 5f, 8.8f), travel, new Float3(-6.2f, 2.5f, 59f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Flip Wall", new Float3(3.2f, 5f, 0.4f), travel, new Float3(-4.8f, 2.5f, 54.8f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Flip Wall", new Float3(0.4f, 5f, 8.8f), travel, new Float3(2.1999998f, 2.5f, 59f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Flip Wall", new Float3(3.2f, 5f, 0.4f), travel, new Float3(0.8000002f, 2.5f, 54.8f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Ceiling Block", new Float3(2f, 0.6f, 2f), edge, new Float3(-4f, 4.7f, 60.5f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Ceiling Block", new Float3(2f, 1.2f, 2f), plain, new Float3(0f, 4.4f, 61f), new Quaternion(0f, 0f, 0f, 1f)));

        {
            GameObject go = Pose(new GameObject("Gravity Zone"), new Float3(-2f, 2.5f, 59f), new Quaternion(0f, 0f, 0f, 1f));
            GravityZone zone = go.AddComponent<GravityZone>();
            zone.Size = new Float3(7.6f, 5f, 7.6f);
            zone.Direction = new Float3(0f, 1f, 0f);
            zone.Priority = 1;
            scene.Add(go);
        }

        scene.Add(Box("Walking Wall", new Float3(0.6f, 8f, 16f), travel, new Float3(-31.3f, 4f, 56f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Wall Ledge", new Float3(0.8f, 1.6f, 1.6f), edge, new Float3(-30.6f, 2f, 51.5f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Wall Ledge", new Float3(0.8f, 1.6f, 1.6f), edge, new Float3(-30.6f, 3.4f, 54.5f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Wall Ledge", new Float3(0.8f, 1.6f, 1.6f), edge, new Float3(-30.6f, 4.8f, 57.5f), new Quaternion(0f, 0f, 0f, 1f)));
        scene.Add(Box("Wall Ledge", new Float3(0.8f, 1.6f, 1.6f), edge, new Float3(-30.6f, 6.2f, 60.5f), new Quaternion(0f, 0f, 0f, 1f)));

        {
            GameObject go = Pose(new GameObject("Gravity Zone"), new Float3(-28.5f, 4f, 56f), new Quaternion(0f, 0f, 0f, 1f));
            GravityZone zone = go.AddComponent<GravityZone>();
            zone.Size = new Float3(5f, 8f, 14f);
            zone.Direction = new Float3(-1f, 0f, 0f);
            zone.Priority = 1;
            scene.Add(go);
        }

        {
            GameObject go = Pose(Model("Zone Edge", Mesh.CreateCube(new Float3(0.1f, 0.02f, 14f)), Lit(new Color(0.02f, 0.02f, 0.03f, 1f), 0f, 0.4f).Emissive(new Color(0.3f, 0.6f, 1f, 1f), 2f), Float3.Zero), new Float3(-26f, 0.01f, 56f), new Quaternion(0f, 0f, 0f, 1f));
            scene.Add(go);
        }

        gate0.Exit = gate1;
        gate1.Exit = gate0;
        gate2.Exit = gate3;
        gate3.Exit = gate2;

        return
        [
            new("Stair lanes", "every lane climbs 4 m, in steps from 0.1 m to 0.5 m either side of the step limit; the first, third and fifth lanes have an invisible ramp over the steps", new Float3(2.6847394f, 1.0740684f, 28.705587f), 15f),
            new("Slope fan", "ramps from 10 to 70 degrees, walkable up to the slope limit and slid down past it", new Float3(3.2817142f, 1.6528571f, 13.874572f), 9f),
            new("Crest and trough", "a ridge to walk over and a dip to walk through, where the ground turns sharply under the feet", new Float3(38.478783f, 0.6914286f, -3.536857f), 8f),
            new("Steep valley", "two slopes too steep to stand on, meeting in a crease the controller must not jitter in", new Float3(29.0765f, 1.766f, 13.6825f), 5f),
            new("Spiral tower", "eight flights of thin steps around a column, with open air under every step; jump off the top onto a plate only 5 cm thick, which a fast fall must not pass through", new Float3(17.838345f, 3.9011629f, -10.718292f), 5f),
            new("Doorway wall", "openings 1.3, 1.85 and 2.2 m high, then 0.7, 0.9 and 1.2 m wide, either side of the character's size", new Float3(11.84877f, 1.9555385f, -23.564f), 9f),
            new("Bars", "bars at shin height to step over, knee height to jump, and duck height to crouch under", new Float3(10.477f, 0.78000003f, -15.424601f), 4f),
            new("Crawl tunnel", "too low to stand in, so only crouching gets through, and standing waits until there is room", new Float3(7.6031666f, 0.9513333f, -16.474943f), 5f),
            new("Open box", "a box to run into, pressing from three sides and above", new Float3(22.897f, 2.475f, 8.974f), 3f),
            new("Narrowing V", "two walls narrowing to a point", new Float3(24.8345f, 1.5f, 3.2735f), 3f),
            new("Ceiling wedge", "a ceiling sloping down to meet the floor", new Float3(27.231f, 1.3f, -2.227f), 3f),
            new("Leaning pocket", "walls that lean in overhead and close toward the back", new Float3(29.6275f, 2f, -7.7279997f), 3f),
            new("Funnel", "walls narrowing to a gap the character cannot fit through", new Float3(32.0235f, 1.5f, -13.2285f), 4f),
            new("Quarter pipes", "floors that curve up into walls, from a tight 1 m radius to a gentle 4.5 m one", new Float3(-6.906f, 0.78125f, -14.941125f), 7f),
            new("Arched tunnel", "a tunnel whose walls curve over into the ceiling", new Float3(-12.11f, 0f, -25.794f), 2f),
            new("Arched tunnel", "a wider tunnel whose curved walls start walkable and turn steep", new Float3(-8.662f, 0f, -22.509f), 3f),
            new("Logs", "logs lying on the ground, from a curb the size of a step to a hump", new Float3(-4.3034997f, 0.51f, -27.845001f), 6f),
            new("Dome", "a low dome to walk over", new Float3(-10.215f, -0.42f, -34.303f), 1.5f),
            new("Dome", "a dome whose sides turn too steep near the ground", new Float3(-12.983f, -0.77f, -32.194f), 2.5f),
            new("Dome", "a big dome to climb", new Float3(-17.398f, -1.12f, -28.915f), 3.5f),
            new("Jagged field", "sharp random peaks with steep faces in every direction", new Float3(-31.305f, 0.02f, 14.909f), 7f),
            new("Rolling bumps", "smooth bumps whose slope changes every step", new Float3(-31.933f, 0.02f, 1.924f), 7f),
            new("Stone field", "a dense field of small stones of different heights, low ones to walk over and taller ones to step up or catch on", new Float3(-18.605328f, 0.12644371f, 13.776789f), 6.5f),
            new("Sliding platform", "a platform gliding back and forth", new Float3(-19f, 0.498f, 31.62f), 5f),
            new("Lift", "a lift up to a ledge", new Float3(-26.176f, 1.05f, 27.713001f), 3f),
            new("Spinning disc", "a turntable", new Float3(-17.393f, 0.13f, -7.782f), 3.5f),
            new("Tilted disc", "a tilted turntable, so the floor under the feet rises and falls as it turns", new Float3(-23.499f, 0.8f, -13.695f), 3.5f),
            new("Sweepers", "panels sweeping round at waist height", new Float3(-30.953f, 0.85f, -9.6449995f), 5f),
            new("Log rollers", "logs turning on their long axis", new Float3(-19.720001f, -0.8075f, 1.6129999f), 4f),
            new("Hop blocks", "blocks of rising height to jump up", new Float3(-12.0098f, 0.75f, 1.2631999f), 6f),
            new("Beams", "beams 1, 0.5 and 0.25 m wide to balance along", new Float3(15.617665f, 1.4266667f, 12.147834f), 8f),
            new("Jump pads", "pads that throw you up onto towers 2, 4 and 7 m tall", new Float3(-12f, 1.1033334f, 47.5f), 8f),
            new("Teleporters", "step onto a ring to come out at its partner, here the ground and the top of a 9 m column", new Float3(2f, 4.52f, 48f), 3f),
            new("Planet", "a small world with its own gravity pulling toward its middle; the orange pad throws you up into its pull and the ring on top brings you back", new Float3(13.564517f, 19.106117f, 53.48762f), 9f),
            new("Flip room", "a room whose gravity pulls toward the ceiling, so walking in drops you onto it", new Float3(-2f, 3.2555556f, 58.922222f), 6f),
            new("Wall walk", "a corridor whose gravity pulls toward its wall, so you walk along the wall", new Float3(-29.742857f, 3.4871428f, 56f), 7f),
        ];
    }

    private static GameObject Pose(GameObject go, Float3 position, Quaternion rotation, Float3? scale = null)
    {
        go.Transform.Position = position;
        go.Transform.Rotation = rotation;
        if (scale.HasValue) go.Transform.LocalScale = scale.Value;
        return go;
    }

    /// <summary>A box with a matching static collider.</summary>
    private static GameObject Box(string name, Float3 size, Material material, Float3 position, Quaternion rotation, Float3? scale = null)
        => Pose(Block(name, size, material, Float3.Zero), position, rotation, scale);

    /// <summary>A collider with nothing drawn, for the invisible ramps laid over stairs.</summary>
    private static GameObject Hidden(string name, Float3 size, Float3 position, Quaternion rotation, Float3? scale = null)
    {
        var go = new GameObject(name);
        go.AddComponent<BoxCollider>().Size = size;
        return Pose(go, position, rotation, scale);
    }

    /// <summary>A mesh that collides with its own shape.</summary>
    private static GameObject Piece(string name, Mesh mesh, Material material, Float3 position, Quaternion rotation, Float3? scale = null)
    {
        GameObject go = Model(name, mesh, material, Float3.Zero);
        go.AddComponent<MeshCollider>().Mesh = mesh;
        return Pose(go, position, rotation, scale);
    }

    /// <summary>A kinematic body, moved through its velocity by a script so what rides it is carried along.</summary>
    private static void Kinematic(GameObject go)
    {
        var body = go.AddComponent<Rigidbody3D>();
        body.MotionType = Jitter2.Dynamics.MotionType.Kinematic;
        body.AffectedByGravity = false;
    }

    private static Material GridMaterial(Color tint) => Lit(tint, 0f, 0.85f).With("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid)).Tiled(2f, 2f);

    /// <summary>A floor that curves up into a wall, the inside of a quarter cylinder, facing its centre.</summary>
    private static Mesh QuarterPipe(float radius, float width, int segments)
    {
        var builder = new MeshBuilder();
        Float3 Point(int s, float x)
        {
            float a = s * MathF.PI * 0.5f / segments;
            return new Float3(x, radius - radius * MathF.Cos(a), radius * MathF.Sin(a));
        }

        for (int s = 0; s < segments; s++)
        {
            Float3 a = Point(s, -width * 0.5f), b = Point(s, width * 0.5f), d = Point(s + 1, -width * 0.5f), e = Point(s + 1, width * 0.5f);
            Float3 facing = new Float3(0f, radius, 0f) - (a + e) * 0.5f;
            builder.Quad(a, b, e, d, new Float2(0f, s), new Float2(width, s), new Float2(width, s + 1), new Float2(0f, s + 1), facing);
        }
        return builder.Build();
    }

    /// <summary>A half cylinder shell standing on the floor, so its walls curve over into the ceiling, seen from both sides.</summary>
    private static Mesh ArchTunnel(float radius, float length, int segments)
    {
        var builder = new MeshBuilder();
        Float3 Point(int s, float z)
        {
            float a = s * MathF.PI / segments;
            return new Float3(radius * MathF.Cos(a), radius * MathF.Sin(a), z);
        }

        for (int s = 0; s < segments; s++)
        {
            Float3 a = Point(s, 0f), b = Point(s, length), d = Point(s + 1, 0f), e = Point(s + 1, length);
            Float3 facing = -(a + e) * 0.5f;
            facing.Z = 0f;
            builder.Quad(a, b, e, d, new Float2(s, 0f), new Float2(s, length), new Float2(s + 1, length), new Float2(s + 1, 0f), facing);
            builder.Quad(a, b, e, d, new Float2(s, 0f), new Float2(s, length), new Float2(s + 1, length), new Float2(s + 1, 0f), -facing);
        }
        return builder.Build();
    }

    /// <summary>The top half of a sphere, facing out.</summary>
    private static Mesh Dome(float radius, int segments)
    {
        var builder = new MeshBuilder();
        Float3 Point(int ring, int slice)
        {
            float pitch = ring * MathF.PI * 0.5f / segments, yaw = slice * MathF.PI * 2f / segments;
            return new Float3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Sin(yaw)) * radius;
        }

        for (int ring = 0; ring < segments; ring++)
            for (int slice = 0; slice < segments; slice++)
            {
                Float3 a = Point(ring, slice), b = Point(ring, slice + 1), d = Point(ring + 1, slice), e = Point(ring + 1, slice + 1);
                builder.Quad(a, b, e, d, new Float2(slice, ring), new Float2(slice + 1, ring), new Float2(slice + 1, ring + 1), new Float2(slice, ring + 1), (a + e) * 0.5f);
            }
        return builder.Build();
    }

    private static float RollingBumpHeight(float u, float v) => (MathF.Sin(u * 18f) * MathF.Cos(v * 15f) * 0.5f + 0.5f) * 1.2f * Edge(u, v);

    private static float Edge(float u, float v)
    {
        float edge = MathF.Min(MathF.Min(u, 1f - u), MathF.Min(v, 1f - v));
        return Math.Clamp(edge * 6f, 0f, 1f);
    }

    private static Mesh Heightfield(float size, int cells, float[] heights)
        => Heightfield(size, cells, (u, v) => heights[(int)MathF.Round(v * cells) * (cells + 1) + (int)MathF.Round(u * cells)]);

    private static Mesh Heightfield(float size, int cells, Func<float, float, float> height)
    {
        var heights = new float[cells + 1, cells + 1];
        for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
                heights[x, z] = height(x / (float)cells, z / (float)cells);

        var builder = new MeshBuilder();
        Float3 P(int x, int z) => new((x / (float)cells - 0.5f) * size, heights[x, z], (z / (float)cells - 0.5f) * size);
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
                builder.Quad(P(x, z), P(x + 1, z), P(x + 1, z + 1), P(x, z + 1), new Float2(x, z), new Float2(x + 1, z), new Float2(x + 1, z + 1), new Float2(x, z + 1), Float3.UnitY);
        return builder.Build();
    }
}
