using System;

namespace Prowl.Scribe
{
    readonly struct LayoutCacheKey : IEquatable<LayoutCacheKey>
    {
        public readonly string Text;
        public readonly int PxQ, LsQ, WsQ, LhQ, MwQ; // quantized floats
        public readonly int TabSize, FontId;        // stable id, not object ref
        public readonly TextWrapMode Wrap;
        public readonly TextAlignment Align;

        static int Q(float v, float stepTimes) => (int)MathF.Round(v * stepTimes);

        public LayoutCacheKey(
            string text, float pixelSize, float letterSpacing, float wordSpacing, float lineHeight,
            int tabSize, TextWrapMode wrap, TextAlignment align, float maxWidth, int fontId)
        {
            Text = text ?? string.Empty;
            PxQ = Q(pixelSize, 8f);  // ~1/8 px
            LsQ = Q(letterSpacing, 8f);
            WsQ = Q(wordSpacing, 8f);
            LhQ = Q(lineHeight, 8f);
            MwQ = Q(maxWidth, 4f);  // ~1/4 px
            TabSize = tabSize; Wrap = wrap; Align = align; FontId = fontId;
        }

        public bool Equals(LayoutCacheKey o) =>
            (ReferenceEquals(Text, o.Text) || (Text?.Equals(o.Text) ?? o.Text is null))
            && PxQ == o.PxQ && LsQ == o.LsQ && WsQ == o.WsQ && LhQ == o.LhQ && MwQ == o.MwQ
            && TabSize == o.TabSize && Wrap == o.Wrap && Align == o.Align && FontId == o.FontId;

        public override bool Equals(object obj) => obj is LayoutCacheKey o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Text?.GetHashCode() ?? 0;
                h = (h * 397) ^ PxQ; h = (h * 397) ^ LsQ; h = (h * 397) ^ WsQ; h = (h * 397) ^ LhQ; h = (h * 397) ^ MwQ;
                h = (h * 397) ^ TabSize; h = (h * 397) ^ (int)Wrap; h = (h * 397) ^ (int)Align; h = (h * 397) ^ FontId;
                return h;
            }
        }
    }
}
