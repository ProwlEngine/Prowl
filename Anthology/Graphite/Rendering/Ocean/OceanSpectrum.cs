using System;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// How much wave power the ocean has in each octave of wavelength, 0.0625 m to 2048 m, as powers of ten. The wind
/// then cuts the long waves off, see <see cref="OceanFeature.WindSpeed"/>.
/// </summary>
public sealed class OceanSpectrum
{
    public const int Octaves = 14;
    public const float SmallestWavelength = 0.0625f;
    public const float MinPower = -8f, MaxPower = 5f;

    /// <summary>The power of each octave as a power of ten, from the shortest waves up. A moderate sea by default.</summary>
    public float[] PowerLog { get; } = [-7.1418724f, -6.539813f, -5.937754f, -5.3357f, -4.733663f, -4.1316957f, -3.5300062f, -2.929428f, -2.3332953f, -1.7549441f, -1.2477198f, -1.0250019f, -1.9403106f, -7.39794f];

    /// <summary>Octaves left out of the waves.</summary>
    public bool[] Disabled { get; } = new bool[Octaves];

    /// <summary>Scales every wave's height.</summary>
    public float Multiplier { get; set; } = 1f;

    /// <summary>How far waves pinch sideways into sharp crests.</summary>
    public float Chop { get; set; } = 1.54f;

    /// <summary>Speeds the waves up or down.</summary>
    public float TimeScale { get; set; } = 1f;

    /// <summary>The shortest wavelength in an octave, in metres.</summary>
    public static float Wavelength(int octave) => SmallestWavelength * MathF.Pow(2f, octave);

    /// <summary>Sets every octave to the Pierson Moskowitz spectrum at its shortest wavelength, before the wind cutoff.</summary>
    public void PiersonMoskowitz(float gravity = 9.81f)
    {
        for (int i = 0; i < Octaves; i++)
        {
            float w = MathF.Sqrt(gravity * 2f * MathF.PI / Wavelength(i));
            float power = 8.1e-3f * gravity * gravity / MathF.Pow(w, 5f);
            PowerLog[i] = Math.Clamp(MathF.Log10(power), MinPower, MaxPower);
        }
    }

    /// <summary>The linear power of an octave, the multiplier applied.</summary>
    public float Power(int octave) => Disabled[octave] ? 0f : MathF.Pow(10f, PowerLog[octave]) * Multiplier * Multiplier;
}
