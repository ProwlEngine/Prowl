using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

using Prowl.Echo;

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
        /// [GET] The <see cref="System.Type"/> that is described.
        /// </summary>
        public TypeInfo Type { get; }

        /// <summary>
        /// [GET] An array of <see cref="System.Reflection.FieldInfo">fields</see> which are cloned.
        /// </summary>
        public CloneField[] FieldData { get; private set; }
        /// <summary>
        /// [GET] Specifies whether this Type can be deep-copied / cloned by assignment.
        /// </summary>
        public bool IsCopyByAssignment { get; }
        /// <summary>
        /// [GET] Returns whether the encapsulated Type is an array.
        /// </summary>
        public bool IsArray => Type.IsArray;
        /// <summary>
        /// [GET] Returns the elements <see cref="CloneType"/>, if this one is an array.
        /// </summary>
        public CloneType ElementType { get; }
        /// <summary>
        /// [GET] Returns whether the cached Type could be derived by others.
        /// </summary>
        public bool CouldBeDerived => !Type.IsValueType && !Type.IsSealed;
        /// <summary>
        /// [GET] Specifies whether this Type requires any ownership handling, i.e. contains children or weak references.
        /// </summary>
        public bool InvestigateOwnership { get; private set; }
        /// <summary>
        /// [GET] Returns whether the cached type is handled by a <see cref="ICloneSurrogate.RequireMerge">merge surrogate</see>.
        /// </summary>
        public bool IsMergeSurrogate => Surrogate != null && Surrogate.RequireMerge;
        /// <summary>
        /// [GET] Returns the default <see cref="CloneBehavior"/> exposed by this type.
        /// </summary>
        public CloneBehavior DefaultCloneBehavior { get; }
        /// <summary>
        /// [GET] The surrogate that will handle this types cloning operations.
        /// </summary>
        public ICloneSurrogate Surrogate { get; }
        /// <summary>
        /// [GET] When available, this property returns a compiled lambda function that assigns all plain old data fields of this Type
        /// </summary>
        public AssignmentFunc PrecompiledAssignmentFunc { get; private set; }
        public SetupFunc PrecompiledSetupFunc { get; private set; }
        public Delegate PrecompiledValueAssignmentFunc { get; private set; }
        public Delegate PrecompiledValueSetupFunc { get; private set; }

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
						"Consider skipping the referring field via [CloneField] or [DontSerialize] " +
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
                    // Skip if it is private and not serialized
                    if (!field.IsPublic && !field.HasAttribute<SerializeFieldAttribute>())
                        continue;
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

			// Build precompile functions for setup and (partially) assignment
			CompileAssignmentFunc();
			CompileSetupFunc();
			CompileValueAssignmentFunc();
			CompileValueSetupFunc();
		}

		private void CompileAssignmentFunc()
		{
			if (Surrogate != null) return;
			if (Type.IsValueType) return;
			if (FieldData.Length == 0) return;

			ParameterExpression sourceParameter = Expression.Parameter(typeof(object), "source");
			ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
			ParameterExpression operationParameter = Expression.Parameter(typeof(ICloneOperation), "operation");
			ParameterExpression sourceCastVar = Expression.Variable(Type.AsType(), "sourceCast");
			ParameterExpression targetCastVar = Expression.Variable(Type.AsType(), "targetCast");

			List<Expression> mainBlock = CreateAssignmentFuncContent(operationParameter, sourceCastVar, targetCastVar);

			mainBlock.Insert(0, Expression.Assign(sourceCastVar, Expression.TypeAs(sourceParameter, Type.AsType())));
			mainBlock.Insert(1, Expression.Assign(targetCastVar, Expression.TypeAs(targetParameter, Type.AsType())));

			Expression mainBlockExpression = Expression.Block(new[] { sourceCastVar, targetCastVar }, mainBlock);
			PrecompiledAssignmentFunc = Expression.Lambda<AssignmentFunc>(mainBlockExpression, sourceParameter, targetParameter, operationParameter).Compile();
		}

		private void CompileSetupFunc()
		{
			if (Surrogate != null) return;
			if (FieldData.Length == 0) return;
			if (!InvestigateOwnership) return;

			ParameterExpression sourceParameter = Expression.Parameter(typeof(object), "source");
			ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
			ParameterExpression setupParameter = Expression.Parameter(typeof(ICloneTargetSetup), "setup");

			ParameterExpression sourceCastVar = Expression.Variable(Type.AsType(), "sourceCast");
			ParameterExpression targetCastVar = Expression.Variable(Type.AsType(), "targetCast");

			List<Expression> mainBlock = CreateSetupFuncContent(setupParameter, sourceCastVar, targetCastVar);
			if (mainBlock == null) return;

			mainBlock.Insert(0, Expression.Assign(sourceCastVar, Type.IsValueType ? Expression.Convert(sourceParameter, Type.AsType()) : Expression.TypeAs(sourceParameter, Type.AsType())));
			mainBlock.Insert(1, Expression.Assign(targetCastVar, Type.IsValueType ? Expression.Convert(targetParameter, Type.AsType()) : Expression.TypeAs(targetParameter, Type.AsType())));
			Expression mainBlockExpression = Expression.Block(new[] { sourceCastVar, targetCastVar }, mainBlock);
			PrecompiledSetupFunc = Expression.Lambda<SetupFunc>(mainBlockExpression, sourceParameter, targetParameter, setupParameter).Compile();
		}

		private void CompileValueAssignmentFunc()
		{
			if (Surrogate != null) return;
			if (!Type.IsValueType) return;
			if (FieldData.Length == 0) return;

			ParameterExpression sourceParameter = Expression.Parameter(Type.MakeByRefType(), "source");
			ParameterExpression targetParameter = Expression.Parameter(Type.MakeByRefType(), "target");
			ParameterExpression operationParameter = Expression.Parameter(typeof(ICloneOperation), "operation");

			List<Expression> mainBlock = CreateAssignmentFuncContent(operationParameter, sourceParameter, targetParameter);

			Expression mainBlockExpression = Expression.Block(mainBlock);
			PrecompiledValueAssignmentFunc = 
				Expression.Lambda(
					typeof(ValueAssignmentFunc<>).GetTypeInfo().MakeGenericType(Type.AsType()), 
					mainBlockExpression, 
					sourceParameter, 
					targetParameter, 
					operationParameter)
				.Compile();
		}

		private void CompileValueSetupFunc()
		{
			if (!Type.IsValueType) return;
			if (Surrogate != null) return;
			if (FieldData.Length == 0) return;
			if (!InvestigateOwnership) return;

			ParameterExpression sourceParameter = Expression.Parameter(Type.MakeByRefType(), "source");
			ParameterExpression targetParameter = Expression.Parameter(Type.MakeByRefType(), "target");
			ParameterExpression setupParameter = Expression.Parameter(typeof(ICloneTargetSetup), "setup");

			List<Expression> mainBlock = CreateSetupFuncContent(setupParameter, sourceParameter, targetParameter);
			if (mainBlock == null) return;

			Expression mainBlockExpression = Expression.Block(mainBlock);
			PrecompiledValueSetupFunc = 
				Expression.Lambda(
					typeof(ValueSetupFunc<>).GetTypeInfo().MakeGenericType(Type.AsType()), 
					mainBlockExpression, 
					sourceParameter, 
					targetParameter, 
					setupParameter)
				.Compile();
		}

		private List<Expression> CreateAssignmentFuncContent(Expression operation, Expression source, Expression target)
		{
			List<Expression> mainBlock = [];

			for (int i = 0; i < FieldData.Length; i++)
			{
				FieldInfo field = FieldData[i].Field;
				Expression assignment;

				if (FieldData[i].FieldType.IsCopyByAssignment)
				{
					assignment = Expression.Assign(
						Expression.Field(target, field), 
						Expression.Field(source, field));
				}
				else if (FieldData[i].FieldType.Type.IsValueType)
				{
					assignment = Expression.Call(operation, 
						s_copyHandleValue.MakeGenericMethod(field.FieldType), 
						Expression.Field(source, field), 
						Expression.Field(target, field));
				}
				else
				{
					assignment = Expression.Call(operation, 
						s_copyHandleObject.MakeGenericMethod(field.FieldType), 
						Expression.Field(source, field), 
						Expression.Field(target, field));
				}

				if ((FieldData[i].Flags & CloneFieldFlags.IdentityRelevant) != CloneFieldFlags.None)
				{
					assignment = Expression.IfThen(
						Expression.Not(Expression.Property(Expression.Property(operation, "Context"), "PreserveIdentity")),
						assignment);
				}
				mainBlock.Add(assignment);
			}

			return mainBlock;
		}

		private List<Expression> CreateSetupFuncContent(Expression setup, Expression source, Expression target)
		{
			List<Expression> mainBlock = [];
			bool anyContent = false;
			for (int i = 0; i < FieldData.Length; i++)
			{
				// Don't need to scan "plain old data" and reference fields
				if (FieldData[i].FieldType.IsCopyByAssignment) continue;
				if (FieldData[i].IsAlwaysReference) continue;
				if (FieldData[i].FieldType.Type.IsValueType && !FieldData[i].FieldType.InvestigateOwnership) continue;
				anyContent = true;

				// Call HandleObject on the fields value
				CloneBehaviorAttribute behaviorAttribute = FieldData[i].Behavior;
				FieldInfo field = FieldData[i].Field;
				Expression handleObjectExpression;
				if (FieldData[i].FieldType.Type.IsValueType)
				{
					if (behaviorAttribute == null)
					{
						handleObjectExpression = Expression.Call(setup, 
							s_setupHandleValue.MakeGenericMethod(field.FieldType), 
							Expression.Field(source, field), 
							Expression.Field(target, field),
							Expression.Constant(CloneBehavior.Default),
							Expression.Constant(null, typeof(TypeInfo)));
					}
					else
					{
						handleObjectExpression = Expression.Call(setup, 
							s_setupHandleValue.MakeGenericMethod(field.FieldType), 
							Expression.Field(source, field), 
							Expression.Field(target, field), 
							Expression.Constant(behaviorAttribute.Behavior), 
							Expression.Constant(behaviorAttribute.TargetType));
					}
				}
				else
				{
					if (behaviorAttribute == null)
					{
						handleObjectExpression = Expression.Call(setup, 
							s_setupHandleObject.MakeGenericMethod(field.FieldType), 
							Expression.Field(source, field), 
							Expression.Field(target, field),
							Expression.Constant(CloneBehavior.Default),
							Expression.Constant(null, typeof(TypeInfo)));
					}
					else if (behaviorAttribute.TargetType == null || field.FieldType.GetTypeInfo().IsAssignableFrom(behaviorAttribute.TargetType.GetTypeInfo()))
					{
						handleObjectExpression = Expression.Call(setup, 
							s_setupHandleObject.MakeGenericMethod(field.FieldType), 
							Expression.Field(source, field), 
							Expression.Field(target, field), 
							Expression.Constant(behaviorAttribute.Behavior),
							Expression.Constant(null, typeof(TypeInfo)));
					}
					else
					{
						handleObjectExpression = Expression.Call(setup, 
							s_setupHandleObject.MakeGenericMethod(field.FieldType), 
							Expression.Field(source, field), 
							Expression.Field(target, field), 
							Expression.Constant(behaviorAttribute.Behavior), 
							Expression.Constant(behaviorAttribute.TargetType));
					}
				}
				mainBlock.Add(handleObjectExpression);
			}
			if (!anyContent) return null;
			return mainBlock;
		}

		public override string ToString()
		{
			return string.Format("CloneType {0}", Type.ToString());
		}

		private static readonly MethodInfo s_setupHandleObject = typeof(ICloneTargetSetup).GetTypeInfo().DeclaredMethods.FirstOrDefault(m => m.Name == "HandleObject");
		private static readonly MethodInfo s_setupHandleValue = typeof(ICloneTargetSetup).GetTypeInfo().DeclaredMethods.FirstOrDefault(m => m.Name == "HandleValue");
		private static readonly MethodInfo s_copyHandleObject = typeof(ICloneOperation).GetTypeInfo().DeclaredMethods.FirstOrDefault(m => m.Name == "HandleObject");
		private static readonly MethodInfo s_copyHandleValue = typeof(ICloneOperation).GetTypeInfo().DeclaredMethods.FirstOrDefault(m => m.Name == "HandleValue");
	}
}
