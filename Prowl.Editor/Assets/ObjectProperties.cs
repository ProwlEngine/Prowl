using Prowl.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Editor.Assets
{
    /// <summary>
    /// A useful class for property modifications
    /// It uses reflection to get the properties of one more more objects and allows modifying them
    /// As well as allowing Multiple objects to be modified at once and undoing/redoing changes
    /// </summary>
    public class SerializedObject
    {
#warning TODO: Undo/Redo

        //public object Target => _targets[0];
        //public object[] Targets => _targets.ToArray();
        //public bool HasMultipleTargets => _targets.Count > 1;
        //public List<string> Properties => _properties.Keys.ToList();
        //
        //private List<object> _targets = new List<object>();
        //private Dictionary<string, Property> _properties = new Dictionary<string, Property>();
        //
        //public SerializedObject(params object[] targets) => _targets.AddRange(targets);
        //
        //public Property FindProperty(string propertyName)
        //{
        //    var commonFields = _targets
        //        .Select(obj => ( obj, obj.GetType().GetField(propertyName)))
        //        .Where(pair => pair.Item2 != null)
        //        .ToList();
        //
        //    if (commonFields.Count == _targets.Count)
        //        return new Property(commonFields!);
        //
        //    throw new ArgumentException($"Property '{propertyName}' not found in all objects.");
        //}
        //
        //public IEnumerable<Property> FindAllProperties()
        //{
        //    var commonProperties = new Dictionary<object, List<PropertyInfo>>();
        //
        //    foreach (var obj in _targets)
        //    {
        //        var properties = new List<PropertyInfo>(obj.GetType().GetFields());
        //        foreach (var property in properties)
        //        {
        //            if (!commonProperties.ContainsKey(obj))
        //            {
        //                commonProperties[obj] = new List<PropertyInfo>();
        //            }
        //
        //            commonProperties[obj].Add(property);
        //        }
        //    }
        //
        //    var result = new List<MultiProperty>();
        //
        //    foreach (var pair in commonProperties)
        //    {
        //        result.Add(new MultiProperty(pair.Key, pair.Value));
        //    }
        //
        //    return result;
        //}
        //
        //public Property[] FindAllProperties()
        //{
        //    FieldInfo[] allProperties = _targets
        //        .SelectMany(obj => obj.GetType().GetFields())
        //        .ToArray();
        //
        //    var groupedProperties = allProperties
        //        .GroupBy(prop => prop.Name)
        //        .Where(group => group.Count() == _targets.Count)
        //        .ToArray();
        //
        //    return groupedProperties
        //        .Select(group => new Property(group.ToArray()))
        //        .ToArray();
        //}
        //public class Property
        //{
        //    private readonly List<(object, FieldInfo)> _properties;
        //
        //    public Property(List<(object, FieldInfo)> properties) => _properties = properties;
        //
        //    public object? GetValue() => _properties[0].Item2.GetValue(_properties[0].Item1);
        //
        //    public void SetValue(object value)
        //    {
        //        foreach (var property in _properties)
        //            property.Item2.SetValue(property.Item1, value);
        //    }
        //}

    }
}
