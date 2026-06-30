using System.Numerics;
using Prowl.Wicked;

namespace MultiplayerGame;

/// <summary>
/// A networked player entity. Colored square that moves around.
/// </summary>
public class PlayerEntity : NetworkEntity
{
    public SyncVarInterpolated X = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };
    public SyncVarInterpolated Y = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };
    public SyncVar<byte> ColorR = new((byte)255);
    public SyncVar<byte> ColorG = new((byte)255);
    public SyncVar<byte> ColorB = new((byte)255);
    public SyncVar<string> Name = new("");

    // Server-side target position from client input
    private float _targetX;
    private float _targetY;

    private static readonly Random _rng = new();

    public override void OnSpawn()
    {
        _targetX = X.Value;
        _targetY = Y.Value;
    }

    public override void OnStartServer()
    {
        Console.WriteLine($"[Server] Player spawned: {Name} at ({X.Value:F0},{Y.Value:F0})");
    }

    public override void OnStartClient()
    {
        Console.WriteLine($"[Client] Player appeared: {Name} at ({X.Value:F0},{Y.Value:F0})");
    }

    public override void ServerTick()
    {
        // Lerp toward target position
        float speed = 300f * Server.DeltaTime;
        float dx = _targetX - X.Value;
        float dy = _targetY - Y.Value;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > 1f)
        {
            float move = MathF.Min(speed, dist);
            X.Value += dx / dist * move;
            Y.Value += dy / dist * move;
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
        ColorR.Value = (byte)_rng.Next(100, 256);
        ColorG.Value = (byte)_rng.Next(100, 256);
        ColorB.Value = (byte)_rng.Next(100, 256);
    }
}
