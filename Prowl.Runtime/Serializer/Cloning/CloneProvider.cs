using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Runtime.Utils;

namespace Prowl.Runtime.Cloning
{
    public class CloneProvider : ICloneTargetSetup, ICloneOperation
	{
		private struct LateSetupEntry(object source, object target)
        {
			public object Source = source;
			public object Target = target;

            public override readonly bool Equals(object? obj)
			{
				if (obj is not LateSetupEntry) return false;
				LateSetupEntry other = (LateSetupEntry)obj;
				return
					object.ReferenceEquals(other.Source, Source) &&
					object.ReferenceEquals(other.Target, Target);
			}

            public override readonly int GetHashCode() => HashCode.Combine(Target is not null ? Target : 0, Source is not null ? Source : 0);
        }

		private class LocalCloneBehavior(TypeInfo targetType, CloneBehavior behavior)
        {
            public TypeInfo TargetType { get; } = targetType;
            public CloneBehavior Behavior { get; } = behavior;
            public bool Locked { get; set; }
        }

        private object _sourceRoot = null;
		private	object _targetRoot = null;
		private	object _currentObject = null;
		private	CloneType _currentCloneType	= null;
		private readonly Dictionary<object,object> _targetMapping = new (ReferenceEqualityComparer.Instance);
		private readonly HashSet<object> _targetSet = new (ReferenceEqualityComparer.Instance);
		private readonly HashSet<LateSetupEntry> _lateSetupSchedule	= [];
		private readonly HashSet<object> _handledObjects = new (ReferenceEqualityComparer.Instance);
		private	List<LocalCloneBehavior> _localBehavior = [];

        /// <summary>
        /// [GET] Provides information about the context in which the operation is performed.
        /// </summary>
        public CloneProviderContext Context { get; } = CloneProviderContext.Default;

        public CloneProvider(CloneProviderContext context = null)
		{
			if (context != null) Context = context;
		}

		/// <summary>
		/// Clones the specified object and returns the cloned instance.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="source"></param>
		/// <param name="preserveCache">
		/// If true, the mapping between source and target object graph is preserved.
		/// 
		/// This can be useful for doing a partial clone operation that is later continued or
		/// repeated using the same <see cref="CloneProvider"/> instance. Since the mapping is
		/// already present, performance of subsequent clone operations within the same object
		/// graph can benefit by this.
		/// </param>
		public T CloneObject<T>(T source, bool preserveCache = false)
		{
			object target; // Don't use T, we'll need to make sure "target" is a reference Type
			try
			{
				target = BeginCloneOperation(source);
				PerformCopyObject(source, target, null);
			}
			finally
			{
				EndCloneOperation(preserveCache);
			}
			return (T)target;
		}

		/// <summary>
		/// Copies the specified source object graph to the specified target object
		/// graph. Where possible, existing objects will be preserved and updated,
		/// rather than being overwritten.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="source"></param>
		/// <param name="target"></param>
		/// <param name="preserveCache">
		/// If true, the mapping between source and target object graph is preserved.
		/// 
		/// This can be useful for doing a partial clone operation that is later continued or
		/// repeated using the same <see cref="CloneProvider"/> instance. Since the mapping is
		/// already present, performance of subsequent clone operations within the same object
		/// graph can benefit by this.
		/// </param>
		public void CopyObject<T>(T source, T target, bool preserveCache = false)
		{
			try
			{
				BeginCloneOperation(source, target);
				PerformCopyObject(source, target, null);
			}
			finally
			{
				EndCloneOperation(preserveCache);
			}
		}

        /// <summary>
        /// Clears the clone providers internal mapping between source and target
        /// object graph. This is done automatically, unless the copy or clone
        /// operation has been performed with explicitly preserving the cache.
        ///
        /// NOTE: You must call this before the project can Recompile, as it holds references to type that can prevent assemblies from being garbage collected.
        /// </summary>
		public void ClearCachedMapping()
		{
			_targetMapping.Clear();
			_targetSet.Clear();
		}
		
