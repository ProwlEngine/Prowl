using Prowl.Vector;

namespace BRDFGen;

class Program
{
    private const int Size = 256;
    private const int SampleCount = 1024;

    static void Main(string[] args)
    {
        Console.WriteLine("Generating BRDF Lookup Table...");
        Console.WriteLine($"Size: {Size}x{Size}");
        Console.WriteLine($"Samples per pixel: {SampleCount}");

        var pixels = GenerateBRDFLut(Size, SampleCount);

        string outputPath = args.Length > 0 ? args[0] : "brdf_lut.brdf";
        File.WriteAllBytes(outputPath, pixels);

        Console.WriteLine($"BRDF LUT saved to: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"File size: {pixels.Length} bytes ({pixels.Length / 1024.0:F2} KB)");
    }

    static byte[] GenerateBRDFLut(int size, int sampleCount)
    {
        var pixels = new byte[size * size * 4]; // RGBA8
        int totalPixels = size * size;
        int lastPercent = -1;

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

                // Progress indicator
                int pixelsDone = y * size + x + 1;
                int percent = (pixelsDone * 100) / totalPixels;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    Console.Write($"\rProgress: {percent}%");
                }
            }
        }
        Console.WriteLine("\rProgress: 100%");

        return pixels;
    }

    static void IntegrateBRDF(float NdotV, float roughness, int sampleCount, out float scale, out float bias)
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

    static float GeometrySchlickGGX_IBL(float NdotV, float alpha)
    {
        float k = alpha / 2f;
        return NdotV / (NdotV * (1f - k) + k);
    }

    static float GeometrySmithIBL(float NdotV, float NdotL, float alpha)
    {
        return GeometrySchlickGGX_IBL(NdotV, alpha) * GeometrySchlickGGX_IBL(NdotL, alpha);
    }

    static Float2 Hammersley(int i, int N)
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

    static Float3 ImportanceSampleGGX(Float2 Xi, Float3 N, float alpha)
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
}
