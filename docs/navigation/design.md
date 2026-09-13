# Navigation

## Purpose and scope

This document describes Prowl's navigation subsystem: baking a walkable mesh from scene geometry,
querying it for paths, moving characters along it with crowd simulation, and changing it at runtime
through obstacles, links, and area painting. It covers the architectural decisions the subsystem is built
on, each capability layered on top of that foundation, and the boundaries that were deliberately drawn
around it.

The subsystem is built on DotRecast, a managed port of Recast and Detour. Prowl owns the public
abstraction; DotRecast is an implementation detail reachable when a caller needs to go around the
abstraction, the same way Jitter sits behind Prowl's physics API. A Detour type crosses the boundary of
`Prowl.Runtime.Navigation` in only a handful of files (the bake pipeline, the query engine, the crowd
wrapper, the cost-aware filter); every other file in the namespace deals exclusively in engine-owned
types.

## Representation: a tile cache is the only carve-capable one

Recast produces two different on-disk representations of a baked navmesh. The first is a set of finished
Detour tiles: fast to query, but frozen once built. The second is a tile cache, a set of compressed voxel
heightfield layers that Detour turns into polygons on demand, and that can be re-contoured whenever an
obstacle enters or leaves a tile without touching the surrounding voxel data. Only the second is capable
of being carved at runtime.

Prowl bakes every surface as a tile cache, unconditionally. There is no per-surface choice between the two
representations. The reasoning is that exposing that choice turns every subsequent feature into a
question with two different answers depending on which representation a given surface picked: whether an
obstacle can carve it, where a link gets woven in, what the rebuild entry points look like, what the tile
size limits are. Baking everything as a tile cache removes the question at the source. A surface that
never has anything carved into it pays for exactly what a finished-tile bake would have cost anyway: its
tiles are built once from the compressed layers and never touched again, and nothing about the tile cache
that produced them costs anything further at runtime unless something actually changes.

A navmesh is also always tiled, even a small single-tile bake. This is what makes rebuilding a bounded
region of a surface possible without touching every other tile: the tile grid is a property of the bake
regardless of how large the baked area happens to be.

## Agent types

Every navmesh belongs to exactly one agent type: a persistent, named entry in a project-wide table
recording a radius, height, maximum slope, and maximum step height. A `NavMeshSurface` references an
agent type by a stable numeric id rather than storing its own copy of these values, so every surface built
for a given type stays in lockstep with it, and renaming or removing a type never silently repoints a
surface at the wrong dimensions. The built-in `Humanoid` type always exists and cannot be removed, matching
the common case where a project only ever has one kind of character walking its levels.

Two surfaces of different agent types occupy entirely separate worlds from the subsystem's point of view:
separate bakes, separate queries, separate crowd simulations. An agent of one type can never resolve
another type's query, and nothing about a project needing two very differently sized characters (a
crouching enemy and a vehicle, say) requires either one to compromise on its own bake settings.

The agent-type table (`NavMeshAgentTypes`) and the area table (`NavMeshAreas`, an agent-type-independent
project-wide table of named traversal costs) are both process-wide statics an editor settings page writes
and a query reads, including from a worker thread, since a query's own filter rebuilds its area-cost
snapshot on every call that names a specific area mask. Both publish an entirely new table on every write
rather than mutating the live one's structure or a shared slot in place, so a concurrent reader always sees
either the table from before a write or the one from after it, never a half-applied one, and an enumeration
never races a structural change the way it would against a plain, in-place-mutated list. What this does not
promise is that two separate calls observe the same generation of the table if a write lands between them,
which is why a query snapshots everything it needs from either table in one pass rather than calling back
into it repeatedly over a single search.

## Agents and crowds

A `NavMeshAgent` is a thin client of a `NavMeshCrowd`, one shared Detour crowd simulation per agent type,
not a standalone simulation of its own. The agent's own state is limited to which crowd slot it occupies
and how it renders that slot's simulated position onto its own transform; steering, avoidance, and
separation all happen inside the crowd, once per fixed tick, for every agent of that type together. This
is deliberate: crowd avoidance is inherently a property of a group of agents considered jointly, and
running it per-agent independently would mean redoing the same neighbor queries once per agent instead of
once per crowd.

An agent joins its crowd exactly once, when it becomes enabled, and every later destination change
retargets the same slot rather than leaving and rejoining. Retargeting is cheap and safe to call every
frame if a caller wants to; leaving and rejoining discards in-flight steering state and is reserved for
cases that actually need a fresh corridor, such as a hard teleport.

