// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Loads a pre-computed 2D BRDF Integration LUT for split-sum IBL approximation.
/// The LUT is indexed by (NdotV, roughness) and stores (scale, bias) for
/// the Fresnel split-sum: F0 * scale + bias.
/// The LUT is loaded from an embedded resource and set as a global texture "_BRDFLut".
/// </summary>
public static class BRDFLutGenerator
{
    private static Texture2D? _lut;
    private const int Size = 256;

    /// <summary>
    /// Get or load the pre-computed BRDF LUT texture from embedded resources.
    /// </summary>
    public static Texture2D GetLut()
    {
        if (_lut != null && _lut.IsValid())
            return _lut;

        _lut = LoadPrecomputed();
        return _lut;
    }

    /// <summary>
    /// Upload the BRDF LUT as a global texture so all shaders can access it.
    /// Call once at startup or when the LUT is invalidated.
    /// </summary>
    public static void UploadGlobal()
    {
        PropertyState.SetGlobalTexture("_BRDFLut", GetLut());
    }

    /// <summary>
    /// Load the pre-computed BRDF LUT from embedded resources.
    /// The LUT is generated offline by the BRDFGen tool and embedded into the runtime.
    /// </summary>
    private static Texture2D LoadPrecomputed()
    {
        using Stream stream = EmbeddedResources.GetStream("Assets/brdf_lut.brdf");

        // Read the raw RGBA8 pixel data (256x256x4 = 262144 bytes)
        int expectedSize = Size * Size * 4;
        byte[] pixels = new byte[expectedSize];

        int bytesRead = stream.Read(pixels, 0, expectedSize);
        if (bytesRead != expectedSize)
            throw new InvalidDataException($"BRDF LUT file size mismatch. Expected {expectedSize} bytes, got {bytesRead}.");

        var tex = new Texture2D((uint)Size, (uint)Size, false, TextureImageFormat.Color4b);
        tex.SetData<byte>(pixels);
        tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        tex.SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
        tex.Name = "BRDF LUT";
        return tex;
    }

    #region Legacy Generation Code (Kept for Reference / Regeneration Tool)
    // The code below was used to generate the brdf_lut.brdf file.
    // It's kept here for reference and for potential regeneration needs.
    // The actual runtime now loads the pre-computed file above.

    /*
    private const int SampleCount = 1024;

    private static Texture2D Generate(int size, int sampleCount)
    {
        var pixels = new byte[size * size * 4]; // RGBA8

        for (int y = 0; y < size; y++)
        {
            float roughness = (y + 0.5f) / size;

            for (int x = 0; x < size; x++)
            {
                float NdotV = (x + 0.5f) / size;
                NdotV = MathF.Max(NdotV, 0.001f);

                IntegrateBRDF(NdotV, roughness, sampleCount, out float scale, out float bias);

                int idx = (y * size + x) * 4;
                pixels[idx + 0] = (byte)(MathF.Min(scale, 1f) * 255f + 0.5f);
                pixels[idx + 1] = (byte)(MathF.Min(bias, 1f) * 255f + 0.5f);
                pixels[idx + 2] = 0;
                pixels[idx + 3] = 255;
            }
        }

        var tex = new Texture2D((uint)size, (uint)size, false, TextureImageFormat.Color4b);
        tex.SetData<byte>(pixels);
        tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
        tex.SetWrapModes(TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
        tex.Name = "BRDF LUT";
        return tex;
    }

    private static void IntegrateBRDF(float NdotV, float roughness, int sampleCount, out float scale, out float bias)
    {
        // View vector in tangent space (N = (0,0,1))
        Float3 V = new Float3(MathF.Sqrt(1f - NdotV * NdotV), 0f, NdotV);
        Float3 N = new Float3(0f, 0f, 1f);

        float a = 0f;
        float b = 0f;
        float alpha = roughness * roughness;

        for (int i = 0; i < sampleCount; i++)
        {
            // Low-discrepancy sequence (Hammersley)
            Float2 Xi = Hammersley(i, sampleCount);

            // Importance sample GGX
            Float3 H = ImportanceSampleGGX(Xi, N, alpha);
            Float3 L = Float3.Normalize(2f * Float3.Dot(V, H) * H - V);

            float NdotL = MathF.Max(L.Z, 0f);
            float NdotH = MathF.Max(H.Z, 0f);
            float VdotH = MathF.Max(Float3.Dot(V, H), 0f);

            if (NdotL > 0f)
            {
                float G = GeometrySmithIBL(NdotV, NdotL, alpha);
                float G_Vis = (G * VdotH) / (NdotH * NdotV + 0.0001f);
                float Fc = MathF.Pow(1f - VdotH, 5f);

                a += (1f - Fc) * G_Vis;
                b += Fc * G_Vis;
            }
        }

        scale = a / sampleCount;
        bias = b / sampleCount;
    }

    private static float GeometrySchlickGGX_IBL(float NdotV, float alpha)
    {
        float k = alpha / 2f;
        return NdotV / (NdotV * (1f - k) + k);
    }

    private static float GeometrySmithIBL(float NdotV, float NdotL, float alpha)
    {
        return GeometrySchlickGGX_IBL(NdotV, alpha) * GeometrySchlickGGX_IBL(NdotL, alpha);
    }

    private static Float2 Hammersley(int i, int N)
    {
        uint bits = (uint)i;
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        float rdi = bits * 2.3283064365386963e-10f;
        return new Float2((float)i / N, rdi);
    }

    private static Float3 ImportanceSampleGGX(Float2 Xi, Float3 N, float alpha)
    {
        float a2 = alpha * alpha;
        float phi = 2f * MathF.PI * Xi.X;
        float cosTheta = MathF.Sqrt((1f - Xi.Y) / (1f + (a2 - 1f) * Xi.Y));
        float sinTheta = MathF.Sqrt(1f - cosTheta * cosTheta);

        // Spherical to cartesian (tangent space, N = (0,0,1))
        Float3 H = new Float3(
            MathF.Cos(phi) * sinTheta,
            MathF.Sin(phi) * sinTheta,
            cosTheta
        );

        return Float3.Normalize(H);
    }
    */
    #endregion
}
