using System;
using System.Collections.Generic;
using System.IO;
using AssetPrimitives;

namespace AssetProcessor
{
    class Program
    {
        private static readonly Dictionary<string, BinaryAssetProcessor> s_assetProcessors = GetAssetProcessors();
        private static readonly Dictionary<Type, BinaryAssetSerializer> s_assetSerializers = DefaultSerializers.Get();

        private static Dictionary<string, BinaryAssetProcessor> GetAssetProcessors()
        {
            ImageSharpProcessor texProcessor = new ImageSharpProcessor();
            AssimpProcessor assimpProcessor = new AssimpProcessor();

            return new Dictionary<string, BinaryAssetProcessor>()
            {
                { ".png", texProcessor },
                { ".ktx", new KtxFileProcessor() },
                { ".dae", assimpProcessor },
                { ".obj", assimpProcessor },
            };
        }

        static int Main(string[] args)
        {
            string outputDirectory = args[0];
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                Console.WriteLine($"Processing {arg}");

                string extension = Path.GetExtension(arg);
                if (string.IsNullOrEmpty(extension))
                {
                    Console.Error.WriteLine($"Invalid path: {arg}");
                    return -1;
                }

                if (!s_assetProcessors.TryGetValue(extension, out BinaryAssetProcessor processor))
                {
                    Console.Error.WriteLine($"Unable to process asset with extension {extension}.");
                    return -1;
                }

                object processedAsset;
                using (FileStream fs = File.OpenRead(arg))
                {
                    processedAsset = processor.Process(fs, extension);
                }

                Type assetType = processedAsset.GetType();
                if (!s_assetSerializers.TryGetValue(assetType, out BinaryAssetSerializer serializer))
                {
                    Console.Error.WriteLine($"Unable to serialize asset of type {assetType}.");
                    return -1;
                }

                string fileName = Path.GetFileNameWithoutExtension(arg);
                string outputFileName = Path.Combine(outputDirectory, fileName + ".binary");
                using (FileStream outFS = File.Create(outputFileName))
                {
                    BinaryWriter writer = new BinaryWriter(outFS);
                    serializer.Write(writer, processedAsset);
                }
                Console.WriteLine($"Processed asset: {arg} => {outputFileName}");
            }

            return 0;
        }
    }
}
