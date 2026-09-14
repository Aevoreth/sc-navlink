# SC-navLink Cursor Development Handoff

This document is a supplemental implementation handoff for continuing SC-navLink work in Cursor.

It summarizes current product intent, known codebase facts, implementation sequencing, and issue structure after the September 14, 2026 roadmap review.

It is **not** more authoritative than the canonical project documents or implemented code.

## Read this first in Cursor

Before making architectural changes, read these in order:

1. `PROJECT.md` — product scope, domain boundaries, module intent, and long-lived rules.
2. `docs/ROADMAP.md` — milestone sequence and dependency flow.
3. `docs/DECISIONS.md` — why major architecture choices exist.
4. `ARCHITECTURE.md` — what the current code actually implements.
5. `SECURITY.md` — hard game-integration/safety boundary.
6. `CONTRIBUTING.md` — clean-room, branch, testing, and provider rules.
7. The GitHub issue being implemented — immediate scope and acceptance criteria.

When these conflict, follow the documentation-authority rules in `PROJECT.md`: current code is authoritative for implemented behavior; the specialized canonical document is authoritative for its policy/planning area; stale documentation should be corrected when practical.

## Branch context

At the time of this handoff:

- `main` is still the stable inherited/project baseline and does not contain all current foundation work.
- `foundation/0.1` is the active integration branch for the SC-navLink foundation.
- Roadmap/charter refresh work was prepared from `foundation/0.1` on `docs/product-roadmap-refresh`.

For new feature work, branch from the current integration branch appropriate to the active milestone rather than assuming `main` contains the newest architecture.

## Known-good baseline

Issue #2 established the inherited technical baseline.

The project uses:

- C# / .NET 10;
- WPF;
- CommunityToolkit.Mvvm;
- SQLite;
- Windows OCR;
- xUnit.

The verified foundation test baseline was 2935 passing tests after correction of one inherited scheduler-sensitive test assertion.

Typical verification commands are:

```text
dotnet restore NexusApp.Tests/NexusApp.Tests.csproj
dotnet build NexusApp/NexusApp.csproj -c Release --no-restore
dotnet test NexusApp.Tests/NexusApp.Tests.csproj -c Release --no-restore
```

Preserve the security and clean-room constraints while evolving inherited Nexus code.

## Product in one sentence

SC-navLink should maintain one trustworthy picture of the player's current economic operation and deterministically answer:

> **Given my current operation and the best available data, what should I do next?**

## Product layers

Think in three layers when designing features.

### 1. Operational truth

Facts/observations about the current operation:

- session/shard;
- location;
- active ship/capacity;
- wallet;
- contracts/objectives;
- carried cargo;
- mining observations;
- refinery jobs;
- blueprint/material goals;
- current route/operation progress.

### 2. Planning

Deterministic logic operating on shared state + cached reference/provider data:

- contract selection;
- capacity checks;
- pickup/drop-off ordering;
- trade scoring;
- mixed-load liquidation;
- crackability;
- refinery/material/value decisions;
- NEXT candidate selection.

### 3. Presentation

Views of the same shared operation:

- **Operations** — high-level mission control;
- **Starmap** — route geography;
- **module pages** — detailed workflows;
- **overlay** — minimal immediate guidance.

Do not put business truth or independent planners into presentation surfaces merely because it is convenient for one screen.

## Critical dependency chain

The next major product loop should be implemented in this direction:

```text
SHIP CATALOG -> MY HANGAR -> ACTIVE SHIP
                               |
                               v
                        USABLE CAPACITY
                               |
                               v
CONTRACTS ------------->   CARGO STATE   <---- USER / TRANSACTIONS / OCR
      |                        |
      +-----------+------------+
                  v
              ROUTE PLAN
           /      |       \
      HAULING    TRADE    SELL LOAD
           \      |       /
                  v
                 NEXT
           /      |       \
    OPERATIONS  STARMAP  OVERLAY
```

The sequence matters because route/trade/hauling recommendations cannot be trustworthy if active ship capacity and carried cargo are still undefined or duplicated across modules.

## NEXT is already a domain concept, not a future blank slate

Do not treat 0.5 as the point where NEXT begins.

The foundation already introduced an initial UI-agnostic `NextAction`/recommendation contract and deterministic hauling logic. The intended evolution is:

- foundation: domain contract + first rule set;
- 0.2: expose useful NEXT in Operations/overlay and connect it to shared RoutePlan;
- 0.3/0.4: add better observations/state;
- 0.5: deepen cross-domain scoring, alternatives, and explanations.

When adding a domain, prefer contributing candidate actions/constraints to the existing coordination layer instead of designing a parallel “smart assistant.”

## Cargo boundaries are intentional

Never assume these are the same thing:

