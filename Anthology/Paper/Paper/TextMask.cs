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
    /// breaks are left alone so a field of several lines keeps its shape.
    /// </summary>
    internal static class TextMask
    {
        // One customizer per mask character, kept because Scribe's layout cache matches customizers
        // by identity, so a fresh delegate each frame would miss the cache every frame. Shared by
        // every Paper instance, which may live on different threads, hence the lock.
        private static readonly Dictionary<char, GlyphCustomizer> _masks = new Dictionary<char, GlyphCustomizer>();

        public static GlyphCustomizer For(char? mask)
        {
            if (!mask.HasValue) return null;

            lock (_masks)
            {
                if (!_masks.TryGetValue(mask.Value, out var customizer))
                    _masks[mask.Value] = customizer = Create(mask.Value);

                return customizer;
            }
        }

        // Separate so the closure is only allocated when a new mask is made, not on every lookup.
        private static GlyphCustomizer Create(char c) => (ref GlyphStyle g) =>
        {
            if (g.Codepoint != '\n' && g.Codepoint != '\r') g.Codepoint = c;
        };
    }
}
