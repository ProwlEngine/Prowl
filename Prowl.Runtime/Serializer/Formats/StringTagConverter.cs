using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Prowl.Runtime
{

    // TODO: Convert to YAML and support Unity's YAML format

    public static class StringTagConverter
    {
        public static void WriteToFile(SerializedProperty tag, FileInfo file)
        {
            string json = Write(tag);
            File.WriteAllText(file.FullName, json);
        }

        public static string Write(SerializedProperty tag)
        {
            return JsonSerializer.Serialize(tag, new JsonSerializerOptions { WriteIndented = true, MaxDepth = 1024 });
        }

        public static SerializedProperty ReadFromFile(FileInfo file)
        {
            string json = File.ReadAllText(file.FullName);
            return Read(json);
        }

        public static SerializedProperty Read(string json)
        {
            return JsonSerializer.Deserialize<SerializedProperty>(json, new JsonSerializerOptions { MaxDepth = 1024 });
        }

        }

    }
}
