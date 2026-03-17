using Prowl.Wicked;

namespace MobaGame;

// ── Per-client server data ──

public class ServerPlayerData
{
    public string Username = "";
    public LobbyEntity? LobbyEntity;
    public GameRoom? CurrentRoom;
    public int Team;
    public int SelectedCharacter = -1;
    public bool LockedIn;
}

// ── Main Server Logic ──

public static class MobaServer
{
    private static LobbyMap? _lobbyMap;
    private static readonly Dictionary<RemoteClient, ServerPlayerData> _playerData = new();
    private static readonly Dictionary<string, RemoteClient> _playersByName = new();
    private static readonly List<RemoteClient> _queue = new();
    private static readonly List<GameRoom> _rooms = new();

    public static void Start(int port)
    {
        AccountManager.Load();
        Server.OnClientConnected += OnClientConnected;
        Server.OnClientDisconnected += OnClientDisconnected;
        Server.Start(port);
        _lobbyMap = Server.CreateMap<LobbyMap>();
        Console.WriteLine($"[Server] MOBA server started on port {port}");
    }

    public static void Tick()
    {
        Server.Tick();
        float dt = Server.DeltaTime;

        for (int i = _rooms.Count - 1; i >= 0; i--)
        {
            var room = _rooms[i];
            TickRoom(room, dt);
            if (room.Phase == GamePhase.Finished && room.PostGameTimer <= 0)
            {
                CleanupRoom(room);
                _rooms.RemoveAt(i);
            }
        }

        TryMatchmake();
    }

    // ── Connection ──

    private static void OnClientConnected(RemoteClient client)
    {
        _playerData[client] = new ServerPlayerData();
        Console.WriteLine($"[Server] Client {client.ClientId} connected");
    }

    private static void OnClientDisconnected(RemoteClient client)
    {
        if (_playerData.TryGetValue(client, out var data))
        {
            _queue.Remove(client);

            if (data.CurrentRoom != null)
                HandlePlayerLeaveRoom(client, data);

            if (data.LobbyEntity != null && data.LobbyEntity.IsSpawned)
            {
                if (client.PlayerEntity == data.LobbyEntity)
                    client.UnassignPlayerEntity();
                Server.Despawn(data.LobbyEntity);
            }

            if (!string.IsNullOrEmpty(data.Username))
                _playersByName.Remove(data.Username);

            _playerData.Remove(client);
        }
        Console.WriteLine($"[Server] Client {client.ClientId} disconnected");
    }

    private static ServerPlayerData? GetData(RemoteClient client)
        => _playerData.GetValueOrDefault(client);

    // ── Auth ──

    public static void HandleLogin(RemoteClient client, string username, string password)
    {
        var data = GetData(client);
        if (data == null) return;
        if (!string.IsNullOrEmpty(data.Username))
        { LobbyCommands.RpcLoginResult(client, false, "Already logged in."); return; }
        if (_playersByName.ContainsKey(username))
        { LobbyCommands.RpcLoginResult(client, false, "Account already logged in."); return; }

        var (success, error) = AccountManager.Login(username, password);
        if (!success) { LobbyCommands.RpcLoginResult(client, false, error); return; }
        FinishLogin(client, data, username);
    }

    public static void HandleRegister(RemoteClient client, string username, string password)
    {
        var data = GetData(client);
        if (data == null) return;
        if (!string.IsNullOrEmpty(data.Username))
        { LobbyCommands.RpcLoginResult(client, false, "Already logged in."); return; }

        var (success, error) = AccountManager.Register(username, password);
        if (!success) { LobbyCommands.RpcLoginResult(client, false, error); return; }
        FinishLogin(client, data, username);
    }

    private static void FinishLogin(RemoteClient client, ServerPlayerData data, string username)
    {
        data.Username = username;
        _playersByName[username] = client;

        var lobby = Server.Spawn<LobbyEntity>(_lobbyMap!, client, e => { e.PlayerName.Value = username; });
        data.LobbyEntity = lobby;
        client.AssignPlayerEntity(lobby);

        LobbyCommands.RpcLoginResult(client, true, "Welcome!");
        Console.WriteLine($"[Server] {username} logged in");
    }

    // ── Queue / Matchmaking ──

    public static void HandleJoinQueue(RemoteClient client)
    {
        var data = GetData(client);
        if (data == null || string.IsNullOrEmpty(data.Username) || data.CurrentRoom != null) return;

        if (!_queue.Contains(client))
            _queue.Add(client);
        LobbyCommands.RpcQueueStatus(client, true);
    }

    public static void HandleLeaveQueue(RemoteClient client)
    {
        _queue.Remove(client);
        LobbyCommands.RpcQueueStatus(client, false);
    }

    private static void TryMatchmake()
    {
        int needed = GameConfig.TeamSize * 2;
        while (_queue.Count >= needed)
        {
            var matched = _queue.Take(needed).ToList();
            _queue.RemoveRange(0, needed);
            CreateRoom(matched);
        }
    }

