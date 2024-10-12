using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Prowl.Runtime.Cloning
{
    /// <summary>
    /// The CloneType class provides cached cloning-relevant information
    /// that has been generated basing on a <see cref="System.Type"/>.
    /// </summary>
    public sealed class CloneType
	{
		public delegate void AssignmentFunc(object source, object target, ICloneOperation operation);
		public delegate void SetupFunc(object source, object target, ICloneTargetSetup setup);
		public delegate void ValueAssignmentFunc<T>(ref T source, ref T target, ICloneOperation operation) where T : struct;
		public delegate void ValueSetupFunc<T>(ref T source, ref T target, ICloneTargetSetup setup) where T : struct;

		public readonly struct CloneField(FieldInfo field, CloneType typeInfo, CloneFieldFlags flags, CloneBehaviorAttribute behavior, bool isAlwaysReference)
        {
            public readonly FieldInfo Field { get; } = field;
            public readonly CloneType FieldType { get; } = typeInfo;
            public readonly CloneFieldFlags Flags { get; } = flags;
            public readonly CloneBehaviorAttribute Behavior { get; } = behavior;
            public readonly bool IsAlwaysReference { get; } = isAlwaysReference;

            public override readonly string ToString() => string.Format("Field {0}", Field.ToString());
        }

        /// <summary>
        /// The <see cref="System.Type"/> that is described.
        /// </summary>
        public TypeInfo Type { get; }

        /// <summary>
        /// An array of <see cref="System.Reflection.FieldInfo">fields</see> which are cloned.
        /// </summary>
        public CloneField[] FieldData { get; private set; }
        /// <summary>
        /// Specifies whether this Type can be deep-copied / cloned by assignment.
        /// </summary>
        public bool IsCopyByAssignment { get; }
        /// <summary>
        /// Returns whether the encapsulated Type is an array.
        /// </summary>
        public bool IsArray => Type.IsArray;
        /// <summary>
        /// Returns the elements <see cref="CloneType"/>, if this one is an array.
        /// </summary>
        public CloneType ElementType { get; }
        /// <summary>
        /// Returns whether the cached Type could be derived by others.
        /// </summary>
        public bool CouldBeDerived => !Type.IsValueType && !Type.IsSealed;
        /// <summary>
        /// Specifies whether this Type requires any ownership handling, i.e. contains children or weak references.
        /// </summary>
        public bool InvestigateOwnership { get; private set; }
        /// <summary>
        /// Returns whether the cached type is handled by a <see cref="ICloneSurrogate.RequireMerge">merge surrogate</see>.
        /// </summary>
        public bool IsMergeSurrogate => Surrogate != null && Surrogate.RequireMerge;
        /// <summary>
        /// Returns the default <see cref="CloneBehavior"/> exposed by this type.
        /// </summary>
        public CloneBehavior DefaultCloneBehavior { get; }
        /// <summary>
        /// The surrogate that will handle this types cloning operations.
        /// </summary>
        public ICloneSurrogate Surrogate { get; }

        /// <summary>
        /// Creates a new CloneType based on a <see cref="System.Type"/>, gathering all the information that is necessary for cloning.
        /// </summary>
        /// <param name="type"></param>
        public CloneType(Type type)
		{
			Type = type.GetTypeInfo();
			IsCopyByAssignment =
				Type.IsDeepCopyByAssignment() ||
				typeof(MemberInfo).GetTypeInfo().IsAssignableFrom(Type); /* Handle MemberInfo like POD */ 
			InvestigateOwnership = !IsCopyByAssignment;
			Surrogate = CloneProvider.GetSurrogateFor(Type);
			if (Type.IsArray)
			{
				if (Type.GetArrayRank() > 1)
				{
					throw new NotSupportedException(
						"Cloning multidimensional arrays is not supported in Prowl. " +
                        "Consider skipping the referring field via [CloneField], [NonSerialized] or [SerializeIgnore] " +
						"attribute, or use a regular array instead.");
				}
				ElementType = CloneProvider.GetCloneType(Type.GetElementType());
			}

			CloneBehaviorAttribute defaultBehaviorAttrib = CloneProvider.GetCloneBehaviorAttribute(Type);
			if (defaultBehaviorAttrib != null && defaultBehaviorAttrib.Behavior != CloneBehavior.Default)
				DefaultCloneBehavior = defaultBehaviorAttrib.Behavior;
			else
				DefaultCloneBehavior = CloneBehavior.ChildObject;
		}

		public void Init()
		{
			if (Surrogate != null) return;
			if (IsCopyByAssignment) return;

			if (Type.IsArray)
			{
				InvestigateOwnership = !(ElementType.IsCopyByAssignment || (ElementType.Type.IsValueType && !ElementType.InvestigateOwnership));
				return;
			}
			else
			{
				InvestigateOwnership = typeof(ICloneExplicit).GetTypeInfo().IsAssignableFrom(Type) || Surrogate != null;
			}

			// Retrieve field data
			List<CloneField> fieldData = [];
			foreach (FieldInfo field in Type.DeclaredFieldsDeep())
			{
				if (field.IsStatic) continue;
				if (field.IsInitOnly) continue;
				if (field.HasAttribute<ManuallyClonedAttribute>()) continue;
				if (field.DeclaringType.GetTypeInfo().HasAttribute<ManuallyClonedAttribute>()) continue;

				CloneFieldFlags flags = CloneFieldFlags.None;
				CloneFieldAttribute fieldAttrib = field.GetAttributes<CloneFieldAttribute>().FirstOrDefault();
				if (fieldAttrib != null) flags = fieldAttrib.Flags;

                if (!flags.HasFlag(CloneFieldFlags.DontSkip))
                {
                    if (field.HasAttribute<SerializeIgnoreAttribute>())
                        continue;
                    if (field.HasAttribute<NonSerializedAttribute>())
                        continue;
                }
				if (flags.HasFlag(CloneFieldFlags.Skip))
					continue;

				CloneBehaviorAttribute behaviorAttrib = field.GetAttributes<CloneBehaviorAttribute>().FirstOrDefault();
				CloneType fieldType = CloneProvider.GetCloneType(field.FieldType);
				bool isAlwaysReference = 
					(behaviorAttrib != null) && 
					(behaviorAttrib.TargetType == null || field.FieldType.GetTypeInfo().IsAssignableFrom(behaviorAttrib.TargetType.GetTypeInfo())) &&
					(behaviorAttrib.Behavior == CloneBehavior.Reference);

				// Can this field own any objects itself?
				if (!InvestigateOwnership)
				{
					bool fieldCanOwnObjects = true;
					if (fieldType.IsCopyByAssignment)
						fieldCanOwnObjects = false;
					if (isAlwaysReference)
						fieldCanOwnObjects = false;
					if (fieldType.Type.IsValueType && !fieldType.InvestigateOwnership)
						fieldCanOwnObjects = false;

					if (fieldCanOwnObjects)
						InvestigateOwnership = true;
				}

				CloneField fieldEntry = new(field, fieldType, flags, behaviorAttrib, isAlwaysReference);
				fieldData.Add(fieldEntry);
			}
			FieldData = [.. fieldData];
		}

        public void PerformAssignment(ref object source, ref object target, ICloneOperation operation)
        {
            if (Surrogate != null || Type.IsValueType || FieldData.Length == 0) return;

            foreach (CloneField fieldData in FieldData)
            {
                if ((fieldData.Flags & CloneFieldFlags.IdentityRelevant) != CloneFieldFlags.None && operation.Context.PreserveIdentity)
                    continue;

                object sourceValue = fieldData.Field.GetValue(source);
                object targetValue = fieldData.Field.GetValue(target);

                if (fieldData.FieldType.IsCopyByAssignment)
                {
                    fieldData.Field.SetValue(target, sourceValue);
                }
                else if (fieldData.FieldType.Type.IsValueType)
                {
                    MethodInfo method = typeof(ICloneOperation).GetMethod("HandleValue").MakeGenericMethod(fieldData.Field.FieldType);
                    method.Invoke(operation, [sourceValue, targetValue]);
                    fieldData.Field.SetValue(target, targetValue);
                }
                else
                {
                    MethodInfo method = typeof(ICloneOperation).GetMethod("HandleObject").MakeGenericMethod(fieldData.Field.FieldType);
                    object? result = method.Invoke(operation, [sourceValue, targetValue]);
                    fieldData.Field.SetValue(target, result);
                }
            }
        }

        public void PerformAssignment<T>(ref T source, ref T target, ICloneOperation operation) where T : struct
        {
            if (Surrogate != null || FieldData.Length == 0) return;

            foreach (CloneField fieldData in FieldData)
            {
                if ((fieldData.Flags & CloneFieldFlags.IdentityRelevant) != CloneFieldFlags.None && operation.Context.PreserveIdentity)
                    continue;

                if (fieldData.FieldType.IsCopyByAssignment)
                {
                    object? sourceValue = fieldData.Field.GetValue(source);
                    fieldData.Field.SetValue(target, sourceValue);
                }
                else if (fieldData.FieldType.Type.IsValueType)
                {
                    MethodInfo method = typeof(ICloneOperation).GetMethod("HandleValue").MakeGenericMethod(fieldData.Field.FieldType);
                    object? sourceValue = fieldData.Field.GetValue(source);
                    object? targetValue = fieldData.Field.GetValue(target);
                    method.Invoke(operation, [sourceValue, targetValue]);
                    fieldData.Field.SetValue(target, targetValue);
                }
                else
                {
                    MethodInfo method = typeof(ICloneOperation).GetMethod("HandleObject").MakeGenericMethod(fieldData.Field.FieldType);
                    object? sourceValue = fieldData.Field.GetValue(source);
                    object? targetValue = fieldData.Field.GetValue(target);
                    object? result = method.Invoke(operation, [sourceValue, targetValue]);
                    fieldData.Field.SetValue(target, result);
                }
            }
        }

        public void PerformSetup(ref object source, ref object target, ICloneTargetSetup setup)
        {
            if (Surrogate != null || FieldData.Length == 0 || !InvestigateOwnership) return;

            foreach (CloneField fieldData in FieldData)
            {
                if (fieldData.FieldType.IsCopyByAssignment || fieldData.IsAlwaysReference ||
                    (fieldData.FieldType.Type.IsValueType && !fieldData.FieldType.InvestigateOwnership))
                    continue;

                CloneBehavior behavior = fieldData.Behavior?.Behavior ?? CloneBehavior.Default;
                TypeInfo? behaviorTarget = fieldData.Behavior?.TargetType?.GetTypeInfo();

                object? sourceValue = fieldData.Field.GetValue(source);
                object? targetValue = fieldData.Field.GetValue(target);

                if (fieldData.FieldType.Type.IsValueType)
                {
                    MethodInfo method = typeof(ICloneTargetSetup).GetMethod("HandleValue").MakeGenericMethod(fieldData.Field.FieldType);
                    method.Invoke(setup, [sourceValue, targetValue, behavior, behaviorTarget]);
                }
                else
                {
                    MethodInfo method = typeof(ICloneTargetSetup).GetMethod("HandleObject").MakeGenericMethod(fieldData.Field.FieldType);
                    method.Invoke(setup, [sourceValue, targetValue, behavior, behaviorTarget]);
                }
            }
        }

        public void PerformSetup<T>(ref T source, ref T target, ICloneTargetSetup setup) where T : struct
        {
            if (Surrogate != null || FieldData.Length == 0 || !InvestigateOwnership) return;

            foreach (CloneField fieldData in FieldData)
            {
                if (fieldData.FieldType.IsCopyByAssignment || fieldData.IsAlwaysReference ||
                    (fieldData.FieldType.Type.IsValueType && !fieldData.FieldType.InvestigateOwnership))
                    continue;

                CloneBehavior behavior = fieldData.Behavior?.Behavior ?? CloneBehavior.Default;
                TypeInfo? behaviorTarget = fieldData.Behavior?.TargetType?.GetTypeInfo();

                object? sourceValue = fieldData.Field.GetValue(source);
                object? targetValue = fieldData.Field.GetValue(target);

                if (fieldData.FieldType.Type.IsValueType)
                {
                    MethodInfo method = typeof(ICloneTargetSetup).GetMethod("HandleValue").MakeGenericMethod(fieldData.Field.FieldType);
                    method.Invoke(setup, [sourceValue, targetValue, behavior, behaviorTarget]);
                }
                else
                {
                    MethodInfo method = typeof(ICloneTargetSetup).GetMethod("HandleObject").MakeGenericMethod(fieldData.Field.FieldType);
                    method.Invoke(setup, [sourceValue, targetValue, behavior, behaviorTarget]);
                }
            }
        }

        public override string ToString()
		{
			return string.Format("CloneType {0}", Type.ToString());
		}
	}
}
