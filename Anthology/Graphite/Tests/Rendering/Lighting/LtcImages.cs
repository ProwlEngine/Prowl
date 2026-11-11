using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

using Xunit;

using D3 = Prowl.Graphite.Rendering.Tests.LtcTables.D3;

namespace Prowl.Graphite.Rendering.Tests;

// CPU images of area light specular, the LTC table against brute force, each beside the other and their difference.
// Set LTC_IMAGES to a folder to render them.
public sealed class LtcImages
{
    private static readonly int Width = int.TryParse(Environment.GetEnvironmentVariable("LTC_WIDTH"), out int w) ? w : 160, Height = Width * 3 / 4;

    private static readonly Lazy<ushort[]> s_table = new(() => Environment.GetEnvironmentVariable("LTC_TABLE") is { Length: > 0 } path
        ? System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(File.ReadAllBytes(path)).ToArray()
        : LtcTables.Embedded());
    private static readonly ushort[] s_dfg = EnergyTables.EmbeddedDfg();

    private enum Kind { Rect, Disk, Tube }

    private sealed record Light(Kind Kind, D3 Center, D3 H1, D3 H2);

    private sealed record Scene(string Name, bool Sphere, D3 Eye, D3 Target, Light Light, double Roughness, double Zoom = 0.3);

    private static D3 Local(D3 w, D3 t1, D3 t2, D3 n) => new(D3.Dot(w, t1), D3.Dot(w, t2), D3.Dot(w, n));

    // The specular of a one nit light toward the eye, Fresnel 1: the table's LTC share times the albedo, and brute force
    private static (double Ltc, double Truth) Shade(Light light, D3 p, D3 n, D3 v, double roughness)
    {
        double alpha = LtcTables.AlphaOf(roughness);
        double mu = Math.Max(D3.Dot(n, v), 1e-4);
        D3 t1 = v - n * D3.Dot(n, v);
        t1 = t1.Length > 1e-6 ? t1.Normalized : D3.Cross(n, Math.Abs(n.Y) < 0.99 ? new D3(0, 1, 0) : new D3(1, 0, 0)).Normalized;
        D3 t2 = D3.Cross(n, t1);
        D3 c = Local(light.Center - p, t1, t2, n), h1 = Local(light.H1, t1, t2, n), h2 = Local(light.H2, t1, t2, n);
        // A tube shows its width across its axis and the view of it
        if (light.Kind == Kind.Tube)
            h2 = D3.Cross(h1, c).Normalized * light.H2.Length;
        D3 vl = new(Math.Sqrt(Math.Max(1 - mu * mu, 0)), 0, mu);
        D3 normal = D3.Cross(h1, h2).Normalized;
        bool front = light.Kind == Kind.Tube || D3.Dot(normal, c) < 0;

        LtcTables.M3 inverse = LtcTables.Lookup(s_table.Value, mu, roughness);
        D3 ci = inverse * c, ai = inverse * h1, bi = inverse * h2;
        double share = !front ? 0 : light.Kind switch
        {
            Kind.Rect => LtcTables.Polygon([ci - ai - bi, ci + ai - bi, ci + ai + bi, ci - ai + bi]),
            Kind.Disk => LtcTables.Disk(ci, ai, bi),
            _ => LtcTables.Strip(ci - ai, ci + ai, bi * 2),
        };
        System.Numerics.Vector3 dfg = EnergyTables.LookupDfg(s_dfg, mu, roughness);
        double ltc = share * (dfg.X + dfg.Y);

        const int steps = 72;
        double truth = 0;
        if (front)
        {
            for (int i = 0; i < steps; i++)
            {
                for (int j = 0; j < (light.Kind == Kind.Tube ? 1 : steps); j++)
                {
                    double u = (i + 0.5) / steps * 2 - 1, w = light.Kind == Kind.Tube ? 0 : (j + 0.5) / steps * 2 - 1;
                    if (light.Kind == Kind.Disk && u * u + w * w > 1)
                        continue;
                    D3 q = c + h1 * u + h2 * w;
                    double d2 = D3.Dot(q, q);
                    D3 l = q / Math.Sqrt(d2);
                    double f = LtcTables.Brdf(vl, l, alpha);
                    if (light.Kind == Kind.Tube)
                    {
                        double along = D3.Dot(l, h1.Normalized);
                        truth += f * 2 * h2.Length * Math.Sqrt(Math.Max(1 - along * along, 0)) / d2 * (2 * h1.Length / steps);
                    }
                    else
                    {
                        truth += f * Math.Max(-D3.Dot(l, normal), 0) / d2 * (4 * h1.Length * h2.Length / (steps * steps));
                    }
                }
            }
        }
        return (ltc, truth);
    }

