// From kcp2k (MIT, see Warp/Transports/Kcp/Protocol/LICENSE.txt).

using Prowl.Warp.Transports.Kcp.Protocol;

using Xunit;

namespace Prowl.Warp.Tests.Kcp;

public class SegmentTests
{
    [Fact]
    public void Encode()
    {
        // get a segment
        Segment seg = new();

        // set some unique values
        seg.conv = 0x04030201;
        seg.cmd = 0x05;
        seg.frg = 0x06;
        seg.wnd = 0x0807;
        seg.ts = 0x0C0B0A09;
        seg.sn = 0x100F0E0D;
        seg.una = 0x14131211;

        // encode with offset
        byte[] data = new byte[100];
        int offset = 4;
        int encoded = seg.Encode(data, offset);
        Assert.Equal(24, encoded);

        // compare every single byte to be 100% sure
        // => 20 bytes conv/cmd/frg/wnd/ts/sn/una
        Assert.Equal(0x01, data[offset + 0]);
        Assert.Equal(0x02, data[offset + 1]);
        Assert.Equal(0x03, data[offset + 2]);
        Assert.Equal(0x04, data[offset + 3]);
        Assert.Equal(0x05, data[offset + 4]);
        Assert.Equal(0x06, data[offset + 5]);
        Assert.Equal(0x07, data[offset + 6]);
        Assert.Equal(0x08, data[offset + 7]);
        Assert.Equal(0x09, data[offset + 8]);
        Assert.Equal(0x0A, data[offset + 9]);
        Assert.Equal(0x0B, data[offset + 10]);
        Assert.Equal(0x0C, data[offset + 11]);
        Assert.Equal(0x0D, data[offset + 12]);
        Assert.Equal(0x0E, data[offset + 13]);
        Assert.Equal(0x0F, data[offset + 14]);
        Assert.Equal(0x10, data[offset + 15]);
        Assert.Equal(0x11, data[offset + 16]);
        Assert.Equal(0x12, data[offset + 17]);
        Assert.Equal(0x13, data[offset + 18]);
        Assert.Equal(0x14, data[offset + 19]);
        // 4 bytes segment.buffer readable bytes (=0)
        Assert.Equal(0x00, data[offset + 20]);
        Assert.Equal(0x00, data[offset + 21]);
        Assert.Equal(0x00, data[offset + 22]);
        Assert.Equal(0x00, data[offset + 23]);
    }

    [Fact]
    public void Reset()
    {
        // get a segment
        Segment seg = new();

        // set some unique values
        seg.conv = 0x04030201;
        seg.cmd = 0x05;
        seg.frg = 0x06;
        seg.wnd = 0x0807;
        seg.ts = 0x0C0B0A09;
        seg.sn = 0x100F0E0D;
        seg.una = 0x14131211;
        seg.data.WriteByte(0xFF);

        // reset
        seg.Reset();
        Assert.Equal(0u, seg.conv);
        Assert.Equal(0u, seg.cmd);
        Assert.Equal(0u, seg.frg);
        Assert.Equal(0u, seg.wnd);
        Assert.Equal(0u, seg.ts);
        Assert.Equal(0u, seg.sn);
        Assert.Equal(0u, seg.una);
        Assert.Equal(0, seg.rto);
        Assert.Equal(0u, seg.xmit);
        Assert.Equal(0u, seg.resendts);
        Assert.Equal(0u, seg.fastack);
        Assert.Equal(0, seg.data.Position);
    }
}
