# Horizontal Directional Drilling - implementation plan

## Architecture

Keep the HDD truck as one persistent vehicle entity. It uses normal movement
and pathfinding while travelling, then becomes assigned and immobilized while
deployed. A separate static HDD site/job entity owns route geometry,
construction progress, deliveries, reinforcement and installed transport.

Add a dedicated **HDD construction mode**. While HDD mode is
enabled, supported belt/pipe tools create underground transport. The items
follow native belt placement constraints, including applying belt restrictions
to pipes, and behave like ordinary buildable items afterward:

- A plan is a non-dispatched design.
- A waiting-to-build underground transport is eligible for HDD dispatch.
- Paused/cancelled construction follows the normal construction lifecycle.

This mode owns only HDD belt/pipe (`HDD B/P`) placement semantics. HDD does not
depend on or inspect another construction-mode mod.

Avoid converting or replacing a vehicle with a building. Converting entities
would introduce identity, inventory, job-scheduler and save/load risks.

## Gameplay loop

1. The player enables HDD mode and places a supported belt or pipe underground.
   The native belt construction constraints apply to the route.
2. The HDD B/P remains a normal plan until the player starts construction. A
   waiting-to-build HDD B/P becomes an eligible HDD job.
3. An idle HDD truck claims the route and selects an endpoint. Prefer
   the closest reachable endpoint, then the endpoint with the best available
   staging/delivery space and current logistics access.
4. The truck travels to the selected endpoint, deploys and becomes fixed there.
5. The job site requests drill rods and drilling mud.
6. The truck consumes one rod and one mud per drilled tile.
7. When its onboard buffer empties, drilling stops at a tile boundary and waits
   for replenishment.
8. At the far endpoint, drilling stops and construction logistics deliver CP3.
9. Reinforcement is a long operation. When it finishes, the HDD job enters the
   momentary `Reinforced` state, records successful completion and immediately
   starts retraction; the reinforcement process itself is not shortened.
10. The truck retracts every deployed drill rod.
11. Mud is lost; recovered drill rods return to truck inventory.
12. Rods that exceed truck storage remain at the HDD site and must be
    transported away by other trucks before cleanup can finish.
13. The truck packs up, becoming mobile again, and is released to its normal
    vehicle scheduler.
14. The HDD B/P becomes ready for its ordinary transport construction work and
    can be completed with the pipe/belt selected in the original plan.

## MVP scope

- HDD truck based on a Truck 2 placeholder.
- Dedicated research node.
- Drilling mud product and mixer recipe.
- Reusable drill-rod product and assembly recipe.
- Native HDD mode for underground belt/pipe construction.
- Ordinary plan and waiting-to-build behavior for HDD B/P items.
- HDD discovery and claiming for waiting-to-build HDD B/P items.
- Endpoint selection based on travel and logistics availability.
- Elevation-aware routes generated from the transport's slope.
- Minimum route length of 9 tiles; route length is not capped at 40.
- 40-unit mud and 40-unit drill-rod onboard buffers. Longer jobs replenish
  through logistics and can create outbound rod overflow during retraction.
- Tile-by-tile drilling progress with map highlighting and inspector progress.
- Logistics replenishment while deployed.
- Stationary refueling and explicit refueling-failure messages.
- Diesel use while moving; diesel and electricity while drilling.
- CP3 delivery and reinforcement.
- Rod retraction on completion or cancellation.
- Persistent job state and save/load.
- Completion of pipe HDD B/P construction first.

The supported route shape follows the underground transport being built. Do not
limit HDD to straight lines solely for implementation convenience. Curvature,
turn radius and nonuniform elevation are nevertheless constrained by the
native transport geometry and placement rules. Defer arbitrary free-form
routes, upgrades, drill wear, soil-dependent speed, terrain deformation and
multiple drill heads.

## Truck

Derive or adapt the normal Truck 2 behavior so that the HDD truck retains
standard depot, fuel, replacement, pathfinding and rendering support.

