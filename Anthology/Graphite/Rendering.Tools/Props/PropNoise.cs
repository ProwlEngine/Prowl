using System;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>Deterministic gradient noise for the prop generators, the 2D kinds tiling over a whole number period.</summary>
internal static class PropNoise
{
    public static uint Hash(int x, int y, int z, int seed)
    {
        uint h = (uint)seed * 0x9E3779B9u ^ (uint)x * 0x85EBCA6Bu ^ (uint)y * 0xC2B2AE35u ^ (uint)z * 0x27D4EB2Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        return h ^ h >> 15;
    }

    public static double Unit(uint hash) => hash / 4294967296.0;

    // A stream of numbers from 0 to 1 that only depends on its seed
    public sealed class Random(int seed)
    {
        private int _next;

        public double Next() => Unit(Hash(_next++, 0, 0, seed));

        public double Range(double min, double max) => min + (max - min) * Next();

        public Float3 Direction()
        {
            double z = Range(-1, 1), a = Range(0, Math.PI * 2), r = Math.Sqrt(1 - z * z);
            return new Float3((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a)), (float)z);
        }
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Grad2(int x, int y, int seed, double dx, double dy)
    {
        double a = Unit(Hash(x, y, 0, seed)) * Math.PI * 2;
        return Math.Cos(a) * dx + Math.Sin(a) * dy;
    }

    /// <summary>2D gradient noise about -1 to 1, repeating every px and py units.</summary>
    public static double Noise(double x, double y, int px, int py, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        int x0 = Wrap(ix, px), x1 = Wrap(ix + 1, px), y0 = Wrap(iy, py), y1 = Wrap(iy + 1, py);
        double u = Fade(fx), v = Fade(fy);
        double a = Grad2(x0, y0, seed, fx, fy), b = Grad2(x1, y0, seed, fx - 1, fy);
        double c = Grad2(x0, y1, seed, fx, fy - 1), d = Grad2(x1, y1, seed, fx - 1, fy - 1);
        return ((a + (b - a) * u) * (1 - v) + (c + (d - c) * u) * v) * 1.4;
    }

    /// <summary>Octaves of <see cref="Noise"/>, each twice the frequency and half the amplitude.</summary>
    public static double Fbm(double x, double y, int octaves, int px, int py, int seed)
    {
        double sum = 0, amplitude = 0.5, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(x, y, px, py, seed + o * 101) * amplitude;
            norm += amplitude;
            x *= 2;
            y *= 2;
            px *= 2;
            py *= 2;
            amplitude *= 0.5;
        }
        return sum / norm;
    }

    /// <summary>The distance to the nearest of one random point a unit cell, repeating every period, 0 to about 1.</summary>
    public static double Cells(double x, double y, int px, int py, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double best = 9;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int cx = ix + i, cy = iy + j;
                uint h = Hash(Wrap(cx, px), Wrap(cy, py), 1, seed);
                double fx = cx + Unit(h) - x, fy = cy + Unit(Hash((int)h, 2, 3, seed)) - y;
                best = Math.Min(best, fx * fx + fy * fy);
            }
        return Math.Sqrt(best);
    }

    /// <summary>3D gradient noise about -1 to 1.</summary>
    public static double Noise(Float3 p, int seed)
    {
        int ix = (int)Math.Floor(p.X), iy = (int)Math.Floor(p.Y), iz = (int)Math.Floor(p.Z);
        double fx = p.X - ix, fy = p.Y - iy, fz = p.Z - iz;
        double u = Fade(fx), v = Fade(fy), w = Fade(fz);
        double G(int x, int y, int z, double dx, double dy, double dz)
        {
            uint h = Hash(x, y, z, seed);
            double a = Unit(h) * 2 - 1, b = Unit(Hash((int)h, 7, 9, seed)) * Math.PI * 2, r = Math.Sqrt(1 - a * a);
            return r * Math.Cos(b) * dx + r * Math.Sin(b) * dy + a * dz;
        }
        double Lerp(double a, double b, double t) => a + (b - a) * t;
        double x00 = Lerp(G(ix, iy, iz, fx, fy, fz), G(ix + 1, iy, iz, fx - 1, fy, fz), u);
        double x10 = Lerp(G(ix, iy + 1, iz, fx, fy - 1, fz), G(ix + 1, iy + 1, iz, fx - 1, fy - 1, fz), u);
        double x01 = Lerp(G(ix, iy, iz + 1, fx, fy, fz - 1), G(ix + 1, iy, iz + 1, fx - 1, fy, fz - 1), u);
        double x11 = Lerp(G(ix, iy + 1, iz + 1, fx, fy - 1, fz - 1), G(ix + 1, iy + 1, iz + 1, fx - 1, fy - 1, fz - 1), u);
        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w) * 1.6;
    }

    public static double Fbm(Float3 p, int octaves, int seed)
    {
        double sum = 0, amplitude = 0.5, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(p, seed + o * 101) * amplitude;
            norm += amplitude;
            p *= 2.03f;
            amplitude *= 0.5;
        }
        return sum / norm;
    }

    private static int Wrap(int v, int period) => period <= 0 ? v : (v % period + period) % period;
}