		/// <summary>
		/// Prepares the clone operation by generating a mapping between source
		/// and target object graph, and creating the target objects where required in the
		/// process. 
		/// </summary>
		/// <param name="source"></param>
		/// <param name="target"></param>
		/// <returns>Returns a reference to the target root object.</returns>
		private object BeginCloneOperation(object source, object target = null)
		{
			if (_targetSet.Contains(source))
			{
				throw new InvalidOperationException("You may not use a CloneProvider for cloning its own clone results after preserving the internal cache.");
			}

			// Prepare the target object graph we'll copy the source graph's values over to
			_sourceRoot = source;
			_targetRoot = target;
			PrepareCloneGraph();

			// Get the target object which was either re-used or created in the preparation step
			target = GetTargetOf(source);
			_targetRoot = target;
			return target;
		}

		/// <summary>
		/// Ends the current clone operation by clearing all the working data that was
		/// allocated in the process.
		/// </summary>
		/// <param name="preserveMapping">
		/// If true, the mapping between source and target object graph is preserved.
		/// 
		/// This can be useful for doing a partial clone operation that is later continued or
		/// repeated using the same <see cref="CloneProvider"/> instance. Since the mapping is
		/// already present, performance of subsequent clone operations within the same object
		/// graph can benefit by this.
		/// </param>
		private void EndCloneOperation(bool preserveMapping)
		{
			_sourceRoot = null;
			_currentObject = null;
			_currentCloneType = null;

			_localBehavior.Clear();
			_lateSetupSchedule.Clear();
			_handledObjects.Clear();

			if (!preserveMapping)
				ClearCachedMapping();
		}

		/// <summary>
		/// Registers a mapping from the specified source object to its
		/// target object graph equivalent. This is a one-to-one relation.
		/// </summary>
		/// <param name="source"></param>
		/// <param name="target"></param>
		private void SetTargetOf(object source, object target)
		{
			if (source is null) return;

			if (_targetSet.Add(target))
			{
				_targetMapping[source] = target;
			}
		}

		/// <summary>
		/// Retrieves the target graph equivalent of the specified source graph object.
		/// 
		/// Note that the resulting target object will (expected to) be the
		/// same as the source object in cases where there is no mapping, but
		/// reference assignment instead.
		/// </summary>
		/// <param name="source"></param>
		private object GetTargetOf(object source)
		{
			if (source is null) return null;

            if (_targetMapping.TryGetValue(source, out object target))
                return target;
            else
                return source;
        }

        /// <summary>
        /// Adds the specified source object to the handled object stack when
        /// required and returns whether the object need to be investigated as 
        /// part of the copy step at all.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="typeData"></param>
        private bool PushCurrentObject(object source, CloneType typeData) => typeData.Type.IsValueType || source is null || _handledObjects.Add(source);

        /// <summary>
        /// Removes the specified source object from the handled object stack
        /// after finishing its copy step.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="typeData"></param>
        private bool PopCurrentObject(object source, CloneType typeData) => typeData.Type.IsValueType || source is null || _handledObjects.Remove(source);

        private void PrepareCloneGraph()
		{
			// Visit the object graph in order to determine which objects to clone
			PrepareObjectCloneGraph(_sourceRoot, _targetRoot, null, CloneBehavior.ChildObject);
			_localBehavior.Clear();

			// Perform late setup for surrogate objects that required it
			foreach (LateSetupEntry lateSetup in _lateSetupSchedule)
			{
				CloneType typeData = GetCloneType((lateSetup.Source ?? lateSetup.Target).GetType());
				ICloneSurrogate surrogate = typeData.Surrogate;

				object lateSetupTarget = lateSetup.Target;
				surrogate.LateSetup(lateSetup.Source, ref lateSetupTarget, this);
				SetTargetOf(lateSetup.Source ?? lateSetup.Target, lateSetupTarget);
			}
		}

