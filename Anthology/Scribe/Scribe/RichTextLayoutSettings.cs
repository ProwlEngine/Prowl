using Prowl.Vector;

namespace Prowl.Scribe
{
    /// <summary>
    /// Settings for <see cref="RichTextLayout"/>. Holds font set, default styling, wrap/alignment,
    /// and default per-effect parameters used when a tag omits an attribute.
    /// </summary>
    public class RichTextLayoutSettings
    {
        // --- Font set ---
        public FontFile RegularFont;
        public FontFile BoldFont;
        public FontFile ItalicFont;
        public FontFile BoldItalicFont;
        public FontFile MonoFont;

        // --- Base text styling ---
        public float PixelSize = 16f;
        public float LineHeight = 1.2f; // multiplier on the dominant line size
        public float LetterSpacing = 0f;
        public float WordSpacing = 0f;
        public int TabSize = 4;
        public FontColor DefaultColor = FontColor.White;

        // --- Wrap / alignment ---
        public float MaxWidth = 0f; // 0 = no limit
        public TextWrapMode WrapMode = TextWrapMode.NoWrap;
        public TextAlignment Alignment = TextAlignment.Left;

        /// <summary>
        /// Multiplier applied to absolute pixel-size tag values (e.g. <c>&lt;size=24&gt;</c>) at
        /// layout time. Lets host code keep tag values in logical units when the engine itself runs
        /// in physical pixels (e.g. for HiDPI). Default 1.
        /// Percent sizes (e.g. <c>&lt;size=200%&gt;</c>) are unaffected — they're already relative
        /// to <see cref="PixelSize"/>, which the host scales separately.
        /// </summary>
        public float AbsoluteSizeScale = 1f;

        // --- Default effect parameters (used when a tag omits the attribute) ---
        public float DefaultShakeAmp = 1.5f;
        public float DefaultShakeFreq = 22f;

        public float DefaultWaveAmp = 4f;
        public float DefaultWaveFreq = 5f;
        public float DefaultWavePhase = 0.45f; // radians per glyph

        public float DefaultRainbowSpeed = 1f;     // hue cycles per second
        public float DefaultRainbowSpread = 0.06f; // hue cycles per glyph
        public float DefaultRainbowSat = 1f;
        public float DefaultRainbowValue = 1f;

        public float DefaultPulseSpeed = 3f;
        public float DefaultPulseAmp = 0.18f; // relative scale amplitude

        public float DefaultFadeSpeed = 2f; // alpha cycles per second

        public float DefaultJitterAmp = 0.6f;
        public float DefaultJitterFreq = 30f;

        public float DefaultTypewriterSpeed = 28f; // glyphs per second
        public float DefaultTypewriterFadeIn = 0.08f; // seconds of alpha ramp per glyph

        public static RichTextLayoutSettings Default => new RichTextLayoutSettings();
    }
}
