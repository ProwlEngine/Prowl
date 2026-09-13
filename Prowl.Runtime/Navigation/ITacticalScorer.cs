// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// Scores one candidate position for <see cref="NavMeshSystem.QueryTacticalPositions"/>. A negative
/// return disqualifies the candidate outright - it never appears in the result list, regardless of how
/// any other scorer rates it - while a non-negative return contributes to that candidate's total score,
/// summed across every scorer a query was given. This single convention covers both a hard requirement
/// (a scorer that only ever returns a fixed positive value or a disqualifying negative one, as every
/// scorer this engine ships does) and genuine graduated scoring (a scorer that returns a magnitude, for
/// a caller writing its own that wants one), without needing two separate methods for the two cases.
/// </summary>
public interface ITacticalScorer
{
    /// <summary>Scores <paramref name="point"/>: negative disqualifies it, non-negative contributes to
    /// its total.</summary>
    float Score(Float3 point);
}
