using System.Diagnostics;
using System.Numerics;

namespace AssimpSharp
{
    public class Importer
    {
        private const int MaxLenHint = 200;
        private readonly ImporterPimpl impl;

        public Importer()
        {
            impl = new ImporterPimpl();
        }

        public Importer(Importer other) : this()
        {
            impl.IntProperties = new Dictionary<int, int>(other.impl.IntProperties);
            impl.FloatProperties = new Dictionary<int, float>(other.impl.FloatProperties);
            impl.StringProperties = new Dictionary<int, string>(other.impl.StringProperties);
            impl.MatrixProperties = new Dictionary<int, Matrix4x4>(other.impl.MatrixProperties);
        }

        public AiReturn RegisterLoader(BaseImporter imp)
        {
            var st = imp.ExtensionList;
            var baked = "";
            foreach (var ext in st)
            {
                if (ASSIMP.BUILD.DEBUG && IsExtensionSupported(ext))
                {
                    Console.WriteLine($"Warning: The file extension {ext} is already in use");
                }
                baked += $"{ext} ";
            }
            impl.Importer.Add(imp);
            Console.WriteLine($"Registering custom importer for these file extensions: {baked}");
            return AiReturn.Success;
        }

        public AiReturn UnregisterLoader(BaseImporter imp)
        {
            if (impl.Importer.Remove(imp))
            {
                Console.WriteLine("Unregistering custom importer");
                return AiReturn.Success;
            }
            else
            {
                Console.WriteLine("Warning: Unable to remove custom importer: I can't find you ...");
                return AiReturn.Failure;
            }
        }

        public AiReturn RegisterPPStep(BaseProcess imp)
        {
            impl.PostProcessingSteps.Add(imp);
            Console.WriteLine("Registering custom post-processing step");
            return AiReturn.Success;
        }

        public AiReturn UnregisterPPStep(BaseProcess imp)
        {
            if (impl.PostProcessingSteps.Remove(imp))
            {
                Console.WriteLine("Unregistering custom post-processing step");
                return AiReturn.Success;
            }
            else
            {
                Console.WriteLine("Warning: Unable to remove custom post-processing step: I can't find you ..");
                return AiReturn.Failure;
            }
        }

        public bool SetPropertyInteger(string szName, int value)
        {
            return GenericProperty.SetGenericProperty(impl.IntProperties, szName, value);
        }

        public bool SetPropertyBool(string szName, bool value)
        {
            return SetPropertyInteger(szName, value ? 1 : 0);
        }

        public bool SetPropertyFloat(string szName, float value)
        {
            return GenericProperty.SetGenericProperty(impl.FloatProperties, szName, value);
        }

        public bool SetPropertyString(string szName, string value)
        {
            return GenericProperty.SetGenericProperty(impl.StringProperties, szName, value);
        }

        public bool SetPropertyMatrix(string szName, Matrix4x4 value)
        {
            return GenericProperty.SetGenericProperty(impl.MatrixProperties, szName, value);
        }

        public int GetPropertyInteger(string szName, int errorReturn)
        {
            return GenericProperty.GetGenericProperty(impl.IntProperties, szName, errorReturn);
        }

        public bool GetPropertyBool(string szName, bool errorReturn = false)
        {
            return GetPropertyInteger(szName, errorReturn ? 1 : 0) != 0;
        }

        public float GetPropertyFloat(string szName, float errorReturn = 10e10f)
        {
            return GenericProperty.GetGenericProperty(impl.FloatProperties, szName, errorReturn);
        }

        public string GetPropertyString(string szName, string errorReturn = "")
        {
            return GenericProperty.GetGenericProperty(impl.StringProperties, szName, errorReturn);
        }

        public Matrix4x4 GetPropertyMatrix(string szName, Matrix4x4 errorReturn = default)
        {
            return GenericProperty.GetGenericProperty(impl.MatrixProperties, szName, errorReturn);
        }

        public bool ValidateFlags(int flags)
        {
            if ((flags & (int)AiPostProcessSteps.GenSmoothNormals) != 0 && (flags & (int)AiPostProcessSteps.GenNormals) != 0)
            {
                Console.Error.WriteLine("#aiProcess_GenSmoothNormals and #aiProcess_GenNormals are incompatible");
                return false;
            }
            if ((flags & (int)AiPostProcessSteps.OptimizeGraph) != 0 && (flags & (int)AiPostProcessSteps.PreTransformVertices) != 0)
            {
                Console.Error.WriteLine("#aiProcess_OptimizeGraph and #aiProcess_PreTransformVertices are incompatible");
                return false;
            }
            return true;
        }