### Cargo obligations
What a contract/objective requires.

Example: “Deliver 32 SCU Titanium.”

### CargoState
What NavLink believes is physically aboard the active ship.

Example: “32 SCU Titanium + 16 SCU Laranite observed/confirmed aboard C2.”

### Cargo placement plan
Where containers should be positioned/loaded for practical unloading.

Example: “Place delivery-A boxes near the forward edge; keep later-stop boxes behind them.”

A contract objective alone is not evidence that cargo is aboard.

CargoState should support provenance such as user-confirmed, OCR-observed, inferred, or transaction-derived values.

## RoutePlan is shared truth

Do not let Hauling, Trade, Starmap, Operations, and overlay grow separate route representations.

The target `RoutePlan` is an ordered operation containing stops/actions such as:

- required contract pickup;
- required contract delivery;
- optional commodity buy;
- optional sell;
- partial pickup/drop-off;
- current/next leg;
- completion/progress.

Starmap visualizes it. Operations summarizes it. Overlay compresses it. NEXT interprets it. Hauling/Trade contribute to it.

## Module intent snapshot

### Operations

Keep the inherited slogan: **“Everything Live, in one place.”**

Operations is mission control, not another editor. It should show the current operation, NEXT, ship/capacity, route progress, wallet, and relevant alerts with paths into detailed modules.

### Starmap

Keep focused on geography. Add ordered route stops/current-next leg/from-to context from shared RoutePlan. Do not create a second planner inside the map.

### Mission Guides

Currently mostly curated graphical overviews. Later structured guides are reasonable, but this is not on the critical route/NEXT path.

### RS Decoder

Important implementation fact: current RS OCR is local.

The existing path uses Windows `OcrEngine` after local screen capture/preprocessing; it does not send images to SCMDB, SC Miner, UEX, or another web service for OCR.

The scanner timer is nominally 150 ms but recognition is asynchronous and the timer rearms after a pass. Confirmation/stability logic adds additional perceived delay.

The current parser is fundamentally single-value: it returns the first valid 4–6 digit RS-range run. Multi-signature support is Issue #22.

Contained multi-signature fixes can land before the full Vision platform.

### Refinery

Manual entry is valid. Do not block useful refinery state/planning on OCR.

