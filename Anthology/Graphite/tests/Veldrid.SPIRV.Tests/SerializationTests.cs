using System.IO;
using System.Text;
using Xunit;

namespace Veldrid.SPIRV.Tests
{
    public class SerializationTests
    {
        [Fact]
        public void SpirvReflection_DeserializeFromString()
        {
            const string SerializedJson =
@"{
  ""VertexElements"": [],
  ""ResourceLayouts"": [
    {
      ""Elements"": [
        {
          ""Name"": ""vdspv_0_0"",
          ""Kind"": ""TextureReadOnly"",
          ""Stages"": ""Fragment"",
          ""Options"": ""None""
        },
        {
          ""Name"": ""vdspv_0_1"",
          ""Kind"": ""Sampler"",
          ""Stages"": ""Fragment"",
          ""Options"": ""None""
        }
      ]
    },
    {
      ""Elements"": [
        {
          ""Name"": ""vdspv_1_0"",
          ""Kind"": ""UniformBuffer"",
          ""Stages"": ""Fragment"",
          ""Options"": ""None""
        }
      ]
    }
  ]
}";
            byte[] bytes = Encoding.UTF8.GetBytes(SerializedJson);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                SpirvReflection refl = SpirvReflection.LoadFromJson(ms);
                Assert.Empty(refl.VertexElements);
                Assert.Equal(2, refl.ResourceLayouts.Length);
                Assert.Equal(2, refl.ResourceLayouts[0].Elements.Length);
                Assert.Single(refl.ResourceLayouts[1].Elements);
            }
        }
    }
}