		private void PrepareObjectCloneGraph(object source, object target, CloneType typeData, CloneBehavior behavior = CloneBehavior.Default)
		{
			// Early-out for null values
			if (source is null)
			{
				if (target is null) return;
				typeData ??= GetCloneType(target.GetType());
				if (!typeData.IsMergeSurrogate) return;
			}
			
			// Determine the object Type and early-out if it's just plain old data
			typeData ??= GetCloneType(source.GetType());
			if (typeData.IsCopyByAssignment) return;
			if (typeData.Type.IsValueType && !typeData.InvestigateOwnership) return;
			
			// Determine cloning behavior for this object
			object behaviorLock = null;
			if (!typeData.Type.IsValueType && source is not null)
			{
				// If we already registered a target for that source, stop right here.
				if (_targetMapping.ContainsKey(source))
					return;

				// If no specific behavior was specified, fetch the default one set by class and field attributes
				if (behavior == CloneBehavior.Default)
				{
					behavior = GetCloneBehavior(typeData, true, out behaviorLock);
				}
				// Apply the current behavior
				if (behavior != CloneBehavior.ChildObject)
				{
					UnlockCloneBehavior(behaviorLock);
					return;
				}

				// If the target doesn't match the source, discard it
				if (target != null && target.GetType() != typeData.Type.AsType())
					target = null;
			}

			object lastObject = _currentObject;
			CloneType lastCloneType = _currentCloneType;
			_currentObject = source;
			_currentCloneType = typeData;

			// If it's a value type, use the fast lane without surrogate and custom checks
			if (typeData.Type.IsValueType)
			{
				//target ??= typeData.Type.CreateInstanceOf();
                target ??= Activator.CreateInstance(typeData.Type.AsType());
				PrepareObjectChildCloneGraph(source, target, typeData);
			}
			// Check whether there is a surrogate for this object
			else if (typeData.Surrogate != null)
			{
                typeData.Surrogate.SetupCloneTargets(source, target, out bool requireLateSetup, this);
                if (requireLateSetup)
				{
					_lateSetupSchedule.Add(new LateSetupEntry(source, target));
				}
			}
			// Otherwise, use the default algorithm
			else
			{
				// Create a new target array. Always necessary due to their immutable size.
				Array originalTargetArray = null;
				if (typeData.IsArray)
				{
					Array sourceArray = source as Array;
					originalTargetArray = target as Array;
					target = Array.CreateInstance(typeData.ElementType.Type.AsType(), sourceArray.Length);
				}
				// Only create target object when no reuse is possible
				//else target ??= typeData.Type.CreateInstanceOf();
				else target ??= Activator.CreateInstance(typeData.Type.AsType());

				// Create a mapping from the source object to the target object
				SetTargetOf(source, target);
				
				// If we are dealing with an array, use the original one for object reuse mapping
				if (originalTargetArray != null) target = originalTargetArray;

                // If it implements custom cloning behavior, use that
                if (source is ICloneExplicit customSource)
                {
                    customSource.SetupCloneTargets(target, this);
                }
                // Otherwise, traverse its child objects using default behavior
                else
                {
                    PrepareObjectChildCloneGraph(source, target, typeData);
                }
            }
			
			_currentObject = lastObject;
			_currentCloneType = lastCloneType;
			UnlockCloneBehavior(behaviorLock);
		}

		private void PrepareObjectChildCloneGraph(object source, object target, CloneType typeData)
		{
			// If the object is a simple and shallow type, there's nothing to investigate.
			if (!typeData.InvestigateOwnership) return;

			// If it's an array, we'll need to traverse its elements
			if (typeData.IsArray)
			{
				CloneType elementTypeData = typeData.ElementType.CouldBeDerived ? null : typeData.ElementType;
				Array sourceArray = source as Array;
				Array targetArray = target as Array;
				for (int i = 0; i < sourceArray.Length; i++)
				{
					object sourceElementValue = sourceArray.GetValue(i);
					object targetElementValue = targetArray.Length > i ? targetArray.GetValue(i) : null;
					PrepareObjectCloneGraph(
						sourceElementValue, 
						targetElementValue, 
						elementTypeData);
				}
			}
			// If it's an object, we'll need to traverse its fields
			else
            {
                typeData.PrecompiledSetupFunc?.Invoke(source, target, this);
            }
        }

