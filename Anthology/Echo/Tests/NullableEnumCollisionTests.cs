using Prowl.Echo;
using Xunit;

namespace AsmA
{
    public enum BlendFactor : byte { Zero, One, SourceAlpha, InverseSourceAlpha }
}

namespace AsmB
{
    public enum BlendFactor : uint { Zero = 0, One = 1, SrcAlpha = 770, OneMinusSrcAlpha = 771 }
}

namespace Echo.Tests
{
    public class NullableEnumCollisionTests
    {
        private sealed class PassStateLike
        {
            public bool? EnableBlend;
            public AsmA.BlendFactor? BlendSrcRgb;
            public AsmA.BlendFactor? BlendDstRgb;
        }

        [Fact]
        public void NullableEnum_RoundTrips_WhenSimpleNameCollidesAcrossNamespaces()
        {
            // Force the OTHER same-named enum to be referenced so its type is loaded/enumerable.
            _ = AsmB.BlendFactor.SrcAlpha;

            var src = new PassStateLike
            {
                EnableBlend = true,
                BlendSrcRgb = AsmA.BlendFactor.SourceAlpha,
                BlendDstRgb = AsmA.BlendFactor.InverseSourceAlpha,
            };

            var echo = Serializer.Serialize(src);
            var dst = Serializer.Deserialize<PassStateLike>(echo);

            Assert.NotNull(dst);
            Assert.Equal(true, dst!.EnableBlend);
            Assert.Equal(AsmA.BlendFactor.SourceAlpha, dst.BlendSrcRgb);
            Assert.Equal(AsmA.BlendFactor.InverseSourceAlpha, dst.BlendDstRgb);
        }
    }
}
