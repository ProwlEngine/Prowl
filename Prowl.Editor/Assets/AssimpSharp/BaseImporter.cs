using System;
using System.Diagnostics;

namespace AssimpSharp
{
    public enum AiImporterFlags
    {
        SupportTextFlavour = 0x1,
        SupportBinaryFlavour = 0x2
    }

    public struct AiImporterDesc
    {
        public string Name;
        public string[] FileExtensions;
        public AiImporterFlags Flags;
    }

    public abstract class BaseImporter
    {
        public string ErrorText { get; private set; } = "";

        public abstract bool CanRead(Uri file, bool checkSig);

        public AiScene ReadFile(Importer imp, Uri file)
        {
            SetupProperties(imp);

            var sc = new AiScene();

            try
            {
                InternReadFile(file, sc);
            }
            catch (Exception err)
            {
                ErrorText = err.Message;
                Console.Error.WriteLine(ErrorText);
                return null;
            }

            return sc;
        }

        public virtual void SetupProperties(Importer imp) { }

        public abstract AiImporterDesc Info { get; }

        public string[] ExtensionList => Info.FileExtensions;

        public virtual void InternReadFile(Uri file, AiScene scene) { }

        protected static uint AiMakeMagic(string str)
        {
            return (uint)((str[0] << 24) + (str[1] << 16) + (str[2] << 8) + str[3]);
        }
    }
}