		private void PrepareValueCloneGraph<T>(ref T source, ref T target, CloneType typeData) where T : struct
		{
			// Determine the object Type and early-out if it's just plain old data
			typeData ??= GetCloneType(typeof(T));
			if (typeData.IsCopyByAssignment) return;
			if (!typeData.InvestigateOwnership) return;

			object lastObject = _currentObject;
			CloneType lastCloneType = _currentCloneType;
			_currentObject = source;
			_currentCloneType = typeData;

			PrepareValueChildCloneGraph<T>(ref source, ref target, typeData);
			
			_currentObject = lastObject;
			_currentCloneType = lastCloneType;
		}

		private void PrepareValueChildCloneGraph<T>(ref T source, ref T target, CloneType typeData) where T : struct
		{
            if (typeData.PrecompiledValueSetupFunc is CloneType.ValueSetupFunc<T> typedSetupFunc)
            {
                typedSetupFunc(ref source, ref target, this);
            }
        }

		private void PerformCopyObject(object source, object target, CloneType typeData)
		{
			// Early-out for same-instance values
			if (object.ReferenceEquals(source, target)) return;

			// Early-out for null values
			if (source is null)
			{
				typeData ??= GetCloneType(target.GetType());
				if (!typeData.IsMergeSurrogate) return;
			}

			// If we already handled this object, back out to avoid loops.
			typeData ??= GetCloneType(source.GetType());
			if (!PushCurrentObject(source, typeData)) return;
			
			object lastObject = _currentObject;
			CloneType lastCloneType = _currentCloneType;
			_currentObject = source;
			_currentCloneType = typeData;

            // Check whether there is a surrogare for this object
            if (typeData.Surrogate != null)
            {
                typeData.Surrogate.CopyDataTo(source, target, this);
            }
            // If it implements custom cloning behavior, use that
            else if (source is ICloneExplicit customSource)
            {
                customSource.CopyDataTo(target, this);
            }
            // Otherwise, traverse its child objects using default behavior
            else if (!typeData.IsCopyByAssignment)
            {
                PerformCopyChildObject(source, target, typeData);
            }

            _currentObject = lastObject;
			_currentCloneType = lastCloneType;
			PopCurrentObject(source, typeData);
		}

		private void PerformCopyChildObject(object source, object target, CloneType typeData)
		{
			// Handle array data
			if (typeData.IsArray)
			{
				Array sourceArray = source as Array;
				Array targetArray = target as Array;
				CloneType sourceElementType = typeData.ElementType;

				// If the array contains plain old data, no further handling is required
				if (sourceElementType.IsCopyByAssignment)
				{
					sourceArray.CopyTo(targetArray, 0);
				}
				// If the array contains a value type, handle each element in order to allow them to perform a mapping
				else if (sourceElementType.Type.IsValueType)
				{
					for (int i = 0; i < sourceArray.Length; ++i)
					{
						object sourceElement = sourceArray.GetValue(i);
						object targetElement = targetArray.GetValue(i);
						PerformCopyObject(
							sourceElement, 
							targetElement, 
							sourceElementType);
						targetArray.SetValue(targetElement, i);
					}
				}
				// If it contains reference types, a direct element mapping is necessary, as well as complex value handling
				else
				{
					bool couldRequireMerge = sourceElementType.CouldBeDerived || sourceElementType.IsMergeSurrogate;
					for (int i = 0; i < sourceArray.Length; ++i)
					{
						CloneType elementTypeData = sourceElementType.CouldBeDerived ? null : sourceElementType;

						object sourceElement = sourceArray.GetValue(i);
						object targetElement;

						// If there is no source value, check if we're dealing with a merge surrogate and get the old target value when necessary.
						bool sourceNullMerge = false;
						if (couldRequireMerge && sourceElement is null)
						{
							if (elementTypeData == null || elementTypeData.IsMergeSurrogate)
							{
								sourceElement = targetArray.GetValue(i);
								if (sourceElement is not null)
								{
									elementTypeData ??= GetCloneType(sourceElement.GetType());
									if (elementTypeData.IsMergeSurrogate)
										sourceNullMerge = true;
									else
										sourceElement = null;
								}
							}
						}

						// Perform target mapping and assign the copied value to the target field
						targetElement = GetTargetOf(sourceElement);
						PerformCopyObject(sourceNullMerge ? null : sourceElement, targetElement, elementTypeData);
						targetArray.SetValue(targetElement, i);
					}
				}
			}
			// Handle structural data
			else
			{
				// When available, take the shortcut for assigning all POD fields
				if (typeData.PrecompiledAssignmentFunc != null)
				{
					typeData.PrecompiledAssignmentFunc(source, target, this);
				}
				// Otherwise, fall back to reflection. This is currently necessary for value types.
				else
				{
					for (int i = 0; i < typeData.FieldData.Length; i++)
					{
						if ((typeData.FieldData[i].Flags & CloneFieldFlags.IdentityRelevant) != CloneFieldFlags.None && Context.PreserveIdentity)
							continue;
						PerformCopyField(source, target, typeData.FieldData[i].Field, typeData.FieldData[i].FieldType.IsCopyByAssignment);
					}
				}
			}
		}

