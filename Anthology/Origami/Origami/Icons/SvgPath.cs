// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Minimal SVG path renderer for the Origami icon set: a 16x16 viewBox, stroke-based. Supports
/// M/m L/l H/h V/v C/c A/a Z/z (curves and arcs flattened to short segments), matching the browser's
/// rounded-stroke rendering closely. Used by <see cref="SvgIcon"/>.
/// </summary>
internal static class SvgPath
{
    /// <summary>Stroke (or fill) a 16x16 viewBox path scaled to <paramref name="size"/> at (<paramref name="x"/>,<paramref name="y"/>).</summary>
    public static void Stroke(Canvas vg, string path, float x, float y, float size, Color color, float strokeWidth = 1.5f, bool fill = false)
    {
        float s = size / 16f;
        float px(double vx) => x + (float)vx * s;
        float py(double vy) => y + (float)vy * s;
        var c32 = Color32.FromArgb(color.A, color.R, color.G, color.B);

        vg.SaveState();
        vg.SetStrokeColor(c32);
        vg.SetFillColor(c32);
        vg.SetStrokeWidth(strokeWidth * s);
        vg.SetStrokeJoint(JointStyle.Round);
        vg.SetStrokeCap(EndCapStyle.Round);

        foreach (var sub in ParseCached(path))
        {
            if (sub.Points.Count == 0) continue;
            vg.BeginPath();
            vg.MoveTo(px(sub.Points[0].x), py(sub.Points[0].y));
            for (int i = 1; i < sub.Points.Count; i++)
                vg.LineTo(px(sub.Points[i].x), py(sub.Points[i].y));
            if (sub.Closed) vg.ClosePath();

            if (fill) vg.Fill();
            else vg.Stroke();
        }

        vg.RestoreState();
    }

    private struct Sub { public List<(double x, double y)> Points; public bool Closed; }

    // A path string always flattens to the same points, so parse each unique path once and reuse it
    // (the result is only read during Stroke, never mutated). Bounded by the number of distinct icons.
    private static readonly Dictionary<string, List<Sub>> _parseCache = new();

    private static List<Sub> ParseCached(string d)
    {
        if (!_parseCache.TryGetValue(d, out var subs))
            _parseCache[d] = subs = Parse(d);
        return subs;
    }

    private static List<Sub> Parse(string d)
    {
        var subs = new List<Sub>();
        var cur = new List<(double, double)>();
        bool closed = false;
        double cx = 0, cy = 0, startX = 0, startY = 0;
        double lastCtrlX = 0, lastCtrlY = 0; // last curve control point, for S/T smooth reflection
        char lastCurve = '\0';               // 'C' after a cubic (C/S), 'Q' after a quadratic (Q/T)
        int i = 0;
        char cmd = '\0';

        void Flush()
        {
            if (cur.Count > 0) subs.Add(new Sub { Points = cur, Closed = closed });
            cur = new List<(double, double)>();
            closed = false;
        }

        void Add(double xx, double yy) { cur.Add((xx, yy)); cx = xx; cy = yy; }

        while (i < d.Length)
        {
            int startI = i; // guarantee forward progress no matter what the input is
            char c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c)) { cmd = c; i++; }

