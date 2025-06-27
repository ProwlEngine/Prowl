// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

namespace Prowl.Echo.Formatters;

/// <summary>
/// Handles serialization of anonymous types. Deserialization is not supported
/// as anonymous types cannot be reconstructed at runtime.
/// </summary>
internal sealed class AnonymousTypeFormat : ISerializationFormat
{
    public bool CanHandle(Type type) => IsAnonymousType(type);

    public EchoObject Serialize(Type? targetType, object value, SerializationContext context)
    {
        var compound = EchoObject.NewCompound();
        Type actualType = value.GetType();

        context.BeginDependencies();

        // Serialize all public properties of the anonymous type
        foreach (PropertyInfo property in actualType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                object? propValue = property.GetValue(value);
                if (propValue == null)
                {
                    compound.Add(property.Name, new EchoObject(EchoType.Null, null));
                }
                else
                {
                    // For aggressive mode, we want to ensure type preservation even for exact matches
                    // So we pass the property type to enable polymorphism detection
                    EchoObject tag = Serializer.Serialize(property.PropertyType, propValue, context);
                    compound.Add(property.Name, tag);
                }
            }
            catch (Exception ex)
            {
                Serializer.Logger.Error($"Failed to serialize anonymous type property {property.Name}", ex);
                // Continue with other properties
            }
        }
        
        context.EndDependencies();

        return compound;
    }

    public object? Deserialize(EchoObject value, Type targetType, SerializationContext context)
    {
        // Anonymous types cannot be deserialized since they're compiler-generated
        throw new NotSupportedException("Cannot deserialize anonymous types");
    }

    /// <summary>
    /// Determines if a type is an anonymous type by checking compiler-generated attributes
    /// and naming conventions.
    /// </summary>
    private static bool IsAnonymousType(Type type)
    {
        if (type == null) return false;

        // Anonymous types are compiler-generated and have specific characteristics:
        // 1. They have the CompilerGenerated attribute
        // 2. Their name contains "AnonymousType"
        // 3. They are generic with read-only properties

        return type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null &&
               type.Name.Contains("AnonymousType") &&
               type.IsGenericType &&
               type.IsSealed &&
               type.Namespace == null; // Anonymous types have no namespace
    }

    /// <summary>
    /// Creates a readable signature for the anonymous type for debugging purposes.
    /// </summary>
    private static string GetAnonymousTypeSignature(Type anonymousType)
    {
        var properties = anonymousType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertySignatures = properties.Select(p => $"{p.PropertyType.Name} {p.Name}");
        return $"{{ {string.Join(", ", propertySignatures)} }}";
    }
}