    private static void Draw(Scene scene, string folder)
    {
        D3 forward = (scene.Target - scene.Eye).Normalized;
        D3 right = D3.Cross(forward, new D3(0, 1, 0)).Normalized, up = D3.Cross(right, forward);
        var ltc = new double[Width * Height];
        var truth = new double[Width * Height];
        var hit = new bool[Width * Height];
        Parallel.For(0, Height, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                double sx = (x + 0.5) / Width * 2 - 1, sy = 1 - (y + 0.5) / Height * 2;
                D3 dir = (forward + right * (sx * scene.Zoom) + up * (sy * scene.Zoom * Height / Width)).Normalized;
                D3 p, n;
                if (scene.Sphere)
                {
                    // A unit sphere at the origin
                    double b = D3.Dot(scene.Eye, dir), cc = D3.Dot(scene.Eye, scene.Eye) - 1, disc = b * b - cc;
                    if (disc < 0)
                        continue;
                    double t = -b - Math.Sqrt(disc);
                    p = scene.Eye + dir * t;
                    n = p.Normalized;
                }
                else
                {
                    // The floor at y = 0
                    if (dir.Y >= 0)
                        continue;
                    double t = -scene.Eye.Y / dir.Y;
                    p = scene.Eye + dir * t;
                    n = new D3(0, 1, 0);
                }
                (ltc[y * Width + x], truth[y * Width + x]) = Shade(scene.Light, p, n, (scene.Eye - p).Normalized, scene.Roughness);
                hit[y * Width + x] = true;
            }
        });

        double peak = 0;
        foreach (double t in truth)
            peak = Math.Max(peak, t);
        byte[] rgb = new byte[Width * 3 * Height * 3];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                // Four decades below the peak to it, so the faint falloff shows
                byte Tone(double value) => (byte)(255 * Math.Clamp(1 + Math.Log10(Math.Max(value / Math.Max(peak, 1e-12), 1e-9)) / 4, 0, 1));
                void Put(int panel, byte r, byte g, byte b)
                {
                    int at = (y * Width * 3 + panel * Width + x) * 3;
                    rgb[at] = r;
                    rgb[at + 1] = g;
                    rgb[at + 2] = b;
                }
                if (!hit[i])
                {
                    for (int panel = 0; panel < 3; panel++)
                        Put(panel, 30, 30, 40);
                    continue;
                }
                byte a = Tone(ltc[i]), t = Tone(truth[i]);
                Put(0, a, a, a);
                Put(1, t, t, t);
                double error = (ltc[i] - truth[i]) / Math.Max(peak, 1e-12) * 4;
                Put(2, (byte)(255 * Math.Clamp(error, 0, 1)), 0, (byte)(255 * Math.Clamp(-error, 0, 1)));
            }
        }
        WritePng(Path.Combine(folder, scene.Name + ".png"), Width * 3, Height, rgb);
    }

    [Fact]
    public void Render_TheLtcTable_BesideBruteForce()
    {
        string? folder = Environment.GetEnvironmentVariable("LTC_IMAGES");
        Assert.SkipWhen(string.IsNullOrEmpty(folder), "Set LTC_IMAGES to a folder to render the images.");
        Directory.CreateDirectory(folder);
        D3 Turn(D3 v, double angle) => new(v.X * Math.Cos(angle) - v.Z * Math.Sin(angle), v.Y, v.X * Math.Sin(angle) + v.Z * Math.Cos(angle));

        // An upright panel beside the sphere, facing it
        Light panel = new(Kind.Rect, new D3(2.2, 0.3, 0.6), new D3(0, 0, 0.9), new D3(0, 0.9, 0));
        Light disk = new(Kind.Disk, new D3(2.2, 0.3, 0.6), new D3(0, 0, 0.6), new D3(0, 0.6, 0));
        Light tube = new(Kind.Tube, new D3(1.8, 0.4, 0.8), new D3(-0.3, 0.6, 0.4), new D3(0.02, 0.0, 0.015));
        // Over the floor: a panel facing down turned 45 degrees about y, a tube lying across
        Light ceiling = new(Kind.Rect, new D3(0, 1.5, 3), Turn(new D3(0.6, 0, 0), Math.PI / 4), Turn(new D3(0, 0, 0.6), Math.PI / 4));
        Light lying = new(Kind.Tube, new D3(0.3, 0.6, 3), new D3(1.0, 0, 0.5), new D3(0, 0.02, 0));
        foreach (Scene scene in new Scene[]
        {
            new("sphere-rect-0.25", true, new D3(0, 0.2, 4), new D3(0, 0, 0), panel, 0.25),
            new("sphere-rect-0.5", true, new D3(0, 0.2, 4), new D3(0, 0, 0), panel, 0.5),
            new("sphere-disk-0.3", true, new D3(0, 0.2, 4), new D3(0, 0, 0), disk, 0.3),
            new("sphere-tube-0.3", true, new D3(0, 0.2, 4), new D3(0, 0, 0), tube, 0.3),
            new("floor-rect-0.3", false, new D3(0, 1.2, -2), new D3(0, 0, 3), ceiling, 0.3, 0.5),
            new("floor-rect-0.1", false, new D3(0, 1.2, -2), new D3(0, 0, 3), ceiling, 0.1, 0.5),
            new("floor-tube-0.3", false, new D3(0, 1.2, -2), new D3(0, 0, 3), lying, 0.3, 0.5),
            new("sphere-rect-0.1", true, new D3(0, 0.2, 4), new D3(0, 0, 0), panel, 0.1),
            new("backlit-rect-0.25", true, new D3(0, 0.2, 4), new D3(0, 0, 0), new(Kind.Rect, new D3(0, 1.8, -5), new D3(3.6, 0, 0), new D3(0, 0.9, 0)), 0.25),
            new("backlit-rect-0.5", true, new D3(0, 0.2, 4), new D3(0, 0, 0), new(Kind.Rect, new D3(0, 1.8, -5), new D3(3.6, 0, 0), new D3(0, 0.9, 0)), 0.5),
        })
        {
            Draw(scene, folder);
        }
    }

    private static void WritePng(string path, int width, int height, byte[] rgb)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgb, y * width * 3, width * 3);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            raw.WriteTo(z);
        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        void Chunk(string type, byte[] data)
        {
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            file.Write(BigEndian((uint)data.Length));
            file.Write(typeBytes);
            file.Write(data);
            file.Write(BigEndian(Crc(typeBytes, data)));
        }
        byte[] header = [.. BigEndian((uint)width), .. BigEndian((uint)height), 8, 2, 0, 0, 0];
        Chunk("IHDR", header);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
    }

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint Crc(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        void Feed(byte[] bytes)
        {
            foreach (byte b in bytes)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }
        Feed(type);
        Feed(data);
        return ~crc;
    }
}
