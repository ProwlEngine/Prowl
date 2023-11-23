using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime
{
    public interface IAssetProvider
    {
        public bool HasAsset(Guid assetID);
        public T? LoadAsset<T>(string relativeAssetPath) where T : EngineObject;
        public T? LoadAsset<T>(Guid guid) where T : EngineObject;
    }
}
