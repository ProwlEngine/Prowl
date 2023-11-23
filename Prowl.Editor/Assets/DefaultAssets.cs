using System.Reflection;

namespace Prowl.Editor.Assets;

public static class DefaultAssets
{
    internal static void CreateDefaults(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder)) throw new ArgumentException("Root Folder cannot be null or whitespace");
        DirectoryInfo info = new(Path.Combine(Project.ProjectDirectory, rootFolder));
        if (!info.Exists) info.Create();

#warning TODO: Only copy if the file doesn't exist, or if somehow if the engine version is different or something...

        // Copy embedded defaults to rootFolder, this is just actual Files, so Image.png, not the asset variants
        foreach (string file in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(x => x.StartsWith("Prowl.Editor.EmbeddedResources.DefaultAssets.")))
        {
            string[] nodes = file.Split('.');
            string fileName = nodes[^2];
            string fileExtension = nodes[^1];
            string filePath = Path.Combine(info.FullName, fileName + "." + fileExtension);
            if (File.Exists(filePath))
                File.Delete(filePath);
            //if (!File.Exists(filePath))
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(file);
                using FileStream fileStream = File.Create(filePath);
                stream.CopyTo(fileStream);
            }
        }

    }
}
