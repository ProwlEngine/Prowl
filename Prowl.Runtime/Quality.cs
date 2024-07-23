using System;
using System.IO;
using Prowl.Runtime.Utils;
using Prowl.Runtime.RenderPipelines;

namespace Prowl.Runtime
{
    public class QualitySetting
    {
        public AssetRef<RenderPipelines.RenderPipeline> RenderPipeline = null;
        public bool testSetting1;
        public float testSetting2;
    }

    [FilePath("QualitySettings.projsetting", FilePathAttribute.Location.Setting)]
    public class QualitySettings : ScriptableSingleton<QualitySettings>
    {
        [SerializeField]
        public QualitySetting[] Qualities; 
    }

    public static class Quality
    {
        private static QualitySetting EmptyQuality = new()
        {
            RenderPipeline = null
        };

        private static void EnsureValidQualities()
        {   
            QualitySetting[] Qualities = QualitySettings.Instance.Qualities;

            if (Qualities != null && Qualities.Length > 0 && Qualities[0] != null && Qualities[0].RenderPipeline.IsAvailable)
                return;

            /*
            var defaultPipeline = Application.AssetProvider.LoadAsset<RenderPipeline>("Defaults/DefaultRenderPipeline.scriptobj");

            if (defaultPipeline.IsAvailable == false)
                Debug.LogError($"Missing Default Render Pipeline!");
            */

            QualitySettings.Instance.Qualities = [
                new QualitySetting()
                {
                    RenderPipeline = new(Guid.Parse("f047d341-111b-4450-ad49-dd5f9e2070a9")),
                }
            ];
        }

        public static int QualityLevel { get; private set; }

        public static void SetQualityLevel(int qualityLevel)
        {
            if (Application.isEditor && Application.DataPath == null)
                return;

            EnsureValidQualities();
            QualityLevel = qualityLevel;
        }

        public static QualitySetting GetQualitySettings(int? qualityLevel = null)
        {
            if (Application.isEditor && Application.DataPath == null)
                return EmptyQuality;

            EnsureValidQualities();
            return QualitySettings.Instance.Qualities[qualityLevel.GetValueOrDefault(QualityLevel)];
        }
    }
}
