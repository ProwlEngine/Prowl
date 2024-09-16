// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

namespace Prowl.Runtime;

[Flags]
public enum SamplerAxis
{
    U, V, W,
}

public enum FilterType
{
    Linear,
    Point,
}

public enum TextureWrapMode
{
    Border = SamplerAddressMode.Border,
    Clamp = SamplerAddressMode.Clamp,
    Mirror = SamplerAddressMode.Mirror,
    Wrap = SamplerAddressMode.Wrap,
}

public sealed class TextureSampler : EngineObject, ISerializable
{
    private Sampler _internalSampler;
    private SamplerDescription _internalDescription;

    /// <summary>The internal <see cref="Sampler"/> representation.</summary>
    internal Sampler InternalSampler
    {
        get
        {
            RecreateInternalSampler();
            return _internalSampler;
        }
    }


    public TextureWrapMode WrapModeU = TextureWrapMode.Clamp;
    public TextureWrapMode WrapModeV = TextureWrapMode.Clamp;
    public TextureWrapMode WrapModeW = TextureWrapMode.Clamp;

    public SamplerBorderColor BorderColor = SamplerBorderColor.OpaqueBlack;
    public SamplerFilter Filter = SamplerFilter.MinLinear_MagLinear_MipLinear;

    public int LodBias;
    public uint MaximumAnisotropy;
    public uint MaximumLod;
    public uint MinimumLod;

    public bool Anisotropic => Filter == SamplerFilter.Anisotropic;

    public static TextureSampler CreateAniso4x() => new TextureSampler(SamplerDescription.Aniso4x);
    public static TextureSampler CreateLinear() => new TextureSampler(SamplerDescription.Linear);
    public static TextureSampler CreatePoint() => new TextureSampler(SamplerDescription.Point);


    internal TextureSampler() : base("New Sampler") { }

    internal TextureSampler(SamplerDescription description) : this()
    {
        WrapModeU = (TextureWrapMode)description.AddressModeU;
        WrapModeV = (TextureWrapMode)description.AddressModeV;
        WrapModeW = (TextureWrapMode)description.AddressModeW;
        BorderColor = description.BorderColor;
        Filter = description.Filter;
        LodBias = description.LodBias;
        MaximumAnisotropy = description.MaximumAnisotropy;
        MaximumLod = description.MaximumLod;
        MinimumLod = description.MinimumLod;
    }

    private void RecreateInternalSampler()
    {
        SamplerDescription description = new()
        {
            AddressModeU = (SamplerAddressMode)WrapModeU,
            AddressModeV = (SamplerAddressMode)WrapModeV,
            AddressModeW = (SamplerAddressMode)WrapModeW,
            BorderColor = BorderColor,
            Filter = Filter,
            LodBias = LodBias,
            MaximumAnisotropy = MaximumAnisotropy,
            MaximumLod = MaximumLod,
            MinimumLod = MinimumLod
        };

        if (_internalSampler == null || !CompareDescriptions(in description, in _internalDescription))
        {
            OnDispose();
            _internalDescription = description;
            _internalSampler = Graphics.Factory.CreateSampler(in _internalDescription);
        }
    }

    public void Copy(TextureSampler other)
    {
        WrapModeU = other.WrapModeU;
        WrapModeV = other.WrapModeV;
        WrapModeW = other.WrapModeW;
        BorderColor = other.BorderColor;
        Filter = other.Filter;
        LodBias = other.LodBias;
        MaximumAnisotropy = other.MaximumAnisotropy;
        MaximumLod = other.MaximumLod;
        MinimumLod = other.MinimumLod;
    }

    public void SetWrapMode(SamplerAxis axis, TextureWrapMode mode)
    {
        if (axis.HasFlag(SamplerAxis.U))
            WrapModeU = mode;
        if (axis.HasFlag(SamplerAxis.V))
            WrapModeV = mode;
        if (axis.HasFlag(SamplerAxis.W))
            WrapModeW = mode;
    }

    public void SetLodLimits(uint maxLod, uint minLod)
    {
        MaximumLod = maxLod;
        MinimumLod = minLod;
    }