    private static void CreateRoom(List<RemoteClient> players)
    {
        var room = new GameRoom();

        for (int i = 0; i < players.Count; i++)
        {
            var client = players[i];
            var data = GetData(client);
            if (data == null) continue;

            if (i < GameConfig.TeamSize)
            { room.BluePlayers.Add(client); data.Team = 0; }
            else
            { room.RedPlayers.Add(client); data.Team = 1; }

            data.CurrentRoom = room;
            data.SelectedCharacter = -1;
            data.LockedIn = false;
            LobbyCommands.RpcQueueStatus(client, false);
            LobbyCommands.RpcMatchFound(client);
        }

        _rooms.Add(room);
        BroadcastCharSelectUpdate(room);
        Console.WriteLine($"[Server] Match created with {players.Count} players");
    }

    // ── Character Select ──

    public static void HandleSelectCharacter(RemoteClient client, int charId)
    {
        var data = GetData(client);
        if (data?.CurrentRoom == null || data.LockedIn) return;
        if (data.CurrentRoom.Phase != GamePhase.CharSelect) return;
        if (charId < 0 || charId >= GameConfig.Kits.Length) return;

        data.SelectedCharacter = charId;
        BroadcastCharSelectUpdate(data.CurrentRoom);
    }

    public static void HandleLockIn(RemoteClient client)
    {
        var data = GetData(client);
        if (data?.CurrentRoom == null || data.LockedIn) return;
        if (data.CurrentRoom.Phase != GamePhase.CharSelect) return;
        if (data.SelectedCharacter < 0) return;

        data.LockedIn = true;
        BroadcastCharSelectUpdate(data.CurrentRoom);

        if (data.CurrentRoom.AllPlayers.All(p => GetData(p)?.LockedIn == true))
            StartGame(data.CurrentRoom);
    }

    private static void BroadcastCharSelectUpdate(GameRoom room)
    {
        var all = room.AllPlayers;
        var names = new string[all.Count];
        var teams = new int[all.Count];
        var chars = new int[all.Count];
        var locked = new bool[all.Count];

        for (int i = 0; i < all.Count; i++)
        {
            var d = GetData(all[i]);
            names[i] = d?.Username ?? "";
            teams[i] = d?.Team ?? 0;
            chars[i] = d?.SelectedCharacter ?? -1;
            locked[i] = d?.LockedIn ?? false;
        }

        foreach (var p in all)
            LobbyCommands.RpcCharSelectUpdate(p, names, teams, chars, locked, room.CharSelectTimer);
    }

    // ── Game Lifecycle ──

    private static void TickRoom(GameRoom room, float dt)
    {
        if (room.Phase == GamePhase.CharSelect)
        {
            room.CharSelectTimer -= dt;
            if (room.CharSelectTimer <= 0)
                StartGame(room);
            return;
        }

        if (room.Phase == GamePhase.Finished)
        {
            room.PostGameTimer -= dt;
            if (room.PostGameTimer <= 0)
                ReturnPlayersToLobby(room);
            return;
        }

        GameSimulation.Tick(room, dt);
    }

    private static void StartGame(GameRoom room)
    {
        room.Phase = GamePhase.Playing;
        room.GameStarted = true;
        room.MinionSpawnTimer = GameConfig.FirstMinionSpawnTime;
        room.Map = Server.CreateMap<ArenaMap>();

        // Assign random characters to players who didn't pick
        var rng = new Random();
        foreach (var p in room.AllPlayers)
        {
            var d = GetData(p);
            if (d != null && d.SelectedCharacter < 0)
            {
                d.SelectedCharacter = rng.Next(GameConfig.Kits.Length);
                d.LockedIn = true;
            }
        }

        // Spawn turrets & nexuses
        SpawnTurrets(room, Team.Blue, GameConfig.BlueTurretX);
        SpawnTurrets(room, Team.Red, GameConfig.RedTurretX);
        room.BlueNexus = Server.Spawn<NexusEntity>(room.Map, e => { e.X.Value = GameConfig.BlueNexusX; e.Y.Value = GameConfig.LaneY; e.HP.Value = e.MaxHP.Value = GameConfig.NexusHP; e.TeamId.Value = (byte)Team.Blue; });
        room.RedNexus = Server.Spawn<NexusEntity>(room.Map, e => { e.X.Value = GameConfig.RedNexusX; e.Y.Value = GameConfig.LaneY; e.HP.Value = e.MaxHP.Value = GameConfig.NexusHP; e.TeamId.Value = (byte)Team.Red; });

        // Spawn champions
        foreach (var p in room.AllPlayers)
        {
            var d = GetData(p);
            if (d == null) continue;
            var kit = GameConfig.Kits[d.SelectedCharacter];
            float spawnX = d.Team == 0 ? GameConfig.BlueBaseX + 5f : GameConfig.RedBaseX - 5f;

            if (p.PlayerEntity != null)
                p.UnassignPlayerEntity();

            var champ = Server.Spawn<ChampionEntity>(room.Map!, p, c => {
                c.CharId.Value = d.SelectedCharacter;
                c.TeamId.Value = (byte)d.Team;
                c.PlayerName.Value = d.Username;
                c.X.Value = spawnX; c.Y.Value = GameConfig.LaneY;
                c.TargetX = spawnX; c.TargetY = GameConfig.LaneY;
                c.HP.Value = c.MaxHP.Value = kit.BaseHP;
                c.Mana.Value = c.MaxMana.Value = kit.BaseMana;
                c.Level.Value = 1;
                c.Gold.Value = GameConfig.StartingGold;
            });
            kit.OnSpawn(champ);
            room.Champions.Add(champ);
            p.AssignPlayerEntity(champ);
        }

        foreach (var p in room.AllPlayers)
        {
            LobbyCommands.RpcGameStart(p);
            LobbyCommands.RpcScoreUpdate(p, 0, 0);
        }
        Console.WriteLine("[Server] Game started!");
    }

