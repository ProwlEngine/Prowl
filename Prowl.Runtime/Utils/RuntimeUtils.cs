using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    public static class RuntimeUtils
    {

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
    }
}