    public void SetFilter(FilterType minFilter = FilterType.Linear, FilterType magFilter = FilterType.Linear, FilterType mipFilter = FilterType.Linear, bool anisotropic = false)
    {
        if (anisotropic == true)
        {
            Filter = SamplerFilter.Anisotropic;
            return;
        }

        // Ugly nested if-else chain. Unfortunately the only other options are enum parsing or a nastier, more verbose chain.
        if (minFilter == FilterType.Linear)
        {
            if (magFilter == FilterType.Linear)
            {
                if (mipFilter == FilterType.Linear)
                    Filter = SamplerFilter.MinLinear_MagLinear_MipLinear;
                else
                    Filter = SamplerFilter.MinLinear_MagLinear_MipPoint;
            }
            else
            {
                if (mipFilter == FilterType.Linear)
                    Filter = SamplerFilter.MinLinear_MagPoint_MipLinear;
                else
                    Filter = SamplerFilter.MinLinear_MagPoint_MipPoint;
            }
        }
        else
        {
            if (magFilter == FilterType.Linear)
            {
                if (mipFilter == FilterType.Linear)
                    Filter = SamplerFilter.MinPoint_MagLinear_MipLinear;
                else
                    Filter = SamplerFilter.MinPoint_MagLinear_MipPoint;
            }
            else
            {
                if (mipFilter == FilterType.Linear)
                    Filter = SamplerFilter.MinPoint_MagPoint_MipLinear;
                else
                    Filter = SamplerFilter.MinPoint_MagPoint_MipPoint;
            }
        }
    }

    public override void OnDispose()
    {
        _internalSampler?.Dispose();
        _internalSampler = null;
    }

    private static bool CompareDescriptions(in SamplerDescription desc1, in SamplerDescription desc2)
    {
        return desc1.AddressModeU == desc2.AddressModeU &&
               desc1.AddressModeV == desc2.AddressModeV &&
               desc1.AddressModeW == desc2.AddressModeW &&
               desc1.BorderColor == desc2.BorderColor &&
               desc1.ComparisonKind == desc2.ComparisonKind &&
               desc1.Filter == desc2.Filter &&
               desc1.LodBias == desc2.LodBias &&
               desc1.MaximumAnisotropy == desc2.MaximumAnisotropy &&
               desc1.MaximumLod == desc2.MaximumLod &&
               desc1.MinimumLod == desc2.MinimumLod;
    }

    public SerializedProperty Serialize(Serializer.SerializationContext ctx)
    {
        SerializedProperty compoundTag = SerializedProperty.NewCompound();

        SerializeHeader(compoundTag);

        compoundTag.Add("WrapModeU", new((int)WrapModeU));
        compoundTag.Add("WrapModeV", new((int)WrapModeV));
        compoundTag.Add("WrapModeW", new((int)WrapModeW));
        compoundTag.Add("BorderColor", new((int)BorderColor));
        compoundTag.Add("Filter", new((int)Filter));
        compoundTag.Add("LodBias", new(LodBias));
        compoundTag.Add("MaxAniso", new(MaximumAnisotropy));
        compoundTag.Add("MaxLod", new(MaximumLod));
        compoundTag.Add("MinLod", new(MinimumLod));

        return compoundTag;
    }

    public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
    {
        DeserializeHeader(value);

        WrapModeU = (TextureWrapMode)value["WrapModeU"].IntValue;
        WrapModeV = (TextureWrapMode)value["WrapModeV"].IntValue;
        WrapModeW = (TextureWrapMode)value["WrapModeW"].IntValue;
        BorderColor = (SamplerBorderColor)value["BorderColor"].IntValue;
        Filter = (SamplerFilter)value["Filter"].IntValue;
        LodBias = value["LodBias"].IntValue;
        MaximumAnisotropy = (uint)value["MaxAniso"].IntValue;
        MaximumLod = (uint)value["MaxLod"].IntValue;
        MinimumLod = (uint)value["MinLod"].IntValue;
    }
}
