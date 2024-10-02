using System;
using System.Collections.Generic;

namespace AssimpSharp
{
    public static class GenericProperty
    {
        public static bool SetGenericProperty<T>(IDictionary<int, T> list, string szName, T value)
        {
            if (string.IsNullOrEmpty(szName)) throw new ArgumentException("Name cannot be empty", nameof(szName));
            int hash = StringHash(szName);

            return list.TryAdd(hash, value);
        }

        public static T GetGenericProperty<T>(IDictionary<int, T> list, string szName, T errorReturn)
        {
            if (string.IsNullOrEmpty(szName)) throw new ArgumentException("Name cannot be empty", nameof(szName));
            int hash = StringHash(szName);

            return list.TryGetValue(hash, out T value) ? value : errorReturn;
        }

        public static void SetGenericPropertyPtr<T>(IDictionary<int, T> list, string szName, T value, ref bool wasExisting)
        {
            if (string.IsNullOrEmpty(szName)) throw new ArgumentException("Name cannot be empty", nameof(szName));
            int hash = StringHash(szName);

            wasExisting = list.ContainsKey(hash);

            if (value == null)
                list.Remove(hash);
            else
                list[hash] = value;
        }

        public static bool HasGenericProperty<T>(IDictionary<int, T> list, string szName)
        {
            if (string.IsNullOrEmpty(szName)) throw new ArgumentException("Name cannot be empty", nameof(szName));
            int hash = StringHash(szName);

            return list.ContainsKey(hash);
        }

        private static int StringHash(string str) => str.GetHashCode();
    }
}
