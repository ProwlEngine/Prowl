// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Echo;

/// <summary>
/// Marks a class or struct for automatic ISerializable implementation via source generation.
/// The generator will create optimized Serialize and Deserialize methods based on the type's fields.
/// </summary>
/// <remarks>This is still under active development, and isnt guraunteed to function correctly, Be sure to check the results when using this Attribute!</remarks>
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class GenerateSerializerAttribute : System.Attribute
{
}
