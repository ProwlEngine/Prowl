using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AssimpSharp
{
    public class SharedPostProcessInfo
    {
        public interface IBase { }

        private readonly Dictionary<int, IBase> map = new Dictionary<int, IBase>();

        public void AddProperty(string name, IBase data)
        {
            bool wasExisting = false;
            GenericProperty.SetGenericPropertyPtr(map, name, data, ref wasExisting);
        }

        public IBase GetProperty(string name)
        {
            return GenericProperty.GetGenericProperty(map, name, null);
        }

        public void RemoveProperty(string name)
        {
            bool wasExisting = false;
            GenericProperty.SetGenericPropertyPtr(map, name, null, ref wasExisting);
        }
    }

    public abstract class BaseProcess
    {
        public SharedPostProcessInfo Shared;

        protected BaseProcess() { }

        public abstract bool IsActive(int flags);

        public virtual bool RequireVerboseFormat => true;

        public void ExecuteOnScene(Importer imp)
        {
            Debug.Assert(imp.Scene != null);

            SetupProperties(imp);

            try
            {
                Execute(imp.Scene);
            }
            catch (Exception err)
            {
                imp.ErrorString = err.ToString();
                Console.Error.WriteLine(imp.ErrorString);
                imp.Scene = null;
            }
        }

        public virtual void SetupProperties(Importer imp) { }

        public abstract void Execute(AiScene scene);
    }
}