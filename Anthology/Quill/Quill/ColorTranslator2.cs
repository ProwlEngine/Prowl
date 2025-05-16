// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Drawing
{
    /// <summary>
    /// Modified version of System.Drawing.ColorTranslator so .netstandard2.1 can use it
    /// </summary>
    public static class ColorTranslator2
    {
        // COLORREF is 0x00BBGGRR
        internal const int COLORREF_RedShift = 0;
        internal const int COLORREF_GreenShift = 8;
        internal const int COLORREF_BlueShift = 16;

        private static Dictionary<string, Color>? s_htmlSysColorTable;

        /// <summary>
        /// Translates an Html color representation to a GDI+ <see cref='Color'/>.
        /// </summary>
        public static Color FromHtml(string htmlColor)
        {
            Color c = Color.Empty;

            // empty color
            if ((htmlColor == null) || (htmlColor.Length == 0))
                return c;

            // #RRGGBB or #RGB
            if ((htmlColor[0] == '#') &&
                ((htmlColor.Length == 7) || (htmlColor.Length == 4)))
            {
                if (htmlColor.Length == 7)
                {
                    c = Color.FromArgb(Convert.ToInt32(htmlColor.Substring(1, 2), 16),
                                       Convert.ToInt32(htmlColor.Substring(3, 2), 16),
                                       Convert.ToInt32(htmlColor.Substring(5, 2), 16));
                }
                else
                {
                    string r = char.ToString(htmlColor[1]);
                    string g = char.ToString(htmlColor[2]);
                    string b = char.ToString(htmlColor[3]);

                    c = Color.FromArgb(Convert.ToInt32(r + r, 16),
                                       Convert.ToInt32(g + g, 16),
                                       Convert.ToInt32(b + b, 16));
                }
            }

            // special case. Html requires LightGrey, but .NET uses LightGray
            if (c.IsEmpty && string.Equals(htmlColor, "LightGrey", StringComparison.OrdinalIgnoreCase))
            {
                c = Color.LightGray;
            }

            // System color
            if (c.IsEmpty)
            {
                if (s_htmlSysColorTable == null)
                {
                    InitializeHtmlSysColorTable();
                }

                s_htmlSysColorTable!.TryGetValue(htmlColor.ToLowerInvariant(), out c);
            }

            return c;
        }

        private static void InitializeHtmlSysColorTable()
        {
            s_htmlSysColorTable = new Dictionary<string, Color>(27)
            {
                ["activeborder"] = Color.FromKnownColor(KnownColor.ActiveBorder),
                ["activecaption"] = Color.FromKnownColor(KnownColor.ActiveCaption),
                ["appworkspace"] = Color.FromKnownColor(KnownColor.AppWorkspace),
                ["background"] = Color.FromKnownColor(KnownColor.Desktop),
                ["buttonface"] = Color.FromKnownColor(KnownColor.Control),
                ["buttonhighlight"] = Color.FromKnownColor(KnownColor.ControlLightLight),
                ["buttonshadow"] = Color.FromKnownColor(KnownColor.ControlDark),
                ["buttontext"] = Color.FromKnownColor(KnownColor.ControlText),
                ["captiontext"] = Color.FromKnownColor(KnownColor.ActiveCaptionText),
                ["graytext"] = Color.FromKnownColor(KnownColor.GrayText),
                ["highlight"] = Color.FromKnownColor(KnownColor.Highlight),
                ["highlighttext"] = Color.FromKnownColor(KnownColor.HighlightText),
                ["inactiveborder"] = Color.FromKnownColor(KnownColor.InactiveBorder),
                ["inactivecaption"] = Color.FromKnownColor(KnownColor.InactiveCaption),
                ["inactivecaptiontext"] = Color.FromKnownColor(KnownColor.InactiveCaptionText),
                ["infobackground"] = Color.FromKnownColor(KnownColor.Info),
                ["infotext"] = Color.FromKnownColor(KnownColor.InfoText),
                ["menu"] = Color.FromKnownColor(KnownColor.Menu),
                ["menutext"] = Color.FromKnownColor(KnownColor.MenuText),
                ["scrollbar"] = Color.FromKnownColor(KnownColor.ScrollBar),
                ["threeddarkshadow"] = Color.FromKnownColor(KnownColor.ControlDarkDark),
                ["threedface"] = Color.FromKnownColor(KnownColor.Control),
                ["threedhighlight"] = Color.FromKnownColor(KnownColor.ControlLight),
                ["threedlightshadow"] = Color.FromKnownColor(KnownColor.ControlLightLight),
                ["window"] = Color.FromKnownColor(KnownColor.Window),
                ["windowframe"] = Color.FromKnownColor(KnownColor.WindowFrame),
                ["windowtext"] = Color.FromKnownColor(KnownColor.WindowText)
            };
        }
    }
}
