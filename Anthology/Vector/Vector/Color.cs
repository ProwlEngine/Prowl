// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace Prowl.Vector
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Color : IEquatable<Color>
    {
        #region Predefined Colors

        /// <summary>Gets a color representing <c>AliceBlue</c></summary>
        public static Color AliceBlue => new Color(0.941176534f, 0.972549081f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>AntiqueWhite</c></summary>
        public static Color AntiqueWhite => new Color(0.980392218f, 0.921568692f, 0.843137324f, 1.000000000f);

        /// <summary>Gets a color representing <c>Aqua</c></summary>
        public static Color Aqua => new Color(0.000000000f, 1.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Aquamarine</c></summary>
        public static Color Aquamarine => new Color(0.498039246f, 1.000000000f, 0.831372619f, 1.000000000f);

        /// <summary>Gets a color representing <c>Azure</c></summary>
        public static Color Azure => new Color(0.941176534f, 1.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Beige</c></summary>
        public static Color Beige => new Color(0.960784376f, 0.960784376f, 0.862745166f, 1.000000000f);

        /// <summary>Gets a color representing <c>Bisque</c></summary>
        public static Color Bisque => new Color(1.000000000f, 0.894117713f, 0.768627524f, 1.000000000f);

        /// <summary>Gets a color representing <c>Black</c></summary>
        public static Color Black => new Color(0.000000000f, 0.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>BlanchedAlmond</c></summary>
        public static Color BlanchedAlmond => new Color(1.000000000f, 0.921568692f, 0.803921640f, 1.000000000f);

        /// <summary>Gets a color representing <c>Blue</c></summary>
        public static Color Blue => new Color(0.000000000f, 0.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>BlueViolet</c></summary>
        public static Color BlueViolet => new Color(0.541176498f, 0.168627456f, 0.886274576f, 1.000000000f);

        /// <summary>Gets a color representing <c>Brown</c></summary>
        public static Color Brown => new Color(0.647058845f, 0.164705887f, 0.164705887f, 1.000000000f);

        /// <summary>Gets a color representing <c>BurlyWood</c></summary>
        public static Color BurlyWood => new Color(0.870588303f, 0.721568644f, 0.529411793f, 1.000000000f);

        /// <summary>Gets a color representing <c>CadetBlue</c></summary>
        public static Color CadetBlue => new Color(0.372549027f, 0.619607866f, 0.627451003f, 1.000000000f);

        /// <summary>Gets a color representing <c>Chartreuse</c></summary>
        public static Color Chartreuse => new Color(0.498039246f, 1.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Chocolate</c></summary>
        public static Color Chocolate => new Color(0.823529482f, 0.411764741f, 0.117647067f, 1.000000000f);

        /// <summary>Gets a color representing <c>Coral</c></summary>
        public static Color Coral => new Color(1.000000000f, 0.498039246f, 0.313725501f, 1.000000000f);

        /// <summary>Gets a color representing <c>CornflowerBlue</c></summary>
        public static Color CornflowerBlue => new Color(0.392156899f, 0.584313750f, 0.929411829f, 1.000000000f);

        /// <summary>Gets a color representing <c>Cornsilk</c></summary>
        public static Color Cornsilk => new Color(1.000000000f, 0.972549081f, 0.862745166f, 1.000000000f);

        /// <summary>Gets a color representing <c>Crimson</c></summary>
        public static Color Crimson => new Color(0.862745166f, 0.078431375f, 0.235294133f, 1.000000000f);

        /// <summary>Gets a color representing <c>Cyan</c></summary>
        public static Color Cyan => new Color(0.000000000f, 1.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkBlue</c></summary>
        public static Color DarkBlue => new Color(0.000000000f, 0.000000000f, 0.545098066f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkCyan</c></summary>
        public static Color DarkCyan => new Color(0.000000000f, 0.545098066f, 0.545098066f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkGoldenrod</c></summary>
        public static Color DarkGoldenrod => new Color(0.721568644f, 0.525490224f, 0.043137256f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkGray</c></summary>
        public static Color DarkGray => new Color(0.662745118f, 0.662745118f, 0.662745118f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkGreen</c></summary>
        public static Color DarkGreen => new Color(0.000000000f, 0.392156899f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkKhaki</c></summary>
        public static Color DarkKhaki => new Color(0.741176486f, 0.717647076f, 0.419607878f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkMagenta</c></summary>
        public static Color DarkMagenta => new Color(0.545098066f, 0.000000000f, 0.545098066f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkOliveGreen</c></summary>
        public static Color DarkOliveGreen => new Color(0.333333343f, 0.419607878f, 0.184313729f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkOrange</c></summary>
        public static Color DarkOrange => new Color(1.000000000f, 0.549019635f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkOrchid</c></summary>
        public static Color DarkOrchid => new Color(0.600000024f, 0.196078449f, 0.800000072f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkRed</c></summary>
        public static Color DarkRed => new Color(0.545098066f, 0.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkSalmon</c></summary>
        public static Color DarkSalmon => new Color(0.913725555f, 0.588235319f, 0.478431404f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkSeaGreen</c></summary>
        public static Color DarkSeaGreen => new Color(0.560784340f, 0.737254918f, 0.545098066f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkSlateBlue</c></summary>
        public static Color DarkSlateBlue => new Color(0.282352954f, 0.239215702f, 0.545098066f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkSlateGray</c></summary>
        public static Color DarkSlateGray => new Color(0.184313729f, 0.309803933f, 0.309803933f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkTurquoise</c></summary>
        public static Color DarkTurquoise => new Color(0.000000000f, 0.807843208f, 0.819607913f, 1.000000000f);

        /// <summary>Gets a color representing <c>DarkViolet</c></summary>
        public static Color DarkViolet => new Color(0.580392182f, 0.000000000f, 0.827451050f, 1.000000000f);

        /// <summary>Gets a color representing <c>DeepPink</c></summary>
        public static Color DeepPink => new Color(1.000000000f, 0.078431375f, 0.576470613f, 1.000000000f);

        /// <summary>Gets a color representing <c>DeepSkyBlue</c></summary>
        public static Color DeepSkyBlue => new Color(0.000000000f, 0.749019623f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>DimGray</c></summary>
        public static Color DimGray => new Color(0.411764741f, 0.411764741f, 0.411764741f, 1.000000000f);

        /// <summary>Gets a color representing <c>DodgerBlue</c></summary>
        public static Color DodgerBlue => new Color(0.117647067f, 0.564705908f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Firebrick</c></summary>
        public static Color Firebrick => new Color(0.698039234f, 0.133333340f, 0.133333340f, 1.000000000f);

        /// <summary>Gets a color representing <c>FloralWhite</c></summary>
        public static Color FloralWhite => new Color(1.000000000f, 0.980392218f, 0.941176534f, 1.000000000f);

        /// <summary>Gets a color representing <c>ForestGreen</c></summary>
        public static Color ForestGreen => new Color(0.133333340f, 0.545098066f, 0.133333340f, 1.000000000f);

        /// <summary>Gets a color representing <c>Fuchsia</c></summary>
        public static Color Fuchsia => new Color(1.000000000f, 0.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Gainsboro</c></summary>
        public static Color Gainsboro => new Color(0.862745166f, 0.862745166f, 0.862745166f, 1.000000000f);

        /// <summary>Gets a color representing <c>GhostWhite</c></summary>
        public static Color GhostWhite => new Color(0.972549081f, 0.972549081f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Gold</c></summary>
        public static Color Gold => new Color(1.000000000f, 0.843137324f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Goldenrod</c></summary>
        public static Color Goldenrod => new Color(0.854902029f, 0.647058845f, 0.125490203f, 1.000000000f);

        /// <summary>Gets a color representing <c>Gray</c></summary>
        public static Color Gray => new Color(0.501960814f, 0.501960814f, 0.501960814f, 1.000000000f);

        /// <summary>Gets a color representing <c>Green</c></summary>
        public static Color Green => new Color(0.000000000f, 0.501960814f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>GreenYellow</c></summary>
        public static Color GreenYellow => new Color(0.678431392f, 1.000000000f, 0.184313729f, 1.000000000f);

        /// <summary>Gets a color representing <c>Honeydew</c></summary>
        public static Color Honeydew => new Color(0.941176534f, 1.000000000f, 0.941176534f, 1.000000000f);

        /// <summary>Gets a color representing <c>HotPink</c></summary>
        public static Color HotPink => new Color(1.000000000f, 0.411764741f, 0.705882370f, 1.000000000f);

        /// <summary>Gets a color representing <c>IndianRed</c></summary>
        public static Color IndianRed => new Color(0.803921640f, 0.360784322f, 0.360784322f, 1.000000000f);

        /// <summary>Gets a color representing <c>Indigo</c></summary>
        public static Color Indigo => new Color(0.294117659f, 0.000000000f, 0.509803951f, 1.000000000f);

        /// <summary>Gets a color representing <c>Ivory</c></summary>
        public static Color Ivory => new Color(1.000000000f, 1.000000000f, 0.941176534f, 1.000000000f);

        /// <summary>Gets a color representing <c>Khaki</c></summary>
        public static Color Khaki => new Color(0.941176534f, 0.901960850f, 0.549019635f, 1.000000000f);

        /// <summary>Gets a color representing <c>Lavender</c></summary>
        public static Color Lavender => new Color(0.901960850f, 0.901960850f, 0.980392218f, 1.000000000f);

        /// <summary>Gets a color representing <c>LavenderBlush</c></summary>
        public static Color LavenderBlush => new Color(1.000000000f, 0.941176534f, 0.960784376f, 1.000000000f);

        /// <summary>Gets a color representing <c>LawnGreen</c></summary>
        public static Color LawnGreen => new Color(0.486274540f, 0.988235354f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>LemonChiffon</c></summary>
        public static Color LemonChiffon => new Color(1.000000000f, 0.980392218f, 0.803921640f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightBlue</c></summary>
        public static Color LightBlue => new Color(0.678431392f, 0.847058892f, 0.901960850f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightCoral</c></summary>
        public static Color LightCoral => new Color(0.941176534f, 0.501960814f, 0.501960814f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightCyan</c></summary>
        public static Color LightCyan => new Color(0.878431439f, 1.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightGoldenrodYellow</c></summary>
        public static Color LightGoldenrodYellow => new Color(0.980392218f, 0.980392218f, 0.823529482f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightGreen</c></summary>
        public static Color LightGreen => new Color(0.564705908f, 0.933333397f, 0.564705908f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightGray</c></summary>
        public static Color LightGray => new Color(0.827451050f, 0.827451050f, 0.827451050f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightPink</c></summary>
        public static Color LightPink => new Color(1.000000000f, 0.713725507f, 0.756862819f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightSalmon</c></summary>
        public static Color LightSalmon => new Color(1.000000000f, 0.627451003f, 0.478431404f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightSeaGreen</c></summary>
        public static Color LightSeaGreen => new Color(0.125490203f, 0.698039234f, 0.666666687f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightSkyBlue</c></summary>
        public static Color LightSkyBlue => new Color(0.529411793f, 0.807843208f, 0.980392218f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightSlateGray</c></summary>
        public static Color LightSlateGray => new Color(0.466666698f, 0.533333361f, 0.600000024f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightSteelBlue</c></summary>
        public static Color LightSteelBlue => new Color(0.690196097f, 0.768627524f, 0.870588303f, 1.000000000f);

        /// <summary>Gets a color representing <c>LightYellow</c></summary>
        public static Color LightYellow => new Color(1.000000000f, 1.000000000f, 0.878431439f, 1.000000000f);

        /// <summary>Gets a color representing <c>Lime</c></summary>
        public static Color Lime => new Color(0.000000000f, 1.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>LimeGreen</c></summary>
        public static Color LimeGreen => new Color(0.196078449f, 0.803921640f, 0.196078449f, 1.000000000f);

        /// <summary>Gets a color representing <c>Linen</c></summary>
        public static Color Linen => new Color(0.980392218f, 0.941176534f, 0.901960850f, 1.000000000f);

        /// <summary>Gets a color representing <c>Magenta</c></summary>
        public static Color Magenta => new Color(1.000000000f, 0.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Maroon</c></summary>
        public static Color Maroon => new Color(0.501960814f, 0.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumAquamarine</c></summary>
        public static Color MediumAquamarine => new Color(0.400000036f, 0.803921640f, 0.666666687f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumBlue</c></summary>
        public static Color MediumBlue => new Color(0.000000000f, 0.000000000f, 0.803921640f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumOrchid</c></summary>
        public static Color MediumOrchid => new Color(0.729411781f, 0.333333343f, 0.827451050f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumPurple</c></summary>
        public static Color MediumPurple => new Color(0.576470613f, 0.439215720f, 0.858823597f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumSeaGreen</c></summary>
        public static Color MediumSeaGreen => new Color(0.235294133f, 0.701960802f, 0.443137288f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumSlateBlue</c></summary>
        public static Color MediumSlateBlue => new Color(0.482352972f, 0.407843173f, 0.933333397f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumSpringGreen</c></summary>
        public static Color MediumSpringGreen => new Color(0.000000000f, 0.980392218f, 0.603921592f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumTurquoise</c></summary>
        public static Color MediumTurquoise => new Color(0.282352954f, 0.819607913f, 0.800000072f, 1.000000000f);

        /// <summary>Gets a color representing <c>MediumVioletRed</c></summary>
        public static Color MediumVioletRed => new Color(0.780392230f, 0.082352944f, 0.521568656f, 1.000000000f);

        /// <summary>Gets a color representing <c>MidnightBlue</c></summary>
        public static Color MidnightBlue => new Color(0.098039225f, 0.098039225f, 0.439215720f, 1.000000000f);

        /// <summary>Gets a color representing <c>MintCream</c></summary>
        public static Color MintCream => new Color(0.960784376f, 1.000000000f, 0.980392218f, 1.000000000f);

        /// <summary>Gets a color representing <c>MistyRose</c></summary>
        public static Color MistyRose => new Color(1.000000000f, 0.894117713f, 0.882353008f, 1.000000000f);

        /// <summary>Gets a color representing <c>Moccasin</c></summary>
        public static Color Moccasin => new Color(1.000000000f, 0.894117713f, 0.709803939f, 1.000000000f);

        /// <summary>Gets a color representing <c>NavajoWhite</c></summary>
        public static Color NavajoWhite => new Color(1.000000000f, 0.870588303f, 0.678431392f, 1.000000000f);

        /// <summary>Gets a color representing <c>Navy</c></summary>
        public static Color Navy => new Color(0.000000000f, 0.000000000f, 0.501960814f, 1.000000000f);

        /// <summary>Gets a color representing <c>OldLace</c></summary>
        public static Color OldLace => new Color(0.992156923f, 0.960784376f, 0.901960850f, 1.000000000f);

        /// <summary>Gets a color representing <c>Olive</c></summary>
        public static Color Olive => new Color(0.501960814f, 0.501960814f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>OliveDrab</c></summary>
        public static Color OliveDrab => new Color(0.419607878f, 0.556862772f, 0.137254909f, 1.000000000f);

        /// <summary>Gets a color representing <c>Orange</c></summary>
        public static Color Orange => new Color(1.000000000f, 0.647058845f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>OrangeRed</c></summary>
        public static Color OrangeRed => new Color(1.000000000f, 0.270588249f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>Orchid</c></summary>
        public static Color Orchid => new Color(0.854902029f, 0.439215720f, 0.839215755f, 1.000000000f);

        /// <summary>Gets a color representing <c>PaleGoldenrod</c></summary>
        public static Color PaleGoldenrod => new Color(0.933333397f, 0.909803987f, 0.666666687f, 1.000000000f);

        /// <summary>Gets a color representing <c>PaleGreen</c></summary>
        public static Color PaleGreen => new Color(0.596078455f, 0.984313786f, 0.596078455f, 1.000000000f);

        /// <summary>Gets a color representing <c>PaleTurquoise</c></summary>
        public static Color PaleTurquoise => new Color(0.686274529f, 0.933333397f, 0.933333397f, 1.000000000f);

        /// <summary>Gets a color representing <c>PaleVioletRed</c></summary>
        public static Color PaleVioletRed => new Color(0.858823597f, 0.439215720f, 0.576470613f, 1.000000000f);

        /// <summary>Gets a color representing <c>PapayaWhip</c></summary>
        public static Color PapayaWhip => new Color(1.000000000f, 0.937254965f, 0.835294187f, 1.000000000f);

        /// <summary>Gets a color representing <c>PeachPuff</c></summary>
        public static Color PeachPuff => new Color(1.000000000f, 0.854902029f, 0.725490212f, 1.000000000f);

        /// <summary>Gets a color representing <c>Peru</c></summary>
        public static Color Peru => new Color(0.803921640f, 0.521568656f, 0.247058839f, 1.000000000f);

        /// <summary>Gets a color representing <c>Pink</c></summary>
        public static Color Pink => new Color(1.000000000f, 0.752941251f, 0.796078503f, 1.000000000f);

        /// <summary>Gets a color representing <c>Plum</c></summary>
        public static Color Plum => new Color(0.866666734f, 0.627451003f, 0.866666734f, 1.000000000f);

        /// <summary>Gets a color representing <c>PowderBlue</c></summary>
        public static Color PowderBlue => new Color(0.690196097f, 0.878431439f, 0.901960850f, 1.000000000f);

        /// <summary>Gets a color representing <c>Purple</c></summary>
        public static Color Purple => new Color(0.501960814f, 0.000000000f, 0.501960814f, 1.000000000f);

        /// <summary>Gets a color representing <c>Red</c></summary>
        public static Color Red => new Color(1.000000000f, 0.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>RosyBrown</c></summary>
        public static Color RosyBrown => new Color(0.737254918f, 0.560784340f, 0.560784340f, 1.000000000f);

        /// <summary>Gets a color representing <c>RoyalBlue</c></summary>
        public static Color RoyalBlue => new Color(0.254901975f, 0.411764741f, 0.882353008f, 1.000000000f);

        /// <summary>Gets a color representing <c>SaddleBrown</c></summary>
        public static Color SaddleBrown => new Color(0.545098066f, 0.270588249f, 0.074509807f, 1.000000000f);

        /// <summary>Gets a color representing <c>Salmon</c></summary>
        public static Color Salmon => new Color(0.980392218f, 0.501960814f, 0.447058856f, 1.000000000f);

        /// <summary>Gets a color representing <c>SandyBrown</c></summary>
        public static Color SandyBrown => new Color(0.956862807f, 0.643137276f, 0.376470625f, 1.000000000f);

        /// <summary>Gets a color representing <c>SeaGreen</c></summary>
        public static Color SeaGreen => new Color(0.180392161f, 0.545098066f, 0.341176480f, 1.000000000f);

        /// <summary>Gets a color representing <c>SeaShell</c></summary>
        public static Color SeaShell => new Color(1.000000000f, 0.960784376f, 0.933333397f, 1.000000000f);

        /// <summary>Gets a color representing <c>Sienna</c></summary>
        public static Color Sienna => new Color(0.627451003f, 0.321568638f, 0.176470593f, 1.000000000f);

        /// <summary>Gets a color representing <c>Silver</c></summary>
        public static Color Silver => new Color(0.752941251f, 0.752941251f, 0.752941251f, 1.000000000f);

        /// <summary>Gets a color representing <c>SkyBlue</c></summary>
        public static Color SkyBlue => new Color(0.529411793f, 0.807843208f, 0.921568692f, 1.000000000f);

        /// <summary>Gets a color representing <c>SlateBlue</c></summary>
        public static Color SlateBlue => new Color(0.415686309f, 0.352941185f, 0.803921640f, 1.000000000f);

        /// <summary>Gets a color representing <c>SlateGray</c></summary>
        public static Color SlateGray => new Color(0.439215720f, 0.501960814f, 0.564705908f, 1.000000000f);

        /// <summary>Gets a color representing <c>Snow</c></summary>
        public static Color Snow => new Color(1.000000000f, 0.980392218f, 0.980392218f, 1.000000000f);

        /// <summary>Gets a color representing <c>SpringGreen</c></summary>
        public static Color SpringGreen => new Color(0.000000000f, 1.000000000f, 0.498039246f, 1.000000000f);

        /// <summary>Gets a color representing <c>SteelBlue</c></summary>
        public static Color SteelBlue => new Color(0.274509817f, 0.509803951f, 0.705882370f, 1.000000000f);

        /// <summary>Gets a color representing <c>Tan</c></summary>
        public static Color Tan => new Color(0.823529482f, 0.705882370f, 0.549019635f, 1.000000000f);

        /// <summary>Gets a color representing <c>Teal</c></summary>
        public static Color Teal => new Color(0.000000000f, 0.501960814f, 0.501960814f, 1.000000000f);

        /// <summary>Gets a color representing <c>Thistle</c></summary>
        public static Color Thistle => new Color(0.847058892f, 0.749019623f, 0.847058892f, 1.000000000f);

        /// <summary>Gets a color representing <c>Tomato</c></summary>
        public static Color Tomato => new Color(1.000000000f, 0.388235331f, 0.278431386f, 1.000000000f);

        /// <summary>Gets a color representing <c>Transparent</c></summary>
        public static Color Transparent => new Color(0.000000000f, 0.000000000f, 0.000000000f, 0.000000000f);

        /// <summary>Gets a color representing <c>Turquoise</c></summary>
        public static Color Turquoise => new Color(0.250980407f, 0.878431439f, 0.815686345f, 1.000000000f);

        /// <summary>Gets a color representing <c>Violet</c></summary>
        public static Color Violet => new Color(0.933333397f, 0.509803951f, 0.933333397f, 1.000000000f);

        /// <summary>Gets a color representing <c>Wheat</c></summary>
        public static Color Wheat => new Color(0.960784376f, 0.870588303f, 0.701960802f, 1.000000000f);

        /// <summary>Gets a color representing <c>White</c></summary>
        public static Color White => new Color(1.000000000f, 1.000000000f, 1.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>WhiteSmoke</c></summary>
        public static Color WhiteSmoke => new Color(0.960784376f, 0.960784376f, 0.960784376f, 1.000000000f);

        /// <summary>Gets a color representing <c>Yellow</c></summary>
        public static Color Yellow => new Color(1.000000000f, 1.000000000f, 0.000000000f, 1.000000000f);

        /// <summary>Gets a color representing <c>YellowGreen</c></summary>
        public static Color YellowGreen => new Color(0.603921592f, 0.803921640f, 0.196078449f, 1.000000000f);

        #endregion

        public double R, G, B, A;

        public double Grayscale => 0.299 * R + 0.587 * G + 0.114 * B;

        public double this[int index]
        {
            get
            {
                return index switch
                {
                    0 => R,
                    1 => G,
                    2 => B,
                    3 => A,
                    _ => throw new IndexOutOfRangeException("Invalid Color index.")
                };

            }
            set
            {
                _ = index switch
                {
                    0 => R = value,
                    1 => G = value,
                    2 => B = value,
                    3 => A = value,
                    _ => throw new IndexOutOfRangeException("Invalid Color index.")
                };
            }
        }

        public Color(double r, double g, double b, double a)
        {
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }

        public Color(double r, double g, double b)
        {
            this.R = r;
            this.G = g;
            this.B = b;
            A = 1f;
        }

        public Color(byte r, byte g, byte b, byte a)
        {
            this.R = r / 255f;
            this.G = g / 255f;
            this.B = b / 255f;
            this.A = a / 255f;
        }

        public Color(byte r, byte g, byte b)
        {
            this.R = r / 255f;
            this.G = g / 255f;
            this.B = b / 255f;
            A = 1f;
        }

        public uint GetUInt() => ((Color32)this).GetUInt();

        #region Static Functions

        public static Color Lerp(Color a, Color b, double t)
        {
            t = Maths.Min(Maths.Max(t, 0), 1);
            return new Color(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
        }

        public static bool IsGrayscale(Color color)
        {
            return color.R == color.G && color.G == color.B;
        }

        /// <summary>Performs color-space aware linear interpolation between two RGB colors.</summary>
        public static Color LerpColorSpace(Color from, Color to, double t)
        {
            // Convert to HSV for more natural color interpolation
            var hsvFrom = RGBToHSV(from);
            var hsvTo = RGBToHSV(to);

            // Handle hue wrapping for shortest path interpolation
            double deltaHue = hsvTo.X - hsvFrom.X;
            if (deltaHue > 180f)
                hsvFrom.X += 360f;
            else if (deltaHue < -180f)
                hsvTo.X += 360f;

            // Interpolate in HSV space
            var hsvResult = new Double4(
                hsvFrom.X + (hsvTo.X - hsvFrom.X) * t,
                hsvFrom.Y + (hsvTo.Y - hsvFrom.Y) * t,
                hsvFrom.Z + (hsvTo.Z - hsvFrom.Z) * t,
                hsvFrom.W + (hsvTo.W - hsvFrom.W) * t
            );

            // Wrap hue back to 0-360 range
            if (hsvResult.X < 0f) hsvResult.X += 360f;
            if (hsvResult.X >= 360f) hsvResult.X -= 360f;

            // Convert back to RGB
            return HSVToRGB(hsvResult);
        }

        /// <summary>Adjusts the brightness of a color.</summary>
        public static Color Brightness(Color color, double brightness)
        {
            return new Color(
                Maths.Max(0f, color.R * brightness),
                Maths.Max(0f, color.G * brightness),
                Maths.Max(0f, color.B * brightness), color.A
            );
        }

        /// <summary>Adjusts the contrast of a color.</summary>
        public static Color Contrast(Color color, double contrast)
        {
            const double midpoint = 0.5f;
            return new Color(
                Maths.Clamp((color.R - midpoint) * contrast + midpoint, 0f, 1f),
                Maths.Clamp((color.G - midpoint) * contrast + midpoint, 0f, 1f),
                Maths.Clamp((color.B - midpoint) * contrast + midpoint, 0f, 1f), color.A
            );
        }


        /// <summary>Desaturates a color by blending it towards grayscale.</summary>
        public static Color Desaturate(Color color, double amount = 1f)
        {
            // Standard luminance weights for RGB
            double luminance = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
            var gray = new Color(luminance, luminance, luminance, color.A);
            amount = Maths.Clamp(amount, 0f, 1f);
            return color + (gray - color) * amount;
        }

        /// <summary>Applies gamma correction to a color.</summary>
        public static Color Gamma(Color color, double gamma = 2.2f)
        {
            return new Color(
                Maths.Pow(Maths.Max(0f, color.R), 1f / gamma),
                Maths.Pow(Maths.Max(0f, color.G), 1f / gamma),
                Maths.Pow(Maths.Max(0f, color.B), 1f / gamma), color.A
            );
        }

        /// <summary>Converts a gamma space color to linear space.</summary>
        public static Color GammaToLinear(Color gamma)
        {
            return new Color(
                Maths.Pow(Maths.Max(0f, gamma.R), 2.2f),
                Maths.Pow(Maths.Max(0f, gamma.G), 2.2f),
                Maths.Pow(Maths.Max(0f, gamma.B), 2.2f), gamma.A
            );
        }

        /// <summary>Converts a linear color to gamma space (sRGB).</summary>
        public static Color LinearToGamma(Color linear)
        {
            return Gamma(linear, 2.2f);
        }

        /// <summary>Converts HSL to RGB color space.</summary>
        public static Color HSLToRGB(Color hsla)
        {
            double h = hsla.R, s = hsla.G, l = hsla.B;

            if (s <= 0f)
            {
                double gray = l;
                return new Color(gray, gray, gray, hsla.A);
            }

            double c = (1f - Maths.Abs(2f * l - 1f)) * s;
            double x = c * (1f - Maths.Abs((h / 60f) % 2f - 1f));
            double m = l - c / 2f;

            double r = 0f, g = 0f, b = 0f;

            if (h >= 0f && h < 60f)
                (r, g, b) = (c, x, 0f);
            else if (h >= 60f && h < 120f)
                (r, g, b) = (x, c, 0f);
            else if (h >= 120f && h < 180f)
                (r, g, b) = (0f, c, x);
            else if (h >= 180f && h < 240f)
                (r, g, b) = (0f, x, c);
            else if (h >= 240f && h < 300f)
                (r, g, b) = (x, 0f, c);
            else if (h >= 300f && h < 360f)
                (r, g, b) = (c, 0f, x);

            r += m; g += m; b += m;

            return new Color(r, g, b, hsla.A);
        }

        /// <summary>Converts RGB to HSL color space.</summary>
        public static Double4 RGBToHSL(Color rgba)
        {
            double r = rgba.R, g = rgba.G, b = rgba.B;
            double max = Maths.Max(r, Maths.Max(g, b));
            double min = Maths.Min(r, Maths.Min(g, b));
            double delta = max - min;

            double h = 0f, s = 0f, l = (max + min) / 2f;

            if (delta > 0f)
            {
                s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);

                if (max == r)
                    h = 60f * (((g - b) / delta) % 6f);
                else if (max == g)
                    h = 60f * ((b - r) / delta + 2f);
                else if (max == b)
                    h = 60f * ((r - g) / delta + 4f);

                if (h < 0f) h += 360f;
            }

            return new Double4(h, s, l, rgba.A);
        }

        /// <summary>Converts HSV to RGB color space.</summary>
        public static Color HSVToRGB(Color hsva)
        {
            double h = hsva.R, s = hsva.G, v = hsva.B;

            if (s <= 0f)
            {
                double gray = v;
                return new Color(gray, gray, gray, hsva.A);
            }

            double c = v * s;
            double x = c * (1f - Maths.Abs((h / 60f) % 2f - 1f));
            double m = v - c;

            double r = 0f, g = 0f, b = 0f;

            if (h >= 0f && h < 60f)
                (r, g, b) = (c, x, 0f);
            else if (h >= 60f && h < 120f)
                (r, g, b) = (x, c, 0f);
            else if (h >= 120f && h < 180f)
                (r, g, b) = (0f, c, x);
            else if (h >= 180f && h < 240f)
                (r, g, b) = (0f, x, c);
            else if (h >= 240f && h < 300f)
                (r, g, b) = (x, 0f, c);
            else if (h >= 300f && h < 360f)
                (r, g, b) = (c, 0f, x);

            r += m; g += m; b += m;

            return new Color(r, g, b, hsva.A);
        }

        /// <summary>Converts RGB to HSV color space.</summary>
        public static Double4 RGBToHSV(Color rgba)
        {
            double r = rgba.R, g = rgba.G, b = rgba.B;
            double max = Maths.Max(r, Maths.Max(g, b));
            double min = Maths.Min(r, Maths.Min(g, b));
            double delta = max - min;

            double h = 0f, s = 0f, v = max;

            if (delta > 0f)
            {
                s = delta / max;

                if (max == r)
                    h = 60f * (((g - b) / delta) % 6f);
                else if (max == g)
                    h = 60f * ((b - r) / delta + 2f);
                else if (max == b)
                    h = 60f * ((r - g) / delta + 4f);

                if (h < 0f) h += 360f;
            }

            return new Double4(h, s, v, rgba.A);
        }

        #endregion

        public static implicit operator Double4(Color c) => new Double4(c.R, c.G, c.B, c.A);
        public static implicit operator System.Numerics.Vector4(Color c) => new System.Numerics.Vector4((float)c.R, (float)c.G, (float)c.B, (float)c.A);
        public static implicit operator System.Drawing.Color(Color c) => System.Drawing.Color.FromArgb((int)(c.A * 255), (int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));

        public static implicit operator Color(Double4 v) => new Color(v.X, v.Y, v.Z, v.W);
        public static implicit operator Color(System.Numerics.Vector4 v) => new Color(v.X, v.Y, v.Z, v.W);
        public static implicit operator Color(System.Drawing.Color c) => new Color(c.R, c.G, c.B, c.A);

        public static Color operator +(Color a, double b) => new Color(a.R + b, a.G + b, a.B + b, a.A + b);
        public static Color operator -(Color a, double b) => new Color(a.R - b, a.G - b, a.B - b, a.A - b);
        public static Color operator *(Color a, double b) => new Color(a.R * b, a.G * b, a.B * b, a.A * b);
        public static Color operator /(Color a, double b) => new Color(a.R / b, a.G / b, a.B / b, a.A / b);

        public static Color operator +(Color a, Color b) => new Color(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);
        public static Color operator -(Color a, Color b) => new Color(a.R - b.R, a.G - b.G, a.B - b.B, a.A - b.A);
        public static Color operator *(Color a, Color b) => new Color(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);
        public static Color operator /(Color a, Color b) => new Color(a.R / b.R, a.G / b.G, a.B / b.B, a.A / b.A);

        #region Equals and HashCode

        public static bool operator ==(Color lhs, Color rhs) => lhs.Equals(rhs);

        public static bool operator !=(Color lhs, Color rhs) => !lhs.Equals(rhs);

        public bool Equals(Color other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);

        public override bool Equals(object? other)
        {
            if (!(other is Color c)) return false;
            return R.Equals(c.R) && G.Equals(c.G) && B.Equals(c.B) && A.Equals(c.A);
        }

        public override int GetHashCode() => HashCode.Combine(R, G, B, A);

        #endregion

        public override string ToString() => string.Format("RGBA({0}, {1}, {2}, {3})", new object[] { R, G, B, A });
    }
}
