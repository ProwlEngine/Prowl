using Prowl.Wicked;

namespace MobaGame;

public class GameRoom
{
    public Guid RoomId = Guid.NewGuid();
    public ArenaMap? Map;
    public GamePhase Phase = GamePhase.CharSelect;
    public float CharSelectTimer = GameConfig.CharSelectTime;
    public float GameTimer;

    public List<RemoteClient> BluePlayers = new();
    public List<RemoteClient> RedPlayers = new();
    public List<RemoteClient> AllPlayers => BluePlayers.Concat(RedPlayers).ToList();

    // In-game state
    public List<ChampionEntity> Champions = new();
    public List<MinionEntity> Minions = new();
    public List<TurretEntity> Turrets = new();
    public List<ProjectileEntity> Projectiles = new();
    public List<JavelinEntity> Javelins = new();
    public List<TrapEntity> Traps = new();
    public NexusEntity? BlueNexus, RedNexus;
    public int BlueKills, RedKills;
    public float MinionSpawnTimer;
    public float CooldownSyncTimer;
    public bool GameStarted;
    public float PostGameTimer = -1;
    public int WinningTeam = -1;
}
