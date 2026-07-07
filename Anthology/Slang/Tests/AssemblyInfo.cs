// Ideally, tests should be isolated and parallelizable - however Slang cannot be run in parallel without creating multiple GlobalSession instances, which is not supported.

[assembly: CollectionBehavior(DisableTestParallelization = true)]