## Carving without disrupting a crowd

An obstacle carves a real hole in the tile cache's own compressed layers, not a re-bake of anything: it
calls into the tile cache's obstacle queue directly, and the affected tile or tiles re-contour from
already-rasterized voxel data over the next few fixed ticks, spread across a small budget so a burst of
obstacle changes in one frame never turns into a stall. No agent anywhere on the surface is removed from
its crowd or rejoined for this. The persistent Detour navmesh a query's tile cache maintains is never torn
down and rebuilt to carve an obstacle the way a full rebake elsewhere in the subsystem still is; an agent
mid-path through the tile being carved keeps walking through the whole operation uninterrupted.

An obstacle can also skip carving entirely and instead join the crowd as an immovable neighbor, at zero
cost when it moves (repositioning a crowd slot instead of re-cutting a tile). The mesh stays intact in
this mode and a path can lead straight through the obstacle's footprint; real agents steer around it
locally the same way they steer around each other. This is the right choice for something in continuous
motion, since carving something that moves every frame would mean re-contouring the same tiles every
frame. A hybrid mode carves while the obstacle is still and switches to the crowd-neighbor mode the moment
it starts moving, combining both behaviors for something that is mostly stationary but occasionally
relocates.

## Dynamic cost layers

A transient, additive cost can be placed over a region of one agent type's navmesh without touching the
mesh itself: not a hole, just a polygon that becomes less desirable to route through for a period of time.
The intended use is short-lived tactical state, gunfire suppressing a courtyard, a fire spreading across a
floor, rubble that will finish burning away before it would be worth baking a permanent area for it.

The cost lives in a `NavMeshCostLayer`, one per agent type, owned independently of any particular query or
surface. This ownership is deliberate: a query gets rebuilt whenever a surface rebakes or a link changes,
and a cost entry has nothing to do with either of those events, so it must survive both rather than
vanishing the moment something nearby happens to trigger a query rebuild. A query reads the layer that
belongs to its own agent type at construction time and keeps a reference to it for as long as it lives.

Setting a cost is a single call naming a world position, a search radius, a cost value, and a decay rate.
The radius search finds every polygon within that many units of graph distance from the nearest polygon to
the given point, using Detour's own circular polygon search rather than a straight-line distance, so the
search naturally stays within one connected region instead of reaching through a wall to a point that
happens to sit nearby in world space on the far side of it. Every polygon found gets the same cost and
decay rate; a decay rate of zero opts a cost out of fading on its own, for something meant to persist until
explicitly cleared or overwritten.

Costs are read by a filter that wraps whatever area-mask and area-cost filter a query would otherwise use,
adding the layer's own value on top of the area cost Detour already charges for crossing that polygon. A
cost layer never affects which polygons are passable, only what they cost to cross; exclusion remains
entirely the job of an area mask or a genuine hole in the mesh.

Advancing every entry's decay is pumped once per fixed tick, alongside the tile cache pump that applies
carved obstacles, so a cost fades in step with the rest of the subsystem's own timekeeping rather than
needing a separate driver.

### Why a costed region needs more than one polygon to detour around

A cost layer's effect on route selection depends on the bake actually offering more than one polygon to
choose between in the first place. Detour's search accumulates cost per polygon edge crossed and weighs
that against the straight-line distance remaining; an elevated cost only changes the outcome if a genuinely
different, still-connected sequence of polygons exists for the search to prefer instead. A small, open,
otherwise-featureless area can bake into just one or two very large polygons - Recast's own region merging
does this deliberately, since fewer, larger polygons are cheaper to store and walk - and a cost placed
somewhere inside a single polygon that both endpoints already resolve to has nothing for the search to
route around: every path between two points inside the same polygon is that polygon, regardless of what it
costs to cross. This is the same tile-size consideration the flow-field section above calls out for a
different reason (steering resolution rather than route choice); a bake meant to carry meaningful
cost-based routing benefits from the same fix, a tile size small enough that an open area actually
decomposes into several polygons rather than one.

## Path lookahead

An agent already moving toward a destination can inspect the corridor it is actually steering along,
without issuing a fresh path query to do it. Detour's own crowd simulation maintains a live, continuously
replanned corner list for every agent it steers; `NavMeshAgent.UpcomingCorners` reads directly from that
list rather than calling `FindPath` again, so it reflects exactly what the agent is about to do, including
any replanning the crowd has already done in response to a carved obstacle or a moved link. Each corner
comes back paired with the corridor distance to reach it, the sum of segment lengths along the corridor
rather than a straight line, since that is the distance a walking agent will actually cover.

