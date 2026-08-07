// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Prowl.Editor.Build;

/// <summary>A single unit of work in a build.</summary>
/// <remarks>
/// Operations never carry payload bytes. A build can produce tens of gigabytes, so content arrives
/// through a stream opened at execution time rather than sitting in the plan.
/// <para>
/// Operations yielded within one stage are independent by contract and may run concurrently in any
/// order. Anything with a dependency belongs in a later stage.
/// </para>
/// </remarks>
public abstract record BuildOperation
{
    /// <summary>Shown in progress and in any issue raised against this operation.</summary>
    public abstract string Description { get; }

    public sealed record CopyFile(string Source, string Destination) : BuildOperation
    {
        public override string Description => $"copy {Path.GetFileName(Source)}";
    }

    public sealed record WriteFile(Func<CancellationToken, Task<Stream>> Open, string Destination) : BuildOperation
    {
        public override string Description => $"write {Path.GetFileName(Destination)}";
    }

    /// <summary>
    /// Anything else, including running a tool or waiting on an external service. Keeps the set open, so
    /// a pipeline shipped out of tree never has to disguise its work as something it is not.
    /// </summary>
    public sealed record Custom(IOperationHandler Handler, string What) : BuildOperation
    {
        public override string Description => What;
    }
}

/// <summary>Executes a <see cref="BuildOperation.Custom"/>.</summary>
public interface IOperationHandler
{
    Task ExecuteAsync(IBuildContext context, CancellationToken ct);
}