Add:

- HDD-job capability.
- 40-unit drilling-mud buffer.
- 40-unit drill-rod feed buffer.
- Reference to the active deployed job.
- Deployed-rod count.
- Excavator-like movement speed.
- Diesel-only movement consumption.
- Diesel and electricity consumption while working.
- Fixed/deployed state and visual placeholder.

The truck leaves the general job scheduler after arriving at a valid endpoint.
It remains a vehicle internally and exposes a deployed state instead of
becoming a building entity. It cannot abandon the route while rods are
deployed. If fuel is insufficient, the truck remains fixed, requests
refueling and reports that no job-compatible refueling station is available
when logistics cannot provide one.

## Job states

```text
Planned
  -> Assigned
  -> Travelling
  -> Arrived
  -> Setup
  -> Replenishing
  -> Drilling
  -> WaitingForCp3
  -> Reinforcing
  -> Reinforced
  -> Retracting
  -> UnloadingExcessRods
  -> PackingUp
  -> job exits the state machine
```

Also support `Paused`, `Blocked`, `Cancelled` and `Failed`.

`Reinforced` is a momentary internal completion signal reached only after the
long `Reinforcing` process. It immediately advances to `Retracting`.
`PackingUp` is the inverse of setup. Once packing is complete, the HDD job
exits the state machine and the truck returns directly to its normal scheduler.
No separate ready-to-leave or completed operational state is needed.

Pausing preserves the route, truck assignment and deployed drill rods. The
HDD B/P construction pause state must be observed by the active HDD job.
Cancellation must retract deployed rods before releasing the truck. Once
drilling has started, cancellation must not simply discard route or rod state.

## Job/site data

Persist:

- Source HDD B/P transport identity and construction-plan state.
- Selected start and far endpoint.
- Endpoint eligibility and staging score.
- Ordered route tiles derived from the HDD B/P underground transport.
- Slope/elevation segments for the route.
- Route length and transport class.
- Current state and blocked reason.
- Refueling-station availability and last refueling request result.
- Drilled and reinforced tile counts.
- Deployed and buffered rod counts.
- Rods awaiting outbound pickup after truck capacity is reached.
- Mud-buffer quantity.
- CP3 required and delivered.
- Assigned HDD truck ID.
- Pause/cancellation flags.
- Installed transport prototype, if any.
- Current operation progress and timing.

The static job/site service owns route ownership, delivery queues and
persistence. It transfers delivered mud and rods into the assigned truck and
creates outbound jobs for excess rods during retraction. This is simpler and
safer than making a temporarily immobilized vehicle behave as a general
static delivery target.

Inspect the current Captain of Industry transport construction and save APIs.
HDD must own its construction-mode state and serialization and must not depend
on another mod's construction state.

## Drilling operation

For every tile:

1. Verify that the job is active.
2. Verify that setup is complete.
3. Verify diesel and electricity are available.
4. Verify at least one mud and one rod are buffered.
5. Consume one mud.
6. Move one rod from the buffer into the deployed count.
7. Advance drilling progress by one tile.
8. Complete the configured operation duration.

When either onboard buffer is empty:

- Stop at the current tile boundary.
- Enter `Replenishing`.
- Request the missing products at the HDD site.
- Resume at the saved tile index after delivery.

Each completed tile changes the displayed route overlay immediately. Selecting
the deployed HDD truck must:

- Highlight completed, active and remaining route segments on the map.
- Show endpoint choice and underground elevation progression.
- Display a progress bar for drilled/total tiles.
- Display a separate CP3/reinforcement progress bar after drilling.
- Display active replenishment, refueling and blocked conditions.

## Resource consumption

- Moving: diesel only.
- Setup: reduced diesel and electricity.
- Drilling: diesel, electricity, one drill rod and one mud per tile.
- Reinforcing: electricity if the HDD truck participates.
- Retracting: reduced diesel and electricity.
- Idle: normal vehicle standby behavior.

