namespace Prowl.Editor.Assets
{
    public static partial class AssetDatabase
    {

        #region Public Methods

        /// <summary>
        /// Moves a file to a new location.
        /// </summary>
        /// <param name="source">The source file.</param>
        /// <param name="destination">The destination file.</param>
        /// <returns>True if the file was moved successfully, false otherwise.</returns>
        public static bool Move(FileInfo source, FileInfo destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            if (!File.Exists(source.FullName)) return false;
            if (File.Exists(destination.FullName)) return false; // Destination already exists

            if (source.FullName.Equals(destination.FullName, StringComparison.OrdinalIgnoreCase)) return false;

            if (source.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) return false;

            // Move Asset file & meta file if it exists
            source.MoveTo(destination.FullName, true);
            var metaFile = new FileInfo(source.FullName + ".meta");
            if (File.Exists(metaFile.FullName))
                metaFile.MoveTo(destination.FullName + ".meta", true);
            return true;
        }

        /// <summary>
        /// Copies a file to a new location.
        /// </summary>
        /// <param name="source">The source file.</param>
        /// <param name="destination">The destination file.</param>
        /// <returns>True if the file was copied successfully, false otherwise.</returns>
        public static bool Copy(FileInfo source, FileInfo destination)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            if (!File.Exists(source.FullName)) return false;
            if (File.Exists(destination.FullName)) return false; // Destination already exists

            if (source.FullName.Equals(destination.FullName, StringComparison.OrdinalIgnoreCase)) return false;

            if (source.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) return false;

            // Copy Asset file, don't copy meta file as that would create a duplicate asset, instead let refresh handle it
            source.CopyTo(destination.FullName, true);
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
            ArgumentNullException.ThrowIfNullOrEmpty(newName);
            if (!File.Exists(file.FullName)) return false;

            if (file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) return false;

            var newFile = new FileInfo(Path.Combine(file.DirectoryName!, newName + file.Extension));
            if (File.Exists(newFile.FullName)) return false; // Destination already exists

            // Rename Asset file & meta file if it exists
            file.MoveTo(newFile.FullName, true);
            var metaFile = new FileInfo(file.FullName + ".meta");
            if (File.Exists(metaFile.FullName))
            {
                var newMetaFile = new FileInfo(newFile.FullName + ".meta");
                metaFile.MoveTo(newMetaFile.FullName, true);
            }
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
            if(File.Exists(file.FullName))
                file.Delete();
            var metaFile = new FileInfo(file.FullName + ".meta");
            if (File.Exists(metaFile.FullName))
                metaFile.Delete();

            return true;
        }

        #endregion

    }

}