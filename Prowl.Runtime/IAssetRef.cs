using System;

namespace Prowl.Runtime
{
    public interface IAssetRef
    {
        Guid AssetID { get; set; }
        string Name { get; }
        bool IsAvailable { get; }
        bool IsRuntimeResource { get; }
        bool IsLoaded { get; }
        bool IsExplicitNull { get; }
        string TypeName { get; }
    }
}