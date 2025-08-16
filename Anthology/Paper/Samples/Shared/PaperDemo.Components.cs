using FontStashSharp;
using Prowl.PaperUI;
using Prowl.PaperUI.Extras;
using Prowl.Vector;

using System.Drawing;
using System.Reflection;
using NanoidDotNet;
using System.Data.Common;

namespace Shared.Components
{
    public static class Button
    {
        public static ElementBuilder Primary(string id, string text = "")
        {
            return Paper.Box("shadcs-button-" + id)
                .Text(Text.Center(text, Fonts.fontMedium, Themes.lightTextColor))
                .Style("button-primary");
        }
    }

    public static class Input
    {
        public static ElementBuilder Primary(string id, string value, Action<string> onChange = null, string placeholder = "")
        {
            return Paper.Box("shadcs-input-" + id)
                .TextField(value, Fonts.fontMedium, onChange, placeholder)
                .Style("text-field")
                .SetScroll(Scroll.ScrollX);
        }
    }
}
