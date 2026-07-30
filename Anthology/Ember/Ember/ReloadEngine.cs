using System;
using System.Collections.Generic;
using System.Threading;

namespace Prowl.Ember;

/// <summary>
/// In place assembly state migration for hot reload. Both the previous and current assemblies stay loaded.
/// The engine walks the live graph from watched static fields and caller supplied roots, and for every object
/// of a replaced type produces a replacement, carries its state across, and repoints every reference to it,
/// preserving identity and cycles.
/// </summary>
public sealed class ReloadEngine
{
    private int _running;

    public ReloadEngine(ReloadOptions options)
        => Options = options ?? throw new ArgumentNullException(nameof(options));

    public static ReloadEngine Create(Action<ReloadOptions> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var options = new ReloadOptions();
        configure(options);
        return new ReloadEngine(options);
    }

    public ReloadOptions Options { get; }

    /// <summary>
    /// Runs one reload. Not reentrant and not thread safe: a request describes a reload completely, so there is
    /// never a reason to have two in flight.
    /// </summary>
    public ReloadReport Apply(ReloadRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (Interlocked.Exchange(ref _running, 1) == 1)
            throw new InvalidOperationException("A reload is already in progress on this engine.");

        try
        {
            return Run(request);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>
    /// The plan a type would get, and why. Runs the planning phase only and touches no live object, so it is
    /// safe to call for diagnostics at any time.
    /// </summary>
    public PlanExplanation Explain(Type type, ReloadRequest request)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (request == null) throw new ArgumentNullException(nameof(request));

        var session = new Session(Options, request);
        try
        {
            return session.Planner.Explain(type);
        }
        finally
        {
            session.Dispose();
        }
    }

    private ReloadReport Run(ReloadRequest request)
    {
        var session = new Session(Options, request);

        try
        {
            if (session.Assemblies.IsEmpty)
                return session.Report.Build();

            foreach (var scoped in ScopedMigrators())
                scoped.OnReloadStarting(session.PlanContext);

            var rewriter = new GraphRewriter(session.Planner, session.Types, session.Members, session.Report);
            rewriter.Run(EnumerateRoots(session), request.Roots);

            foreach (var scoped in ScopedMigrators())
                scoped.OnReloadFinished(session.PlanContext);

            // The include set moves onto the current side, or the next reload would walk assemblies that no
            // longer exist. This is the only state a reload carries over.
            Options.Scope.ApplyChanges(session.Assemblies);

            session.Report.TypesPlanned = session.Planner.PlannedCount;
            session.Report.TypesInert = session.Analyzer.InertCount;

            return session.Report.Build();
        }
        finally
        {
            session.Dispose();
        }
    }

    private IEnumerable<IReloadScopedMigrator> ScopedMigrators()
    {
        foreach (var migrator in Options.Migrators)
            if (migrator is IReloadScopedMigrator scoped)
                yield return scoped;
    }

    private IEnumerable<Root> EnumerateRoots(Session session)
    {
        var context = new RootContext(session.Assemblies, session.Types, session.Members, Options.Scope, session.Report);

        foreach (var provider in Options.Roots)
            foreach (var root in provider.Enumerate(context))
                yield return root;
    }

    /// <summary>
    /// Everything derived from one request. Nothing here outlives <see cref="Apply"/>, which is why the engine
    /// has no caches to clear and no state to leak into the next reload.
    /// </summary>
    private sealed class Session : IDisposable
    {
        public Session(ReloadOptions options, ReloadRequest request)
        {
            Report = new ReportBuilder(options.Diagnostics, options.CollectStatistics);

            using (Report.Time(ReloadPhase.Plan))
            {
                Assemblies = new AssemblyMap(request.Changes, Report);
                Metadata = new MetadataCache(options.AssemblyBytes, Report);

                var indexes = new SyntheticIndexes(Metadata);

                Types = new TypeMap(Assemblies, indexes, Report);
                Members = new MemberMap(Types, Report);
                Types.UseMembers(Members);
                Members.UseLambdas(new LambdaMatcher(Types, Members, indexes, Report));

                Analyzer = new TypeAnalyzer(Types, options.Scope, options.Migrators, options.InertAnalysis);
                PlanContext = new PlanContext(Assemblies, Types, Members, options, Analyzer, Metadata, Report);
                Planner = new Planner(PlanContext, Types, Analyzer, options.Scope, options.Migrators, Report);
                PlanContext.UsePlanner(Planner);
            }
        }

        public ReportBuilder Report { get; }
        public AssemblyMap Assemblies { get; }
        public MetadataCache Metadata { get; }
        public TypeMap Types { get; }
        public MemberMap Members { get; }
        public TypeAnalyzer Analyzer { get; }
        public PlanContext PlanContext { get; }
        public Planner Planner { get; }

        public void Dispose() => Metadata.Dispose();
    }
}