		private void PerformCopyField(object source, object target, FieldInfo field, bool isPlainOldData)
		{
			// Perform the quick version for plain old data
			if (isPlainOldData)
			{
				field.SetValue(target, field.GetValue(source));
			}
			// If this field stores a value type, no assignment or mapping is necessary. Just handle the struct.
			else if (field.FieldType.GetTypeInfo().IsValueType)
			{
				object sourceFieldValue = field.GetValue(source);
				object targetFieldValue = field.GetValue(target);
				PerformCopyObject(
					sourceFieldValue, 
					targetFieldValue, 
					GetCloneType(field.FieldType));
				field.SetValue(target, targetFieldValue);
			}
			// If it's a reference type, the value needs to be mapped from source to target
			else
			{
				object sourceFieldValue = field.GetValue(source);
				object targetFieldValue;

				// If there is no source value, check if we're dealing with a merge surrogate and get the old target value when necessary.
				bool sourceNullMerge = false;
				CloneType typeData = null;
				if (sourceFieldValue is null)
				{
					sourceFieldValue = field.GetValue(target);
					if (sourceFieldValue is not null)
					{
						typeData = GetCloneType(sourceFieldValue.GetType());
						if (typeData.IsMergeSurrogate)
							sourceNullMerge = true;
						else
							sourceFieldValue = null;
					}
				}

				// Perform target mapping and assign the copied value to the target field
				targetFieldValue = GetTargetOf(sourceFieldValue);
				PerformCopyObject(sourceNullMerge ? null : sourceFieldValue, targetFieldValue, typeData);
				field.SetValue(target, targetFieldValue);
			}
		}
		
		private void PerformCopyValue<T>(ref T source, ref T target, CloneType typeData) where T : struct
		{
			typeData ??= GetCloneType(typeof(T));
			
			object lastObject = _currentObject;
			CloneType lastCloneType = _currentCloneType;
			_currentObject = source;
			_currentCloneType = typeData;
			
			if (typeData.IsCopyByAssignment)
			{
				target = source;
			}
			else
			{
				PerformCopyChildValue(ref source, ref target, typeData);
			}

			_currentObject = lastObject;
			_currentCloneType = lastCloneType;
		}

		private void PerformCopyChildValue<T>(ref T source, ref T target, CloneType typeData) where T : struct
		{
            if (typeData.PrecompiledValueAssignmentFunc is CloneType.ValueAssignmentFunc<T> typedAssignmentFunc)
            {
                typedAssignmentFunc(ref source, ref target, this);
            }
        }

		private void PushCloneBehavior(LocalCloneBehavior behavior)
		{
			_localBehavior.Add(behavior);
		}