    private static void SpawnTurrets(GameRoom room, Team team, float[] positions)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            var turret = Server.Spawn<TurretEntity>(room.Map!, e => {
                e.X.Value = positions[i]; e.Y.Value = GameConfig.LaneY;
                e.HP.Value = e.MaxHP.Value = GameConfig.TurretHP;
                e.TeamId.Value = (byte)team;
                e.TurretIndex = i;
            });
            room.Turrets.Add(turret);
        }
    }

    public static void EndGame(GameRoom room, int winningTeam)
    {
        room.Phase = GamePhase.Finished;
        room.WinningTeam = winningTeam;
        room.PostGameTimer = 10f;

        string winner = winningTeam == 0 ? "Blue" : "Red";
        foreach (var p in room.AllPlayers)
        {
            LobbyCommands.RpcGameOver(p, winningTeam);
            LobbyCommands.RpcKillFeed(p, $"{winner} team wins!");
        }
        Console.WriteLine($"[Server] Game over! {winner} wins!");
    }

    private static void ReturnPlayersToLobby(GameRoom room)
    {
        foreach (var p in room.AllPlayers)
        {
            var data = GetData(p);
            if (data == null) continue;
            data.CurrentRoom = null;

            if (p.PlayerEntity != null)
                p.UnassignPlayerEntity();
            if (data.LobbyEntity != null && data.LobbyEntity.IsSpawned && _lobbyMap != null)
                p.AssignPlayerEntity(data.LobbyEntity);

            LobbyCommands.RpcReturnToLobby(p);
        }
    }

    private static void CleanupRoom(GameRoom room)
    {
        foreach (var c in room.Champions) if (c.IsSpawned) Server.Despawn(c);
        foreach (var m in room.Minions) if (m.IsSpawned) Server.Despawn(m);
        foreach (var t in room.Turrets) if (t.IsSpawned) Server.Despawn(t);
        foreach (var p in room.Projectiles) if (p.IsSpawned) Server.Despawn(p);
        foreach (var j in room.Javelins) if (j.IsSpawned) Server.Despawn(j);
        foreach (var tr in room.Traps) if (tr.IsSpawned) Server.Despawn(tr);
        if (room.BlueNexus?.IsSpawned == true) Server.Despawn(room.BlueNexus);
        if (room.RedNexus?.IsSpawned == true) Server.Despawn(room.RedNexus);
        if (room.Map != null) Server.DestroyMap(room.Map.MapId);
        Console.WriteLine("[Server] Game room cleaned up");
    }

    private static void HandlePlayerLeaveRoom(RemoteClient client, ServerPlayerData data)
    {
        var room = data.CurrentRoom;
        if (room == null) return;

        room.BluePlayers.Remove(client);
        room.RedPlayers.Remove(client);

        var champ = room.Champions.Find(c => c.Owner == client);
        if (champ != null)
        {
            if (champ.IsSpawned) Server.Despawn(champ);
            room.Champions.Remove(champ);
        }
        data.CurrentRoom = null;
    }

    // ── Lobby Chat ──

    public static void HandleLobbyChat(RemoteClient client, string message)
    {
        var data = GetData(client);
        if (data == null || string.IsNullOrEmpty(data.Username) || string.IsNullOrWhiteSpace(message)) return;

        foreach (var (c, d) in _playerData)
        {
            if (!string.IsNullOrEmpty(d.Username))
                LobbyCommands.RpcSystemMessage(c, $"{data.Username}: {message}");
        }
    }

    // ── Ability/Shop forwarding (called from entity commands) ──

    public static void HandleAbility(ChampionEntity champ, int slot, float targetX, float targetY)
    {
        GameRoom? room = null;
        foreach (var r in _rooms)
            if (r.Champions.Contains(champ)) { room = r; break; }
        if (room == null) return;
        CombatUtils.HandleAbility(room, champ, slot, targetX, targetY);
    }

    public static void HandleBuyItem(ChampionEntity champ, int itemId)
        => CombatUtils.HandleBuyItem(champ, itemId);

    public static void HandleSellItem(ChampionEntity champ, int slotIndex)
        => CombatUtils.HandleSellItem(champ, slotIndex);
}
