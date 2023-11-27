using System;

namespace Prowl.Runtime.Utils
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class CreateAssetMenu : Attribute
    {
        public string Name { get; }
        public CreateAssetMenu(string path)
        {
            Name = path;
        }
    }
}