While deployed, the truck must request refueling without leaving the job. If
there is no available refueling path, it raises a persistent blocking warning
and remains assigned and fixed. A deployed truck must never drive to a fuel
station and abandon active rods.

## Products and recipes

### Drilling mud

Initial mixing ratio:

```text
1 dirt + 4 water -> 2 drilling mud
```

The 1:4:2 ratio is provisional and should be replaced if current machine and
fluid recipes already establish a more consistent mixing convention. Initial
duration: 10 seconds.

Use an existing compatible mixer, or add a small dedicated mixer only if its
input/output port layout cannot represent dirt and water together.

### Drill rod

Treat drill rods as reusable sections rather than consumable bits:

The drill-rod recipe is intentionally unresolved. Inspect the current
`Mafi.Base` product IDs and assembly-machine recipes before selecting
components; Captain of Industry does not provide a steel-pipe product. A first
candidate should use steel and an existing common mechanical component, with
concrete or rubber only if the intended research tier supports it. Record the
verified recipe in this plan before registration.

One rod enters the bore per drilled tile and is recovered during retraction.
All rods return to truck storage first; excess rods unload through normal
logistics or at the depot.

A separately wearable drill head can be introduced later if balancing needs a
consumable bit.

### HDD truck placeholder

Verified target recipe, subject to final current product-ID inspection:

```text
20 diesel + 60 vehicle parts II + 30 rubber + 40 steel -> 1 HDD truck
```

Use the existing vehicle construction/depot flow rather than treating the HDD
truck as a building or a recipe assembled from another Truck 2 item.
Confirm the exact diesel quantity semantics and vehicle-parts recipe against
current base-game code before implementation.

## Reinforcement

After reaching the far endpoint:

1. Enter `WaitingForCp3`.
2. Request CP3 from the normal construction delivery system.
3. Reinforce the bore tile-by-tile or in small batches.
4. Enter `Retracting` only after all required reinforcement is complete.

Initial CP3 cost:

```text
ceil(route length / 4)
```

Expose route length, CP3 divisor, work duration and movement/operation
consumption in config for tuning where practical.

## Retraction and cancellation

Retraction:

1. Withdraw one deployed rod per operation.
2. Return each rod to truck inventory.
3. Require inventory capacity before continuing.
4. When the 40-unit truck buffer fills, place excess recovered rods into the
   HDD site's outbound inventory.
5. Keep retraction and truck packing blocked until logistics has removed the
   excess outbound rods.
6. Release the truck only when deployed rod count is zero, outbound rods are
   zero and packing-up is complete.
7. Keep the reinforced route if reinforcement completed.

Safe cancellation rules:

- Before drilling: release the truck and cancel queued deliveries.
- During setup: stop setup and release the truck after cleanup.
- During drilling: enter `Retracting`.
- During reinforcement: pause or cancel reinforcement safely, then retract.
- During retraction: continue until all rods are recovered.
- Recovered rods above truck capacity are exported through ordinary logistics.
- Never release a truck while rods remain underground.
- Never mark an unreinforced bore available for transport.

## Route and placement validation

Initial constraints:

- Route shape follows the original pipe/belt underground plan.
- Minimum length: 9 tiles.
- No route cap at 40 tiles. The 40-unit values are inventory capacities only.
- Both exposed endpoints must be reachable or serviceable as required by the
  chosen start/far endpoint.
- At least one endpoint must be reachable by the HDD truck.
- Route must remain inside the map.
- Reject overlap with another HDD corridor.
- Reject protected, ocean and otherwise invalid crossing tiles.
- Support elevation using the same 4:1 slope convention as native belts.
- Account for four transition tiles per elevation tier at each entrance/exit,
  plus at least one underground tile between them. A one-tier crossing therefore
  starts at a minimum of 9 tiles; deeper multi-tier routes add the necessary
  sloped transport sections.
