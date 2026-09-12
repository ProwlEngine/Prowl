using System.Collections.Generic;

using Prowl.Scribe;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Masked text, for password fields and anything else that should be readable as shape but not
    /// as content.
    ///
    /// The mask is a Scribe <see cref="GlyphCustomizer"/> rather than a second, masked copy of the
    /// string: the layout still holds the real text, so cursor positions, selection and hit testing
    /// all keep working on what the user actually typed, and nothing is allocated per frame. Line
    /// breaks are left alone so a multi-line field keeps its shape.
    /// </summary>
    internal static class TextMask
    {
        // One customizer per mask character, kept because Scribe's layout cache matches customizers
        // by identity: a fresh delegate each frame would miss the cache every frame.
        private static readonly Dictionary<char, GlyphCustomizer> _masks = new Dictionary<char, GlyphCustomizer>();

        public static GlyphCustomizer For(char? mask)
        {
            if (!mask.HasValue) return null;

            char c = mask.Value;
            if (!_masks.TryGetValue(c, out var customizer))
            {
                customizer = (ref GlyphStyle g) =>
                {
                    if (g.Codepoint != '\n' && g.Codepoint != '\r') g.Codepoint = c;
                };
                _masks[c] = customizer;
            }

            return customizer;
        }
    }
}
