using System;
using System.IO;
using Prowl.Runtime.Utils;
using Prowl.Runtime.RenderPipelines;

namespace Prowl.Runtime
{
    public class QualitySetting
    {
        public AssetRef<RenderPipeline> RenderPipeline;
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

            if (Qualities != null && Qualities.Length > 0 && Qualities[0] != null)
                return;

            /*
            var defaultPipeline = Application.AssetProvider.LoadAsset<RenderPipeline>("Defaults/DefaultRenderPipeline.scriptobj");

            if (defaultPipeline.IsAvailable == false)
                Debug.LogError($"Missing Default Render Pipeline!");
            */

            RenderPipeline defaultPipeline = null;

            QualitySettings.Instance.Qualities = [
                new QualitySetting()
                {
                    RenderPipeline = defaultPipeline,
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