The corner buffer a caller passes in (implicitly, by requesting a count) is reused across calls rather
than allocated fresh each time, growing only the first time a request asks for more entries than it
currently holds. This matters because the natural caller for this API is a per-frame animation or camera
system that asks the same question every tick.

`NextCornerDistance` and `NextTurnAngle` answer the two questions a locomotion or camera system most often
needs without walking the corner list themselves: how far to the very next corner, and how sharply the
path bends there. `NextTurnAngle` is zero whenever fewer than two corners are currently known, since there
is nothing to measure a bend between yet.

`IsApproachingLink` scans the same live corridor for an off-mesh connection ahead of the agent, distinct
from `IsOnOffMeshLink`, which only reports one already being crossed. This is what a game telegraphs a
jump or vault animation from: knowing a link is coming up, and how far away it still is, before the agent
actually reaches it.

## Automatic link generation at bake

An agent type with `JumpDistance` or `DropHeight` set gets jump and drop connections generated
automatically wherever the baked mesh's own boundary edges come close enough to each other, without
anyone placing a `NavMeshLink` by hand. This is gated purely by those two settings: a project that never
sets them pays nothing extra during a bake and sees no behavior change at all, since generation is skipped
entirely when both are zero.

Generation runs once a bake's tile layers exist, against a throwaway reconstruction of the tiled navmesh
built purely to scan it: every polygon's wall segments (Detour's own per-polygon boundary query) that have
no neighbor across them are boundary edges, the places the walkable surface simply stops. Every pair of
boundary edges within `JumpDistance` of each other is a candidate; the height difference between them
decides whether it becomes a bidirectional jump (roughly level, within `MaxStepHeight`) or a
one-directional drop from the higher edge to the lower one (further apart vertically, but no more than
`DropHeight`). A candidate already connected by the ordinary walkable mesh is skipped, since no off-mesh
connection is needed to cross what a straight walk already reaches, and a candidate is skipped again if a
clearance check finds something in the way.

The clearance check samples points along the straight line between the two edges rather than sweeping a
capsule continuously, testing each sample against every collected triangle's own 2D footprint (not just
its vertices - a wide wall's own vertices sit at its far corners, nowhere near a sample point crossing its
middle, even though its face plainly crosses the line there) whose height range overlaps the capsule at
that point. This is precise enough to reliably reject a real wall standing in a gap while still being far
cheaper than an exact swept-volume intersection test; the gap against a true continuous sweep is a thin
obstruction that happens to fall entirely between two consecutive samples.

Results are stored on the baked asset itself (`NavMeshTileCacheData.GeneratedLinks`), replaced wholesale
by every bake that regenerates them, and woven into a query's off-mesh connections automatically alongside
whatever `NavMeshLink` components a scene has - a hand-placed link is never touched by this, and a
generated one is never mistaken for a hand-placed one, since the two lists are never merged into scene
state at all.

## Flow fields

A `NavMeshFlowField` is a shared route to one goal that any number of agents can follow at once, built
with a single call rather than once per agent - the answer to a crowd of agents all converging on the same
destination each paying for their own path to it. `NavMeshAgent.FollowFlowField` is the alternative
`SetDestination` is for: instead of requesting one fixed target and letting the crowd's own path-following
resolve a route, an agent following a field asks it for a direction at its own current position every
tick and requests that as its desired velocity directly (Detour's own `RequestMoveVelocity`, the peer to
`RequestMoveTarget` built for exactly this kind of external steering). Crowd separation and avoidance
still apply on top of whatever direction a field reports, the same as they would for a plain
path-following agent; a field only ever supplies the "which way to the goal" answer, never bypasses the
crowd itself.

A field is built as a single flood search outward from the goal across the polygon graph - Detour's own
`FindPolysAroundCircle`, the same primitive the cost layer's own `AddCost` and the link generator's
clearance check already use, which accumulates cost through the same area-cost/cost-layer-aware filter
every other query already reads from. Each reachable polygon gets a direction (toward whichever neighbor
the search actually arrived from) and a remaining distance (the accumulated cost itself), which is what
`NavMeshAgent.FollowFlowField`/`NavMeshFlowField.TryGetDirection` and `TryGetDistanceToGoal` read per
agent per tick.