		private void PopCloneBehavior()
		{
			_localBehavior.RemoveAt(_localBehavior.Count - 1);
		}

		private CloneBehavior GetCloneBehavior(CloneType sourceType, bool lockBehavior, out object acquiredLock)
		{
			CloneBehavior defaultBehavior = (sourceType != null) ? sourceType.DefaultCloneBehavior : CloneBehavior.ChildObject;

			// Local behavior rules
			acquiredLock = null;
			for (int i = _localBehavior.Count - 1; i >= 0; i--)
			{
				if (_localBehavior[i].Locked) continue;
				if (_localBehavior[i].TargetType == null || (sourceType != null && _localBehavior[i].TargetType.IsAssignableFrom(sourceType.Type)))
				{
					acquiredLock = _localBehavior[i];
                    _localBehavior[i].Locked = lockBehavior;
					CloneBehavior behavior = _localBehavior[i].Behavior;
					return (behavior != CloneBehavior.Default) ? behavior : defaultBehavior;
				}
			}

			// Global behavior rules
			return defaultBehavior;
		}

		private void UnlockCloneBehavior(object behaviorLock)
		{
			if (behaviorLock == null) return;

			for (int i = _localBehavior.Count - 1; i >= 0; i--)
			{
				if (_localBehavior[i].Locked && _localBehavior[i] == behaviorLock)
				{
                    _localBehavior[i].Locked = false;
				}
			}
		}
		
		void ICloneTargetSetup.AddTarget<T>(T source, T target)
		{
			SetTargetOf(source, target);
		}

		void ICloneTargetSetup.HandleObject<T>(T source, T target, CloneBehavior behavior, TypeInfo behaviorTarget)
		{
			// Since "fallback to default" is triggered by source being equal to the currently handled object,
			// we need to make sure that this cannot be triggered accidentally by auto-generated clone lambdas.
			// Only allow the fallback when the object in question is actually cloned by user code!
			bool calledFromUserCode = _currentObject is ICloneExplicit || _currentCloneType.Surrogate != null;
			if (object.ReferenceEquals(source, _currentObject) && calledFromUserCode)
			{
				PrepareObjectChildCloneGraph(_currentObject, target, _currentCloneType);
			}
			else if (behaviorTarget != null)
			{
				PushCloneBehavior(new LocalCloneBehavior(behaviorTarget, behavior));
				PrepareObjectCloneGraph(source, target, null);
				PopCloneBehavior();
			}
			else if (behavior == CloneBehavior.Reference)
			{
				return;
			}
			else
			{
				PrepareObjectCloneGraph(source, target, null, behavior);
			}
		}

		void ICloneTargetSetup.HandleValue<T>(ref T source, ref T target, CloneBehavior behavior, TypeInfo behaviorTarget)
		{
			if (typeof(T) == _currentCloneType.Type.AsType())
			{
				// Structs can't contain themselfs. If source's type is equal to our current clone type, this is a handle-self call.
				PrepareValueChildCloneGraph<T>(ref source, ref target, _currentCloneType);
			}
			else if (behaviorTarget != null)
			{
				PushCloneBehavior(new LocalCloneBehavior(behaviorTarget, behavior));
				PrepareValueCloneGraph<T>(ref source, ref target, null);
				PopCloneBehavior();
			}
			else
			{
				PrepareValueCloneGraph<T>(ref source, ref target, null);
			}
		}

		bool ICloneOperation.IsTarget<T>(T target)
		{
			return _targetSet.Contains(target);
		}

		T ICloneOperation.GetTarget<T>(T source)
		{
			object targetObj = GetTargetOf(source);
			return (T)targetObj;
		}

