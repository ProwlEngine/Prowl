using System;
using System.Collections.Generic;
using Prowl.Quill;
using Prowl.Vector;

namespace OrigamiSample
{
    /// <summary>
    /// Minimal SVG path renderer for the Prowl icon set (16x16 viewBox, stroke based).
    /// Supports M/m L/l H/h V/v C/c A/a Z/z which covers every glyph in icons.jsx.
    /// Curves and arcs are flattened to short line segments and stroked, so the result
    /// matches the browser's rounded stroke rendering closely.
    /// </summary>
    public static class SvgIcon
    {
        // The launcher's brand mark glyph (drawn inline in the HTML, not part of ICONS).
        public const string Mark = "M3 13.5 L8 2.5 L9.6 6 L13 13.5 L8.3 10.8 Z";

        public const string Search = "M7.2 12.4a5.2 5.2 0 1 0 0-10.4 5.2 5.2 0 0 0 0 10.4zM11.2 11.2L14.5 14.5";
        public const string FolderOpen = "M2 4.2a1 1 0 0 1 1-1h3l1.2 1.4H13a1 1 0 0 1 1 1v1H4.2L2.6 12.5M2 4.2v8a1 1 0 0 0 1 1h10l1.5-6H4.2";
        public const string Folder = "M2 4.2a1 1 0 0 1 1-1h3l1.2 1.4H13a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1z";
        public const string Sphere = "M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM2 8h12M8 1.5c-2 1.6-2 11 0 13M8 1.5c2 1.6 2 11 0 13";
        public const string Globe = "M8 1.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13zM2 8h12M8 1.5c-2.2 1.7-2.2 11.3 0 13M8 1.5c2.2 1.7 2.2 11.3 0 13";
        public const string ArrowR = "M3 8h10M9 4l4 4-4 4";
        public const string Cube = "M8 1.7L14 5v6L8 14.3 2 11V5zM8 1.7V8M8 8l6-3M8 8l-6-3";
        public const string Doc = "M4 1.7h5l3.2 3.2v8.4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.7a1 1 0 0 1 1-1z";
        public const string Bolt = "M8.5 1.5L3.5 9h4l-1 5.5L13 6.5H8.5z";