Building over the polygon graph rather than the tile cache's own voxel cell grid was a deliberate
trade-off: a polygon is already the mesh's own natural unit of adjacency, so a field needs nothing beyond
a primitive this engine already relies on elsewhere for other features, at negligible implementation risk.
The cost is resolution inside one large polygon - every position within it reports the same single
direction, since the field has no sub-polygon granularity to offer. This has real practical weight on a
large, otherwise-featureless open area, where Recast's own contour merging produces very few, very large
polygons; a bake meant to carry a flow field benefits from a tile size small enough to keep individual
polygons from growing to the point where "reached the goal's polygon" and "reached the goal" stop being
close to the same thing. A true per-voxel-cell field would not have this limitation, at the cost of
needing to reach into the tile cache's own internal layer data rather than a public query primitive.

Fields are cached per goal, keyed to a grid of cells sized to the bake's own tile size (close enough goals
share one field rather than each rebuilding), and the whole cache is dropped whenever a tile actually
changes - an obstacle finishing a carve, or `RebuildTiles` replacing one. Invalidation is coarse (every
cached field is dropped, not just ones whose own reachable set touched the changed tile) rather than
tracking per-field tile coverage, since a bake's tile count is normally small enough next to the cost of
the carve itself that rebuilding on next use is not a meaningful expense.

## Tactical position queries

`NavMeshSystem.QueryTacticalPositions` answers "where nearby is a good spot for this", not "how do I get
from here to there": a cover point out of a target's sight, a flanking position, a spot within throwing
range but outside melee range. Candidates are sampled the same way as a flow field's own reachable set -
`FindPolysAroundCircle` outward from an origin - but every reachable polygon contributes its own centre
and each of its edge midpoints as a candidate, rather than one direction per polygon.

Scoring is a list of `ITacticalScorer` implementations the caller supplies, each returning one float per
candidate: negative disqualifies the candidate outright, regardless of what any other scorer thinks of it,
and non-negative sums into that candidate's total, which results are ranked by. This single convention
covers both a hard requirement (every scorer this engine ships uses it that way - a fixed positive value
or a disqualifying negative one) and genuine graduated preference, for a caller writing its own scorer
that wants to actually weigh candidates against each other rather than just admit or reject them.

Three scorers ship built in. `NavMeshLineOfSightScorer` disqualifies a candidate whose visibility to (or
from) a target doesn't match what was asked for, via one physics raycast between the two points' eye
heights - a hit anywhere along it means blocked. `NavMeshDistanceBandScorer` disqualifies a candidate
outside a minimum/maximum distance from a point. `NavMeshDirectionConeScorer` disqualifies a candidate
outside a cone of a given total angle pointing some direction from a point - a flanking query, roughly
behind a target rather than in front of it.

## Hierarchical pathfinding

`FindPath` switches to a coarser, faster strategy on its own once start and end are far enough apart in
tile terms, transparently: the return type, the thread-safety contract, and every other observable
behavior are identical either way, and which strategy actually ran is never something a caller needs to
know or handle differently. This exists because an unrestricted Detour search across a very large navmesh
visits proportionally more of it the farther apart its two endpoints are; a coarse pass first narrows the
detailed search down to a small corridor before it ever runs.