            bool rel = char.IsLower(cmd);
            switch (char.ToUpper(cmd))
            {
                case 'M':
                    {
                        double nx = ReadNum(d, ref i), ny = ReadNum(d, ref i);
                        Flush();
                        if (rel) { nx += cx; ny += cy; }
                        startX = nx; startY = ny;
                        Add(nx, ny);
                        cmd = rel ? 'l' : 'L'; // subsequent pairs are implicit lineto
                        break;
                    }
                case 'L':
                    {
                        double nx = ReadNum(d, ref i), ny = ReadNum(d, ref i);
                        if (rel) { nx += cx; ny += cy; }
                        Add(nx, ny);
                        break;
                    }
                case 'H':
                    {
                        double nx = ReadNum(d, ref i);
                        if (rel) nx += cx;
                        Add(nx, cy);
                        break;
                    }
                case 'V':
                    {
                        double ny = ReadNum(d, ref i);
                        if (rel) ny += cy;
                        Add(cx, ny);
                        break;
                    }
                case 'C':
                    {
                        double x1 = ReadNum(d, ref i), y1 = ReadNum(d, ref i);
                        double x2 = ReadNum(d, ref i), y2 = ReadNum(d, ref i);
                        double ex = ReadNum(d, ref i), ey = ReadNum(d, ref i);
                        if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; ex += cx; ey += cy; }
                        FlattenCubic(cur, cx, cy, x1, y1, x2, y2, ex, ey);
                        lastCtrlX = x2; lastCtrlY = y2; lastCurve = 'C';
                        cx = ex; cy = ey;
                        break;
                    }
                case 'S': // smooth cubic: first control reflects the previous cubic's second control
                    {
                        double x2 = ReadNum(d, ref i), y2 = ReadNum(d, ref i);
                        double ex = ReadNum(d, ref i), ey = ReadNum(d, ref i);
                        if (rel) { x2 += cx; y2 += cy; ex += cx; ey += cy; }
                        double x1 = lastCurve == 'C' ? 2 * cx - lastCtrlX : cx;
                        double y1 = lastCurve == 'C' ? 2 * cy - lastCtrlY : cy;
                        FlattenCubic(cur, cx, cy, x1, y1, x2, y2, ex, ey);
                        lastCtrlX = x2; lastCtrlY = y2; lastCurve = 'C';
                        cx = ex; cy = ey;
                        break;
                    }
                case 'Q': // quadratic bezier
                    {
                        double x1 = ReadNum(d, ref i), y1 = ReadNum(d, ref i);
                        double ex = ReadNum(d, ref i), ey = ReadNum(d, ref i);
                        if (rel) { x1 += cx; y1 += cy; ex += cx; ey += cy; }
                        FlattenQuad(cur, cx, cy, x1, y1, ex, ey);
                        lastCtrlX = x1; lastCtrlY = y1; lastCurve = 'Q';
                        cx = ex; cy = ey;
                        break;
                    }
                case 'T': // smooth quadratic: control reflects the previous quadratic's control
                    {
                        double ex = ReadNum(d, ref i), ey = ReadNum(d, ref i);
                        if (rel) { ex += cx; ey += cy; }
                        double x1 = lastCurve == 'Q' ? 2 * cx - lastCtrlX : cx;
                        double y1 = lastCurve == 'Q' ? 2 * cy - lastCtrlY : cy;
                        FlattenQuad(cur, cx, cy, x1, y1, ex, ey);
                        lastCtrlX = x1; lastCtrlY = y1; lastCurve = 'Q';
                        cx = ex; cy = ey;
                        break;
                    }
                case 'A':
                    {
                        double rx = ReadNum(d, ref i), ry = ReadNum(d, ref i);
                        double rot = ReadNum(d, ref i);
                        double large = ReadNum(d, ref i), sweep = ReadNum(d, ref i);
                        double ex = ReadNum(d, ref i), ey = ReadNum(d, ref i);
                        if (rel) { ex += cx; ey += cy; }
                        FlattenArc(cur, cx, cy, rx, ry, rot, large != 0, sweep != 0, ex, ey);
                        cx = ex; cy = ey;
                        break;
                    }
                case 'Z':
                    closed = true;
                    cx = startX; cy = startY;
                    Flush();
                    break;
            }

            // Smooth curves (S/T) reflect only the immediately preceding curve; any other command
            // clears the reflection state so the next S/T falls back to the current point.
            char uc = char.ToUpper(cmd);
            if (uc != 'C' && uc != 'S' && uc != 'Q' && uc != 'T')
                lastCurve = '\0';

            // If nothing above consumed a character (unsupported command, or a stray char while no
            // command is active), skip one so the loop can never spin forever on malformed input.
            if (i == startI) i++;
        }
        Flush();
        return subs;
    }

    private static double ReadNum(string d, ref int i)
    {
        while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ',')) i++;
        int start = i;
        if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++;
        bool dot = false;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsDigit(c)) { i++; }
            else if (c == '.' && !dot) { dot = true; i++; }
            else if (c == 'e' || c == 'E') { i++; if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++; }
            else break;
        }
        int len = i - start;
        if (len <= 0 || !double.TryParse(d.Substring(start, len), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double val))
            return 0.0; // malformed / no number here - don't throw
        return val;
    }

    private static void FlattenCubic(List<(double, double)> pts, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        // Icons are tiny (16px viewBox), so a few segments per curve is plenty and keeps the point
        // count (and therefore the polyline mesh) small.
        const int steps = 6;
        for (int k = 1; k <= steps; k++)
        {
            double t = k / (double)steps, u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, cc = 3 * u * t * t, dd = t * t * t;
            pts.Add((a * x0 + b * x1 + cc * x2 + dd * x3, a * y0 + b * y1 + cc * y2 + dd * y3));
        }
    }

    private static void FlattenQuad(List<(double, double)> pts, double x0, double y0, double x1, double y1, double x2, double y2)
    {
        const int steps = 6;
        for (int k = 1; k <= steps; k++)
        {
            double t = k / (double)steps, u = 1 - t;
            double a = u * u, b = 2 * u * t, cc = t * t;
            pts.Add((a * x0 + b * x1 + cc * x2, a * y0 + b * y1 + cc * y2));
        }
    }

    private static void FlattenArc(List<(double, double)> pts, double x1, double y1, double rx, double ry, double rotDeg, bool large, bool sweep, double x2, double y2)
    {
        if (rx == 0 || ry == 0) { pts.Add((x2, y2)); return; }
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        double phi = rotDeg * Math.PI / 180.0;
        double cosP = Math.Cos(phi), sinP = Math.Sin(phi);

        double dx = (x1 - x2) / 2.0, dy = (y1 - y2) / 2.0;
        double x1p = cosP * dx + sinP * dy;
        double y1p = -sinP * dx + cosP * dy;

        double lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lam > 1) { double sl = Math.Sqrt(lam); rx *= sl; ry *= sl; }

        double num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double co = Math.Sqrt(Math.Max(0, num / den));
        if (large == sweep) co = -co;
        double cxp = co * (rx * y1p / ry);
        double cyp = co * (-ry * x1p / rx);

        double cx = cosP * cxp - sinP * cyp + (x1 + x2) / 2.0;
        double cy = sinP * cxp + cosP * cyp + (y1 + y2) / 2.0;

        double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
            double ang = Math.Acos(Math.Clamp(dot / len, -1.0, 1.0));
            if (ux * vy - uy * vx < 0) ang = -ang;
            return ang;
        }

        double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double dTheta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dTheta > 0) dTheta -= 2 * Math.PI;
        if (sweep && dTheta < 0) dTheta += 2 * Math.PI;

        int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 6)));
        for (int k = 1; k <= steps; k++)
        {
            double t = theta1 + dTheta * (k / (double)steps);
            double ex = cosP * rx * Math.Cos(t) - sinP * ry * Math.Sin(t) + cx;
            double ey = sinP * rx * Math.Cos(t) + cosP * ry * Math.Sin(t) + cy;
            pts.Add((ex, ey));
        }
    }
}
