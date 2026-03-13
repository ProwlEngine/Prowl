using System.IO;

namespace AssetProcessor
{
    public abstract class BinaryAssetProcessor
    {
        public abstract object Process(Stream stream, string extension);
    }

    public abstract class BinaryAssetProcessor<T> : BinaryAssetProcessor
    {
        public override object Process(Stream stream, string extension) => ProcessT(stream, extension);

        public abstract T ProcessT(Stream stream, string extension);
    }
}