Later refinery OCR belongs on the shared Vision framework (#33) and is tracked by #37.

### Mining Codex

Reference/catalog surface. Live observations/calculations should remain distinct from the catalog.

### Blueprint Library

The user observed that only a subset of blueprints appears to load. Treat catalog completeness as a data-quality issue because goals/NEXT can otherwise operate from incomplete truth. Tracked by #23.

### Network

The inherited Blueprint Sharing feature is not currently a requirement for live real-time crew state. Real-time group sync is optional later work after single-user truth is reliable.

### Cargo Hauling

Evolve from contract display/tracking into a capacity-aware planner. Contract combinations should respect active ship capacity, stop complexity, partial handling, and later cargo-grid/load-order concerns.

### Trade

Keep the three-tab concept:

- **Planner** — multi-hop, operation-aware trade planning;
- **Sell Load** — liquidation of cargo already on hand, including mixed loads;
- **Market** — cached provider catalog with price/stock/status/freshness.

The differentiator is that Trade should optimize around the operation already in progress, not ignore hauling stops.

### Ships

Ships has moved forward in priority because active ship/capacity is foundational.

Initial tabs:

- **Ship Browser**;
- **My Hangar**;
- **Loadout Calculator**.

The first 0.2 implementation should make Browser + My Hangar functional and establish active ship/capacity. Full loadout simulation can deepen later (#38).

## Provider/data notes

### UEX/cache architecture

Continue using normalized provider/domain/cache models. Views must not call UEX directly.

Keep last-known-good data during refresh failure and expose freshness.

### Market fullness/status

The current normalized `CatalogTradePrice` already carries fields for:

- buy stock SCU;
- sell demand SCU;
- buy status;
- sell status;
- container sizes;
- observation time;
- terminal/commodity identity.

Issue #31 should first expose reliable qualitative provider status (empty/low/medium/high/full style states) plus raw quantity/freshness.

Do **not** invent exact capacity percentages without a reliable denominator/source.

### Ship data

Ship Browser is expected to combine normalized sources as needed, potentially including documented community APIs/reference/wiki data.

Provider DTOs must stay behind normalization boundaries. Pledge price/availability, in-game sale/rental data, and ship/reference data can have different source/update cadences.

## NavLink Vision architecture

Future screen readers should follow this structure:

```text
                 NAVLINK VISION
                /       |       \
       TERMINALS     REFINERY    ASOP
            |           |          |
         MARKET      JOB STATE   HANGAR
                \       |       /
                     GAMESTATE
                        |
                       NEXT
```

Issue #33 is the shared foundation.

Expected shared responsibilities:

- Windows screen/window/region capture;
- reusable ROI definitions;
- preprocessing/scaling/contrast utilities;
- local OCR invocation;
- layout/template helpers;
- normalized recognition result containing region/text/confidence/validation;
- saved fixtures;
- capture/annotation/replay developer helper.

Domain readers define field meaning.

### Recommended OCR development pattern

For a new game screen:

1. Build the manual domain workflow/calculation first if possible.
2. Capture representative screenshots/fixtures.
3. Annotate stable regions and expected values.
4. Prefer deterministic layout/ROI + OCR before heavier CV/ML.
5. Add validation/canonical matching.
6. Present uncertain results for review/correction.
7. Only then publish confirmed state into GameState/domain storage.

This is especially important for Refinery, ASOP, and mining screens.

## Mining crackability implementation principle

Issue #36 should not start as an OCR project.

First define/reference the inputs and deterministic formulas/modifiers for rock characteristics + mining equipment/modules/gadgets. Unit-test clearly crackable/marginal/impractical fixtures.

Later Vision can populate those already-tested inputs.

## Immediate issue map

### Foundation closeout

- **#22** — multi-signature RS scanning.
- **#23** — Blueprint Library completeness/source ingestion.
- Existing foundation issues **#3–#7** should be reconciled against merged work/acceptance criteria before declaring 0.1 complete.

### 0.2 Operations & Route Core

- **#24** — epic.
- **#25** — Ships Browser + My Hangar + active ship/capacity.
- **#26** — canonical CargoState/provenance.
- **#27** — shared multi-stop RoutePlan.
- **#28** — Operations/NEXT + Starmap + overlay presentation.
- **#29** — Cargo Hauling capacity/ordering/complexity.
- **#30** — Trade multi-hop planner + Sell Load strategies.
- **#31** — Market availability/fullness/status.

Suggested implementation order:

1. Close/reconcile 0.1 documentation/issues while keeping CI green.
2. #22 and #23 are useful contained closeout tasks.
3. Begin #25 (Ships) and #26 (CargoState).
4. Implement #27 (RoutePlan) against those stable boundaries.
5. Let #29/#30 contribute route actions/stops.
6. Expose the shared result through #28.
7. #31 can land opportunistically because it is comparatively isolated.

### 0.3 Vision

- **#32** — epic.
- **#33** — shared Vision/fixture/annotation framework.
- **#34** — commodity-terminal reader and reviewed local market updates.

### 0.4 Mining & Refinery

- **#35** — epic.
- **#36** — deterministic crackability calculator.
- **#37** — refinery job OCR via Vision.

### Later Ships / R&D

- **#38** — full Loadout Calculator + named builds.
- **#39** — ASOP Vision for hangar location/rental status.
- **#40** — R&D only: 3D viewer / floor plans / screen-assisted interior reconstruction.

## Things Cursor should not do by accident

- Do not branch major new work from stale `main` while `foundation/0.1` is the active integration branch.
- Do not put new provider HTTP calls in WPF views/view models.
- Do not store whole provider catalogs in GameState.
- Do not treat provider DTOs as shared domain models.
- Do not infer that a contract objective means cargo is aboard.
- Do not create a Trade route, Hauling route, and Starmap route independently.
- Do not wait for OCR before implementing a useful manual deterministic calculator/planner.
- Do not create isolated screen-capture/OCR stacks for Refinery/ASOP/terminal screens.
- Do not make AI required for route/NEXT decisions.
- Do not silently transmit OCR observations to UEX/community services.
- Do not copy/port incompatible third-party implementation code.
- Do not cross `SECURITY.md` boundaries to obtain state more conveniently.
- Do not make 3D/interior-reconstruction research block basic Ships or cargo work.

## Good default implementation style

When beginning an issue:

1. Inventory the inherited/current owners of the affected state.
2. Define/adjust UI-independent domain contracts first.
3. Write deterministic tests/fixtures.
4. Add the service/state integration.
5. Migrate one low-risk consumer.
6. Add/update the WPF presentation.
7. Run the full test suite.
8. Update `ARCHITECTURE.md` if implemented architecture changed.
9. Update canonical project/roadmap/decision docs only when the accepted rule/scope changed.
10. Keep the PR focused enough that state-model changes are reviewable separately from large visual/mechanical rewrites.

## R&D boundaries

The following ideas are valid future research but are not prerequisites for core features:

- rotatable 3D ship model viewer;
- curated floor plans;
- automatic interior reconstruction while walking through a ship using visible screen capture;
- advanced visual SLAM/photogrammetry/scene understanding.

If explored, keep them external to the game process and treat them as independent prototypes until feasibility/licensing/performance are understood.
