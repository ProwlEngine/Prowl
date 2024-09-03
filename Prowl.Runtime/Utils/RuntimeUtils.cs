using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    public static class RuntimeUtils
    {

        public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsFreeBSD() => RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD);
        public static bool IsMac() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static Type? FindType(string qualifiedTypeName)
        {
            Type? t = Type.GetType(qualifiedTypeName);

            if (t != null)
            {
                return t;
            }
            else
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType(qualifiedTypeName);
                    if (t != null)
                        return t;
                }
                return null;
            }
        }

        public static FieldInfo[] GetSerializableFields(this object target)
        {
            FieldInfo[] fields = GetAllFields(target.GetType()).ToArray();
            // Only allow Publics or ones with SerializeField
            fields = fields.Where(field => (field.IsPublic || field.GetCustomAttribute<SerializeFieldAttribute>() != null) && field.GetCustomAttribute<SerializeIgnoreAttribute>() == null).ToArray();
            // Remove Public NonSerialized fields
            fields = fields.Where(field => !field.IsPublic || field.GetCustomAttribute<NonSerializedAttribute>() == null).ToArray();
            return fields;
        }

        public static IEnumerable<FieldInfo> GetAllFields(Type? t)
        {
            if (t == null)
                return Enumerable.Empty<FieldInfo>();

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | BindingFlags.DeclaredOnly;

            return t.GetFields(flags).Concat(GetAllFields(t.BaseType));
        }

        public static object? GetValue(this MemberInfo member, object? target)
        {
            if (member is PropertyInfo prop)
                return prop.GetValue(target);
            else if (member is FieldInfo field)
                return field.GetValue(target);
            else
                return null;
        }

        public static void SetValue(this MemberInfo member, object? target, object? value)
        {
            if (member is PropertyInfo prop)
                prop.SetValue(target, value);
            else if (member is FieldInfo field)
                field.SetValue(target, value);
        }

        public static string Prettify(string label)
        {
            if (label.StartsWith('_'))
                label = label.Substring(1);

            // Use a StringBuilder to avoid modifying the original string in the loop
            StringBuilder result = new StringBuilder(label.Length * 2);
            result.Append(char.ToUpper(label[0]));

            // Add space before each Capital letter (except the first)
            for (int i = 1; i < label.Length; i++)
            {
                if (char.IsUpper(label[i]) && !char.IsUpper(label[i - 1]))
                {
                    result.Append(' ');  // Add space
                    result.Append(label[i]);  // Append the current uppercase character
                }
                else if (label[i] == '_')
                {
                    continue;
                }
                else
                {
                    result.Append(label[i]);  // Append the current character
                }
            }

            return Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(result.ToString());
        }

        public static IEnumerable<Type> GetTypesWithAttribute<T>()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
                foreach (var type in assembly.GetTypes())
                    if (type.GetCustomAttributes(typeof(T), true).Length > 0)
                        yield return type;
        }

        public static List<Type> FindTypesImplementing(Type propertyType)
        {
            List<Type> types = new List<Type>();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in asm.GetTypes())
                {
                    if (propertyType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                    {
                        types.Add(type);
                    }
                }
            }
            return types;
        }

        public static string GetUniquePath(string target)
        {
            if (!System.IO.File.Exists(target))
                return target;

            string path = System.IO.Path.GetDirectoryName(target);
            string name = System.IO.Path.GetFileNameWithoutExtension(target);
            string ext = System.IO.Path.GetExtension(target);

            for (int i = 1; ; i++)
            {
                string temp = System.IO.Path.Combine(path, $"{name} ({i}){ext}");
                if (!System.IO.File.Exists(temp))
                    return temp;
            }
        }
    }
}