        public AiScene ReadFile(string file, AiPostProcessSteps flags = 0)
        {
            return ReadFile(new Uri(file), flags);
        }

        public AiScene ReadFile(Uri file, AiPostProcessSteps flags = 0)
        {
            WriteLogOpening(file.AbsolutePath);

            if (impl.Scene != null)
            {
                Console.WriteLine("(Deleting previous scene)");
                FreeScene();
            }

            if (!File.Exists(file.LocalPath))
            {
                impl.ErrorString = $"Unable to open file \"{file}\".";
                Console.Error.WriteLine(impl.ErrorString);
                return null;
            }

            var imp = impl.Importer.FirstOrDefault(i => i.CanRead(file, false));

            if (imp == null)
            {
                // TODO: Implement format auto detection
                impl.ErrorString = $"No suitable reader found for the file format of file \"{file}\".";
                Console.Error.WriteLine(impl.ErrorString);
                return null;
            }

            var fileSize = new FileInfo(file.LocalPath).Length;

            var desc = imp.Info;
            var ext = desc.Name;
            Console.WriteLine($"Found a matching importer for this file format: {ext}.");

            impl.Scene = imp.ReadFile(this, file);

            if (impl.Scene != null)
            {
                if (!ASSIMP.BUILD.NO.VALIDATEDS_PROCESS && ((int)flags & (int)AiPostProcessSteps.ValidateDataStructure) != 0)
                {
                    ValidateDSProcess.ExecuteOnScene(this);
                    if (impl.Scene == null) return null;
                }

                ScenePreprocessor.ProcessScene(impl.Scene);

                ApplyPostProcessing((int)flags & ~(int)AiPostProcessSteps.ValidateDataStructure);
            }
            else if (impl.Scene == null)
            {
                impl.ErrorString = imp.ErrorText;
            }

            return impl.Scene;
        }

        public AiScene ReadFileFromMemory(byte[] buffer, int flags, string hint = "")
        {
            if (buffer.Length == 0 || hint.Length > MaxLenHint)
            {
                impl.ErrorString = "Invalid parameters passed to ReadFileFromMemory()";
                return null;
            }
            // TODO: Implement memory-based file reading
            throw new NotImplementedException();
        }

        public AiScene ApplyPostProcessing(int flags)
        {
            if (impl.Scene == null) return null;
            if (flags == 0) return impl.Scene;

            Debug.Assert(ValidateFlags(flags));
            Console.WriteLine("Entering post processing pipeline");

            if (!ASSIMP.BUILD.NO.VALIDATEDS_PROCESS && (flags & (int)AiPostProcessSteps.ValidateDataStructure) != 0)
            {
                ValidateDSProcess.ExecuteOnScene(this);
                if (impl.Scene == null) return null;
            }

            if (ASSIMP.BUILD.DEBUG)
            {
                if (impl.ExtraVerbose)
                {
                    if (ASSIMP.BUILD.NO.VALIDATEDS_PROCESS)
                        Console.Error.WriteLine("Verbose Import is not available due to build settings");
                    flags |= (int)AiPostProcessSteps.ValidateDataStructure;
                }
            }
            else if (impl.ExtraVerbose)
                Console.WriteLine("Not a debug build, ignoring extra verbose setting");

            for (int a = 0; a < impl.PostProcessingSteps.Count; a++)
            {
                var process = impl.PostProcessingSteps[a];
                if (process.IsActive(flags))
                {
                    process.ExecuteOnScene(this);
                }
                if (impl.Scene == null) break;
                if (ASSIMP.BUILD.DEBUG && !ASSIMP.BUILD.NO.VALIDATEDS_PROCESS && impl.ExtraVerbose)
                {
                    Console.WriteLine("Verbose Import: revalidating data structures");
                    ValidateDSProcess.ExecuteOnScene(this);
                    if (impl.Scene == null)
                    {
                        Console.Error.WriteLine("Verbose Import: failed to revalidate data structures");
                        break;
                    }
                }
            }

            if (impl.Scene != null)
                Console.WriteLine("Leaving post processing pipeline");

            return impl.Scene;
        }