- Add or validate the corresponding 4:1 sloped pipe geometry for every pipe
  tier that HDD must use. The pipe/belt hidden route must not require a
  slope the completed native transport cannot represent.
- Prefer the geometry already encoded by the HDD B/P route and the current game
  transport slope rules instead of creating a separate free-form drill spline.
- Revalidate route before setup and before each major state transition.
- Prevent normal transport construction until drilling and reinforcement
  complete.
- Keep the source HDD B/P in its construction-prerequisite state throughout
  drilling and reinforcement.
- Do not allow underground mergers or splitters in the initial release. A
  future option may enable them once safe branch/junction routing and
  delivery behavior are implemented.

Validate long routes for performance, reachable replenishment and aggregate
ownership. A practical gameplay/configuration limit can be introduced later
from testing, but must not be confused with the truck's 40-unit buffers.

## Placement behavior

HDD mode intercepts supported belt and pipe placement tools:

- Surface placement is forced into the approved underground route.
- Elevation and bend validation uses belt rules even for pipes.
- Construction creates an ordinary plan owned by the HDD B/P entity.
- The plan remains a visual map design until the player starts construction.
- Once waiting to be built, it becomes available to the HDD job dispatcher.
- Native pause, resume, cancel and remove behavior remains available.
- Merger and splitter templates are rejected underground in the initial
  release.

The MVP turn radius matches native belts: radius 1. A future improvement
should change the default HDD turn radius to 2 and expose a radius-3 option.
The selected radius must alter both route validation and completed transport
geometry, not only the placement preview.

## Future options

- Enable underground mergers and splitters as a configurable mod option after
  branch logistics, construction ordering and collision rules are complete.
- Use radius 2 as the default HDD turn radius and expose radius 3 as an option.
- Add additional transport classes after pipe, flat belt and loose conveyor
  behavior is stable.
- Support mod-defined belt/pipe prototypes that opt in to HDD mode.

## Reinforced corridor and transport

Represent the completed HDD work as reinforcement attached to the original
HDD B/P construction entity.
After completion, the existing HDD B/P construction plan becomes eligible for
normal transport construction using the reinforced route.

The resulting corridor stores:

- Entry and exit endpoint ports.
- Route geometry or tile references.
- Reinforced/available state.
- Selected transport class.
- Installed transport product, if any.
- Throughput and optional travel-latency configuration.

Implement pipe transport first:

1. Preserve the source HDD B/P geometry and selected endpoints.
2. Reinforce the hidden route.
3. Release the validated HDD B/P to its normal transport construction stage.
4. Ensure the normal construction workflow can build the 4:1 sloped sections
   and hidden connection without HDD-specific endpoint buildings.
5. Route fluid through the resulting native pipe where possible.

Then add flat and loose transport using the same corridor abstraction.
Use current Captain of Industry assemblies/XML documentation and game
implementation as references. Do not use old underground-transport mods as a
dependency or implementation guide.

## Research

Add a dedicated `Horizontal Directional Drilling` research node.

Its exact parent is to be selected from the final prototype inspection, but it
should likely follow:

- Truck 2.
- Construction Parts III.
- Assembly Machine II.
- Industrial mixing/fluid-processing progression.

Unlock:

- HDD truck.
- Drill rods and their recipe.
- Drilling mud and its recipe.
- HDD construction mode for supported belt/pipe underground types.
- Reinforced underground pipe slope/connector geometry as required.

## Registration sequence

1. Register products:
   - Drilling mud.
   - Drill rods.
   - HDD truck, if vehicles use the product registry.
2. Register recipes:
   - Drilling mud mixing.
   - Drill-rod assembly.
   - HDD truck placeholder construction.
3. Register static prototypes:
   - Reinforced-route state components.
   - Any required 4:1 sloped pipe/connector prototypes.
4. Register dynamic entity:
   - HDD truck.
