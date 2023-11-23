using System.Reflection;

namespace Prowl.Editor.PropertyDrawers; 

public record class Property {
    
    private readonly MemberInfo _memberInfo;
    
    public Property(PropertyInfo propertyInfo) {
        _memberInfo = propertyInfo;
    }
    
    public Property(FieldInfo fieldInfo) {
        _memberInfo = fieldInfo;
    }
    
    public bool IsReadonly {
        get {
            return _memberInfo switch {
                FieldInfo => false,
                PropertyInfo propertyInfo => !propertyInfo.CanWrite,
                _ => throw new InvalidCastException()
            };
        }
    }
    
    public string Name => _memberInfo.Name;
    public Type Type => _memberInfo switch
    {
        FieldInfo fieldInfo => fieldInfo.FieldType,
        PropertyInfo propertyInfo => propertyInfo.PropertyType,
        _ => throw new InvalidCastException()
    };
    
    // todo: get attributes
}
