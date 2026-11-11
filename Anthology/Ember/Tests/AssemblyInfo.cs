using Xunit.Sdk;
using Xunit.v3;

// Migration tests mutate global static state (the loaded v1/v2 assemblies), so they must not run in parallel.
[assembly: Parallelization(Mode = ParallelMode.None)]
