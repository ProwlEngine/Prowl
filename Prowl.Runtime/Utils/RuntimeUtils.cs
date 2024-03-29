using Silk.NET.Vulkan;
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

        public static Type FindType(string qualifiedTypeName)
        {
            Type t = Type.GetType(qualifiedTypeName);

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
                if (char.IsUpper(label[i]))
                {
                    result.Append(' ');  // Add space
                    result.Append(label[i]);  // Append the current uppercase character
                }
                else
                {
                    result.Append(label[i]);  // Append the current character
                }
            }

            return Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(result.ToString());
        }
    }
}
