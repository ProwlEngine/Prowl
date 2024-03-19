using Prowl.Runtime;

namespace Prowl.Editor.Drawers
{
    public class SerializedObject
    {
        public object targetObject => targetObjects[0];
        public readonly List<object> targetObjects = new();
        public bool isEditingMultipleObjects => targetObjects.Count > 1;

        private SerializedProperty serializedObject = SerializedProperty.NewCompound();

        public SerializedObject(params object[] targetObject)
        {
            // Object must not be a primitive type or null
            if (targetObject == null || targetObject.Length == 0)
                throw new ArgumentNullException("targetObject");
            if (targetObject.Any(x => x == null))
                throw new ArgumentNullException("targetObject", "targetObject cannot contain null values.");
            if (targetObject.Any(x => x.GetType().IsPrimitive))
                throw new ArgumentException("targetObject", "targetObject cannot contain primitive types.");
            // They must all be of the same type
            if (targetObject.Any(x => x.GetType() != targetObject[0].GetType()))
                throw new ArgumentException("targetObject", "targetObject must all be of the same type.");

            targetObjects.AddRange(targetObject);

            Update();
        }

        public SerializedProperty? FindProperty(string name) => serializedObject.Get(name);

        public SerializedProperty? FindPropertyDeep(string name) => serializedObject.Find(name);

        /// <summary>
        /// Update the serialized object representation.
        /// By serializing all target objects into 1 CompoundTag.
        /// </summary>
        public void Update()
        {
            // Loop through all target objects and serialize them
            List<SerializedProperty> tags = new();
            foreach (var target in targetObjects)
                tags.Add(Serializer.Serialize(target));

            // Merge the tags into 1
            // Ignore tags whos value shifts between objects
            serializedObject = SerializedProperty.Merge(tags);
        }

        /// <summary>
        /// Apply the serialized object to the target object.
        /// This will apply the CompoundTag stored to all target objects.
        /// </summary>
        public void ApplyModifiedProperties()
        {
            // TODO: Apply only modified properties
            foreach (var target in targetObjects)
                Serializer.DeserializeInto(serializedObject, target);
        }

    }
}
