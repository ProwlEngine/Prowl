using System.Numerics;
using Prowl.Wicked;

namespace MultiplayerGame;

/// <summary>
/// A networked player entity. Colored square that moves around.
/// </summary>
public class PlayerEntity : NetworkEntity
{
    public float X;
    public float Y;
    public byte ColorR;
    public byte ColorG;
    public byte ColorB;
    public string Name = "";

    // Server-side target position from client input
    private float _targetX;
    private float _targetY;

    private static readonly Random _rng = new();

    public override void PackSpawnData(NetworkWriter writer)
    {
        writer.WriteFloat(X);
        writer.WriteFloat(Y);
        writer.WriteByte(ColorR);
        writer.WriteByte(ColorG);
        writer.WriteByte(ColorB);
        writer.WriteString(Name);
    }

    public override void UnpackSpawnData(NetworkReader reader)
    {
        X = reader.ReadFloat();
        Y = reader.ReadFloat();
        ColorR = reader.ReadByte();
        ColorG = reader.ReadByte();
        ColorB = reader.ReadByte();
        Name = reader.ReadString() ?? "";
    }

    public override void OnSpawn()
    {
        _targetX = X;
        _targetY = Y;
    }

    public override void OnStartServer()
    {
        Console.WriteLine($"[Server] Player spawned: {Name} at ({X:F0},{Y:F0})");
    }

    public override void OnStartClient()
    {
        Console.WriteLine($"[Client] Player appeared: {Name} at ({X:F0},{Y:F0})");
    }

    public override void ServerTick()
    {
        // Lerp toward target position
        float speed = 300f * Server.DeltaTime;
        float dx = _targetX - X;
        float dy = _targetY - Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 1f)
        {
            float move = MathF.Min(speed, dist);
            X += dx / dist * move;
            Y += dy / dist * move;
        }
    }

    /// <summary>
    /// Client sends movement input to the server.
    /// </summary>
    [EntityCommand]
    public void CmdMove(float targetX, float targetY)
    {
        // Clamp to world bounds
        _targetX = Math.Clamp(targetX, 0, 780);
        _targetY = Math.Clamp(targetY, 0, 580);
    }

    /// <summary>
    /// Server broadcasts position updates to all observers.
    /// </summary>
    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcUpdatePosition(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Server sends a chat message to all observers.
    /// </summary>
    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcChat(string message)
    {
        Console.WriteLine($"[Chat] {Name}: {message}");
        // Store last chat message for rendering
        LastChatMessage = message;
        ChatMessageTimer = 3f;
    }

    // Client-side chat display
    public string? LastChatMessage;
    public float ChatMessageTimer;

    public void AssignRandomColor()
    {
        ColorR = (byte)_rng.Next(100, 256);
        ColorG = (byte)_rng.Next(100, 256);
        ColorB = (byte)_rng.Next(100, 256);
    }
}
