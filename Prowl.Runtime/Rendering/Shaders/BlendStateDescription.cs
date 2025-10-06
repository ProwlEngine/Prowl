using System;

namespace Prowl.Runtime.Rendering.Shaders
{
    public enum BlendFactor : uint
    {
        Zero = 0,
        One = 1,
        SrcColor = 768,
        OneMinusSrcColor = 769,
        SrcAlpha = 770,
        OneMinusSrcAlpha = 771,
        DstAlpha = 772,
        OneMinusDstAlpha = 773,
        DstColor = 774,
        OneMinusDstColor = 775,
        SrcAlphaSaturate = 776,
        ConstantColor = 32769,
        OneMinusConstantColor = 32770,
        ConstantAlpha = 32771,
        OneMinusConstantAlpha = 32772,
        Src1Alpha = 34185,
        Src1Color = 35065,
        OneMinusSrc1Color = 35066,
        OneMinusSrc1Alpha = 35067,
    }

    public enum BlendEquation : uint
    {
        Add = 32774,
        ReverseSubtract = 32779,
        Subtract = 32778,
        Min = 32775,
        Max = 32776,
    }

    /// <summary>
    /// A component describing the blend behavior for an individual shader pass.
    /// </summary>
    public struct BlendDescription : IEquatable<BlendDescription>
    {
        /// <summary>
        /// Controls whether blending is enabled for the shader pass.
        /// </summary>
        public bool BlendEnabled;

        /// <summary>
        /// Controls the source color's influence on the blend result.
        /// </summary>
        public BlendFactor SourceColorFactor;

        /// <summary>
        /// Controls the destination color's influence on the blend result.
        /// </summary>
        public BlendFactor DestinationColorFactor;

        /// <summary>
        /// Controls the function used to combine the source and destination color factors.
        /// </summary>
        public BlendEquation ColorFunction;

        /// <summary>
        /// Controls the source alpha's influence on the blend result.
        /// </summary>
        public BlendFactor SourceAlphaFactor;

        /// <summary>
        /// Controls the destination alpha's influence on the blend result.
        /// </summary>
        public BlendFactor DestinationAlphaFactor;

        /// <summary>
        /// Controls the function used to combine the source and destination alpha factors.
        /// </summary>
        public BlendEquation AlphaFunction;

        /// <summary>
        /// Constructs a new <see cref="BlendDescription"/>.
        /// </summary>
        /// <param name="blendEnabled">Controls whether blending is enabled for the color attachment.</param>
        /// <param name="sourceColorFactor">Controls the source color's influence on the blend result.</param>
        /// <param name="destinationColorFactor">Controls the destination color's influence on the blend result.</param>
        /// <param name="colorFunction">Controls the function used to combine the source and destination color factors.</param>
        /// <param name="sourceAlphaFactor">Controls the source alpha's influence on the blend result.</param>
        /// <param name="destinationAlphaFactor">Controls the destination alpha's influence on the blend result.</param>
        /// <param name="alphaFunction">Controls the function used to combine the source and destination alpha factors.</param>
        public BlendDescription(
            bool blendEnabled,
            BlendFactor sourceColorFactor,
            BlendFactor destinationColorFactor,
            BlendEquation colorFunction,
            BlendFactor sourceAlphaFactor,
            BlendFactor destinationAlphaFactor,
            BlendEquation alphaFunction)
        {
            BlendEnabled = blendEnabled;
            SourceColorFactor = sourceColorFactor;
            DestinationColorFactor = destinationColorFactor;
            ColorFunction = colorFunction;
            SourceAlphaFactor = sourceAlphaFactor;
            DestinationAlphaFactor = destinationAlphaFactor;
            AlphaFunction = alphaFunction;
        }

        /// <summary>
        /// Describes a blend attachment state in which the source completely overrides the destination.
        /// Settings:
        ///     BlendEnabled = true
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.One
        ///     DestinationColorFactor = BlendFactor.Zero
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.One
        ///     DestinationAlphaFactor = BlendFactor.Zero
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendDescription OverrideBlend = new() {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.One,
            DestinationColorFactor = BlendFactor.Zero,
            ColorFunction = BlendEquation.Add,
            SourceAlphaFactor = BlendFactor.One,
            DestinationAlphaFactor = BlendFactor.Zero,
            AlphaFunction = BlendEquation.Add,
        };

        /// <summary>
        /// Describes a blend attachment state in which the source and destination are blended in an inverse relationship.
        /// Settings:
        ///     BlendEnabled = true
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.SourceAlpha
        ///     DestinationColorFactor = BlendFactor.InverseSourceAlpha
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.SourceAlpha
        ///     DestinationAlphaFactor = BlendFactor.InverseSourceAlpha
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendDescription AlphaBlend = new() {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.SrcAlpha,
            DestinationColorFactor = BlendFactor.OneMinusSrcAlpha,
            ColorFunction = BlendEquation.Add,
            SourceAlphaFactor = BlendFactor.SrcAlpha,
            DestinationAlphaFactor = BlendFactor.OneMinusSrcAlpha,
            AlphaFunction = BlendEquation.Add,
        };

        /// <summary>
        /// Describes a blend attachment state in which the source is added to the destination based on its alpha channel.
        /// Settings:
        ///     BlendEnabled = true
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.SourceAlpha
        ///     DestinationColorFactor = BlendFactor.One
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.SourceAlpha
        ///     DestinationAlphaFactor = BlendFactor.One
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendDescription AdditiveBlend = new() {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.SrcAlpha,
            DestinationColorFactor = BlendFactor.One,
            ColorFunction = BlendEquation.Add,
            SourceAlphaFactor = BlendFactor.SrcAlpha,
            DestinationAlphaFactor = BlendFactor.One,
            AlphaFunction = BlendEquation.Add,
        };

        /// <summary>
        /// Describes a blend attachment state in which blending is disabled.
        /// Settings:
        ///     BlendEnabled = false
        ///     ColorWriteMask = null
        ///     SourceColorFactor = BlendFactor.One
        ///     DestinationColorFactor = BlendFactor.Zero
        ///     ColorFunction = BlendFunction.Add
        ///     SourceAlphaFactor = BlendFactor.One
        ///     DestinationAlphaFactor = BlendFactor.Zero
        ///     AlphaFunction = BlendFunction.Add
        /// </summary>
        public static readonly BlendDescription Disabled = new() {
            BlendEnabled = false,
            SourceColorFactor = BlendFactor.One,
            DestinationColorFactor = BlendFactor.Zero,
            ColorFunction = BlendEquation.Add,
            SourceAlphaFactor = BlendFactor.One,
            DestinationAlphaFactor = BlendFactor.Zero,
            AlphaFunction = BlendEquation.Add,
        };

        /// <summary>
        /// Element-wise equality.
        /// </summary>
        /// <param name="other">The instance to compare to.</param>
        /// <returns>True if all elements and all array elements are equal; false otherswise.</returns>
        public bool Equals(BlendDescription other)
        {
            return BlendEnabled.Equals(other.BlendEnabled)
                && SourceColorFactor == other.SourceColorFactor
                && DestinationColorFactor == other.DestinationColorFactor && ColorFunction == other.ColorFunction
                && SourceAlphaFactor == other.SourceAlphaFactor && DestinationAlphaFactor == other.DestinationAlphaFactor
                && AlphaFunction == other.AlphaFunction;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                BlendEnabled.GetHashCode(),
                (int)SourceColorFactor,
                (int)DestinationColorFactor,
                (int)ColorFunction,
                (int)SourceAlphaFactor,
                (int)DestinationAlphaFactor,
                (int)AlphaFunction);
        }
    }
}
