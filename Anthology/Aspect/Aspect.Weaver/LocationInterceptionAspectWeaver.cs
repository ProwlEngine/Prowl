using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace Aspect.Weaver;

/// <summary>
/// Weaves LocationInterceptionAspect interceptors into property getters and setters.
/// </summary>
public class LocationInterceptionAspectWeaver
{
    private readonly ModuleDefinition _module;

    public LocationInterceptionAspectWeaver(ModuleDefinition module)
    {
        _module = module;
    }

    public void WeaveProperty(PropertyDefinition property)
    {
        // Find all LocationInterceptionAspect attributes on this property
        var aspectAttributes = property.CustomAttributes
            .Where(attr => IsLocationInterceptionAspect(attr.AttributeType))
            .ToList();

        // Also check class-level attributes (would need filtering)
        if (property.DeclaringType != null)
        {
            var classAspects = property.DeclaringType.CustomAttributes
                .Where(attr => IsLocationInterceptionAspect(attr.AttributeType))
                .ToList();
            aspectAttributes.AddRange(classAspects);
        }

        if (!aspectAttributes.Any())
            return;

        Console.WriteLine($"  Weaving property: {property.FullName} with {aspectAttributes.Count} aspect(s)");

        // Weave getter if it exists
        if (property.GetMethod != null)
        {
            foreach (var aspectAttr in aspectAttributes)
            {
                WeavePropertyGetter(property, property.GetMethod, aspectAttr);
            }
        }

        // Weave setter if it exists
        if (property.SetMethod != null)
        {
            foreach (var aspectAttr in aspectAttributes)
            {
                WeavePropertySetter(property, property.SetMethod, aspectAttr);
            }
        }
    }

    private void WeavePropertyGetter(PropertyDefinition property, MethodDefinition getter, CustomAttribute aspectAttribute)
    {
        // TODO: Implement property getter weaving
        // 1. Create aspect instance
        // 2. Create LocationInterceptionArgs
        // 3. Call OnGetValue
        // 4. Check if ProceedGetValue was called
        // 5. Return the value from args.Value

        Console.WriteLine($"    TODO: Weave getter for {property.Name}");
    }

    private void WeavePropertySetter(PropertyDefinition property, MethodDefinition setter, CustomAttribute aspectAttribute)
    {
        // TODO: Implement property setter weaving
        // 1. Create aspect instance
        // 2. Create LocationInterceptionArgs with the new value
        // 3. Call OnSetValue
        // 4. Check if ProceedSetValue was called
        // 5. Set the backing field if proceeded

        Console.WriteLine($"    TODO: Weave setter for {property.Name}");
    }

    private bool IsLocationInterceptionAspect(TypeReference typeRef)
    {
        var typeDef = typeRef.Resolve();
        if (typeDef == null) return false;

        // Check if it inherits from LocationInterceptionAspect
        var current = typeDef;
        while (current != null)
        {
            if (current.FullName == "Aspect.LocationInterceptionAspect")
                return true;

            current = current.BaseType?.Resolve();
        }

        return false;
    }
}