5. Register research and unlocks.
6. Register global job/site managers and inspectors.
7. Validate all prototype references after locking.
8. Register HDD construction-mode state, input handling, placement validation
   and transport-construction transitions.

## Save compatibility

Once gameplay entities exist, mark the mod as not removable from saved games.

Persist every field required to resume:

- Job state.
- Route.
- Assigned truck.
- Buffers.
- Deployed rods.
- Drilling, reinforcement and retraction progress.
- CP3 inventory.
- Pause/cancel state.
- Endpoint links.
- Source HDD B/P transport identity and construction state.
- Transport state.

Use explicit save-version branches whenever persisted fields change. Never
reuse a version constant. Add removal and fallback behavior for obsolete entity
types rather than relying on silent deserialization defaults.

## UI and feedback

Initial UI:

- Job state and blocked reason.
- Assigned truck.
- Current/total drilled tiles.
- Mud and rod inventory.
- Recovering/outbound rod inventory.
- Route slope and depth segments.
- Selected start endpoint and endpoint score.
- Delivered/required CP3.
- Reinforcement progress.
- Deployed rod count.
- Pause and cancel controls.
- Route and endpoint highlighting.
- Completed/active/remaining route overlay.
- Drilled/total progress bar.
- Reinforcement progress bar.
- Packing-up/refueling status.

Player messages must explicitly explain:

- Unreachable selected endpoint.
- Missing truck or assignment.
- Invalid route.
- No mud.
- No drill rods.
- No fuel or electricity.
- Missing CP3.
- No available path or station for stationary refueling.
- Excess recovered rods awaiting pickup.
- Cancel/retraction in progress.

## Assets

MVP:

- Reuse Truck 2 model and icon as the placeholder.
- Use the existing HDD SVG as a temporary research/toolbar visual.
- Visualize selected native transport endpoints and the deployed truck. No
  bespoke entry or receiving-pad assets are planned.

Polish:

- Dedicated HDD rig model and deployed animation.
- Rod-feed animation.
- Drilling mud hoses.
- Highlighted underground bore progress, including completed, active and
  remaining elevation segments.
- Dust, exhaust and drilling particles.
- Operation and completion sounds.
- Route preview and underground visualization.

## Implementation phases

### Phase A - data-only foundation

- Refactor stable IDs into product, vehicle, recipe, building and research
  groups.
- Register drilling mud and drill-rod products.
- Inspect current product IDs and native vehicle/assembly recipes.
- Finalize the drill-rod and drilling-mud recipes from verified products.
- Register the HDD truck placeholder and its build recipe.
- Register research and unlocks.
- Verify startup, toolbar, recipe and research views.

### Phase B - HDD construction mode and route validation

- Add a dedicated HDD mode alongside the existing transport construction modes.
- While enabled, restrict placement to supported belts and pipes and force the
  route underground.
- Apply native belt placement, elevation and bend constraints to pipes as
  well as belts.
- Reject mergers and splitters underground.
- Make HDD B/Ps behave as normal buildable plans, waiting-to-build entities and
  paused/cancelled construction.
- Discover waiting-to-build HDD B/Ps without adding separate pads.
- Extract native route tiles, elevation segments and both endpoints.
- Validate 4:1 slopes, 9-tile minimum and endpoint
  accessibility.
- Implement route reservation and blocked-reason reporting.
- Create the HDD job/site service.
- Add a basic inspector.

### Phase C - vehicle job lifecycle

- Add HDD vehicle capabilities and job manager.
- Assign idle trucks to eligible waiting-to-build HDD B/P routes.
- Choose the closest reachable endpoint, using logistics/staging availability
  as the tie-breaker.
- Drive to the selected endpoint using standard pathfinding.
- Deploy and immobilize at the endpoint.
- Implement stationary fuel requests and no-refueling-station diagnostics.
- Ensure HDD B/P pause and cancellation states control the HDD job.
- Implement pause and cancellation transitions.
- Restore correct state after save/load.

