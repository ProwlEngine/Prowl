// From kcp2k (MIT, see Warp/Transports/Kcp/Protocol/LICENSE.txt).
// test some of the internal Kcp.cs functions to guarantee stability.

using Prowl.Warp.Transports.Kcp.Protocol;

using Xunit;

namespace Prowl.Warp.Tests.Kcp;

public class KcpTests
{
    [Fact]
    public void InsertSegmentInReceiveBuffer()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert '1' should insert into empty buffer
        Segment one = new() { sn = 1 };
        kcp.InsertSegmentInReceiveBuffer(one);
        Assert.Equal(1, kcp.rcv_buf.Count);
        Assert.Equal(one, kcp.rcv_buf[0]);

        // insert '3' should insert after '1'
        Segment three = new() { sn = 3 };
        kcp.InsertSegmentInReceiveBuffer(three);
        Assert.Equal(2, kcp.rcv_buf.Count);
        Assert.Equal(one, kcp.rcv_buf[0]);
        Assert.Equal(three, kcp.rcv_buf[1]);

        // insert '2' should insert before '3'
        Segment two = new() { sn = 2 };
        kcp.InsertSegmentInReceiveBuffer(two);
        Assert.Equal(3, kcp.rcv_buf.Count);
        Assert.Equal(one, kcp.rcv_buf[0]);
        Assert.Equal(two, kcp.rcv_buf[1]);
        Assert.Equal(three, kcp.rcv_buf[2]);

        // insert '0' should insert before '1'
        Segment zero = new() { sn = 0 };
        kcp.InsertSegmentInReceiveBuffer(zero);
        Assert.Equal(4, kcp.rcv_buf.Count);
        Assert.Equal(zero, kcp.rcv_buf[0]);
        Assert.Equal(one, kcp.rcv_buf[1]);
        Assert.Equal(two, kcp.rcv_buf[2]);
        Assert.Equal(three, kcp.rcv_buf[3]);

