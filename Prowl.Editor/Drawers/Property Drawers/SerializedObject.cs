using Prowl.Runtime;

namespace Prowl.Editor.Drawers
{
    public class SerializedObject
    {
        public object targetObject => targetObjects[0];
        public readonly List<object> targetObjects = new();
        public bool isEditingMultipleObjects => targetObjects.Count > 1;

        private CompoundTag serializedObject = new();

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

        /// <summary>
        /// Update the serialized object representation.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void Update()
        {
            // Loop through all target objects and serialize them
            List<CompoundTag> tags = new();
            foreach (var target in targetObjects)
                tags.Add(TagSerializer.Serialize(target) as CompoundTag);

            // Merge the tags into 1
            // Ignore tags whos value shifts between objects
            serializedObject = CompoundTag.Merge(tags);
        }

        /// <summary>
        /// Apply the serialized object to the target object.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void ApplyModifiedProperties()
        {
            // TODO: Apply only modified properties
            foreach (var target in targetObjects)
                TagSerializer.DeserializeInto(serializedObject, target);
        }

    }
}
