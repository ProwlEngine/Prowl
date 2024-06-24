using System;
using System.IO;

namespace Prowl.Runtime
{
    public static class Quality
    {
        public class QualitySettings
        {
            public AssetRef<RenderPipelines.RenderPipeline> RenderPipeline;
        }

        private static QualitySettings[] Qualities;


        private static void EnsureValidQualities()
        {
            if (Qualities != null && Qualities.Length > 0 && Qualities[0] != null)
                return;

            var defaultPipeline = Application.AssetProvider.LoadAsset<RenderPipelines.RenderPipeline>("Defaults/DefaultRenderPipeline.scriptobj");

            if (defaultPipeline.IsAvailable == false)
                Debug.LogError($"Missing Default Render Pipeline!");

            Qualities = [
                new QualitySettings()
                {
                    RenderPipeline = defaultPipeline,
                }
            ];
        }

        public static int QualityLevel { get; private set; }

        public static void SetQualityLevel(int qualityLevel)
        {
            EnsureValidQualities();
            QualityLevel = qualityLevel;
        }

        public static QualitySettings GetQualitySettings(int? qualityLevel = null)
        {
            EnsureValidQualities();
            return Qualities[qualityLevel.GetValueOrDefault(QualityLevel)];
        }
    }
}
