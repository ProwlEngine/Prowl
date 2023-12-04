using Prowl.Runtime.Serialization;
using System.IO;
using System.Text.Json;

namespace Prowl.Runtime.Serializer
{
    public static class StringTagConverter
    {
        public static void WriteTo(CompoundTag tag, TextWriter writer)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            writer.Write(JsonSerializer.Serialize(tag, options));
        }

        public static CompoundTag ReadFrom(TextReader reader)
        {
            return JsonSerializer.Deserialize<CompoundTag>(reader.ReadToEnd());
        }

    }
}
