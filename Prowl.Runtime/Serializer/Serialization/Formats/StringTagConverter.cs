using Prowl.Runtime.Serializer;
using System.IO;
using System.Text.Json;

namespace Prowl.Runtime.Serializer
{
    public static class StringTagConverter
    {
        public static void WriteToFile(CompoundTag tag, FileInfo file)
        {
            string json = Write(tag);
            File.WriteAllText(file.FullName, json);
        }

        public static string Write(CompoundTag tag)
        {
            return JsonSerializer.Serialize(tag, new JsonSerializerOptions { WriteIndented = true });
        }

        public static CompoundTag ReadFromFile(FileInfo file)
        {
            string json = File.ReadAllText(file.FullName);
            return Read(json);
        }

        public static CompoundTag Read(string json)
        {
            return JsonSerializer.Deserialize<CompoundTag>(json);
        }

    }
}
