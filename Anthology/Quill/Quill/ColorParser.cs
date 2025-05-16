using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace Prowl.Quill
{
    internal class ColorParser
    {
        private static readonly Dictionary<string, Color> NamedColorsMap = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "black", Color.FromArgb(255, 0, 0, 0) }, // Added Alpha for consistency
            { "white", Color.FromArgb(255, 255, 255, 255) },
            { "red", Color.FromArgb(255, 255, 0, 0) },
            { "green", Color.FromArgb(255, 0, 128, 0) },
            { "blue", Color.FromArgb(255, 0, 0, 255) },
            { "yellow", Color.FromArgb(255, 255, 255, 0) },
            { "purple", Color.FromArgb(255, 128, 0, 128) },
            { "aqua", Color.FromArgb(255, 0, 255, 255) },
            { "fuchsia", Color.FromArgb(255, 255, 0, 255) },
            { "gray", Color.FromArgb(255, 128, 128, 128) },
            { "grey", Color.FromArgb(255, 128, 128, 128) },
            { "lime", Color.FromArgb(255, 0, 255, 0) },
            { "maroon", Color.FromArgb(255, 128, 0, 0) },
            { "navy", Color.FromArgb(255, 0, 0, 128) },
            { "olive", Color.FromArgb(255, 128, 128, 0) },
            { "silver", Color.FromArgb(255, 192, 192, 192) },
            { "teal", Color.FromArgb(255, 0, 128, 128) },
            // Transparent is handled by "none" or rgba(...,0)
        };

        public static Color Parse(string attribute)
        {
            var color = Color.Transparent;
            try
            {
                string trimmedAttribute = attribute.Trim();
                if (trimmedAttribute.StartsWith("#"))
                {
                    if (trimmedAttribute.Length == 7) // #RRGGBB
                    {
                        int r = int.Parse(trimmedAttribute.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        int g = int.Parse(trimmedAttribute.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        int b = int.Parse(trimmedAttribute.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        color = Color.FromArgb(255, r, g, b);
                    }
                    else if (trimmedAttribute.Length == 4) // #RGB
                    {
                        string rHex = $"{trimmedAttribute[1]}{trimmedAttribute[1]}";
                        string gHex = $"{trimmedAttribute[2]}{trimmedAttribute[2]}";
                        string bHex = $"{trimmedAttribute[3]}{trimmedAttribute[3]}";
                        int r = int.Parse(rHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        int g = int.Parse(gHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        int b = int.Parse(bHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        color = Color.FromArgb(255, r, g, b);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Invalid hex color format '{trimmedAttribute}'. Defaulting to Transparent.");
                        color = Color.Transparent;
                    }
                }
                else if (trimmedAttribute.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
                {
                    var valuesString = trimmedAttribute.Substring(trimmedAttribute.IndexOf('(') + 1, trimmedAttribute.LastIndexOf(')') - trimmedAttribute.IndexOf('(') - 1);
                    var parts = valuesString.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length == 3)
                    {
                        int r = int.Parse(parts[0], CultureInfo.InvariantCulture);
                        int g = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        int b = int.Parse(parts[2], CultureInfo.InvariantCulture);
                        color = Color.FromArgb(255, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Invalid rgb color format '{trimmedAttribute}'. Defaulting to Transparent.");
                        color = Color.Transparent;
                    }
                }
                else if (trimmedAttribute.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
                {
                    var valuesString = trimmedAttribute.Substring(trimmedAttribute.IndexOf('(') + 1, trimmedAttribute.LastIndexOf(')') - trimmedAttribute.IndexOf('(') - 1);
                    var parts = valuesString.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length == 4)
                    {
                        int r = int.Parse(parts[0], CultureInfo.InvariantCulture);
                        int g = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        int b = int.Parse(parts[2], CultureInfo.InvariantCulture);
                        double alphaDouble = double.Parse(parts[3], CultureInfo.InvariantCulture);
                        int a = (int)Math.Round(Math.Clamp(alphaDouble, 0.0, 1.0) * 255.0);
                        color = Color.FromArgb(a, Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255));
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Invalid rgba color format '{trimmedAttribute}'. Defaulting to Transparent.");
                        color = Color.Transparent;
                    }
                }
                else if (NamedColorsMap.TryGetValue(trimmedAttribute, out Color namedColor))
                {
                    color = namedColor;
                }
                else
                {
                    Console.WriteLine($"Warning: Unknown color name or format '{trimmedAttribute}'. Defaulting to Transparent.");
                    color = Color.Transparent;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not parse color attribute '{attribute}': {ex.Message}. Defaulting to Transparent.");
            }
            return color;
        }
    }
}