An abstract graph sits above the polygon mesh: one node per tile that currently holds at least one
polygon, an edge between two tiles (including diagonal neighbors, not just the four orthogonal ones - an
orthogonal-only graph forces a "staircase" route across a genuinely diagonal trip, confirmed empirically
to overshoot the true distance by a wide margin) when a real probe search between their own representative
points actually succeeds, that edge's cost the probe path's own length rather than a straight-line guess
at it. Finding a route between two far-apart points first runs an ordinary A* over this small graph - a
few hundred nodes at most for a very large bake, nothing like the polygon count underneath it - producing
an ordered list of tiles to pass through. The actual detailed path is then one ordinary Detour search from
the real start to the real end, with its own polygon visitation restricted (by wrapping the query's filter)
to just the polygons belonging to that tile list, padded with each tile's own immediate neighbors so a
portal polygon just outside the strict corridor isn't wrongly excluded.

Both the coarse graph and any route found over it fall back to a plain, unrestricted search on failure -
no coarse route between the two tiles, or the restricted detailed search itself coming up empty - so the
hierarchical strategy can only ever change how a path is found, never whether one is reported when a real
one exists. The graph itself is cached and, like the flow-field cache, dropped in its entirety whenever a
tile actually changes rather than patched incrementally - the same trade-off, for the same reason: a
bake's tile count is normally small enough next to the cost of the change itself that a full rebuild on
next use is not a meaningful expense.

## Tile streaming

`NavMeshSystem.UnloadTiles`/`LoadTiles` remove and restore a region of one agent type's live navmesh at
runtime without touching the baked asset's own stored tile layers, the primitive a large streaming world
needs to keep an off-screen chunk's navmesh out of memory the same way its render meshes and colliders
already stream out, and bring it back the moment the chunk streams back in.

Unloading a region walks the baked asset's own stored layers to find every tile grid coordinate whose world
bounds overlap it, then, for each currently-live tile at those coordinates, removes it from the query's
underlying Detour navmesh directly rather than only from the tile cache's own bookkeeping. The distinction
matters: the tile cache's own `RemoveTile` frees its compressed-layer slot for reuse but does not by itself
touch the live navmesh a query actually searches, since normally that side is handled as a side effect of
immediately rebuilding the tile from fresh data (the same call `ReplaceTile` makes). A pure unload has
nothing to rebuild, so it drives that removal itself. What is never touched is the baked asset's own stored
layer bytes; those stay exactly as they were baked, which is what makes a later reload possible without
rebaking anything.

Loading a region reverses this from storage: every stored layer at a coordinate the region covers that is
not currently live gets added back to the tile cache and rebuilt into the live navmesh, in one synchronous
call, the same primitive `ReplaceTile` already uses to bring a freshly rebuilt tile online. A tile that was
never unloaded, or is already live, is left untouched, so calling `LoadTiles` speculatively over a region
that turns out to already be loaded is a safe no-op rather than a redundant rebuild.

An agent whose own polygon sat on a tile that gets unloaded is not removed from its crowd or otherwise
disturbed; it simply keeps whatever position and corridor state it last had, the same as it would if its
destination became genuinely unreachable for any other reason. A path query into or across the unloaded
region reports no route, the same outcome as querying toward any other unreachable destination, not an
error. Once the region reloads, a fresh path request succeeds again immediately; nothing about the agent's
crowd membership needed to change at any point in the round trip.

`NavMeshSystem.RebuildTiles`/`RebuildTilesAsync` are the sibling operation for when a region's underlying
geometry has actually changed rather than just needing to come back unmodified: both re-collect scene
geometry and re-run the bake pipeline over it, but scope the result to only the tiles overlapping the given
region, so a large surface's own already-correct tiles elsewhere are left alone. The async form splits
geometry collection (which reads mutable scene and registry state, so it must run on the calling thread) from
the actual Recast build (the only part safe to run on a background thread), matching the split the
subsystem's own async bake job already uses elsewhere. Both forms write the newly built tiles back into the
baked asset's own stored layers as well as the live navmesh, so a later unload/reload round trip over the
same region picks up the rebuilt content rather than stale data from the original bake.

## Comparison with Unity's NavMeshComponents

Unity's own navigation package (`NavMeshSurface`, `NavMeshAgent`, `NavMeshObstacle`, `NavMeshLink`,
`NavMeshModifier`, `NavMeshModifierVolume`) documents the following, current as of Unity's published
package documentation for `com.unity.ai.navigation`:

- A `NavMeshObstacle` can either carve a hole or act as a crowd-avoided obstacle without carving, matching
  Prowl's own `Carve` toggle, and offers a "carve only when stationary" setting matching Prowl's
  `CarveOnlyStationary`.
- `NavMeshSurface` supports collecting geometry from the whole scene, from a declared volume, or from an
  object's own hierarchy, and choosing between render meshes and physics colliders as the geometry source;
  Prowl's `NavMeshCollectObjects` and `NavMeshGeometrySource` mirror both choices.
- `NavMeshLink` connects two points not otherwise connected by the baked mesh, matching Prowl's
  `NavMeshLink`, though Unity's link is a single connection per component rather than a width-based pair of
  parallel connections.
- Area costs are a documented, supported feature of Unity's own `NavMesh.SetAreaCost`, described as biasing
  path selection toward cheaper areas. Unity's own manual does not document a runtime, decaying,
  positional cost overlay distinct from its fixed, bake-time area-cost table; Prowl's cost layer is
  additional to what Unity's own documented feature set describes.
- Unity does not document a partial, region-scoped retile operation equivalent to `RebuildTiles`; its own
  `NavMeshSurface.UpdateNavMesh` re-bakes the whole surface.