### Phase D - drilling simulation

- Add onboard buffers and delivery transfer.
- Implement one-tile drilling operations.
- Add diesel and electricity requirements.
- Request replenishment after depletion.
- Persist deployed rods and progress.
- Highlight completed, active and remaining drilling segments when the
  deployed truck is selected.
- Add drilling and reinforcement progress bars to the inspector.

### Phase E - reinforcement and retraction

- Request CP3 after drilling.
- Integrate with construction logistics.
- Run the long CP3 reinforcement process and track its progress.
- On successful reinforcement, record `Reinforced` only as a momentary
  transition and begin retraction immediately.
- Retract all drill rods.
- Recover rods into the 40-unit truck buffer.
- Create outbound logistics jobs for rods exceeding truck capacity.
- Block final packing until excess rods have been collected.
- Pack up and release the truck safely.

### Phase F - HDD transport construction completion

- Preserve the original HDD B/P identity throughout HDD work.
- Complete the HDD prerequisite stage on that construction entity.
- Release the reinforced route to normal pipe/belt construction.
- Support pipe transport first, including required 4:1 sloped sections.
- Let the normal transport construction system complete endpoint and port
  connections.
- Add pause, disconnect and removal handling.
- Add flat and loose transport after pipe support is stable.

### Phase G - polish and release

- Add Unity assets and animations.
- Add localization files.
- Add configurable turn-radius and underground-junction options only after
  their underlying route geometry and construction behavior exist.
- Tune route limits, recipes and consumption.
- Add save migration tests.
- Add lifecycle tests for every state transition.
- Add performance validation for many completed/active corridors.
- Update manifest save-removal metadata before the first gameplay release.

## Focused test cases

- Truck finds and travels to the nearest eligible job.
- Endpoint choice uses the closest reachable endpoint and logistics tie-breaker.
- Two trucks cannot claim one job.
- Setup cannot begin at an invalid or unreachable endpoint.
- A 9-tile one-tier 4:1 route is valid.
- Multi-tier elevation routes include all required sloped sections.
- HDD mode applies belt constraints to pipes.
- HDD placement rejects underground mergers and splitters.
- A planned HDD B/P is not dispatched until it is waiting to build.
- Drilling consumes exactly one mud and one rod per tile.
- Drilling stops and resumes cleanly at buffer depletion.
- Mud is not recovered; all rods are recovered.
- Recovered rods beyond capacity remain outbound until another truck removes
  them.
- Electricity loss pauses drilling without consuming another tile.
- A deployed truck with low fuel remains fixed and requests refueling.
- No available refueling station produces a persistent warning.
- Pausing preserves route and deployed rods.
- Pausing/cancelling the source HDD B/P updates the active HDD job safely.
- Cancellation before drilling releases the truck.
- Cancellation during drilling retracts rods before release.
- Selecting the deployed truck highlights completed and remaining map segments.
- Inspector progress bars match saved drilling and reinforcement progress.
- Reinforcement cannot begin before the far endpoint is reached.
- Transport cannot be installed before CP3 reinforcement finishes.
- Reinforced status transitions directly to retraction before the truck packs
  up and becomes mobile.
- Completed HDD work releases the matching HDD B/P to normal construction.
- Save/load resumes every state, including deployed retraction states.
- A removed endpoint blocks or safely disconnects its corridor.
- Fluid throughput and both endpoint port states remain symmetric.

## Primary technical risk

The main risk is integrating custom delivery and assignment behavior with an
immobilized vehicle.

Mitigate it by making the static HDD site own:

- Job assignment.
- Logistics requests.
- Route progress.
- Reinforcement.
- Cancellation and pause coordination.

The vehicle then supplies:

- Movement.
- Fuel.
- Onboard storage.
- Reusable rods and outbound overflow.
- Electricity while deployed.
- Deployment visual/state.

This split keeps the custom job system small and retains the engine's normal
vehicle lifecycle.
