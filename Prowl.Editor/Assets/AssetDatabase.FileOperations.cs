// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Assets;

public static partial class AssetDatabase
{

    #region Public Methods

    /// <summary>
    /// Moves a file to a new location.
    /// </summary>
    /// <param name="source">The source file.</param>
    /// <param name="destination">The destination file.</param>
    /// <returns>True if the file was moved successfully, false otherwise.</returns>
    public static bool Move(FileInfo source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        // Check if source and destination are the same directory
        if (source.FullName.Equals(destination, StringComparison.OrdinalIgnoreCase))
            return false;

        // Source does not exist
        if (!File.Exists(source.FullName)) return false;

        // Destination already exists
        if (File.Exists(destination)) return false;

        if (source.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
            return false;

        // Move Asset file & meta file if it exists
        var metaFile = new FileInfo(source.FullName + ".meta");
        if (File.Exists(metaFile.FullName))
            metaFile.MoveTo(destination + ".meta", true);
        source.MoveTo(destination, true);
        Update();
        return true;
    }

    /// <summary>
    /// Moves a folder to a new location.
    /// </summary>
    /// <param name="source">The source folder.</param>
    /// <param name="destination">The destination folder.</param>
    /// <returns>True if the folder was moved successfully, false otherwise.</returns>
    public static bool Move(DirectoryInfo source, string destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        // Check if source and destination are the same directory
        if (source.FullName.Equals(destination, StringComparison.OrdinalIgnoreCase))
            return false;

        // Source does not exist
        if (!Directory.Exists(source.FullName)) return false;

        // Destination already exists
        if (Directory.Exists(destination)) return false;

        // Move folder
        source.MoveTo(destination);
        Update();
        return true;
    }

    /// <summary>
    /// Renames a file.
    /// </summary>
    /// <param name="file">The file to rename.</param>
    /// <param name="newName">The new name of the file.</param>
    /// <returns>True if the file was renamed successfully, false otherwise.</returns>
    public static bool Rename(FileInfo file, string newName)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        if (!File.Exists(file.FullName)) return false;

        if (file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) return false;

        var newFile = new FileInfo(Path.Combine(file.DirectoryName!, newName + file.Extension));
        if (File.Exists(newFile.FullName)) return false; // Destination already exists

        // Rename Asset file & meta file if it exists
        var metaFile = new FileInfo(file.FullName + ".meta");
        if (File.Exists(metaFile.FullName))
        {
            var newMetaFile = new FileInfo(newFile.FullName + ".meta");
            metaFile.MoveTo(newMetaFile.FullName, true);
        }
        file.MoveTo(newFile.FullName, true);
        Update();
        return true;
    }

    /// <summary>
    /// Renames a folder.
    /// </summary>
    /// <param name="source">The folder to rename.</param>
    /// <param name="newName">The new name of the folder.</param>
    /// <returns>True if the folder was renamed successfully, false otherwise.</returns>
    public static bool Rename(DirectoryInfo source, string newName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(newName);
        if (!Directory.Exists(source.FullName)) return false;

        var newFile = new FileInfo(Path.Combine(source.Parent!.FullName, newName));
        if (Directory.Exists(newFile.FullName)) return false; // Destination already exists

        // Rename Asset file & meta file if it exists
        source.MoveTo(newFile.FullName);
        Update();
        return true;
    }

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="file">The file to delete.</param>
    /// <returns>True if the file was deleted successfully, false otherwise.</returns>
    public static bool Delete(FileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) return false;

        // Just deleting the files should be enough for the AssetDatabase to pick it up
        var metaFile = new FileInfo(file.FullName + ".meta");
        if (File.Exists(metaFile.FullName))
            metaFile.Delete();
        if (File.Exists(file.FullName))
            file.Delete();

        Update();
        return true;
    }

    /// <summary>
    /// Deletes a folder.
    /// </summary>
    /// <param name="source">The folder to delete.</param>
    /// <returns>True if the folder was deleted successfully, false otherwise.</returns>
    public static bool Delete(DirectoryInfo source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Just deleting the files should be enough for the AssetDatabase to pick it up
        if (Directory.Exists(source.FullName))
            source.Delete();
        Update();
        return true;
    }

    #endregion

}
