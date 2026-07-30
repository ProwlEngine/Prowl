namespace Prowl.Ember;

/// <summary>
/// Stable identifier for a reload diagnostic. The numeric value is the code, so an identifier is a format
/// rather than a lookup table.
/// </summary>
public enum ReloadCode
{
    // 1xxx, scope and configuration
    ScopeAssemblySkipped = 1001,
    ScopeAssemblyExcluded = 1002,
    AssemblySwapCycle = 1003,
    JsonCacheRepopulated = 1004,
    NoAssemblyBytes = 1005,

    // 2xxx, type and member mapping
    TypeRemoved = 2001,
    TypeSubstitutionRejected = 2002,
    SyntheticTypeUnmatched = 2003,
    AnonymousTypeUnmatched = 2004,
    ScopeMethodUnmatched = 2005,
    MemberUnmatched = 2006,

    // 3xxx, fields
    FieldTypeChanged = 3001,
    FieldWriteFailed = 3002,
    NewFieldDefaulted = 3003,
    InitializerMethodMissing = 3004,
    InitializerMethodFailed = 3005,
    InitializerExpressionUnsupported = 3006,
    StaticInitializerThrew = 3007,
    EnumValueTruncated = 3008,
    ReadOnlyStaticUnset = 3009,
    FieldReadFailed = 3010,

    // 4xxx, delegates
    DelegateBroken = 4001,
    MulticastEntriesDropped = 4002,
    DelegateSignatureChanged = 4003,
    LambdaScopeUnresolved = 4004,

    // 5xxx, containers
    CollectionKeyCollision = 5001,
    CollectionKeyNull = 5002,
    CollectionRebuildFailed = 5003,
    CollectionComparerDropped = 5004,
    CollectionElementFailed = 5005,

    // 6xxx, lifecycle
    DetachHookThrew = 6001,
    AttachHookThrew = 6002,
    ObserverHookThrew = 6003,

    // 7xxx, metadata
    MetadataUnavailable = 7001,
    MetadataReadFailed = 7002,
    MetadataResolveFailed = 7003,

    // 9xxx, engine
    NoPlanForType = 9001,
    RehashCycle = 9002,
    AllocateCalledMap = 9003,
    MigratorThrew = 9004,
    ResolutionCycle = 9005,
}

public enum ReloadSeverity
{
    Info,
    Warning,
    Error,
}