        public AiScene ApplyCustomizedPostProcessing(BaseProcess rootProcess, bool requestValidation)
        {
            if (impl.Scene == null) return null;
            if (rootProcess == null) return impl.Scene;

            Console.WriteLine("Entering customized post processing pipeline");

            if (!ASSIMP.BUILD.NO.VALIDATEDS_PROCESS && requestValidation)
            {
                ValidateDSProcess.ExecuteOnScene(this);
                if (impl.Scene == null) return null;
            }

            if (ASSIMP.BUILD.DEBUG && impl.ExtraVerbose && ASSIMP.BUILD.NO.VALIDATEDS_PROCESS)
                Console.Error.WriteLine("Verbose Import is not available due to build settings");
            else if (impl.ExtraVerbose)
                Console.WriteLine("Not a debug build, ignoring extra verbose setting");

            rootProcess.ExecuteOnScene(this);

            if (impl.ExtraVerbose || requestValidation)
            {
                Console.WriteLine("Verbose Import: revalidating data structures");
                ValidateDSProcess.ExecuteOnScene(this);
                if (impl.Scene == null)
                    Console.Error.WriteLine("Verbose Import: failed to revalidate data structures");
            }

            Console.WriteLine("Leaving customized post processing pipeline");
            return impl.Scene;
        }

        public void FreeScene()
        {
            impl.Scene = null;
            impl.ErrorString = "";
        }

        public string ErrorString {
            get => impl.ErrorString;
            set => impl.ErrorString = value;
        }

        public AiScene Scene {
            get => impl.Scene;
            set => impl.Scene = value;
        }

        public AiScene OrphanedScene {
            get {
                var s = impl.Scene;
                impl.Scene = null;
                impl.ErrorString = "";
                return s;
            }
        }

        public bool IsExtensionSupported(string szExtension)
        {
            return GetImporter(szExtension) != null;
        }

        public string ExtensionList {
            get {
                return string.Join(";", impl.Importer.Select(i => "*." + string.Join(";*.", i.ExtensionList)));
            }
        }

        public int ImporterCount => impl.Importer.Count;

        public AiImporterDesc GetImporterInfo(int index)
        {
            return impl.Importer[index].Info;
        }

        public BaseImporter GetImporter(int index)
        {
            return index >= 0 && index < impl.Importer.Count ? impl.Importer[index] : null;
        }

        public BaseImporter GetImporter(string szExtension)
        {
            int index = GetImporterIndex(szExtension);
            return index != -1 ? impl.Importer[index] : null;
        }

        public int GetImporterIndex(string szExtension)
        {
            if (string.IsNullOrEmpty(szExtension)) return -1;

            int p = 0;
            while (p < szExtension.Length && (szExtension[p] == '*' || szExtension[p] == '.')) p++;

            string ext = szExtension.Substring(p).ToLower();
            if (string.IsNullOrEmpty(ext)) return -1;

            for (int i = 0; i < impl.Importer.Count; i++)
            {
                if (impl.Importer[i].ExtensionList.Contains(ext))
                {
                    return i;
                }
            }

            return -1;
        }

        public void SetExtraVerbose(bool verbose)
        {
            impl.ExtraVerbose = verbose;
        }

        private void WriteLogOpening(string file)
        {
            Console.WriteLine($"Load {file}");

            int flags = CompileFlags;
            string message = $"Assimp {VersionMajor}.{VersionMinor}.{VersionRevision}";
            if (ASSIMP.BUILD.DEBUG) message += " debug";
            Console.WriteLine(message);
        }

        private bool ValidateFlagsInternal(int flags)
        {
            if ((flags & (int)AiPostProcessSteps.GenSmoothNormals) != 0 && (flags & (int)AiPostProcessSteps.GenNormals) != 0)
            {
                Console.Error.WriteLine("AiProcess_GenSmoothNormals and AiProcess_GenNormals are incompatible");
                return false;
            }
            if ((flags & (int)AiPostProcessSteps.OptimizeGraph) != 0 && (flags & (int)AiPostProcessSteps.PreTransformVertices) != 0)
            {
                Console.Error.WriteLine("AiProcess_OptimizeGraph and AiProcess_PreTransformVertices are incompatible");
                return false;
            }
            return true;
        }

        internal float GetFloatProperty(object aI_CONFIG_PP_GSN_MAX_SMOOTHING_ANGLE, float v) => throw new NotImplementedException();

        // Additional properties and methods from the original Kotlin class
        public int VersionMajor => 4; // Replace with actual version
        public int VersionMinor => 0; // Replace with actual version
        public int VersionRevision => 0; // Replace with actual version
        public int CompileFlags => 0; // Replace with actual compile flags
    }
}
