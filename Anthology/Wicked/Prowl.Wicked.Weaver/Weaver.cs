using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

namespace Prowl.Wicked.Weaver;

public class Weaver
{
    private readonly string _targetAssemblyPath;

    // Modules
    private ModuleDefinition _critsparkModule = null!;
    private ModuleDefinition _targetModule = null!;

    // Prowl.Wicked types (resolved from critspark module, used directly or imported)
    private TypeDefinition _networkObjectType = null!;
    private TypeDefinition _networkEntityType = null!;
    private TypeDefinition _remoteClientType = null!;
    private TypeDefinition _mapType = null!;
    private TypeDefinition _networkWriterType = null!;
    private TypeDefinition _networkReaderType = null!;
    private TypeDefinition _serverType = null!;
    private TypeDefinition _clientType = null!;
    private TypeDefinition _rpcPromiseType = null!;
    private TypeDefinition _rpcPromiseGenericType = null!;

    // New attribute types
    private TypeDefinition _entityCommandAttrType = null!;
    private TypeDefinition _entityRpcAttrType = null!;
    private TypeDefinition _mapRpcAttrType = null!;
    private TypeDefinition _staticCommandAttrType = null!;
    private TypeDefinition _staticRpcAttrType = null!;

    // Imported method references (imported into target module)
    private MethodReference _writerCtor = null!;
    private MethodReference _writerWriteByte = null!;
    private MethodReference _writerWriteSByte = null!;
    private MethodReference _writerWriteShort = null!;
    private MethodReference _writerWriteUShort = null!;
    private MethodReference _writerWriteInt = null!;
    private MethodReference _writerWriteUInt = null!;
    private MethodReference _writerWriteLong = null!;
    private MethodReference _writerWriteULong = null!;
    private MethodReference _writerWriteFloat = null!;
    private MethodReference _writerWriteDouble = null!;
    private MethodReference _writerWriteString = null!;
    private MethodReference _writerWriteBool = null!;
    private MethodReference _writerWriteVector2 = null!;
    private MethodReference _writerWriteGuid = null!;
    private MethodReference _writerWriteByteArray = null!;
    private MethodReference _writerWriteIntArray = null!;
    private MethodReference _writerWriteUIntArray = null!;
    private MethodReference _writerWriteFloatArray = null!;
    private MethodReference _writerWriteDoubleArray = null!;
    private MethodReference _writerWriteStringArray = null!;
    private MethodReference _writerWriteLongArray = null!;
    private MethodReference _writerWriteULongArray = null!;
    private MethodReference _writerWriteShortArray = null!;
    private MethodReference _writerWriteUShortArray = null!;
    private MethodReference _writerWriteBoolArray = null!;
    private MethodReference _writerWriteSerializableArray = null!;
    private MethodReference _writerToArraySegment = null!;
    private MethodReference _readerReadByte = null!;
    private MethodReference _readerReadSByte = null!;
    private MethodReference _readerReadShort = null!;
    private MethodReference _readerReadUShort = null!;
    private MethodReference _readerReadInt = null!;
    private MethodReference _readerReadUInt = null!;
    private MethodReference _readerReadLong = null!;
    private MethodReference _readerReadULong = null!;
    private MethodReference _readerReadFloat = null!;
    private MethodReference _readerReadDouble = null!;
    private MethodReference _readerReadString = null!;
    private MethodReference _readerReadBool = null!;
    private MethodReference _readerReadVector2 = null!;
    private MethodReference _readerReadByteArray = null!;
    private MethodReference _readerReadIntArray = null!;
    private MethodReference _readerReadUIntArray = null!;
    private MethodReference _readerReadFloatArray = null!;
    private MethodReference _readerReadDoubleArray = null!;
    private MethodReference _readerReadStringArray = null!;
    private MethodReference _readerReadLongArray = null!;
    private MethodReference _readerReadULongArray = null!;
    private MethodReference _readerReadShortArray = null!;
    private MethodReference _readerReadUShortArray = null!;
    private MethodReference _readerReadBoolArray = null!;
    private MethodReference _readerReadSerializableArray = null!;
    private MethodReference _readerReadGuid = null!;
    private MethodReference _writerWriteEntityRef = null!;
    private MethodReference _readerReadEntityRef = null!;
    private MethodReference _writerWriteSerializable = null!;
    private MethodReference _writerWriteEnum = null!;
    private MethodReference _readerReadSerializable = null!;
    private MethodReference _readerReadEnum = null!;
    private MethodReference _getIsServer = null!;
    private MethodReference _getIsClient = null!;
    private MethodReference _getSender = null!;
    private MethodReference _entityGetNetworkId = null!;
    private MethodReference _entityGetOwner = null!;
    private MethodReference _mapGetMapId = null!;
    private MethodReference _clientSendToServer = null!;
    private MethodReference _clientTrackPromise = null!;
    private MethodReference _rpcPromiseCtor = null!;
    private MethodReference _rpcPromiseGetCompleted = null!;
    private MethodReference _serverSendToEntityObservers = null!;
    private MethodReference _serverSendToEntityOwner = null!;
    private MethodReference _serverSendToMapObservers = null!;
    private MethodReference _serverSendToRemoteClient = null!;
    private MethodReference _serverSendToRemoteClients = null!;
    private MethodReference _serverSendRpcResponseVoid = null!;
    private MethodReference _serverSendRpcResponseInt = null!;
    private MethodReference _serverSendRpcResponseUInt = null!;
    private MethodReference _serverSendRpcResponseBool = null!;
    private MethodReference _serverSendRpcResponseString = null!;
    private MethodReference _serverSendRpcResponseFloat = null!;
    private MethodReference _serverSendRpcResponseVector2 = null!;
    private MethodReference _serverSendRpcResponseByte = null!;
    private MethodReference _serverSendRpcResponseSByte = null!;
    private MethodReference _serverSendRpcResponseShort = null!;
    private MethodReference _serverSendRpcResponseUShort = null!;
    private MethodReference _serverSendRpcResponseLong = null!;
    private MethodReference _serverSendRpcResponseULong = null!;
    private MethodReference _serverSendRpcResponseDouble = null!;
    private MethodReference _serverSendRpcResponseGuid = null!;
    private MethodReference _serverSendRpcResponseByteArray = null!;
    private MethodReference _serverSendRpcResponseIntArray = null!;
    private MethodReference _serverSendRpcResponseUIntArray = null!;
    private MethodReference _serverSendRpcResponseFloatArray = null!;
    private MethodReference _serverSendRpcResponseDoubleArray = null!;
    private MethodReference _serverSendRpcResponseStringArray = null!;
    private MethodReference _serverSendRpcResponseBoolArray = null!;
    private MethodReference _serverSendRpcResponseLongArray = null!;
    private MethodReference _serverSendRpcResponseULongArray = null!;
    private MethodReference _serverSendRpcResponseShortArray = null!;
    private MethodReference _serverSendRpcResponseUShortArray = null!;
    private MethodReference _serverSendRpcError = null!;
    private MethodReference _serverGetActive = null!;
    private MethodReference _clientGetIsConnected = null!;
    private MethodReference _serverRegisterStaticRpc = null!;
    private MethodReference _clientRegisterStaticRpc = null!;

    // Imported type references
    private TypeReference _voidRef = null!;
    private TypeReference _networkWriterRef = null!;
    private TypeReference _networkReaderRef = null!;
    private TypeReference _rpcPromiseRef = null!;
    private TypeReference _networkEntityRef = null!;
    private TypeReference _remoteClientRef = null!;
    private TypeReference _mapRef = null!;

    public Weaver(string targetAssemblyPath)
    {
        _targetAssemblyPath = targetAssemblyPath;
    }

    public bool Run()
    {
        var assemblyDir = Path.GetDirectoryName(Path.GetFullPath(_targetAssemblyPath))!;
        var critsparkDllPath = Path.Combine(assemblyDir, "Prowl.Wicked.dll");

        if (!File.Exists(critsparkDllPath))
        {
            Console.Error.WriteLine($"Prowl.Wicked.dll not found in: {assemblyDir}");
            return false;
        }

        var resolver = new WeaverAssemblyResolver();
        resolver.AddSearchDirectory(assemblyDir);

        // Read both assemblies into memory to avoid file locks
        // Also read PDB symbols so breakpoints survive weaving
        using var critsparkAssembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(File.ReadAllBytes(critsparkDllPath)),
            CreateReaderParams(critsparkDllPath, resolver));
        _critsparkModule = critsparkAssembly.MainModule;
        resolver.Register(critsparkAssembly);

