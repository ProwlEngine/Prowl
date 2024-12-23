﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace Prowl.Runtime.Cloning.Surrogates
{
    public class DelegateSurrogate : CloneSurrogate<Delegate>
	{
        protected override bool IsImmutableTarget => true;
        public override bool RequireMerge => true;

        public override void CreateTargetObject(Delegate source, ref Delegate target, ICloneTargetSetup setup)
		{
			// Because delegates are immutable, we'll need to defer their creation until we know exactly how the cloned object graph looks like.
			target = null;
		}

		public override void SetupCloneTargets(Delegate source, Delegate target, ICloneTargetSetup setup) { }

		public override void CreateTargetObjectLate(Delegate source, ref Delegate target, ICloneOperation operation)
		{
			Delegate[] sourceInvokeList = source?.GetInvocationList();
			Delegate[] targetInvokeList = target?.GetInvocationList();
			List<Delegate> mergedInvokeList = new(
				((sourceInvokeList != null) ? sourceInvokeList.Length : 0) + 
				((targetInvokeList != null) ? targetInvokeList.Length : 0));

			// Iterate over our sources invocation list and copy entries are part of the target object graph
			if (sourceInvokeList != null)
			{
				for (int i = 0; i < sourceInvokeList.Length; i++)
				{
					if (sourceInvokeList[i].Target == null) continue;

					object invokeTargetObject = operation.GetWeakTarget(sourceInvokeList[i].Target);
					if (invokeTargetObject != null)
					{
						MethodInfo method = sourceInvokeList[i].GetMethodInfo();
						Delegate targetSubDelegate = method.CreateDelegate(sourceInvokeList[i].GetType(), invokeTargetObject);
						mergedInvokeList.Add(targetSubDelegate);
					}
				}
			}

			// Iterate over our targets invocation list and keep entries that are NOT part of the target object graph
			if (targetInvokeList != null)
			{
				for (int i = 0; i < targetInvokeList.Length; i++)
				{
					if (targetInvokeList[i].Target == null) continue;

					if (!operation.IsTarget(targetInvokeList[i].Target))
					{
						MethodInfo method = targetInvokeList[i].GetMethodInfo();
						Delegate targetSubDelegate = method.CreateDelegate(targetInvokeList[i].GetType(), targetInvokeList[i].Target);
						mergedInvokeList.Add(targetSubDelegate);
					}
				}
			}

			target = Delegate.Combine([.. mergedInvokeList]);
		}

		public override void CopyDataTo(Delegate source, Delegate target, ICloneOperation operation)
		{
			// Delegates are immutable. Nothing to do here.
		}
	}
}