        // Widget Playground glyphs (from icons.jsx).
        public const string Grid3 = "M2 2h3.5v3.5H2zM6.5 2H10v3.5H6.5zM11 2h3v3.5h-3zM2 6.5h3.5V10H2zM6.5 6.5H10V10H6.5zM11 6.5h3V10h-3zM2 11h3.5v3H2zM6.5 11H10v3H6.5zM11 11h3v3h-3z";
        public const string Plus = "M8 3v10M3 8h10";
        public const string Gear = "M8 5.6a2.4 2.4 0 1 0 0 4.8 2.4 2.4 0 0 0 0-4.8zM8 1.5l.5 1.6 1.6-.6 1 1.4-1 1.3 1.5.8-.4 1.6h-1.6l-.8 1.5-1.4-.7-1.4.7-.8-1.5H4.3l-.4-1.6 1.5-.8-1-1.3 1-1.4 1.6.6z";
        public const string List = "M5.5 4h8M5.5 8h8M5.5 12h8M2.5 4v0M2.5 8v0M2.5 12v0";
        public const string Layers = "M8 2l6 3-6 3-6-3zM2 8l6 3 6-3M2 11l6 3 6-3";
        public const string Close = "M4 4l8 8M12 4l-8 8";
        public const string DotsH = "M3.2 8h.1M8 8h.1M12.8 8h.1";
        public const string Material = "M8 1.7a6.3 6.3 0 1 0 0 12.6c1.2 0 1.5-.8 1.5-1.5 0-1.3-1.5-1.2-1.5-2.5 0-.8.7-1.3 1.7-1.3h1.1A3.7 3.7 0 0 0 14 5.2 6.3 6.3 0 0 0 8 1.7zM5 6.2v0M9 4.2v0M11 7.2v0";
        public const string Script = "M4 1.7h5l3.2 3.2v8.4a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V2.7a1 1 0 0 1 1-1zM5.5 8h5M5.5 10.5h5M5.5 5.5h2";
        public const string Trash = "M3 4.5h10M6.5 4.5V3h3v1.5M4.6 4.5l.6 8.5a1 1 0 0 0 1 1h3.6a1 1 0 0 0 1-1l.6-8.5M6.7 7v4M9.3 7v4";
        public const string Pencil = "M11.4 2.4l2.2 2.2-8 8-2.9.7.7-2.9zM10 3.8l2.2 2.2";
        public const string Link = "M6.6 9.4a2.6 2.6 0 0 0 3.6 0l2-2a2.6 2.6 0 0 0-3.6-3.6l-1 1M9.4 6.6a2.6 2.6 0 0 0-3.6 0l-2 2a2.6 2.6 0 0 0 3.6 3.6l1-1";
        public const string Warn = "M8 2.3L14.6 13.6H1.4zM8 6.3v3.6M8 11.6v.1";
        public const string Expand = "M6 2.5H2.5V6M10 2.5h3.5V6M6 13.5H2.5V10M10 13.5h3.5V10";
        public const string ChevL = "M10 3.5l-4.5 4.5 4.5 4.5";
        public const string ChevR = "M6 3.5l4.5 4.5-4.5 4.5";
        public const string ChevD = "M3.5 6l4.5 4.5 4.5-4.5";
        public const string Clock = "M8 1.7a6.3 6.3 0 1 0 0 12.6 6.3 6.3 0 0 0 0-12.6zM8 4.6V8l2.4 1.5";
        public const string Calendar = "M3 3.5h10a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-8a1 1 0 0 1 1-1zM2 6.5h12M5 2v3M11 2v3";

        /// <summary>
        /// Strokes the given 16x16 viewBox path into the canvas, scaled to fit <paramref name="size"/>
        /// at top-left (<paramref name="x"/>,<paramref name="y"/>).
        /// </summary>
        public static void Draw(Canvas vg, string path, float x, float y, float size, Color32 color, float strokeWidth = 1.4f, bool fill = false)
        {
            float s = size / 16f;
            float px(double vx) => x + (float)vx * s;
            float py(double vy) => y + (float)vy * s;

            vg.SaveState();
            vg.SetStrokeColor(color);
            vg.SetFillColor(color);
            vg.SetStrokeWidth(strokeWidth * s);
            vg.SetStrokeJoint(JointStyle.Round);
            vg.SetStrokeCap(EndCapStyle.Round);

            foreach (var sub in Parse(path))
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

        private static List<Sub> Parse(string d)
        {
            var subs = new List<Sub>();
            var cur = new List<(double, double)>();
            bool closed = false;
            double cx = 0, cy = 0, startX = 0, startY = 0;
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
                else if ((c == 'e' || c == 'E')) { i++; if (i < d.Length && (d[i] == '+' || d[i] == '-')) i++; }
                else break;
            }
            return double.Parse(d.Substring(start, i - start), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void FlattenCubic(List<(double, double)> pts, double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
        {
            const int steps = 16;
            for (int k = 1; k <= steps; k++)
            {
                double t = k / (double)steps, u = 1 - t;
                double a = u * u * u, b = 3 * u * u * t, cc = 3 * u * t * t, dd = t * t * t;
                pts.Add((a * x0 + b * x1 + cc * x2 + dd * x3, a * y0 + b * y1 + cc * y2 + dd * y3));
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

            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 16)));
            for (int k = 1; k <= steps; k++)
            {
                double t = theta1 + dTheta * (k / (double)steps);
                double ex = cosP * rx * Math.Cos(t) - sinP * ry * Math.Sin(t) + cx;
                double ey = sinP * rx * Math.Cos(t) + cosP * ry * Math.Sin(t) + cy;
                pts.Add((ex, ey));
            }
        }
    }
}
