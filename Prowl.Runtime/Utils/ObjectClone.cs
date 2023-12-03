using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

// Based on: https://github.com/Burtsev-Alexey/net-object-deep-copy

//namespace Prowl.Runtime
//{
//    public static class ObjectExtensions
//    {
//        private static readonly MethodInfo CloneMethod = typeof(object).GetTypeInfo().GetDeclaredMethod("MemberwiseClone");
//
//        public static bool IsValue(this Type type) => type == typeof(string) || type.GetTypeInfo().IsValueType;
//
//        /// <summary>
//        /// Create a Deep Copy of the object
//        /// This copies everything and leaves nothing linked
//        /// Be careful, if you have a GameObject with a Parent, this will copy the Parent as well
//        /// So if you use this on a GameObject in the middle of the hierarchy, it will copy the entire hierarchy
//        /// </summary>
//        /// <param name="originalObject"></param>
//        /// <returns></returns>
//        public static object? DeepCopy(this object? originalObject)
//        {
//            return InternalCopy(originalObject, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
//        }
//
//        private static object? InternalCopy(object? originalObject, IDictionary<object, object> visited)
//        {
//            if (originalObject == null) return null;
//
//            var typeToReflect = originalObject.GetType();
//            if (IsValue(typeToReflect)) return originalObject;
//
//            if (visited.TryGetValue(originalObject, out object? value)) return value;
//
//            if (typeof(Delegate).GetTypeInfo().IsAssignableFrom(typeToReflect.GetTypeInfo())) return null;
//
//            var cloneObject = CloneMethod.Invoke(originalObject, null)!; // MemberwiseClone
//            if (typeToReflect.IsArray)
//            {
//                // MemberwiseClone each index of the array
//                if (!IsValue(typeToReflect.GetElementType()!))
//                {
//                    Array clonedArray = (Array)cloneObject;
//                    clonedArray.ForEach((array, indices) => array.SetValue(InternalCopy(clonedArray.GetValue(indices), visited), indices));
//                }
//            }
//            // Track objects we have seen
//            visited.Add(originalObject, cloneObject);
//
//            // MemberwiseClone keeps the same reference, so we need to copy the references
//            // Copy the Fields over
//            CopyFields(originalObject, visited, cloneObject, typeToReflect, info => !info.IsStatic && !info.FieldType.GetTypeInfo().IsPrimitive);
//            RecursiveCopyBaseTypeFields(originalObject, visited, cloneObject, typeToReflect);
//            return cloneObject;
//        }
//
//        private static void RecursiveCopyBaseTypeFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect)
//        {
//            if (typeToReflect.GetTypeInfo().BaseType != null)
//            {
//                RecursiveCopyBaseTypeFields(originalObject, visited, cloneObject, typeToReflect.GetTypeInfo().BaseType);
//                CopyFields(originalObject, visited, cloneObject, typeToReflect.GetTypeInfo().BaseType, info => !info.IsStatic && !info.FieldType.GetTypeInfo().IsPrimitive);
//            }
//        }
//
//        private static void CopyFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect, Predicate<FieldInfo> filter = null)
//        {
//            List<FieldInfo> filtered = new List<FieldInfo>(typeToReflect.GetTypeInfo().DeclaredFields);
//            if (filter != null)
//            {
//                filtered = filtered.FindAll(filter);
//            }
//            foreach (FieldInfo fieldInfo in filtered)
//            {
//                var originalFieldValue = fieldInfo.GetValue(originalObject);
//                var clonedFieldValue = InternalCopy(originalFieldValue, visited);
//                fieldInfo.SetValue(cloneObject, clonedFieldValue);
//            }
//        }
//
//        public static T Copy<T>(this T original)
//        {
//            return (T)Copy((object)original);
//        }
//
//        public static void ForEach(this Array array, Action<Array, int[]> action)
//        {
//            if (array.Length == 0) return;
//            ArrayTraverse walker = new ArrayTraverse(array);
//            do action(array, walker.Position);
//            while (walker.Step());
//        }
//
//        internal class ArrayTraverse
//        {
//            public int[] Position;
//            private int[] maxLengths;
//
//            public ArrayTraverse(Array array)
//            {
//                maxLengths = new int[array.Rank];
//                for (int i = 0; i < array.Rank; ++i)
//                {
//                    maxLengths[i] = array.GetLength(i) - 1;
//                }
//                Position = new int[array.Rank];
//            }
//
//            public bool Step()
//            {
//                for (int i = 0; i < Position.Length; ++i)
//                {
//                    if (Position[i] < maxLengths[i])
//                    {
//                        Position[i]++;
//                        for (int j = 0; j < i; j++)
//                        {
//                            Position[j] = 0;
//                        }
//                        return true;
//                    }
//                }
//                return false;
//            }
//        }
//    }
//}
