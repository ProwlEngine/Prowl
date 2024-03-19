using System.Collections.Generic;

namespace Prowl.Runtime
{
    public sealed partial class SerializedProperty
    {
        public List<SerializedProperty> List => Value as List<SerializedProperty>;

        public SerializedProperty this[int tagIdx]
        {
            get { return Get(tagIdx); }
            set { List[tagIdx] = value; }
        }

        public SerializedProperty Get(int tagIdx)
        {
            if (TagType != PropertyType.List)
                throw new System.InvalidOperationException("Cannot get tag from non-list tag");
            return List[tagIdx];
        }
        public void Add(SerializedProperty tag)
        {
            if (TagType != PropertyType.List)
                throw new System.InvalidOperationException("Cannot add tag to non-list tag");
            List.Add(tag);
        }
    }
}
