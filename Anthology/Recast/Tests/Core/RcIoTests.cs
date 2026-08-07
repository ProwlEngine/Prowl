using System.IO;

namespace Prowl.Recast.Core.Tests;

public class RcIoTests
{
    [Fact]
    public void Test()
    {
        const long tileRef = 281474976710656L;
        const int dataSize = 344;

        byte[] actual;

        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter bw = new BinaryWriter(ms);

            RcIO.Write(bw, tileRef, RcByteOrder.LITTLE_ENDIAN);
            RcIO.Write(bw, dataSize, RcByteOrder.LITTLE_ENDIAN);

            bw.Flush();
            actual= ms.ToArray();
        }

        {
            using MemoryStream ms = new MemoryStream(actual);
            using BinaryReader br = new BinaryReader(ms);
            var byteBuffer = RcIO.ToByteBuffer(br);
            byteBuffer.Order(RcByteOrder.LITTLE_ENDIAN);

            Assert.Equal(tileRef, byteBuffer.GetLong());
            Assert.Equal(dataSize, byteBuffer.GetInt());
        }
    }
}