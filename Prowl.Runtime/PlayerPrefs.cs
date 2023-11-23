using System;
using System.IO;

namespace Prowl.Runtime
{
    public static class PlayerPrefs
    {
        // TODO: Need project paths and stuff figured out
        // But everything in here is really easy just JsonConverter.SerializeObject/DeserializeObject
        static string PlayerPrefsPath => Path.Combine("");

        internal static void Initialize()
        {
        }

        public static bool HasKey(string key) => throw new NotImplementedException();

        public static void DeleteAll() => throw new NotImplementedException();
        public static void DeleteKey(string key) => throw new NotImplementedException();

        public static float GetFloat(string key) => throw new NotImplementedException();
        public static int GetInt(string key) => throw new NotImplementedException();
        public static string GetString(string key) => throw new NotImplementedException();
        public static object GetObject(string key) => throw new NotImplementedException();

        public static void SetFloat(string key, float value) => throw new NotImplementedException();
        public static void SetInt(string key, int value) => throw new NotImplementedException();
        public static void SetString(string key, string value) => throw new NotImplementedException();
        public static void SetObject(string key, object value) => throw new NotImplementedException();

        public static void SaveAll() => throw new NotImplementedException();

    }
}
