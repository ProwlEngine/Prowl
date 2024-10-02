using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml.Linq;

namespace AssimpSharp
{
    public static class Extensions
    {
        public const float AI_MATH_TWO_PI = (float)(Math.PI * 2);
        public const float AI_MATH_TWO_PIf = (float)(Math.PI * 2);
        public const float AI_MATH_HALF_PI = (float)(Math.PI * 0.5);

        public static int AI_MAX_ALLOC(int size) => (256 * 1024 * 1024) / size;

        public static string Plus(this FileInfo file, string another) => Path.Combine(file.FullName, another);

        public static bool Exists(this Uri uri) => File.Exists(uri.LocalPath);

        public static string GetExtension(this Uri uri) => Path.GetExtension(uri.LocalPath).ToLowerInvariant();

        public static string S(this Uri uri) => uri.ToString();

        public static XElement[] ElementChildren(this XElement element) => element.Elements().ToArray();

        public static string GetAttributeOrNull(this XElement element, string attributeName)
            => element.Attribute(attributeName)?.Value;

        public static string[] Words(this string str) => str.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        public static bool IsNewLine(this char c) => c == '\n';

        public static bool IsNumeric(this char c) => char.IsDigit(c) || c == '-' || c == '+';
    }
}