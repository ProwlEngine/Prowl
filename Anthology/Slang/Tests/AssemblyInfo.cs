// Ideally, tests should be isolated and parallelizable - however Slang cannot be run in parallel without creating multiple GlobalSession instances, which is not supported.
// Parallelization also conflicts with the use of the static ModuleBuilder in ProxyEmitter, which is not thread-safe.

[assembly: CollectionBehavior(DisableTestParallelization = true)]
