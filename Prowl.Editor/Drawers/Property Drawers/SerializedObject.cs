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
            serializedObject = MergeTags(tags);
        }

        // TODO: Find a faster way to do this, this is just for the Inspector to support multiple objects at once
        private CompoundTag MergeTags(List<CompoundTag> tags)
        {
            CompoundTag result = new CompoundTag();

            if (tags.Count == 0) return result;

            var referenceTag = tags[0];
            foreach (var nameVal in referenceTag.Tags)
            {
                bool isConsistent = true;
                Tag firstTagValue = nameVal.Value;

                for (int i = 1; i < tags.Count; i++)
                {
                    if (tags[i].TryGet(nameVal.Key, out Tag nTag) && nTag.GetTagType() == firstTagValue.GetTagType())
                    {
                        // Check for value equality for primitive types
                        if (firstTagValue is CompoundTag)
                        {
                            // Handle recursion for CompoundTags
                            var subTagsToMerge = tags.Select(tag => tag.Get<CompoundTag>(nameVal.Key)).ToList();
                            var mergedSubTag = MergeTags(subTagsToMerge);
                            if (mergedSubTag.Count > 0) // Only add if the mergedSubTag contains keys
                            {
                                result.Add(nameVal.Key, mergedSubTag);
                            }
                            isConsistent = false; // Prevent adding the CompoundTag again outside the if-block
                        }
                        // List tags and Byte arrays are ignored when multiple objects are selected
                        else if (firstTagValue is ListTag || firstTagValue is ByteArrayTag)
                        {
                            isConsistent = false;
                            break;
                        }
                        else if (!firstTagValue.GetValue().Equals(tags[i].Get<Tag>(nameVal.Key).GetValue()))
                        {
                            isConsistent = false;
                            break;
                        }
                    }
                    else
                    {
                        isConsistent = false;
                        break;
                    }
                }

                if (isConsistent)
                    result.Add(nameVal.Key, firstTagValue);
            }

            return result;
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
