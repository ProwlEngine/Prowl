using Prowl.Echo;

namespace Tests.SourceGeneratorTests;

[GenerateSerializer]
[FixedEchoStructure]
public partial struct Vector3
{
    public float X;
    public float Y;
    public float Z;
}

[GenerateSerializer]
[FixedEchoStructure]
public partial struct Quaternion
{
    public float X;
    public float Y;
    public float Z;
    public float W;
}

[GenerateSerializer]
[FixedEchoStructure]
public partial struct Color
{
    public byte R;
    public byte G;
    public byte B;
    public byte A;
}

[GenerateSerializer]
[FixedEchoStructure]
public partial class NetworkPacket
{
    public int PacketId = 0;
    public long Timestamp = 0;
    public byte[] Data = Array.Empty<byte>();
}

[GenerateSerializer]
[FixedEchoStructure]
public partial struct Transform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
}

[GenerateSerializer]
public partial class EntityWithFixedStructures
{
    public int EntityId = 0;
    public Vector3 Position;
    public Quaternion Rotation;
    public List<Vector3> Path = new();
}

[GenerateSerializer]
[FixedEchoStructure]
public partial struct Point2D
{
    public int X;
    public int Y;
}

[GenerateSerializer]
[FixedEchoStructure]
public partial struct Rectangle
{
    public Point2D TopLeft;
    public Point2D BottomRight;
}

[GenerateSerializer]
[FixedEchoStructure]
public partial class FixedNetworkMessage
{
    public byte MessageType = 0;
    public int SenderId = 0;
    public int ReceiverId = 0;
    public string Payload = "";
}