- Unity's `NavMeshAgent.path` exposes the whole corner array for a path, and `NavMeshAgent.isOnOffMeshLink`
  reports a link already being crossed; Unity's own documentation does not describe a distance-annotated
  lookahead over a live, reused buffer, a turn-angle query, or a way to detect an upcoming link before an
  agent reaches it.
- Automatic drop-height/jump-distance off-mesh link generation at bake time has long been a documented
  Unity NavMesh baking setting; Prowl's own generation is scoped per agent type (via `JumpDistance`/
  `DropHeight` on that type rather than a single project-wide bake setting) and stores its output on the
  baked asset rather than adding scene objects for each generated link.
- Unity's own manual does not document a shared, crowd-scale flow-field primitive; its `NavMeshAgent` is a
  per-agent path follower throughout, one `CalculatePath`/destination per agent.
- Unity's own manual does not document a tactical position query (a scored search over candidate points
  for cover, flanking, or similar); a project wanting one builds it directly against `NavMesh.SamplePosition`
  and `Physics.Raycast` itself.
- Unity's own manual does not document a hierarchical/coarse-then-detailed pathfinding strategy for
  `CalculatePath`; a single call always searches the full navmesh.
- Unity's own manual does not document an API to unload or reload a region of a baked navmesh at runtime
  independent of a full re-bake; `NavMeshSurface.UpdateNavMesh` is the only documented way to change what a
  surface's navmesh contains.

## Non-goals

- **Volume (3D) navigation.** The subsystem assumes agents walk on a 2D-ish walkable surface embedded in
  3D space, the same assumption Detour itself makes; genuinely three-dimensional pathing (flight through
  open volumes, submarines) needs a different representation entirely and is out of scope.
- **Non-holonomic agents.** Steering assumes an agent can accelerate toward any direction on the plane at
  will; agents with a turning radius constraint (most wheeled vehicles) need a different local controller
  layered on top of the corridor this subsystem produces, not a change to the corridor itself.
- **Cross-platform determinism.** Floating-point voxelization and crowd simulation are not guaranteed to
  produce bit-identical results across platforms or over time as compiler and runtime versions change,
  matching Detour's own long-standing position on this and Unity's own documented stance for its
  navigation system.

## Known limitations

- Geometry collection always re-collects and re-voxelizes a surface's entire declared scope; there is no
  bounded, declared-bake-extent geometry provider that would let a partial retile skip voxelizing anything
  outside the region actually being rebuilt.
- An obstacle's oriented box is carved as its own axis-aligned world bounds, since the tile cache's own box
  obstacle primitive has no rotation; a heavily rotated box obstacle carves a looser hole than its true
  footprint.
- A capsule obstacle is carved as an upright cylinder; the tile cache has no capsule primitive with rounded
  caps.
- Generated jump/drop links use a sampled, not continuously swept, clearance check; a thin obstruction
  that falls entirely between two consecutive samples along a candidate link is not guaranteed to be
  caught. Boundary-edge pairing is a simple pairwise scan over every edge in a bake, which scales
  quadratically with edge count; a very large, edge-dense bake with generation enabled pays for that.
- A flow field's direction resolution is per polygon, not per voxel cell; a bake with very large polygons
  (a wide, featureless open area with a tile size too generous for it) gives a field almost nothing to
  steer by inside them - reaching "the goal's polygon" and reaching the goal itself stop being close to
  the same thing. Flow-field cache invalidation on a tile change drops the entire cache, not just fields
  whose own reachable set touched that tile.
- A tactical query's candidates are the polygon graph's own vertices and centres, not a uniform grid; a
  region baked as few, large polygons offers a tactical query the same coarse sampling it gives a flow
  field. Scoring is a linear scan over every candidate against every scorer, with no spatial partitioning
  to prune obviously-disqualified candidates early.
- The hierarchical tile graph's own edges come from a real probe search between representative points at
  build time, an upfront cost proportional to tile count (with diagonal adjacency, roughly eight probes
  per tile); a bake with a very large number of tiles pays for that the first time the graph is needed
  after a rebuild. Padding a corridor with each tile's immediate neighbors is a fixed, one-level margin,
  not a guarantee against every possible portal placement in unusual geometry.
- Tile streaming's unload/reload round trip is scoped to one agent type's query at a time; a project with
  several agent types sharing the same streamed world region calls `UnloadTiles`/`LoadTiles` once per type
  itself rather than the subsystem doing it for every type together. An agent mid-corridor across a tile at
  the moment it unloads does not have its corridor state explicitly cleared; it is simply left to discover
  the destination is unreachable on its own next replan.