		void ICloneOperation.HandleObject<T>(T source, ref T target)
		{
			// If we're just handling ourselfs, don't bother doing anything else.
			// Since "fallback to default" is triggered by source being equal to the currently handled object,
			// we need to make sure that this cannot be triggered accidentally by auto-generated clone lambdas.
			// Only allow the fallback when the object in question is actually cloned by user code!
			bool calledFromUserCode = _currentObject is ICloneExplicit || _currentCloneType.Surrogate != null;
			if (object.ReferenceEquals(source, _currentObject) && calledFromUserCode)
			{
				if (!_currentCloneType.IsCopyByAssignment)
				{
					PerformCopyChildObject(source, target, _currentCloneType);
				}
				return;
			}

			// If there is no source value, check if we're dealing with a merge surrogate and get the old target value when necessary.
			bool sourceNullMerge = false;
			CloneType typeData = null;
			if (source is null)
			{
				source = target;
				if (source is not null)
				{
					typeData = GetCloneType(source.GetType());
					if (typeData.IsMergeSurrogate)
						sourceNullMerge = true;
					else
						source = default;
				}
			}
			
			// Perform target mapping and assign the copied value to the target field
			object registeredTarget = GetTargetOf(source);
			target = (T)registeredTarget;
			PerformCopyObject(sourceNullMerge ? default : source, target, typeData);
		}

		void ICloneOperation.HandleValue<T>(ref T source, ref T target)
		{
			PerformCopyValue(ref source, ref target, null);
		}


		private	static List<ICloneSurrogate> s_surrogates = null;
		private	static readonly Dictionary<Type,CloneType> s_cloneTypeCache = [];
		private	static readonly Dictionary<Type,CloneBehaviorAttribute> s_cloneBehaviorCache = [];
		private static readonly CloneBehaviorAttribute s_memberInfoCloneBehavior = new (typeof(MemberInfo), CloneBehavior.Reference);

		/// <summary>
		/// Returns the <see cref="CloneType"/> of a Type.
		/// </summary>
		/// <param name="type"></param>
		protected internal static CloneType GetCloneType(Type type)
		{
			if (type == null) return null;

            if (s_cloneTypeCache.TryGetValue(type, out CloneType result)) return result;

            result = new CloneType(type);
			s_cloneTypeCache[type] = result;
			result.Init();
			return result;
		}

		internal static ICloneSurrogate GetSurrogateFor(TypeInfo type)
		{
			if (s_surrogates == null)
			{
				s_surrogates = 
					Prowl.Runtime.RuntimeUtils.FindTypesImplementing(typeof(ICloneSurrogate))
					//.Select(t => t.CreateInstanceOf())
					.Select(t => Activator.CreateInstance(t))
					.OfType<ICloneSurrogate>()
					.Where(s => s != null)
					.ToList();
                s_surrogates.Sort((s1, s2) => s1.Priority - s2.Priority);
			}
			for (int i = 0; i < s_surrogates.Count; i++)
			{
				if (s_surrogates[i].MatchesType(type))
					return s_surrogates[i];
			}
			return null;
		}

		internal static CloneBehaviorAttribute GetCloneBehaviorAttribute(TypeInfo typeInfo)
		{
			// Hardcoded cloning behavior for MemberInfo metadata classes
			if (typeof(MemberInfo).GetTypeInfo().IsAssignableFrom(typeInfo))
			{
				return s_memberInfoCloneBehavior;
			}

            // Attributes attached directly to this Type
            if (!s_cloneBehaviorCache.TryGetValue(typeInfo.AsType(), out CloneBehaviorAttribute directAttrib))
            {
                directAttrib = typeInfo.GetCustomAttributes<CloneBehaviorAttribute>().FirstOrDefault();
                s_cloneBehaviorCache[typeInfo.AsType()] = directAttrib;
            }
            return directAttrib;
		}

		internal static void ClearTypeCache()
		{
			s_surrogates = null;
		}
	}

	public static class ExtMethodsCloning
	{
		public static T DeepClone<T>(this T baseObj, CloneProviderContext context = null)
		{
			CloneProvider provider = new(context);
			return (T)provider.CloneObject(baseObj);
		}

		public static void DeepCopyTo<T>(this T baseObj, T targetObj, CloneProviderContext context = null)
		{
			CloneProvider provider = new(context);
			provider.CopyObject(baseObj, targetObj);
		}
	}
}
