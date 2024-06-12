using Veldrid;
using System;

namespace Prowl.Runtime
{
    [Flags]
    public enum SamplerAxis
    {
        U, V, W,
    }

    public enum FilterType
    {
        Linear,
        Point
    }

    public sealed class TextureSampler : EngineObject, ISerializable
    {
        private Sampler _internalSampler;
        private SamplerDescription _internalDescription;

        /// <summary>The internal <see cref="Sampler"/> representation.</summary>
        public Sampler InternalSampler 
        { 
            get
            {
                RecreateInternalSampler();
                return _internalSampler;
            }
        }


        public SamplerAddressMode wrapModeU = SamplerAddressMode.Clamp;
        public SamplerAddressMode wrapModeV = SamplerAddressMode.Clamp;
        public SamplerAddressMode wrapModeW = SamplerAddressMode.Clamp;

        public SamplerBorderColor borderColor = SamplerBorderColor.OpaqueBlack;
        public SamplerFilter filter = SamplerFilter.MinLinear_MagLinear_MipLinear;

        public int lodBias;
        public uint maximumAnisotropy;
        public uint maximumLod;
        public uint minimumLod;

        public static readonly TextureSampler Aniso4x = new TextureSampler(SamplerDescription.Aniso4x);
        public static readonly TextureSampler Linear = new TextureSampler(SamplerDescription.Linear);
        public static readonly TextureSampler Point = new TextureSampler(SamplerDescription.Point);


        internal TextureSampler() : base("New Sampler") { }

        internal TextureSampler(SamplerDescription description) : this()
        {
            wrapModeU = description.AddressModeU;
            wrapModeV = description.AddressModeV;
            wrapModeW = description.AddressModeW;
            borderColor = description.BorderColor;
            filter = description.Filter;
            lodBias = description.LodBias;
            maximumAnisotropy = description.MaximumAnisotropy;
            maximumLod = description.MaximumLod;
            minimumLod = description.MinimumLod;
        }

        private void RecreateInternalSampler()
        {
            SamplerDescription description = new()
            {
                AddressModeU = wrapModeU,
                AddressModeV = wrapModeV,
                AddressModeW = wrapModeW,
                BorderColor = borderColor,
                Filter = filter,
                LodBias = lodBias,
                MaximumAnisotropy = maximumAnisotropy,
                MaximumLod = maximumLod,
                MinimumLod = minimumLod
            };

            if (_internalSampler == null || !CompareDescriptions(in description, in _internalDescription))
            {
                OnDispose();
                _internalDescription = description;
                _internalSampler = Graphics.ResourceFactory.CreateSampler(ref _internalDescription);
            }
        }

        public void SetWrapMode(SamplerAxis axis, SamplerAddressMode mode)
        {
            if (axis.HasFlag(SamplerAxis.U))
                wrapModeU = mode;
            if (axis.HasFlag(SamplerAxis.V))
                wrapModeV = mode;
            if (axis.HasFlag(SamplerAxis.W))
                wrapModeW = mode;
        }

        public void SetLodLimits(uint maxLod, uint minLod)
        {
            maximumLod = maxLod;
            minimumLod = minLod;
        }

        public void SetFilter(FilterType minFilter = FilterType.Linear, FilterType magFilter = FilterType.Linear, FilterType mipFilter = FilterType.Linear, bool anisotropic = false)
        {
            if (anisotropic == true)
            {
                filter = SamplerFilter.Anisotropic;
                return;
            }

            // Ugly nested if-else chain. Unfortunately the only other options are enum parsing or a nastier, more verbose chain.
            if (minFilter == FilterType.Linear)
            {   
                if (magFilter == FilterType.Linear)
                {
                    if (mipFilter == FilterType.Linear)
                        filter = SamplerFilter.MinLinear_MagLinear_MipLinear;
                    else
                        filter = SamplerFilter.MinLinear_MagLinear_MipPoint;
                }
                else
                {
                    if (mipFilter == FilterType.Linear)
                        filter = SamplerFilter.MinLinear_MagPoint_MipLinear;
                    else
                        filter = SamplerFilter.MinLinear_MagPoint_MipPoint;
                }
            }
            else
            {
                if (magFilter == FilterType.Linear)
                {
                    if (mipFilter == FilterType.Linear)
                        filter = SamplerFilter.MinPoint_MagLinear_MipLinear;
                    else
                        filter = SamplerFilter.MinPoint_MagLinear_MipPoint;
                }
                else
                {
                    if (mipFilter == FilterType.Linear)
                        filter = SamplerFilter.MinPoint_MagPoint_MipLinear;
                    else
                        filter = SamplerFilter.MinPoint_MagPoint_MipPoint;
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
            compoundTag.Add("WrapModeU", new((int)wrapModeU));
            compoundTag.Add("WrapModeV", new((int)wrapModeV));
            compoundTag.Add("WrapModeW", new((int)wrapModeW));
            compoundTag.Add("BorderColor", new((int)borderColor));
            compoundTag.Add("Filter", new((int)filter));
            compoundTag.Add("LodBias", new(lodBias));
            compoundTag.Add("MaxAniso", new(maximumAnisotropy));
            compoundTag.Add("MaxLod", new(maximumLod));
            compoundTag.Add("MinLod", new(minimumLod));

            return compoundTag;
        }

        public void Deserialize(SerializedProperty value, Serializer.SerializationContext ctx)
        {
            wrapModeU = (SamplerAddressMode)value["WrapModeU"].IntValue;
            wrapModeV = (SamplerAddressMode)value["WrapModeV"].IntValue;
            wrapModeW = (SamplerAddressMode)value["WrapModeW"].IntValue;
            borderColor = (SamplerBorderColor)value["BorderColor"].IntValue;
            filter = (SamplerFilter)value["Filter"].IntValue;
            lodBias = value["LodBias"].IntValue;
            maximumAnisotropy = (uint)value["MaxAniso"].IntValue;
            maximumLod = (uint)value["MaxLod"].IntValue;
            minimumLod = (uint)value["MinLod"].IntValue;
        }
    }
}