        var targetPdbPath = Path.ChangeExtension(_targetAssemblyPath, ".pdb");
        bool targetHasSymbols = File.Exists(targetPdbPath);
        using var targetAssembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(File.ReadAllBytes(_targetAssemblyPath)),
            CreateReaderParams(_targetAssemblyPath, resolver));
        _targetModule = targetAssembly.MainModule;

        // Double-weave check: look for __UserCode_ methods in the target assembly (including nested types)
        if (GetAllTypes(_targetModule).Any(t => t.Methods.Any(m => m.Name.StartsWith("__UserCode_"))))
        {
            Console.WriteLine("Assembly already weaved, skipping.");
            return true;
        }

        // Resolve all types and methods we need
        ResolveCritsparkTypes();
        ImportReferences();

        // ── RPC weaving ──
        bool success = WeaveRpcs();

        if (!success)
            return false;

        // Write target assembly back (preserving PDB symbols if present)
        var writerParams = new WriterParameters();
        if (targetHasSymbols)
        {
            writerParams.WriteSymbols = true;
            writerParams.SymbolWriterProvider = new PdbWriterProvider();
        }
        targetAssembly.Write(_targetAssemblyPath, writerParams);

        Console.WriteLine("Weaving complete.");
        return true;
    }

    // ── Type/method resolution ──

    private void ResolveCritsparkTypes()
    {
        _networkObjectType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.NetworkObject");
        _networkEntityType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.NetworkEntity");
        _remoteClientType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.RemoteClient");
        _mapType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.Map");
        _serverType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.Server");
        _clientType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.Client");
        _networkWriterType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.NetworkWriter");
        _networkReaderType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.NetworkReader");
        _rpcPromiseType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.RpcPromise");
        _rpcPromiseGenericType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.RpcPromise`1");

        // Attributes
        _entityCommandAttrType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.EntityCommandAttribute");
        _entityRpcAttrType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.EntityRpcAttribute");
        _mapRpcAttrType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.MapRpcAttribute");
        _staticCommandAttrType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.StaticCommandAttribute");
        _staticRpcAttrType = _critsparkModule.Types.First(t => t.FullName == "Prowl.Wicked.StaticRpcAttribute");
    }

    private void ImportReferences()
    {
        _voidRef = _targetModule.TypeSystem.Void;
        _networkWriterRef = _targetModule.ImportReference(_networkWriterType);
        _networkReaderRef = _targetModule.ImportReference(_networkReaderType);
        _rpcPromiseRef = _targetModule.ImportReference(_rpcPromiseType);
        _networkEntityRef = _targetModule.ImportReference(_networkEntityType);
        _remoteClientRef = _targetModule.ImportReference(_remoteClientType);
        _mapRef = _targetModule.ImportReference(_mapType);

        // NetworkWriter methods
        _writerCtor = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0));
        _writerWriteByte = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteByte"));
        _writerWriteSByte = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteSByte"));
        _writerWriteShort = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteShort"));
        _writerWriteUShort = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteUShort"));
        _writerWriteInt = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteInt"));
        _writerWriteUInt = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteUInt"));
        _writerWriteLong = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteLong"));
        _writerWriteULong = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteULong"));
        _writerWriteFloat = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteFloat"));
        _writerWriteDouble = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteDouble"));
        _writerWriteString = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteString"));
        _writerWriteBool = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteBool"));
        _writerWriteVector2 = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteVector2"));
        _writerWriteGuid = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteGuid"));
        _writerWriteByteArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteByteArray"));
        _writerWriteIntArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteIntArray"));
        _writerWriteUIntArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteUIntArray"));
        _writerWriteFloatArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteFloatArray"));
        _writerWriteDoubleArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteDoubleArray"));
        _writerWriteStringArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteStringArray"));
        _writerWriteLongArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteLongArray"));
        _writerWriteULongArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteULongArray"));
        _writerWriteShortArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteShortArray"));
        _writerWriteUShortArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteUShortArray"));
        _writerWriteBoolArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteBoolArray"));
        _writerWriteSerializableArray = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteArray" && m.HasGenericParameters));
        _writerToArraySegment = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "ToArraySegment"));

        // NetworkReader methods
        _readerReadByte = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadByte"));
        _readerReadSByte = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadSByte"));
        _readerReadShort = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadShort"));
        _readerReadUShort = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadUShort"));
        _readerReadInt = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadInt"));
        _readerReadUInt = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadUInt"));
        _readerReadLong = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadLong"));
        _readerReadULong = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadULong"));
        _readerReadFloat = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadFloat"));
        _readerReadDouble = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadDouble"));
        _readerReadString = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadString"));
        _readerReadBool = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadBool"));
        _readerReadVector2 = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadVector2"));
        _readerReadByteArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadByteArray"));
        _readerReadIntArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadIntArray"));
        _readerReadUIntArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadUIntArray"));
        _readerReadFloatArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadFloatArray"));
        _readerReadDoubleArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadDoubleArray"));
        _readerReadStringArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadStringArray"));
        _readerReadLongArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadLongArray"));
        _readerReadULongArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadULongArray"));
        _readerReadShortArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadShortArray"));
        _readerReadUShortArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadUShortArray"));
        _readerReadBoolArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadBoolArray"));
        _readerReadSerializableArray = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadArray" && m.HasGenericParameters));
        _readerReadGuid = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadGuid"));
        _writerWriteEntityRef = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteEntityRef"));
        _readerReadEntityRef = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadEntityRef"));
        _writerWriteSerializable = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "Write" && m.HasGenericParameters));
        _writerWriteEnum = _targetModule.ImportReference(_networkWriterType.Methods.First(m => m.Name == "WriteEnum" && m.HasGenericParameters));
        _readerReadSerializable = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "Read" && m.HasGenericParameters));
        _readerReadEnum = _targetModule.ImportReference(_networkReaderType.Methods.First(m => m.Name == "ReadEnum" && m.HasGenericParameters));

        // NetworkObject properties
        _getIsServer = _targetModule.ImportReference(_networkObjectType.Methods.First(m => m.Name == "get_IsServer"));
        _getIsClient = _targetModule.ImportReference(_networkObjectType.Methods.First(m => m.Name == "get_IsClient"));
        _getSender = _targetModule.ImportReference(_networkObjectType.Methods.First(m => m.Name == "get_Sender"));

        // NetworkEntity properties
        _entityGetNetworkId = _targetModule.ImportReference(_networkEntityType.Methods.First(m => m.Name == "get_NetworkId"));
        _entityGetOwner = _targetModule.ImportReference(_networkEntityType.Methods.First(m => m.Name == "get_Owner"));

        // Map property
        _mapGetMapId = _targetModule.ImportReference(_mapType.Methods.First(m => m.Name == "get_MapId"));

        // Client helpers
        _clientSendToServer = _targetModule.ImportReference(_clientType.Methods.First(m => m.Name == "__SendToServer"));
        _clientTrackPromise = _targetModule.ImportReference(_clientType.Methods.First(m => m.Name == "__TrackPromise"));

        // RpcPromise
        _rpcPromiseCtor = _targetModule.ImportReference(_rpcPromiseType.Methods.First(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0));
        _rpcPromiseGetCompleted = _targetModule.ImportReference(_rpcPromiseType.Methods.First(m => m.Name == "get_Completed"));

        // Server send helpers
        _serverSendToEntityObservers = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendToEntityObservers"));
        _serverSendToEntityOwner = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendToEntityOwner"));
        _serverSendToMapObservers = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendToMapObservers"));
        _serverSendToRemoteClient = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendToRemoteClient"));
        _serverSendToRemoteClients = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendToRemoteClients"));

        // Server RPC response helpers
        _serverSendRpcResponseVoid = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseVoid"));
        _serverSendRpcResponseInt = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseInt"));
        _serverSendRpcResponseUInt = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseUInt"));
        _serverSendRpcResponseBool = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseBool"));
        _serverSendRpcResponseString = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseString"));
        _serverSendRpcResponseFloat = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseFloat"));
        _serverSendRpcResponseVector2 = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseVector2"));
        _serverSendRpcResponseByte = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseByte"));
        _serverSendRpcResponseSByte = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseSByte"));
        _serverSendRpcResponseShort = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseShort"));
        _serverSendRpcResponseUShort = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseUShort"));
        _serverSendRpcResponseLong = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseLong"));
        _serverSendRpcResponseULong = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseULong"));
        _serverSendRpcResponseDouble = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseDouble"));
        _serverSendRpcResponseGuid = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseGuid"));
        _serverSendRpcResponseByteArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseByteArray"));
        _serverSendRpcResponseIntArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseIntArray"));
        _serverSendRpcResponseUIntArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseUIntArray"));
        _serverSendRpcResponseFloatArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseFloatArray"));
        _serverSendRpcResponseDoubleArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseDoubleArray"));
        _serverSendRpcResponseStringArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseStringArray"));
        _serverSendRpcResponseBoolArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseBoolArray"));
        _serverSendRpcResponseLongArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseLongArray"));
        _serverSendRpcResponseULongArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseULongArray"));
        _serverSendRpcResponseShortArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseShortArray"));
        _serverSendRpcResponseUShortArray = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcResponseUShortArray"));
        _serverSendRpcError = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "__SendRpcError"));

        // Static RPC support
        _serverGetActive = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "get_Active"));
        _clientGetIsConnected = _targetModule.ImportReference(_clientType.Methods.First(m => m.Name == "get_IsConnected"));
        _serverRegisterStaticRpc = _targetModule.ImportReference(_serverType.Methods.First(m => m.Name == "RegisterStaticRpc"));
        _clientRegisterStaticRpc = _targetModule.ImportReference(_clientType.Methods.First(m => m.Name == "RegisterStaticRpc"));
    }

    // ── RPC weaving (into target assembly) ──

    private bool WeaveRpcs()
    {
        bool success = true;

        // ── Pass 1: Instance RPCs (Entity and Map subtypes, excluding RemoteClient) ──
        var allTypes = GetAllTypes(_targetModule)
            .Where(t => InheritsFrom(t, "Prowl.Wicked.NetworkObject") && !InheritsFrom(t, "Prowl.Wicked.RemoteClient"))
            .OrderBy(GetInheritanceDepth)
            .ToList();

        // Track accumulated RPC counts per type for inheritance chain
        var rpcCounts = new Dictionary<string, (int command, int rpc)>();

        foreach (var type in allTypes)
        {
            bool isEntity = InheritsFrom(type, "Prowl.Wicked.NetworkEntity");
            bool isMap = InheritsFrom(type, "Prowl.Wicked.Map");

            var commands = new List<MethodDefinition>(); // client→server
            var rpcs = new List<MethodDefinition>();     // server→client

            foreach (var method in type.Methods.ToList())
            {
                if (method.IsAbstract) continue;

                bool hasEntityCommand = HasAttribute(method, _entityCommandAttrType.FullName);
                bool hasEntityRpc = HasAttribute(method, _entityRpcAttrType.FullName);
                bool hasMapRpc = HasAttribute(method, _mapRpcAttrType.FullName);
                bool hasStaticCommand = HasAttribute(method, _staticCommandAttrType.FullName);
                bool hasStaticRpc = HasAttribute(method, _staticRpcAttrType.FullName);

                // Validation: wrong context
                if (isMap && hasEntityCommand)
                {
                    Console.Error.WriteLine($"Error: [EntityCommand] on Map subtype method {type.FullName}.{method.Name}");
                    success = false; continue;
                }
                if (isMap && hasEntityRpc)
                {
                    Console.Error.WriteLine($"Error: [EntityRpc] on Map subtype method {type.FullName}.{method.Name}");
                    success = false; continue;
                }
                if (isEntity && hasMapRpc)
                {
                    Console.Error.WriteLine($"Error: [MapRpc] on Entity subtype method {type.FullName}.{method.Name}");
                    success = false; continue;
                }

                // Instance attributes on static methods
                if ((hasEntityCommand || hasEntityRpc || hasMapRpc) && method.IsStatic)
                {
                    var attrName = hasEntityCommand ? "EntityCommand" : hasEntityRpc ? "EntityRpc" : "MapRpc";
                    Console.Error.WriteLine($"Error: [{attrName}] on static method {type.FullName}.{method.Name} — use [StaticCommand]/[StaticRpc] for static methods");
                    success = false; continue;
                }

                // Static attributes on instance methods
                if (hasStaticCommand && !method.IsStatic)
                {
                    Console.Error.WriteLine($"Error: [StaticCommand] on non-static method {type.FullName}.{method.Name}");
                    success = false; continue;
                }
                if (hasStaticRpc && !method.IsStatic)
                {
                    Console.Error.WriteLine($"Error: [StaticRpc] on non-static method {type.FullName}.{method.Name}");
                    success = false; continue;
                }

                // Detect multiple RPC attributes on same method
                int attrCount = (hasEntityCommand ? 1 : 0) + (hasEntityRpc ? 1 : 0) + (hasMapRpc ? 1 : 0) + (hasStaticCommand ? 1 : 0) + (hasStaticRpc ? 1 : 0);
                if (attrCount > 1)
                {
                    Console.Error.WriteLine($"Error: Multiple RPC attributes on method {type.FullName}.{method.Name}");
                    success = false; continue;
                }

                // EntityCommand must return void, RpcPromise, or RpcPromise<T>
                if (hasEntityCommand)
                {
                    var ret = method.ReturnType.FullName;
                    bool validReturn = ret == "System.Void"
                        || ret == _rpcPromiseType.FullName
                        || ret.StartsWith("Prowl.Wicked.RpcPromise`1");
                    if (!validReturn)
                    {
                        Console.Error.WriteLine($"Error: [EntityCommand] method {type.FullName}.{method.Name} must return void, RpcPromise, or RpcPromise<T>");
                        success = false; continue;
                    }
                }

                // EntityRpc/MapRpc must return void — non-void produces invalid IL
                if (hasEntityRpc && method.ReturnType.FullName != "System.Void")
                {
                    Console.Error.WriteLine($"Error: [EntityRpc] method {type.FullName}.{method.Name} must return void");
                    success = false; continue;
                }
                if (hasMapRpc && method.ReturnType.FullName != "System.Void")
                {
                    Console.Error.WriteLine($"Error: [MapRpc] method {type.FullName}.{method.Name} must return void");
                    success = false; continue;
                }

                // MapRpc with Owner target — compile-time error
                if (hasMapRpc)
                {
                    var attr = method.CustomAttributes.First(a => a.AttributeType.FullName == _mapRpcAttrType.FullName);
                    foreach (var prop in attr.Properties)
                    {
                        if (prop.Name == "Target" && (int)prop.Argument.Value == (int)RpcTargetValue.Owner)
                        {
                            Console.Error.WriteLine($"Error: [MapRpc(Target = RpcTarget.Owner)] on {type.FullName}.{method.Name} — maps have no owner");
                            success = false;
                        }
                    }
                    if (!success) continue;
                }

                // Player-target EntityRpc/MapRpc — first param must be RemoteClient or RemoteClient[]
                if (hasEntityRpc || hasMapRpc)
                {
                    var attr = method.CustomAttributes.First(a =>
                        a.AttributeType.FullName == _entityRpcAttrType.FullName ||
                        a.AttributeType.FullName == _mapRpcAttrType.FullName);
                    bool isPlayerTarget = false;
                    foreach (var prop in attr.Properties)
                    {
                        if (prop.Name == "Target" && (int)prop.Argument.Value == (int)RpcTargetValue.Player)
                            isPlayerTarget = true;
                    }
                    if (isPlayerTarget)
                    {
                        var attrName = hasEntityRpc ? "EntityRpc" : "MapRpc";
                        if (method.Parameters.Count == 0 ||
                            (method.Parameters[0].ParameterType.FullName != _remoteClientType.FullName &&
                             method.Parameters[0].ParameterType.FullName != _remoteClientType.FullName + "[]"))
                        {
                            Console.Error.WriteLine($"Error: [{attrName}(Target = RpcTarget.Player)] method {type.FullName}.{method.Name} first parameter must be RemoteClient or RemoteClient[]");
                            success = false; continue;
                        }
                    }
                }

                // ExcludeOwner only makes sense with Observers target
                if (hasEntityRpc)
                {
                    var attr = method.CustomAttributes.First(a => a.AttributeType.FullName == _entityRpcAttrType.FullName);
                    var target = RpcTargetValue.Observers;
                    bool excludeOwner = false;
                    foreach (var prop in attr.Properties)
                    {
                        if (prop.Name == "Target") target = (RpcTargetValue)(int)prop.Argument.Value;
                        else if (prop.Name == "ExcludeOwner") excludeOwner = (bool)prop.Argument.Value;
                    }
                    if (excludeOwner && target != RpcTargetValue.Observers)
                    {
                        Console.Error.WriteLine($"Error: [EntityRpc] on {type.FullName}.{method.Name} — ExcludeOwner is only valid with Target = Observers");
                        success = false; continue;
                    }
                }

                if (hasEntityCommand)
                    commands.Add(method);
                else if (hasEntityRpc)
                    rpcs.Add(method);
                else if (hasMapRpc)
                    rpcs.Add(method);
            }

            // Look up parent's accumulated counts
            int inheritedCommandCount = 0;
            int inheritedRpcCount = 0;
            if (type.BaseType != null)
            {
                var baseName = type.BaseType.Resolve()?.FullName;
                if (baseName != null && rpcCounts.TryGetValue(baseName, out var parentCounts))
                {
                    inheritedCommandCount = parentCounts.command;
                    inheritedRpcCount = parentCounts.rpc;
                }
            }

            rpcCounts[type.FullName] = (inheritedCommandCount + commands.Count, inheritedRpcCount + rpcs.Count);

            if (commands.Count == 0 && rpcs.Count == 0)
                continue;

            // Sort for deterministic ordering
            commands.Sort((a, b) => string.Compare(GetUserCodeName(a), GetUserCodeName(b), StringComparison.Ordinal));
            rpcs.Sort((a, b) => string.Compare(GetUserCodeName(a), GetUserCodeName(b), StringComparison.Ordinal));

            byte objectKind = GetObjectKind(type);
            Console.WriteLine($"  RPC weaving: {type.FullName} ({commands.Count} commands, {rpcs.Count} rpcs, inherited: {inheritedCommandCount}c/{inheritedRpcCount}r)");

            // Weave each command (offset by inherited count)
            for (int i = 0; i < commands.Count; i++)
            {
                var method = commands[i];
                var userCode = MoveBodyToUserCode(type, method);
                WeaveServerRpcBody(type, method, userCode, (ushort)(inheritedCommandCount + i), objectKind);
            }

            // Weave each RPC (offset by inherited count)
            for (int i = 0; i < rpcs.Count; i++)
            {
                var method = rpcs[i];
                var userCode = MoveBodyToUserCode(type, method);
                WeaveClientRpcBody(type, method, userCode, (ushort)(inheritedRpcCount + i), objectKind);
            }

            // Generate dispatch overrides
            if (commands.Count > 0)
                GenerateServerRpcDispatch(type, commands, objectKind, inheritedCommandCount);
            if (rpcs.Count > 0)
                GenerateClientRpcDispatch(type, rpcs, inheritedRpcCount);
        }

        // ── Pass 2: Static RPCs ──
        var staticRpcClasses = new List<(TypeDefinition type, List<MethodDefinition> commands, List<MethodDefinition> rpcs)>();

        foreach (var type in GetAllTypes(_targetModule))
        {
            var staticCommands = new List<MethodDefinition>();
            var staticRpcs = new List<MethodDefinition>();

            foreach (var method in type.Methods.ToList())
            {
                if (method.IsAbstract) continue;

                bool hasStaticCommand = HasAttribute(method, _staticCommandAttrType.FullName);
                bool hasStaticRpc = HasAttribute(method, _staticRpcAttrType.FullName);

                if (hasStaticCommand)
                {
                    if (!method.IsStatic)
                    {
                        Console.Error.WriteLine($"Error: [StaticCommand] on non-static method {type.FullName}.{method.Name}");
                        success = false; continue;
                    }
                    var ret = method.ReturnType.FullName;
                    bool validReturn = ret == "System.Void"
                        || ret == _rpcPromiseType.FullName
                        || ret.StartsWith("Prowl.Wicked.RpcPromise`1");
                    if (!validReturn)
                    {
                        Console.Error.WriteLine($"Error: [StaticCommand] method {type.FullName}.{method.Name} must return void, RpcPromise, or RpcPromise<T>");
                        success = false; continue;
                    }
                    staticCommands.Add(method);
                }
                else if (hasStaticRpc)
                {
                    if (!method.IsStatic)
                    {
                        Console.Error.WriteLine($"Error: [StaticRpc] on non-static method {type.FullName}.{method.Name}");
                        success = false; continue;
                    }
                    // Validate first param is RemoteClient or RemoteClient[]
                    if (method.Parameters.Count == 0 ||
                        (method.Parameters[0].ParameterType.FullName != _remoteClientType.FullName &&
                         method.Parameters[0].ParameterType.FullName != _remoteClientType.FullName + "[]"))
                    {
                        Console.Error.WriteLine($"Error: [StaticRpc] method {type.FullName}.{method.Name} first parameter must be RemoteClient or RemoteClient[]");
                        success = false; continue;
                    }
                    // Validate no return value (static RPCs are server→client)
                    if (method.ReturnType.FullName != "System.Void")
                    {
                        Console.Error.WriteLine($"Error: [StaticRpc] method {type.FullName}.{method.Name} must return void");
                        success = false; continue;
                    }
                    staticRpcs.Add(method);
                }
            }

            if (staticCommands.Count > 0 || staticRpcs.Count > 0)
                staticRpcClasses.Add((type, staticCommands, staticRpcs));
        }

        // Sort classes by FullName for deterministic rpcTypeId assignment
        staticRpcClasses.Sort((a, b) => string.Compare(a.type.FullName, b.type.FullName, StringComparison.Ordinal));

        ushort nextRpcTypeId = 1;
        var registrationCalls = new List<(ushort rpcTypeId, TypeDefinition type, bool hasCommands, bool hasRpcs)>();

        for (int c = 0; c < staticRpcClasses.Count; c++)
        {
            var (type, staticCommands, staticRpcs) = staticRpcClasses[c];
            ushort rpcTypeId = nextRpcTypeId++;

            staticCommands.Sort((a, b) => string.Compare(GetUserCodeName(a), GetUserCodeName(b), StringComparison.Ordinal));
            staticRpcs.Sort((a, b) => string.Compare(GetUserCodeName(a), GetUserCodeName(b), StringComparison.Ordinal));

            Console.WriteLine($"  Static RPC weaving: {type.FullName} (typeId={rpcTypeId}, {staticCommands.Count} commands, {staticRpcs.Count} rpcs)");

            for (int i = 0; i < staticCommands.Count; i++)
            {
                var method = staticCommands[i];
                var userCode = MoveBodyToStaticUserCode(type, method);
                WeaveStaticCommandBody(type, method, userCode, (ushort)i, rpcTypeId);
            }

            for (int i = 0; i < staticRpcs.Count; i++)
            {
                var method = staticRpcs[i];
                var userCode = MoveBodyToStaticUserCode(type, method);
                WeaveStaticRpcBody(type, method, userCode, (ushort)i, rpcTypeId);
            }

            if (staticCommands.Count > 0)
                GenerateStaticCommandDispatch(type, staticCommands);
            if (staticRpcs.Count > 0)
                GenerateStaticRpcDispatch(type, staticRpcs);

            registrationCalls.Add((rpcTypeId, type, staticCommands.Count > 0, staticRpcs.Count > 0));
        }

        // Generate static RPC registration class
        if (registrationCalls.Count > 0)
            GenerateStaticRpcRegistration(registrationCalls);

        return success;
    }

    // ── Move original body to __UserCode_X (instance methods) ──

    private static string GetUserCodeName(MethodDefinition method)
    {
        if (method.Parameters.Count == 0)
            return $"__UserCode_{method.Name}";
        var suffix = string.Join("_", method.Parameters.Select(p => p.ParameterType.FullName.Replace(".", "_")));
        return $"__UserCode_{method.Name}_{suffix}";
    }

    private MethodDefinition MoveBodyToUserCode(TypeDefinition type, MethodDefinition method)
    {
        var userCode = new MethodDefinition(
            GetUserCodeName(method),
            MethodAttributes.Private | MethodAttributes.HideBySig,
            method.ReturnType);

        // Copy parameters
        foreach (var param in method.Parameters)
            userCode.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));

        // Swap bodies
        userCode.Body = method.Body;
        method.Body = new MethodBody(method);

        // Move debug info (sequence points + scopes) so breakpoints follow the user code
        MoveDebugInfo(method, userCode);

        // Remap parameter references in the moved body's instructions.
        foreach (var instr in userCode.Body.Instructions)
        {
            if (instr.Operand is ParameterDefinition paramDef)
            {
                int idx = method.Parameters.IndexOf(paramDef);
                if (idx >= 0)
                    instr.Operand = userCode.Parameters[idx];
            }
        }

        type.Methods.Add(userCode);
        return userCode;
    }

    // ── Move original body for static methods ──

    private MethodDefinition MoveBodyToStaticUserCode(TypeDefinition type, MethodDefinition method)
    {
        var userCode = new MethodDefinition(
            GetUserCodeName(method),
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            method.ReturnType);

        // Copy parameters
        foreach (var param in method.Parameters)
            userCode.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));

        // Swap bodies
        userCode.Body = method.Body;
        method.Body = new MethodBody(method);

        // Move debug info (sequence points + scopes) so breakpoints follow the user code
        MoveDebugInfo(method, userCode);

        // Remap parameter references
        foreach (var instr in userCode.Body.Instructions)
        {
            if (instr.Operand is ParameterDefinition paramDef)
            {
                int idx = method.Parameters.IndexOf(paramDef);
                if (idx >= 0)
                    instr.Operand = userCode.Parameters[idx];
            }
        }

        type.Methods.Add(userCode);
        return userCode;
    }

    // ── ServerRpc body weaving (EntityCommand) ──

    private void WeaveServerRpcBody(TypeDefinition type, MethodDefinition method,
        MethodDefinition userCode, ushort methodIndex, byte objectKind)
    {
        var il = method.Body.GetILProcessor();
        var returnType = method.ReturnType;
        bool isVoid = returnType.FullName == "System.Void";
        bool isPromise = !isVoid && returnType.FullName == _rpcPromiseType.FullName;
        bool isGenericPromise = !isVoid && !isPromise && returnType.FullName.StartsWith("Prowl.Wicked.RpcPromise`1");

        // if (IsServer) throw new InvalidOperationException(...)
        var clientPath = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, _getIsServer));
        il.Append(il.Create(OpCodes.Brfalse, clientPath));
        var invalidOpCtor = _targetModule.ImportReference(
            typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }));
        il.Append(il.Create(OpCodes.Ldstr, "Commands can only be called on the client."));
        il.Append(il.Create(OpCodes.Newobj, invalidOpCtor));
        il.Append(il.Create(OpCodes.Throw));

        // Client path: serialize and send
        il.Append(clientPath);

        // var writer = new NetworkWriter();
        var writerVar = new VariableDefinition(_networkWriterRef);
        method.Body.Variables.Add(writerVar);
        il.Append(il.Create(OpCodes.Newobj, _writerCtor));
        il.Append(il.Create(OpCodes.Stloc, writerVar));

        // writer.WriteByte(0x01) - RpcCall
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)0x01));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // writer.WriteByte(objectKind)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)objectKind));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // Write object identifier
        EmitWriteObjectId(il, writerVar, objectKind, type);

        // writer.WriteUShort(methodIndex)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)methodIndex));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        if (isVoid)
        {
            // promiseId = 0
            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

            // Serialize parameters
            EmitSerializeParams(il, writerVar, method.Parameters, objectKind);

            // Client.__SendToServer(writer.ToArraySegment())
            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
            il.Append(il.Create(OpCodes.Call, _clientSendToServer));
            il.Append(il.Create(OpCodes.Ret));
        }
        else if (isPromise)
        {
            EmitPromiseClientPath(il, method, writerVar, objectKind);
        }
        else if (isGenericPromise)
        {
            EmitGenericPromiseClientPath(il, method, writerVar, returnType, objectKind);
        }

        method.Body.InitLocals = true;
    }

    // ── ClientRpc body weaving (EntityRpc / MapRpc) ──

    private void WeaveClientRpcBody(TypeDefinition type, MethodDefinition method,
        MethodDefinition userCode, ushort methodIndex, byte objectKind)
    {
        // Read attribute properties based on whether it's EntityRpc or MapRpc
        RpcTargetValue target;
        bool excludeOwner;

        if (objectKind == 0) // Entity
        {
            var attrData = GetEntityRpcAttribute(method);
            target = attrData.target;
            excludeOwner = attrData.excludeOwner;
        }
        else // Map (objectKind == 1)
        {
            var attrData = GetMapRpcAttribute(method);
            target = attrData.target;
            excludeOwner = false;
        }

        bool isPlayerTarget = target == RpcTargetValue.Player;
        bool isArrayPlayerTarget = false;
        if (isPlayerTarget && method.Parameters.Count > 0)
        {
            isArrayPlayerTarget = method.Parameters[0].ParameterType.FullName == _remoteClientType.FullName + "[]";
        }

        var il = method.Body.GetILProcessor();

        // if (IsClient) throw new InvalidOperationException(...)
        var serverPath = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, _getIsClient));
        il.Append(il.Create(OpCodes.Brfalse, serverPath));
        var invalidOpCtor = _targetModule.ImportReference(
            typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }));
        il.Append(il.Create(OpCodes.Ldstr, "RPCs can only be called on the server."));
        il.Append(il.Create(OpCodes.Newobj, invalidOpCtor));
        il.Append(il.Create(OpCodes.Throw));

        // Server path: serialize and send
        il.Append(serverPath);

        // Validate: Map with Owner or ExcludeOwner → throw
        if (objectKind == 1 && (target == RpcTargetValue.Owner || excludeOwner))
        {
            var invalidOpRef = _targetModule.ImportReference(
                typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }));
            il.Append(il.Create(OpCodes.Ldstr, "Owner/ExcludeOwner is not valid on Map RPCs."));
            il.Append(il.Create(OpCodes.Newobj, invalidOpRef));
            il.Append(il.Create(OpCodes.Throw));
            method.Body.InitLocals = true;
            return;
        }

        // var writer = new NetworkWriter();
        var writerVar = new VariableDefinition(_networkWriterRef);
        method.Body.Variables.Add(writerVar);
        il.Append(il.Create(OpCodes.Newobj, _writerCtor));
        il.Append(il.Create(OpCodes.Stloc, writerVar));

        // writer.WriteByte(0x04) - ClientRpcCall
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)0x04));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // writer.WriteByte(objectKind)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)objectKind));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // Write object identifier
        EmitWriteObjectId(il, writerVar, objectKind, type);

        // writer.WriteUShort(methodIndex)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)methodIndex));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        // Serialize parameters (skip first param for Player target)
        EmitSerializeParams(il, writerVar, method.Parameters, objectKind, isPlayerTarget);

        // Send based on target/objectKind combination
        var dataVar = new VariableDefinition(_targetModule.ImportReference(typeof(ArraySegment<byte>)));
        method.Body.Variables.Add(dataVar);
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
        il.Append(il.Create(OpCodes.Stloc, dataVar));

        if (objectKind == 0) // Entity
        {
            switch (target)
            {
                case RpcTargetValue.Observers:
                    il.Append(il.Create(OpCodes.Ldarg_0));
                    il.Append(il.Create(OpCodes.Ldloc, dataVar));
                    il.Append(il.Create(excludeOwner ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
                    il.Append(il.Create(OpCodes.Call, _serverSendToEntityObservers));
                    break;
                case RpcTargetValue.Owner:
                    il.Append(il.Create(OpCodes.Ldarg_0));
                    il.Append(il.Create(OpCodes.Ldloc, dataVar));
                    il.Append(il.Create(OpCodes.Call, _serverSendToEntityOwner));
                    break;
                case RpcTargetValue.Player:
                    il.Append(il.Create(OpCodes.Ldarg, method.Parameters[0]));
                    il.Append(il.Create(OpCodes.Ldloc, dataVar));
                    if (isArrayPlayerTarget)
                        il.Append(il.Create(OpCodes.Call, _serverSendToRemoteClients));
                    else
                        il.Append(il.Create(OpCodes.Call, _serverSendToRemoteClient));
                    break;
            }
        }
        else if (objectKind == 1) // Map
        {
            switch (target)
            {
                case RpcTargetValue.Observers:
                    il.Append(il.Create(OpCodes.Ldarg_0));
                    il.Append(il.Create(OpCodes.Ldloc, dataVar));
                    il.Append(il.Create(OpCodes.Call, _serverSendToMapObservers));
                    break;
                case RpcTargetValue.Player:
                    il.Append(il.Create(OpCodes.Ldarg, method.Parameters[0]));
                    il.Append(il.Create(OpCodes.Ldloc, dataVar));
                    if (isArrayPlayerTarget)
                        il.Append(il.Create(OpCodes.Call, _serverSendToRemoteClients));
                    else
                        il.Append(il.Create(OpCodes.Call, _serverSendToRemoteClient));
                    break;
            }
        }

        il.Append(il.Create(OpCodes.Ret));
        method.Body.InitLocals = true;
    }

    // ── Static command body weaving ──

    private void WeaveStaticCommandBody(TypeDefinition type, MethodDefinition method,
        MethodDefinition userCode, ushort methodIndex, ushort rpcTypeId)
    {
        var il = method.Body.GetILProcessor();
        var returnType = method.ReturnType;
        bool isVoid = returnType.FullName == "System.Void";
        bool isPromise = !isVoid && returnType.FullName == _rpcPromiseType.FullName;
        bool isGenericPromise = !isVoid && !isPromise && returnType.FullName.StartsWith("Prowl.Wicked.RpcPromise`1");

        // if (!Client.IsConnected) throw new InvalidOperationException(...)
        var clientPath = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Call, _clientGetIsConnected));
        il.Append(il.Create(OpCodes.Brtrue, clientPath));
        var invalidOpCtor = _targetModule.ImportReference(
            typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }));
        il.Append(il.Create(OpCodes.Ldstr, "Commands can only be called on the client."));
        il.Append(il.Create(OpCodes.Newobj, invalidOpCtor));
        il.Append(il.Create(OpCodes.Throw));

        // Client path: serialize and send
        il.Append(clientPath);

        var writerVar = new VariableDefinition(_networkWriterRef);
        method.Body.Variables.Add(writerVar);
        il.Append(il.Create(OpCodes.Newobj, _writerCtor));
        il.Append(il.Create(OpCodes.Stloc, writerVar));

        // writer.WriteByte(0x01) - RpcCall
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)0x01));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // writer.WriteByte(2) - objectKind = Static
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4_2));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // writer.WriteUShort(rpcTypeId)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)rpcTypeId));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        // writer.WriteUShort(methodIndex)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)methodIndex));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        if (isVoid)
        {
            // promiseId = 0
            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

            // Serialize all parameters
            EmitSerializeStaticParams(il, writerVar, method.Parameters);

            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
            il.Append(il.Create(OpCodes.Call, _clientSendToServer));
            il.Append(il.Create(OpCodes.Ret));
        }
        else if (isPromise)
        {
            // var promise = new RpcPromise();
            var promiseVar = new VariableDefinition(_rpcPromiseRef);
            method.Body.Variables.Add(promiseVar);
            il.Append(il.Create(OpCodes.Newobj, _rpcPromiseCtor));
            il.Append(il.Create(OpCodes.Stloc, promiseVar));

            var promiseIdVar = new VariableDefinition(_targetModule.TypeSystem.UInt16);
            method.Body.Variables.Add(promiseIdVar);
            il.Append(il.Create(OpCodes.Ldloc, promiseVar));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Call, _clientTrackPromise));
            il.Append(il.Create(OpCodes.Stloc, promiseIdVar));

            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldloc, promiseIdVar));
            il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

            EmitSerializeStaticParams(il, writerVar, method.Parameters);

            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
            il.Append(il.Create(OpCodes.Call, _clientSendToServer));

            il.Append(il.Create(OpCodes.Ldloc, promiseVar));
            il.Append(il.Create(OpCodes.Ret));
        }
        else if (isGenericPromise)
        {
            var genericReturn = (GenericInstanceType)returnType;
            var genericArg = genericReturn.GenericArguments[0];
            byte typeCode = GetReturnTypeCode(genericArg);

            var closedPromiseType = _targetModule.ImportReference(MakeGenericInstance(_rpcPromiseGenericType, genericArg));
            var closedCtor = new MethodReference(".ctor", _voidRef, closedPromiseType) { HasThis = true };
            closedCtor = _targetModule.ImportReference(closedCtor);

            var promiseVar = new VariableDefinition(closedPromiseType);
            method.Body.Variables.Add(promiseVar);
            il.Append(il.Create(OpCodes.Newobj, closedCtor));
            il.Append(il.Create(OpCodes.Stloc, promiseVar));

            var promiseIdVar = new VariableDefinition(_targetModule.TypeSystem.UInt16);
            method.Body.Variables.Add(promiseIdVar);
            il.Append(il.Create(OpCodes.Ldloc, promiseVar));
            il.Append(il.Create(OpCodes.Ldc_I4, (int)typeCode));
            il.Append(il.Create(OpCodes.Call, _clientTrackPromise));
            il.Append(il.Create(OpCodes.Stloc, promiseIdVar));

            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldloc, promiseIdVar));
            il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

            EmitSerializeStaticParams(il, writerVar, method.Parameters);

            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
            il.Append(il.Create(OpCodes.Call, _clientSendToServer));

            il.Append(il.Create(OpCodes.Ldloc, promiseVar));
            il.Append(il.Create(OpCodes.Ret));
        }

        method.Body.InitLocals = true;
    }

    // ── Static RPC body weaving ──

    private void WeaveStaticRpcBody(TypeDefinition type, MethodDefinition method,
        MethodDefinition userCode, ushort methodIndex, ushort rpcTypeId)
    {
        var il = method.Body.GetILProcessor();
        bool isArrayTarget = method.Parameters[0].ParameterType.FullName == _remoteClientType.FullName + "[]";

        // if (!Server.Active) throw new InvalidOperationException(...)
        var serverPath = il.Create(OpCodes.Nop);
        il.Append(il.Create(OpCodes.Call, _serverGetActive));
        il.Append(il.Create(OpCodes.Brtrue, serverPath));
        var invalidOpCtor = _targetModule.ImportReference(
            typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) }));
        il.Append(il.Create(OpCodes.Ldstr, "RPCs can only be called on the server."));
        il.Append(il.Create(OpCodes.Newobj, invalidOpCtor));
        il.Append(il.Create(OpCodes.Throw));

        // Server path: serialize and send
        il.Append(serverPath);

        var writerVar = new VariableDefinition(_networkWriterRef);
        method.Body.Variables.Add(writerVar);
        il.Append(il.Create(OpCodes.Newobj, _writerCtor));
        il.Append(il.Create(OpCodes.Stloc, writerVar));

        // writer.WriteByte(0x04) - ClientRpcCall
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)0x04));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // writer.WriteByte(2) - objectKind = Static
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4_2));
        il.Append(il.Create(OpCodes.Call, _writerWriteByte));

        // writer.WriteUShort(rpcTypeId)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)rpcTypeId));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        // writer.WriteUShort(methodIndex)
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)methodIndex));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        // Serialize parameters (skip first — it's the target)
        for (int i = 1; i < method.Parameters.Count; i++)
        {
            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldarg, method.Parameters[i]));
            EmitWriteParam(il, method.Parameters[i].ParameterType);
        }

        // Send
        var dataVar = new VariableDefinition(_targetModule.ImportReference(typeof(ArraySegment<byte>)));
        method.Body.Variables.Add(dataVar);
        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
        il.Append(il.Create(OpCodes.Stloc, dataVar));

        il.Append(il.Create(OpCodes.Ldarg, method.Parameters[0])); // target
        il.Append(il.Create(OpCodes.Ldloc, dataVar));
        if (isArrayTarget)
            il.Append(il.Create(OpCodes.Call, _serverSendToRemoteClients));
        else
            il.Append(il.Create(OpCodes.Call, _serverSendToRemoteClient));

        il.Append(il.Create(OpCodes.Ret));
        method.Body.InitLocals = true;
    }

    // ── Dispatch override generation (instance RPCs) ──

    private void GenerateServerRpcDispatch(TypeDefinition type, List<MethodDefinition> commands, byte objectKind, int inheritedCount)
    {
        var dispatchMethod = new MethodDefinition(
            "__DispatchServerRpc",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _voidRef);

        dispatchMethod.Parameters.Add(new ParameterDefinition("methodId", ParameterAttributes.None, _targetModule.TypeSystem.UInt16));
        dispatchMethod.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, _networkReaderRef));
        dispatchMethod.Parameters.Add(new ParameterDefinition("connectionId", ParameterAttributes.None, _targetModule.TypeSystem.UInt32));
        dispatchMethod.Parameters.Add(new ParameterDefinition("promiseId", ParameterAttributes.None, _targetModule.TypeSystem.UInt16));

        var il = dispatchMethod.Body.GetILProcessor();
        var retInstr = il.Create(OpCodes.Ret);

        // Build switch table
        var caseLabels = new Instruction[commands.Count];
        for (int i = 0; i < commands.Count; i++)
            caseLabels[i] = il.Create(OpCodes.Nop);

        // Load methodId, subtract inherited count to normalize own RPCs to 0-based
        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[0])); // methodId
        if (inheritedCount > 0)
        {
            il.Append(il.Create(OpCodes.Ldc_I4, inheritedCount));
            il.Append(il.Create(OpCodes.Sub));
        }
        il.Append(il.Create(OpCodes.Switch, caseLabels));

        // Default: chain to base dispatch if we have inherited RPCs, otherwise just return
        if (inheritedCount > 0)
        {
            var baseDispatch = FindBaseDispatchMethod(type, "__DispatchServerRpc");
            if (baseDispatch != null)
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[0]));
                il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[1]));
                il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[2]));
                il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[3]));
                il.Append(il.Create(OpCodes.Call, baseDispatch));
                il.Append(il.Create(OpCodes.Br, retInstr));
            }
            else
            {
                il.Append(il.Create(OpCodes.Br, retInstr));
            }
        }
        else
        {
            il.Append(il.Create(OpCodes.Br, retInstr));
        }

        for (int i = 0; i < commands.Count; i++)
        {
            il.Append(caseLabels[i]);
            var method = commands[i];
            var userCode = type.Methods.First(m => m.Name == GetUserCodeName(method));
            var returnType = method.ReturnType;
            bool isVoid = returnType.FullName == "System.Void";
            bool isPromise = !isVoid && returnType.FullName == _rpcPromiseType.FullName;
            bool isGenericPromise = !isVoid && !isPromise && returnType.FullName.StartsWith("Prowl.Wicked.RpcPromise`1");

            // RequireOwner check for entities
            if (objectKind == 0 && GetRequireOwner(method))
            {
                var afterCheck = il.Create(OpCodes.Nop);
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Call, _entityGetOwner));
                il.Append(il.Create(OpCodes.Call, _getSender));
                il.Append(il.Create(OpCodes.Beq, afterCheck));
                il.Append(il.Create(OpCodes.Ret));
                il.Append(afterCheck);
            }

            if (isVoid)
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                EmitReadParams(il, dispatchMethod, method.Parameters, objectKind);
                il.Append(il.Create(OpCodes.Call, userCode));
                il.Append(il.Create(OpCodes.Br, retInstr));
            }
            else if (isPromise)
            {
                EmitPromiseDispatch(il, dispatchMethod, method, userCode, retInstr, false);
            }
            else if (isGenericPromise)
            {
                EmitGenericPromiseDispatch(il, dispatchMethod, method, userCode, returnType, retInstr, false);
            }
        }

        il.Append(retInstr);
        dispatchMethod.Body.InitLocals = true;
        type.Methods.Add(dispatchMethod);
    }

    private void GenerateClientRpcDispatch(TypeDefinition type, List<MethodDefinition> rpcs, int inheritedCount)
    {
        var dispatchMethod = new MethodDefinition(
            "__DispatchClientRpc",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _voidRef);

        dispatchMethod.Parameters.Add(new ParameterDefinition("methodId", ParameterAttributes.None, _targetModule.TypeSystem.UInt16));
        dispatchMethod.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, _networkReaderRef));

        var il = dispatchMethod.Body.GetILProcessor();
        var retInstr = il.Create(OpCodes.Ret);

        var caseLabels = new Instruction[rpcs.Count];
        for (int i = 0; i < rpcs.Count; i++)
            caseLabels[i] = il.Create(OpCodes.Nop);

        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[0]));
        if (inheritedCount > 0)
        {
            il.Append(il.Create(OpCodes.Ldc_I4, inheritedCount));
            il.Append(il.Create(OpCodes.Sub));
        }
        il.Append(il.Create(OpCodes.Switch, caseLabels));

        if (inheritedCount > 0)
        {
            var baseDispatch = FindBaseDispatchMethod(type, "__DispatchClientRpc");
            if (baseDispatch != null)
            {
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[0]));
                il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[1]));
                il.Append(il.Create(OpCodes.Call, baseDispatch));
                il.Append(il.Create(OpCodes.Br, retInstr));
            }
            else
            {
                il.Append(il.Create(OpCodes.Br, retInstr));
            }
        }
        else
        {
            il.Append(il.Create(OpCodes.Br, retInstr));
        }

        for (int i = 0; i < rpcs.Count; i++)
        {
            il.Append(caseLabels[i]);
            var method = rpcs[i];
            var userCode = type.Methods.First(m => m.Name == GetUserCodeName(method));

            // Determine if this is a Player target with first param being the target
            bool isPlayerTarget = false;
            if (HasAttribute(method, _entityRpcAttrType.FullName))
            {
                var attrData = GetEntityRpcAttribute(method);
                isPlayerTarget = attrData.target == RpcTargetValue.Player;
            }
            else if (HasAttribute(method, _mapRpcAttrType.FullName))
            {
                var attrData = GetMapRpcAttribute(method);
                isPlayerTarget = attrData.target == RpcTargetValue.Player;
            }

            il.Append(il.Create(OpCodes.Ldarg_0));

            foreach (var param in method.Parameters)
            {
                if (isPlayerTarget && param == method.Parameters[0])
                {
                    il.Append(il.Create(OpCodes.Ldnull));
                }
                else
                {
                    EmitReadParam(il, dispatchMethod.Parameters[1], param.ParameterType);
                }
            }

            il.Append(il.Create(OpCodes.Call, userCode));
            if (method.ReturnType.FullName != "System.Void")
                il.Append(il.Create(OpCodes.Pop));
            il.Append(il.Create(OpCodes.Br, retInstr));
        }

        il.Append(retInstr);
        dispatchMethod.Body.InitLocals = true;
        type.Methods.Add(dispatchMethod);
    }

    // ── Static dispatch generation ──

    private void GenerateStaticCommandDispatch(TypeDefinition type, List<MethodDefinition> commands)
    {
        var dispatchMethod = new MethodDefinition(
            "__DispatchStaticCommand",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            _voidRef);

        dispatchMethod.Parameters.Add(new ParameterDefinition("methodId", ParameterAttributes.None, _targetModule.TypeSystem.UInt16));
        dispatchMethod.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, _networkReaderRef));
        dispatchMethod.Parameters.Add(new ParameterDefinition("connectionId", ParameterAttributes.None, _targetModule.TypeSystem.UInt32));
        dispatchMethod.Parameters.Add(new ParameterDefinition("promiseId", ParameterAttributes.None, _targetModule.TypeSystem.UInt16));

        var il = dispatchMethod.Body.GetILProcessor();
        var retInstr = il.Create(OpCodes.Ret);

        var caseLabels = new Instruction[commands.Count];
        for (int i = 0; i < commands.Count; i++)
            caseLabels[i] = il.Create(OpCodes.Nop);

        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[0])); // methodId
        il.Append(il.Create(OpCodes.Switch, caseLabels));
        il.Append(il.Create(OpCodes.Br, retInstr)); // default: return

        for (int i = 0; i < commands.Count; i++)
        {
            il.Append(caseLabels[i]);
            var method = commands[i];
            var userCode = type.Methods.First(m => m.Name == GetUserCodeName(method));
            var returnType = method.ReturnType;
            bool isVoid = returnType.FullName == "System.Void";
            bool isPromise = !isVoid && returnType.FullName == _rpcPromiseType.FullName;
            bool isGenericPromise = !isVoid && !isPromise && returnType.FullName.StartsWith("Prowl.Wicked.RpcPromise`1");

            if (isVoid)
            {
                // Read params (static — no this), call __UserCode, return
                EmitReadStaticParams(il, dispatchMethod, method.Parameters);
                il.Append(il.Create(OpCodes.Call, userCode));
                il.Append(il.Create(OpCodes.Br, retInstr));
            }
            else if (isPromise)
            {
                EmitPromiseDispatch(il, dispatchMethod, method, userCode, retInstr, true);
            }
            else if (isGenericPromise)
            {
                EmitGenericPromiseDispatch(il, dispatchMethod, method, userCode, returnType, retInstr, true);
            }
        }

        il.Append(retInstr);
        dispatchMethod.Body.InitLocals = true;
        type.Methods.Add(dispatchMethod);
    }

    private void GenerateStaticRpcDispatch(TypeDefinition type, List<MethodDefinition> rpcs)
    {
        var dispatchMethod = new MethodDefinition(
            "__DispatchStaticRpc",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            _voidRef);

        dispatchMethod.Parameters.Add(new ParameterDefinition("methodId", ParameterAttributes.None, _targetModule.TypeSystem.UInt16));
        dispatchMethod.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, _networkReaderRef));

        var il = dispatchMethod.Body.GetILProcessor();
        var retInstr = il.Create(OpCodes.Ret);

        var caseLabels = new Instruction[rpcs.Count];
        for (int i = 0; i < rpcs.Count; i++)
            caseLabels[i] = il.Create(OpCodes.Nop);

        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[0]));
        il.Append(il.Create(OpCodes.Switch, caseLabels));
        il.Append(il.Create(OpCodes.Br, retInstr));

        for (int i = 0; i < rpcs.Count; i++)
        {
            il.Append(caseLabels[i]);
            var method = rpcs[i];
            var userCode = type.Methods.First(m => m.Name == GetUserCodeName(method));

            // First param is target (null on client), then read rest
            il.Append(il.Create(OpCodes.Ldnull)); // null for first param (RemoteClient/RemoteClient[])
            for (int p = 1; p < method.Parameters.Count; p++)
            {
                EmitReadParam(il, dispatchMethod.Parameters[1], method.Parameters[p].ParameterType);
            }

            il.Append(il.Create(OpCodes.Call, userCode));
            il.Append(il.Create(OpCodes.Br, retInstr));
        }

        il.Append(retInstr);
        dispatchMethod.Body.InitLocals = true;
        type.Methods.Add(dispatchMethod);
    }

    // ── Static RPC registration ──

    private void GenerateStaticRpcRegistration(List<(ushort rpcTypeId, TypeDefinition type, bool hasCommands, bool hasRpcs)> registrations)
    {
        var regType = new TypeDefinition(
            "", "ProwlWickedStaticRpcRegistration",
            TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
            _targetModule.TypeSystem.Object);

        var registerAll = new MethodDefinition(
            "RegisterAll",
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
            _voidRef);

        var il = registerAll.Body.GetILProcessor();

        // Resolve delegate constructors
        // Server.RegisterStaticRpc(ushort, Action<ushort, NetworkReader, uint, ushort>)
        // Client.RegisterStaticRpc(ushort, Action<ushort, NetworkReader>)

        var actionServerType = _targetModule.ImportReference(typeof(Action<,,,>)).MakeGenericInstanceType(
            _targetModule.TypeSystem.UInt16,
            _networkReaderRef,
            _targetModule.TypeSystem.UInt32,
            _targetModule.TypeSystem.UInt16);
        var actionClientType = _targetModule.ImportReference(typeof(Action<,>)).MakeGenericInstanceType(
            _targetModule.TypeSystem.UInt16,
            _networkReaderRef);

        var actionServerCtor = new MethodReference(".ctor", _voidRef, actionServerType)
        {
            HasThis = true,
            Parameters = { new ParameterDefinition(_targetModule.TypeSystem.Object), new ParameterDefinition(_targetModule.TypeSystem.IntPtr) }
        };
        var actionClientCtor = new MethodReference(".ctor", _voidRef, actionClientType)
        {
            HasThis = true,
            Parameters = { new ParameterDefinition(_targetModule.TypeSystem.Object), new ParameterDefinition(_targetModule.TypeSystem.IntPtr) }
        };

        foreach (var (rpcTypeId, type, hasCommands, hasRpcs) in registrations)
        {
            if (hasCommands)
            {
                var dispatchMethod = type.Methods.First(m => m.Name == "__DispatchStaticCommand");

                il.Append(il.Create(OpCodes.Ldc_I4, (int)rpcTypeId));
                il.Append(il.Create(OpCodes.Ldnull)); // static method, no target
                il.Append(il.Create(OpCodes.Ldftn, dispatchMethod));
                il.Append(il.Create(OpCodes.Newobj, actionServerCtor));
                il.Append(il.Create(OpCodes.Call, _serverRegisterStaticRpc));
            }

            if (hasRpcs)
            {
                var dispatchMethod = type.Methods.First(m => m.Name == "__DispatchStaticRpc");

                il.Append(il.Create(OpCodes.Ldc_I4, (int)rpcTypeId));
                il.Append(il.Create(OpCodes.Ldnull));
                il.Append(il.Create(OpCodes.Ldftn, dispatchMethod));
                il.Append(il.Create(OpCodes.Newobj, actionClientCtor));
                il.Append(il.Create(OpCodes.Call, _clientRegisterStaticRpc));
            }
        }

        il.Append(il.Create(OpCodes.Ret));
        regType.Methods.Add(registerAll);
        _targetModule.Types.Add(regType);
    }

    // ── Promise emit helpers (shared between instance and static) ──

    private void EmitPromiseClientPath(ILProcessor il, MethodDefinition method,
        VariableDefinition writerVar, byte objectKind)
    {
        // var promise = new RpcPromise();
        var promiseVar = new VariableDefinition(_rpcPromiseRef);
        method.Body.Variables.Add(promiseVar);
        il.Append(il.Create(OpCodes.Newobj, _rpcPromiseCtor));
        il.Append(il.Create(OpCodes.Stloc, promiseVar));

        var promiseIdVar = new VariableDefinition(_targetModule.TypeSystem.UInt16);
        method.Body.Variables.Add(promiseIdVar);
        il.Append(il.Create(OpCodes.Ldloc, promiseVar));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Call, _clientTrackPromise));
        il.Append(il.Create(OpCodes.Stloc, promiseIdVar));

        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldloc, promiseIdVar));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        EmitSerializeParams(il, writerVar, method.Parameters, objectKind);

        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
        il.Append(il.Create(OpCodes.Call, _clientSendToServer));

        il.Append(il.Create(OpCodes.Ldloc, promiseVar));
        il.Append(il.Create(OpCodes.Ret));
    }

    private void EmitGenericPromiseClientPath(ILProcessor il, MethodDefinition method,
        VariableDefinition writerVar, TypeReference returnType, byte objectKind)
    {
        var genericReturn = (GenericInstanceType)returnType;
        var genericArg = genericReturn.GenericArguments[0];
        byte typeCode = GetReturnTypeCode(genericArg);

        var closedPromiseType = _targetModule.ImportReference(MakeGenericInstance(_rpcPromiseGenericType, genericArg));
        var closedCtor = new MethodReference(".ctor", _voidRef, closedPromiseType) { HasThis = true };
        closedCtor = _targetModule.ImportReference(closedCtor);

        var promiseVar = new VariableDefinition(closedPromiseType);
        method.Body.Variables.Add(promiseVar);
        il.Append(il.Create(OpCodes.Newobj, closedCtor));
        il.Append(il.Create(OpCodes.Stloc, promiseVar));

        var promiseIdVar = new VariableDefinition(_targetModule.TypeSystem.UInt16);
        method.Body.Variables.Add(promiseIdVar);
        il.Append(il.Create(OpCodes.Ldloc, promiseVar));
        il.Append(il.Create(OpCodes.Ldc_I4, (int)typeCode));
        il.Append(il.Create(OpCodes.Call, _clientTrackPromise));
        il.Append(il.Create(OpCodes.Stloc, promiseIdVar));

        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Ldloc, promiseIdVar));
        il.Append(il.Create(OpCodes.Call, _writerWriteUShort));

        EmitSerializeParams(il, writerVar, method.Parameters, objectKind);

        il.Append(il.Create(OpCodes.Ldloc, writerVar));
        il.Append(il.Create(OpCodes.Call, _writerToArraySegment));
        il.Append(il.Create(OpCodes.Call, _clientSendToServer));

        il.Append(il.Create(OpCodes.Ldloc, promiseVar));
        il.Append(il.Create(OpCodes.Ret));
    }

    private void EmitPromiseDispatch(ILProcessor il, MethodDefinition dispatchMethod,
        MethodDefinition method, MethodDefinition userCode, Instruction retInstr, bool isStatic)
    {
        var tryStart = il.Create(OpCodes.Nop);
        il.Append(tryStart);

        if (!isStatic) il.Append(il.Create(OpCodes.Ldarg_0));
        if (isStatic)
            EmitReadStaticParams(il, dispatchMethod, method.Parameters);
        else
            EmitReadParams(il, dispatchMethod, method.Parameters, 0);
        il.Append(il.Create(OpCodes.Call, userCode));
        il.Append(il.Create(OpCodes.Pop));

        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[2])); // connectionId
        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[3])); // promiseId
        il.Append(il.Create(OpCodes.Call, _serverSendRpcResponseVoid));

        var leaveTarget = il.Create(OpCodes.Br, retInstr);
        il.Append(il.Create(OpCodes.Leave, leaveTarget));

        var catchStart = il.Create(OpCodes.Nop);
        il.Append(catchStart);
        var exVar = new VariableDefinition(_targetModule.ImportReference(typeof(Exception)));
        dispatchMethod.Body.Variables.Add(exVar);
        il.Append(il.Create(OpCodes.Stloc, exVar));

        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[2]));
        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[3]));
        il.Append(il.Create(OpCodes.Ldloc, exVar));
        var getMessageRef = _targetModule.ImportReference(typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Append(il.Create(OpCodes.Callvirt, getMessageRef));
        il.Append(il.Create(OpCodes.Call, _serverSendRpcError));

        var catchLeaveTarget = il.Create(OpCodes.Br, retInstr);
        il.Append(il.Create(OpCodes.Leave, catchLeaveTarget));

        il.Append(leaveTarget);
        il.Append(catchLeaveTarget);

        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = leaveTarget,
            CatchType = _targetModule.ImportReference(typeof(Exception))
        };
        dispatchMethod.Body.ExceptionHandlers.Add(handler);
    }

    private void EmitGenericPromiseDispatch(ILProcessor il, MethodDefinition dispatchMethod,
        MethodDefinition method, MethodDefinition userCode, TypeReference returnType,
        Instruction retInstr, bool isStatic)
    {
        var genericReturn = (GenericInstanceType)returnType;
        var genericArg = genericReturn.GenericArguments[0];

        var tryStart = il.Create(OpCodes.Nop);
        il.Append(tryStart);

        var closedPromiseType = _targetModule.ImportReference(MakeGenericInstance(_rpcPromiseGenericType, genericArg));
        var resultVar = new VariableDefinition(closedPromiseType);
        dispatchMethod.Body.Variables.Add(resultVar);

        if (!isStatic) il.Append(il.Create(OpCodes.Ldarg_0));
        if (isStatic)
            EmitReadStaticParams(il, dispatchMethod, method.Parameters);
        else
            EmitReadParams(il, dispatchMethod, method.Parameters, 0);
        il.Append(il.Create(OpCodes.Call, userCode));
        il.Append(il.Create(OpCodes.Stloc, resultVar));

        var resultGetter = _rpcPromiseGenericType.Properties.First(p => p.Name == "Result").GetMethod;
        var getResultRef = new MethodReference(resultGetter.Name, resultGetter.ReturnType, closedPromiseType) { HasThis = true };
        getResultRef = _targetModule.ImportReference(getResultRef);

        var sendResponse = GetSendRpcResponseMethod(genericArg);
        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[2]));
        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[3]));
        il.Append(il.Create(OpCodes.Ldloc, resultVar));
        il.Append(il.Create(OpCodes.Callvirt, getResultRef));
        il.Append(il.Create(OpCodes.Call, sendResponse));

        var leaveTarget = il.Create(OpCodes.Br, retInstr);
        il.Append(il.Create(OpCodes.Leave, leaveTarget));

        var catchStart = il.Create(OpCodes.Nop);
        il.Append(catchStart);
        var exVar = new VariableDefinition(_targetModule.ImportReference(typeof(Exception)));
        dispatchMethod.Body.Variables.Add(exVar);
        il.Append(il.Create(OpCodes.Stloc, exVar));

        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[2]));
        il.Append(il.Create(OpCodes.Ldarg, dispatchMethod.Parameters[3]));
        il.Append(il.Create(OpCodes.Ldloc, exVar));
        var getMessageRef = _targetModule.ImportReference(typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Append(il.Create(OpCodes.Callvirt, getMessageRef));
        il.Append(il.Create(OpCodes.Call, _serverSendRpcError));

        var catchLeaveTarget = il.Create(OpCodes.Br, retInstr);
        il.Append(il.Create(OpCodes.Leave, catchLeaveTarget));

        il.Append(leaveTarget);
        il.Append(catchLeaveTarget);

        var handler = new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            TryStart = tryStart,
            TryEnd = catchStart,
            HandlerStart = catchStart,
            HandlerEnd = leaveTarget,
            CatchType = _targetModule.ImportReference(typeof(Exception))
        };
        dispatchMethod.Body.ExceptionHandlers.Add(handler);
    }

    // ── IL emit helpers ──

    private void EmitWriteObjectId(ILProcessor il, VariableDefinition writerVar, byte objectKind, TypeDefinition type)
    {
        switch (objectKind)
        {
            case 0: // Entity: write NetworkId (uint)
                il.Append(il.Create(OpCodes.Ldloc, writerVar));
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Call, _entityGetNetworkId));
                il.Append(il.Create(OpCodes.Call, _writerWriteUInt));
                break;
            case 1: // Map: write MapId (Guid)
                il.Append(il.Create(OpCodes.Ldloc, writerVar));
                il.Append(il.Create(OpCodes.Ldarg_0));
                il.Append(il.Create(OpCodes.Call, _mapGetMapId));
                il.Append(il.Create(OpCodes.Call, _writerWriteGuid));
                break;
        }
    }

    private void EmitSerializeParams(ILProcessor il, VariableDefinition writerVar,
        IList<ParameterDefinition> parameters, byte objectKind, bool skipFirst = false)
    {
        foreach (var param in parameters)
        {
            if (skipFirst && param == parameters[0])
                continue;

            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldarg, param));
            EmitWriteParam(il, param.ParameterType);
        }
    }

    private void EmitSerializeStaticParams(ILProcessor il, VariableDefinition writerVar,
        IList<ParameterDefinition> parameters)
    {
        foreach (var param in parameters)
        {
            il.Append(il.Create(OpCodes.Ldloc, writerVar));
            il.Append(il.Create(OpCodes.Ldarg, param));
            EmitWriteParam(il, param.ParameterType);
        }
    }

    private void EmitWriteParam(ILProcessor il, TypeReference paramType)
    {
        var resolved = paramType.Resolve();

        if (resolved != null && resolved.IsEnum)
        {
            var genericWrite = new GenericInstanceMethod(_writerWriteEnum);
            genericWrite.GenericArguments.Add(_targetModule.ImportReference(paramType));
            il.Append(il.Create(OpCodes.Call, genericWrite));
            return;
        }

        switch (paramType.FullName)
        {
            case "System.Byte": il.Append(il.Create(OpCodes.Call, _writerWriteByte)); break;
            case "System.SByte": il.Append(il.Create(OpCodes.Call, _writerWriteSByte)); break;
            case "System.Int16": il.Append(il.Create(OpCodes.Call, _writerWriteShort)); break;
            case "System.UInt16": il.Append(il.Create(OpCodes.Call, _writerWriteUShort)); break;
            case "System.Int32": il.Append(il.Create(OpCodes.Call, _writerWriteInt)); break;
            case "System.UInt32": il.Append(il.Create(OpCodes.Call, _writerWriteUInt)); break;
            case "System.Int64": il.Append(il.Create(OpCodes.Call, _writerWriteLong)); break;
            case "System.UInt64": il.Append(il.Create(OpCodes.Call, _writerWriteULong)); break;
            case "System.Single": il.Append(il.Create(OpCodes.Call, _writerWriteFloat)); break;
            case "System.Double": il.Append(il.Create(OpCodes.Call, _writerWriteDouble)); break;
            case "System.Boolean": il.Append(il.Create(OpCodes.Call, _writerWriteBool)); break;
            case "System.String": il.Append(il.Create(OpCodes.Call, _writerWriteString)); break;
            case "System.Guid": il.Append(il.Create(OpCodes.Call, _writerWriteGuid)); break;
            case "System.Numerics.Vector2": il.Append(il.Create(OpCodes.Call, _writerWriteVector2)); break;
            case "System.Byte[]": il.Append(il.Create(OpCodes.Call, _writerWriteByteArray)); break;
            case "System.Int32[]": il.Append(il.Create(OpCodes.Call, _writerWriteIntArray)); break;
            case "System.UInt32[]": il.Append(il.Create(OpCodes.Call, _writerWriteUIntArray)); break;
            case "System.Single[]": il.Append(il.Create(OpCodes.Call, _writerWriteFloatArray)); break;
            case "System.Double[]": il.Append(il.Create(OpCodes.Call, _writerWriteDoubleArray)); break;
            case "System.String[]": il.Append(il.Create(OpCodes.Call, _writerWriteStringArray)); break;
            case "System.Int64[]": il.Append(il.Create(OpCodes.Call, _writerWriteLongArray)); break;
            case "System.UInt64[]": il.Append(il.Create(OpCodes.Call, _writerWriteULongArray)); break;
            case "System.Int16[]": il.Append(il.Create(OpCodes.Call, _writerWriteShortArray)); break;
            case "System.UInt16[]": il.Append(il.Create(OpCodes.Call, _writerWriteUShortArray)); break;
            case "System.Boolean[]": il.Append(il.Create(OpCodes.Call, _writerWriteBoolArray)); break;
            default:
                if (paramType.IsArray && ImplementsINetworkSerializable(paramType.GetElementType()))
                {
                    var genericWrite = new GenericInstanceMethod(_writerWriteSerializableArray);
                    genericWrite.GenericArguments.Add(_targetModule.ImportReference(paramType.GetElementType()));
                    il.Append(il.Create(OpCodes.Call, genericWrite));
                }
                else if (IsNetworkEntityType(paramType))
                {
                    il.Append(il.Create(OpCodes.Call, _writerWriteEntityRef));
                }
                else if (ImplementsINetworkSerializable(paramType))
                {
                    var genericWrite = new GenericInstanceMethod(_writerWriteSerializable);
                    genericWrite.GenericArguments.Add(_targetModule.ImportReference(paramType));
                    il.Append(il.Create(OpCodes.Call, genericWrite));
                }
                else
                    throw new InvalidOperationException($"Unsupported RPC parameter type: {paramType.FullName}");
                break;
        }
    }

    private void EmitReadParams(ILProcessor il, MethodDefinition dispatchMethod,
        IList<ParameterDefinition> originalParams, byte objectKind)
    {
        var readerParam = dispatchMethod.Parameters[1];
        foreach (var param in originalParams)
            EmitReadParam(il, readerParam, param.ParameterType);
    }

    private void EmitReadStaticParams(ILProcessor il, MethodDefinition dispatchMethod,
        IList<ParameterDefinition> originalParams)
    {
        var readerParam = dispatchMethod.Parameters[1];
        foreach (var param in originalParams)
            EmitReadParam(il, readerParam, param.ParameterType);
    }

    private void EmitReadParam(ILProcessor il, ParameterDefinition readerParam, TypeReference paramType)
    {
        var resolved = paramType.Resolve();

        if (resolved != null && resolved.IsEnum)
        {
            il.Append(il.Create(OpCodes.Ldarg, readerParam));
            var genericRead = new GenericInstanceMethod(_readerReadEnum);
            genericRead.GenericArguments.Add(_targetModule.ImportReference(paramType));
            il.Append(il.Create(OpCodes.Call, genericRead));
            return;
        }

        il.Append(il.Create(OpCodes.Ldarg, readerParam));

        switch (paramType.FullName)
        {
            case "System.Byte": il.Append(il.Create(OpCodes.Call, _readerReadByte)); break;
            case "System.SByte": il.Append(il.Create(OpCodes.Call, _readerReadSByte)); break;
            case "System.Int16": il.Append(il.Create(OpCodes.Call, _readerReadShort)); break;
            case "System.UInt16": il.Append(il.Create(OpCodes.Call, _readerReadUShort)); break;
            case "System.Int32": il.Append(il.Create(OpCodes.Call, _readerReadInt)); break;
            case "System.UInt32": il.Append(il.Create(OpCodes.Call, _readerReadUInt)); break;
            case "System.Int64": il.Append(il.Create(OpCodes.Call, _readerReadLong)); break;
            case "System.UInt64": il.Append(il.Create(OpCodes.Call, _readerReadULong)); break;
            case "System.Single": il.Append(il.Create(OpCodes.Call, _readerReadFloat)); break;
            case "System.Double": il.Append(il.Create(OpCodes.Call, _readerReadDouble)); break;
            case "System.Boolean": il.Append(il.Create(OpCodes.Call, _readerReadBool)); break;
            case "System.String": il.Append(il.Create(OpCodes.Call, _readerReadString)); break;
            case "System.Guid": il.Append(il.Create(OpCodes.Call, _readerReadGuid)); break;
            case "System.Numerics.Vector2": il.Append(il.Create(OpCodes.Call, _readerReadVector2)); break;
            case "System.Byte[]": il.Append(il.Create(OpCodes.Call, _readerReadByteArray)); break;
            case "System.Int32[]": il.Append(il.Create(OpCodes.Call, _readerReadIntArray)); break;
            case "System.UInt32[]": il.Append(il.Create(OpCodes.Call, _readerReadUIntArray)); break;
            case "System.Single[]": il.Append(il.Create(OpCodes.Call, _readerReadFloatArray)); break;
            case "System.Double[]": il.Append(il.Create(OpCodes.Call, _readerReadDoubleArray)); break;
            case "System.String[]": il.Append(il.Create(OpCodes.Call, _readerReadStringArray)); break;
            case "System.Int64[]": il.Append(il.Create(OpCodes.Call, _readerReadLongArray)); break;
            case "System.UInt64[]": il.Append(il.Create(OpCodes.Call, _readerReadULongArray)); break;
            case "System.Int16[]": il.Append(il.Create(OpCodes.Call, _readerReadShortArray)); break;
            case "System.UInt16[]": il.Append(il.Create(OpCodes.Call, _readerReadUShortArray)); break;
            case "System.Boolean[]": il.Append(il.Create(OpCodes.Call, _readerReadBoolArray)); break;
            default:
                if (paramType.IsArray && ImplementsINetworkSerializable(paramType.GetElementType()))
                {
                    var genericRead = new GenericInstanceMethod(_readerReadSerializableArray);
                    genericRead.GenericArguments.Add(_targetModule.ImportReference(paramType.GetElementType()));
                    il.Append(il.Create(OpCodes.Call, genericRead));
                }
                else if (IsNetworkEntityType(paramType))
                {
                    il.Append(il.Create(OpCodes.Call, _readerReadEntityRef));
                    // Safe downcast to the declared parameter type (null if wrong type or not found)
                    il.Append(il.Create(OpCodes.Isinst, _targetModule.ImportReference(paramType)));
                }
                else if (ImplementsINetworkSerializable(paramType))
                {
                    var genericRead = new GenericInstanceMethod(_readerReadSerializable);
                    genericRead.GenericArguments.Add(_targetModule.ImportReference(paramType));
                    il.Append(il.Create(OpCodes.Call, genericRead));
                }
                else
                    throw new InvalidOperationException($"Unsupported RPC parameter type: {paramType.FullName}");
                break;
        }
    }

    // ── Attribute helpers ──

    private enum RpcTargetValue { Observers = 0, Owner = 1, Player = 2 }

    private (RpcTargetValue target, bool excludeOwner) GetEntityRpcAttribute(MethodDefinition method)
    {
        var attr = method.CustomAttributes.First(a => a.AttributeType.FullName == _entityRpcAttrType.FullName);
        var target = RpcTargetValue.Observers;
        bool excludeOwner = false;

        foreach (var prop in attr.Properties)
        {
            if (prop.Name == "Target")
                target = (RpcTargetValue)(int)prop.Argument.Value;
            else if (prop.Name == "ExcludeOwner")
                excludeOwner = (bool)prop.Argument.Value;
        }

        return (target, excludeOwner);
    }

    private (RpcTargetValue target, bool unused) GetMapRpcAttribute(MethodDefinition method)
    {
        var attr = method.CustomAttributes.First(a => a.AttributeType.FullName == _mapRpcAttrType.FullName);
        var target = RpcTargetValue.Observers;

        foreach (var prop in attr.Properties)
        {
            if (prop.Name == "Target")
                target = (RpcTargetValue)(int)prop.Argument.Value;
        }

        return (target, false);
    }

    private bool GetRequireOwner(MethodDefinition method)
    {
        var attr = method.CustomAttributes.First(a => a.AttributeType.FullName == _entityCommandAttrType.FullName);
        foreach (var prop in attr.Properties)
        {
            if (prop.Name == "RequireOwner")
                return (bool)prop.Argument.Value;
        }
        return true; // Default is true
    }

    private MethodReference GetSendRpcResponseMethod(TypeReference genericArg)
    {
        return genericArg.FullName switch
        {
            "System.Int32" => _serverSendRpcResponseInt,
            "System.UInt32" => _serverSendRpcResponseUInt,
            "System.Boolean" => _serverSendRpcResponseBool,
            "System.String" => _serverSendRpcResponseString,
            "System.Single" => _serverSendRpcResponseFloat,
            "System.Numerics.Vector2" => _serverSendRpcResponseVector2,
            "System.Byte" => _serverSendRpcResponseByte,
            "System.SByte" => _serverSendRpcResponseSByte,
            "System.Int16" => _serverSendRpcResponseShort,
            "System.UInt16" => _serverSendRpcResponseUShort,
            "System.Int64" => _serverSendRpcResponseLong,
            "System.UInt64" => _serverSendRpcResponseULong,
            "System.Double" => _serverSendRpcResponseDouble,
            "System.Guid" => _serverSendRpcResponseGuid,
            "System.Byte[]" => _serverSendRpcResponseByteArray,
            "System.Int32[]" => _serverSendRpcResponseIntArray,
            "System.UInt32[]" => _serverSendRpcResponseUIntArray,
            "System.Single[]" => _serverSendRpcResponseFloatArray,
            "System.Double[]" => _serverSendRpcResponseDoubleArray,
            "System.String[]" => _serverSendRpcResponseStringArray,
            "System.Boolean[]" => _serverSendRpcResponseBoolArray,
            "System.Int64[]" => _serverSendRpcResponseLongArray,
            "System.UInt64[]" => _serverSendRpcResponseULongArray,
            "System.Int16[]" => _serverSendRpcResponseShortArray,
            "System.UInt16[]" => _serverSendRpcResponseUShortArray,
            _ => throw new InvalidOperationException($"Unsupported RpcPromise<T> type: {genericArg.FullName}")
        };
    }

    private static byte GetReturnTypeCode(TypeReference type)
    {
        return type.FullName switch
        {
            "System.Int32" => 1,
            "System.UInt32" => 2,
            "System.Boolean" => 3,
            "System.String" => 4,
            "System.Single" => 5,
            "System.Numerics.Vector2" => 6,
            "System.Byte" => 7,
            "System.SByte" => 8,
            "System.Int16" => 9,
            "System.UInt16" => 10,
            "System.Int64" => 11,
            "System.UInt64" => 12,
            "System.Double" => 13,
            "System.Guid" => 14,
            "System.Byte[]" => 15,
            "System.Int32[]" => 16,
            "System.UInt32[]" => 17,
            "System.Single[]" => 18,
            "System.Double[]" => 19,
            "System.String[]" => 20,
            "System.Boolean[]" => 21,
            "System.Int64[]" => 22,
            "System.UInt64[]" => 23,
            "System.Int16[]" => 24,
            "System.UInt16[]" => 25,
            _ => throw new InvalidOperationException($"Unsupported RpcPromise<T> return type: {type.FullName}")
        };
    }

    private static bool ImplementsINetworkSerializable(TypeReference typeRef)
    {
        var resolved = typeRef.Resolve();
        if (resolved == null) return false;
        return resolved.Interfaces.Any(i =>
            i.InterfaceType.FullName == "Prowl.Wicked.INetworkSerializable");
    }

    private bool IsNetworkEntityType(TypeReference typeRef)
    {
        var resolved = typeRef.Resolve();
        if (resolved == null) return false;
        return resolved.FullName == "Prowl.Wicked.NetworkEntity"
            || InheritsFrom(resolved, "Prowl.Wicked.NetworkEntity");
    }

    // ── Object kind helpers ──

    private byte GetObjectKind(TypeDefinition type)
    {
        if (InheritsFrom(type, "Prowl.Wicked.NetworkEntity")) return 0;
        if (InheritsFrom(type, "Prowl.Wicked.Map")) return 1;
        throw new InvalidOperationException($"Type {type.FullName} is not a NetworkEntity or Map subtype.");
    }

    // ── General helpers ──

    private static bool HasAttribute(MethodDefinition method, string attributeFullName)
    {
        return method.CustomAttributes.Any(a => a.AttributeType.FullName == attributeFullName);
    }

    private static int GetInheritanceDepth(TypeDefinition type)
    {
        int depth = 0;
        var current = type.BaseType;
        while (current != null)
        {
            depth++;
            try { current = current.Resolve()?.BaseType; }
            catch { break; }
        }
        return depth;
    }

    private MethodReference? FindBaseDispatchMethod(TypeDefinition type, string methodName)
    {
        var current = type.BaseType;
        while (current != null)
        {
            TypeDefinition? resolved;
            try { resolved = current.Resolve(); }
            catch { break; }
            if (resolved == null) break;

            var method = resolved.Methods.FirstOrDefault(m => m.Name == methodName);
            if (method != null)
                return _targetModule.ImportReference(method);

            current = resolved.BaseType;
        }
        return null;
    }

    private static bool InheritsFrom(TypeDefinition type, string baseTypeFullName)
    {
        if (type.FullName == baseTypeFullName)
            return false;

        var current = type.BaseType;
        while (current != null)
        {
            if (current.FullName == baseTypeFullName)
                return true;

            try
            {
                current = current.Resolve()?.BaseType;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    private static GenericInstanceType MakeGenericInstance(TypeDefinition genericType, TypeReference typeArg)
    {
        var instance = new GenericInstanceType(genericType);
        instance.GenericArguments.Add(typeArg);
        return instance;
    }

    private static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
        {
            yield return type;
            foreach (var nested in GetNestedTypes(type))
                yield return nested;
        }
    }

    private static IEnumerable<TypeDefinition> GetNestedTypes(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes)
        {
            yield return nested;
            foreach (var inner in GetNestedTypes(nested))
                yield return inner;
        }
    }

    /// <summary>
    /// Moves sequence points and scope info from the original method to the __UserCode_ method
    /// so that debugger breakpoints set on source lines land in the moved body.
    /// </summary>
    private static void MoveDebugInfo(MethodDefinition source, MethodDefinition target)
    {
        var srcDebug = source.DebugInformation;
        var tgtDebug = target.DebugInformation;

        if (srcDebug.HasSequencePoints)
        {
            foreach (var sp in srcDebug.SequencePoints)
                tgtDebug.SequencePoints.Add(sp);
            srcDebug.SequencePoints.Clear();
        }

        if (srcDebug.Scope != null)
        {
            tgtDebug.Scope = srcDebug.Scope;
            srcDebug.Scope = null;
        }
    }

    private static ReaderParameters CreateReaderParams(string assemblyPath, IAssemblyResolver resolver)
    {
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        var readerParams = new ReaderParameters { AssemblyResolver = resolver };

        if (File.Exists(pdbPath))
        {
            readerParams.ReadSymbols = true;
            readerParams.SymbolReaderProvider = new PdbReaderProvider();
            readerParams.SymbolStream = new MemoryStream(File.ReadAllBytes(pdbPath));
        }

        return readerParams;
    }

    private class WeaverAssemblyResolver : DefaultAssemblyResolver
    {
        public void Register(AssemblyDefinition assembly) => RegisterAssembly(assembly);
    }
}

static class TypeReferenceExtensions
{
    public static GenericInstanceType MakeGenericInstanceType(this TypeReference self, params TypeReference[] arguments)
    {
        var instance = new GenericInstanceType(self);
        foreach (var arg in arguments)
            instance.GenericArguments.Add(arg);
        return instance;
    }
}