        // insert '2' again should do nothing because duplicate
        Segment two_again = new() { sn = 2 };
        kcp.InsertSegmentInReceiveBuffer(two_again);
        Assert.Equal(4, kcp.rcv_buf.Count);
        Assert.Equal(zero, kcp.rcv_buf[0]);
        Assert.Equal(one, kcp.rcv_buf[1]);
        Assert.Equal(two, kcp.rcv_buf[2]);
        Assert.Equal(three, kcp.rcv_buf[3]);
    }

    [Fact]
    public void ParseAckFirst()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        Segment two = new() { sn = 2 };
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(one);
        kcp.snd_buf.Add(two);
        kcp.snd_buf.Add(three);

        // parse ack only removes if sn < snd_nxt
        kcp.snd_nxt = 999;

        // parse ack with sn == 3, should remove the last segment
        kcp.ParseAck(1);
        Assert.Equal(2, kcp.snd_buf.Count);
        Assert.Equal(two, kcp.snd_buf[0]);
        Assert.Equal(three, kcp.snd_buf[1]);
    }

    [Fact]
    public void ParseAckMiddle()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 2 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(three);

        // parse ack only removes if sn < snd_nxt
        kcp.snd_nxt = 999;

        // parse ack with sn == 2, should remove the middle segment
        kcp.ParseAck(2);
        Assert.Equal(2, kcp.snd_buf.Count);
        Assert.Equal(one, kcp.snd_buf[0]);
        Assert.Equal(three, kcp.snd_buf[1]);
    }

    [Fact]
    public void ParseAckLast()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 2 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(three);

        // parse ack only removes if sn < snd_nxt
        kcp.snd_nxt = 999;

        // parse ack with sn == 3, should remove the last segment
        kcp.ParseAck(3);
        Assert.Equal(2, kcp.snd_buf.Count);
        Assert.Equal(one, kcp.snd_buf[0]);
        Assert.Equal(two, kcp.snd_buf[1]);
    }

    [Fact]
    public void ParseAckSndNxtSmaller()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 2 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(three);

        // parse ack only removes if sn < snd_nxt.
        // it should do nothing if snd_nxt is <= sn
        kcp.snd_nxt = 1;

        // parse ack should not remove anything
        kcp.ParseAck(1);
        Assert.Equal(3, kcp.snd_buf.Count);
        Assert.Equal(one, kcp.snd_buf[0]);
        Assert.Equal(two, kcp.snd_buf[1]);
        Assert.Equal(three, kcp.snd_buf[2]);
    }

    // test with empty buffer
    [Fact]
    public void ParseUna_Empty()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // parse_una should remove all segments < una from send buffer
        kcp.ParseUna(2);
        Assert.Equal(0, kcp.snd_buf.Count);
    }

    // test where no elements should be removed
    [Fact]
    public void ParseUna_None()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 2 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(three);

        // parse_una should remove all segments < una from send buffer
        kcp.ParseUna(1);
        Assert.Equal(3, kcp.snd_buf.Count);
        Assert.Equal(one, kcp.snd_buf[0]);
        Assert.Equal(two, kcp.snd_buf[1]);
        Assert.Equal(three, kcp.snd_buf[2]);
    }

    // test where some elements should be removed
    [Fact]
    public void ParseUna_Some()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 2 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(three);

        // parse_una should remove all segments < una from send buffer
        kcp.ParseUna(2);
        Assert.Equal(2, kcp.snd_buf.Count);
        Assert.Equal(two, kcp.snd_buf[0]);
        Assert.Equal(three, kcp.snd_buf[1]);
    }

    // test where all elements should be removed
    [Fact]
    public void ParseUna_All()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 1 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 2 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 3 };
        kcp.snd_buf.Add(three);

        // parse_una should remove all segments < una from send buffer
        kcp.ParseUna(4);
        Assert.Equal(0, kcp.snd_buf.Count);
    }

    // test with no elements in send buffer
    [Fact]
    public void ParseFastack_Empty()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        kcp.snd_una = 1; // == sn to ensure <= is checked
        kcp.snd_nxt = 2; // sn + 1 to ensure < is checked

        kcp.ParseFastack(1, 0);
        Assert.Equal(0, kcp.snd_buf.Count);
    }

    // test where no elements should be counted
    [Fact]
    public void ParseFastAck_None()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 2 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 3 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 4 };
        kcp.snd_buf.Add(three);

        // sn needs to be between snd_una and snd_nxt
        kcp.snd_una = 1; // == sn to ensure <= is checked
        kcp.snd_nxt = 2; // sn + 1 to ensure < is checked

        // only segments with seg.sn < sn should have their fastack incremented
        // in this case, none
        kcp.ParseFastack(1, 0);
        Assert.Equal(3, kcp.snd_buf.Count);
        Assert.Equal((uint)0, kcp.snd_buf[0].fastack);
        Assert.Equal((uint)0, kcp.snd_buf[1].fastack);
        Assert.Equal((uint)0, kcp.snd_buf[2].fastack);
    }

    // test where some elements should be counted
    [Fact]
    public void ParseFastAck_Some()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 2 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 3 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 4 };
        kcp.snd_buf.Add(three);

        // sn needs to be between snd_una and snd_nxt
        kcp.snd_una = 3; // == sn to ensure <= is checked
        kcp.snd_nxt = 4; // sn + 1 to ensure < is checked

        // only segments with seg.sn < sn should have their fastack incremented
        kcp.ParseFastack(3, 0);
        Assert.Equal(3, kcp.snd_buf.Count);
        Assert.Equal((uint)1, kcp.snd_buf[0].fastack);
        Assert.Equal((uint)0, kcp.snd_buf[1].fastack);
        Assert.Equal((uint)0, kcp.snd_buf[2].fastack);
    }

    // test where all elements should be counted
    [Fact]
    public void ParseFastAck_All()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert three segments into send buffer
        Segment one = new() { sn = 2 };
        kcp.snd_buf.Add(one);
        Segment two = new() { sn = 3 };
        kcp.snd_buf.Add(two);
        Segment three = new() { sn = 4 };
        kcp.snd_buf.Add(three);

        // sn needs to be between snd_una and snd_nxt
        kcp.snd_una = 5; // == sn to ensure <= is checked
        kcp.snd_nxt = 6; // sn + 1 to ensure < is checked

        // only segments with seg.sn < sn should have their fastack incremented
        // in this case, all
        kcp.ParseFastack(5, 0);
        Assert.Equal(3, kcp.snd_buf.Count);
        Assert.Equal((uint)1, kcp.snd_buf[0].fastack);
        Assert.Equal((uint)1, kcp.snd_buf[1].fastack);
        Assert.Equal((uint)1, kcp.snd_buf[2].fastack);
    }

    // peek without any messages in the receive queue
    [Fact]
    public void PeekSize_Empty()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // peek should indicate empty size
        Assert.Equal(-1, kcp.PeekSize());
    }

    // peek with a complete unfragmented message in the receive queue
    [Fact]
    public void PeekSize_Unfragmented()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert a small unfragmented message
        Segment seg = new();
        seg.frg = 0; // frg == 0 indicates unfragmented message
        seg.data.Position = 42;
        kcp.rcv_queue.Enqueue(seg);

        // peek should get the unfragmented message's size
        Assert.Equal(42, kcp.PeekSize());
    }

    // peek with an incomplete fragmented message in the receive queue
    [Fact]
    public void PeekSize_Fragmented_Incomplete()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert only one fragment of a two fragment message
        Segment part1 = new();
        part1.frg = 1; // first segment's .frg indicates the last .frg number. total: 1,0
        part1.data.Position = 42; // set some data
        kcp.rcv_queue.Enqueue(part1);

        // peek should indicate incomplete fragments
        Assert.Equal(-1, kcp.PeekSize());
    }

    // peek with a complete fragmented message in the receive queue
    [Fact]
    public void PeekSize_Fragmented_Complete()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // insert only one fragment of a two fragment message
        Segment part1 = new();
        part1.frg = 1; // first segment's .frg indicates the last .frg number. total: 1,0
        part1.data.Position = 42; // set some data
        kcp.rcv_queue.Enqueue(part1);

        Segment part2 = new();
        part2.frg = 0; // fragment count in reverse
        part2.data.Position = 11; // set some data
        kcp.rcv_queue.Enqueue(part2);

        // peek should peek through all fragments to calculate the whole message's size
        Assert.Equal(42 + 11, kcp.PeekSize());
    }

    [Fact]
    public void WaitSnd()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // add some to send buffer and send buffer and queue
        kcp.snd_buf.Add(new Segment());
        kcp.snd_buf.Add(new Segment());
        kcp.snd_queue.Enqueue(new Segment());

        // WaitSnd should be send buffer + queue
        Assert.Equal(3, kcp.WaitSnd);
    }

    [Fact]
    public void WndUnused()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // add a few entries into receive queue
        kcp.rcv_queue.Enqueue(new Segment());
        kcp.rcv_queue.Enqueue(new Segment());
        kcp.rcv_queue.Enqueue(new Segment());

        // unused should be window size - queue size
        Assert.Equal(kcp.rcv_wnd - 3, kcp.WndUnused());
    }

    [Fact]
    public void ShrinkBufFilledSendBuffer()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // add some to send buffer and send queue
        kcp.snd_buf.Add(new Segment { sn = 2 });
        kcp.snd_buf.Add(new Segment { sn = 3 });

        // ShrinkBuf should set snd_una to first send buffer element's 'sn'
        kcp.ShrinkBuf();
        Assert.Equal((uint)2, kcp.snd_una);
    }

    [Fact]
    public void ShrinkBufEmptySendBuffer()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // ShrinkBuf with empty send buffer should set snd_una to snd_nxt
        kcp.snd_nxt = 42;
        kcp.ShrinkBuf();
        Assert.Equal((uint)42, kcp.snd_una);
    }

    [Fact]
    public void SetMtu()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // set an allowed MTU that's smaller than default
        kcp.SetMtu(500);
        Assert.Equal((uint)500, kcp.mtu);
        Assert.True(kcp.buffer.Length >= 500);

        // set an allowed MTU that's larger than default
        kcp.SetMtu(5000);
        Assert.Equal((uint)5000, kcp.mtu);
        Assert.True(kcp.buffer.Length >= 5000);
    }

    [Fact]
    public void SetNoDelay()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // enable nodelay with specific settings
        kcp.SetNoDelay(1, 11, 2, true);
        Assert.Equal((uint)1, kcp.nodelay);
        Assert.Equal(Transports.Kcp.Protocol.Kcp.RTO_NDL, kcp.rx_minrto);
        Assert.Equal((uint)11, kcp.interval);
        Assert.Equal(2, kcp.fastresend);
        Assert.Equal(true, kcp.nocwnd);

        // disable nodelay with specific settings
        kcp.SetNoDelay(0, 22);
        Assert.Equal((uint)0, kcp.nodelay);
        Assert.Equal(Transports.Kcp.Protocol.Kcp.RTO_MIN, kcp.rx_minrto);
        Assert.Equal((uint)22, kcp.interval);
        Assert.Equal(0, kcp.fastresend);
        Assert.Equal(false, kcp.nocwnd);
    }

    [Fact]
    public void SetIntervalTooSmall()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // setting a too small interval should limit it to 10
        kcp.SetInterval(0);
        Assert.Equal((uint)10, kcp.interval);
    }

    [Fact]
    public void SetInterval()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // set interval
        kcp.SetInterval(2500);
        Assert.Equal((uint)2500, kcp.interval);
    }

    [Fact]
    public void SetIntervalTooBig()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // setting a too big interval should limit it to 5000
        kcp.SetInterval(9999);
        Assert.Equal((uint)5000, kcp.interval);
    }

    [Fact]
    public void SetWindowSize()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // set an allowed window size
        kcp.SetWindowSize(42, 512);
        Assert.Equal((uint)42, kcp.snd_wnd);
        Assert.Equal((uint)512, kcp.rcv_wnd);
    }

    [Fact]
    public void SetWindowSizeWithTooSmallReceiveWindow()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // set window size with receive window < max fragment size (WND_RCV)
        kcp.SetWindowSize(42, Transports.Kcp.Protocol.Kcp.WND_RCV - 1);
        Assert.Equal((uint)42, kcp.snd_wnd);
        Assert.Equal((uint)Transports.Kcp.Protocol.Kcp.WND_RCV, kcp.rcv_wnd);
    }

    [Fact]
    public void Check()
    {
        void Output(byte[] data, int len)
        {
        }

        // setup KCP
        Transports.Kcp.Protocol.Kcp kcp = new(0, Output);

        // update at time = 1
        kcp.Update(1);

        // check at time = 2
        uint next = kcp.Check(2);

        // check returns 'ts_flush + interval', or in other words,
        // 'interval' seconds after UPDATE was called. so 1+100 = 101.
        Assert.Equal(1 + kcp.interval, next);
    }
}
