using System.Collections.Generic;
using System.Numerics;

namespace AssimpSharp
{
    public class ImporterPimpl
    {
        public bool IsDefaultHandler { get; set; } = true;
        public List<BaseImporter> Importer { get; set; }
        public List<BaseProcess> PostProcessingSteps { get; set; }
        public AiScene Scene { get; set; }
        public string ErrorString { get; set; } = "";
        public Dictionary<int, int> IntProperties { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, float> FloatProperties { get; set; } = new Dictionary<int, float>();
        public Dictionary<int, string> StringProperties { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, Matrix4x4> MatrixProperties { get; set; } = new Dictionary<int, Matrix4x4>();
        public bool ExtraVerbose { get; set; } = false;
        public SharedPostProcessInfo PpShared { get; set; }

        public ImporterPimpl()
        {
            Importer = ImporterInstances.GetImporterInstanceList();
            PostProcessingSteps = ImporterInstances.GetPostProcessingStepInstanceList();
            PpShared = new SharedPostProcessInfo();
            foreach (var step in PostProcessingSteps)
            {
                step.Shared = PpShared;
            }
        }
    }
}