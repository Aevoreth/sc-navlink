# SC-navLink Roadmap

SC-navLink is a native, local-first Star Citizen operations companion that combines game-log state, OCR-assisted observations, cached community/reference data, and deterministic planning into one coherent application.

The product is organized around one operational question:

> **Given my current operation and the best available data, what should I do next?**

`NEXT` is therefore not a single late-stage feature. It is a coordination layer that becomes more capable as additional state, planners, and observation sources are added.

The roadmap favors useful, testable domain logic before automation. Manual input is acceptable when it allows a planner or calculator to become correct before OCR or inference is added.

## Product flow

The planned core dependency chain is:

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

Screen-reading features should converge on a shared Vision foundation:

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

## 0.1 — Foundation closeout

Goal: finish the independent SC-navLink foundation and remove remaining ambiguity before expanding the operational model.

Completed or substantially established during the foundation phase:

- preserved Nexus history and MIT attribution;
- SC-navLink project identity, documentation authority, contribution policy, and security boundary;
- verified Windows build, test, publish, and smoke-start baseline;
- shared `GameState` architecture for live operational domains;
- provider abstractions and SQLite-backed UEX cache with freshness metadata and last-known-good behavior;
- native Trade Market browser backed by normalized cached provider data;
- initial UI-agnostic deterministic `NEXT` action contract and hauling rule set;
- controlled rebrand work without destructive namespace churn.

Remaining closeout work should include:

- reconcile/close the original 0.1 architecture issues once their accepted work is confirmed on `foundation/0.1`;
- audit Blueprint Library completeness and determine why only a subset of known blueprints is currently represented;
- improve RS Decoder handling for multiple simultaneous scan signatures;
- keep architecture/project documentation synchronized with the implemented foundation;
- preserve all inherited behavior that has not yet been deliberately replaced.

Exit criteria:

- foundation issues accurately reflect completed vs. deferred work;
- current planning documents no longer identify already-completed tasks as the next task;
- RS Decoder can represent multiple simultaneous signatures without collapsing them into one value;
- blueprint/reference-data gaps are understood and tracked;
- the foundation remains green in CI.

## 0.2 — Operations & Route Core

Goal: make SC-navLink useful during a complete hauling/trading session by connecting ship, capacity, cargo, route, planners, and the first visible operational `NEXT` experience.

### Ships foundation

Add a **Ships** module with these initial tabs:

- **Ship Browser** — searchable/filterable ship reference catalog;
- **My Hangar** — user-maintained pledged, in-game purchased, and rented ships;
- **Loadout Calculator** — reserved in the module structure but full component simulation can land later.

Initial Ship Browser data should support, where reliable data is available:

- manufacturer, model, description, role/class, flyable/concept status;
- cargo capacity and relevant cargo-grid metadata;
- crew and physical/reference statistics;
- current pledge price and pledge availability;
- in-game purchase locations/prices;
- rental locations/prices.

Concept/non-flyable ships should be hidden by default with filters to include them.

My Hangar should establish canonical user-owned ship state, including:

- acquisition type: pledged, purchased in-game, or rented;
- rental expiry when applicable;
- last known/current location when known;
- active ship selection;
- provenance for observed/confirmed ship/location values.

The active ship and its usable cargo capacity become shared operational state consumed by hauling, trade, cargo planning, and `NEXT`.

### Cargo state

Introduce a canonical cargo model that deliberately separates:

1. **cargo obligations** — what contracts require the player to carry/deliver;
2. **actual/inferred cargo** — what NavLink believes is currently aboard;
3. **cargo placement plan** — where containers should be placed on a ship grid.

Cargo state must support provenance and correction because some values will initially be manual or inferred.

### Shared multi-stop RoutePlan

Replace single-hop planning assumptions with one ordered multi-stop route model capable of representing:

- pickup stops;
- delivery stops;
- commodity buy/sell stops;
- optional stops;
- partial pickups/drop-offs;
- route legs and current/next leg;
- reasons/actions associated with each stop.

The route is shared. Individual modules should not own conflicting route copies.

### Operations

Evolve **Operations — “Everything Live, in one place.”** into the mission-control view for the current session.

It should summarize rather than duplicate detailed editors. Initial useful cards include:

- primary `NEXT` action;
- current location and next destination;
- active ship and used/free cargo capacity;
- active hauling/trade operation summary;
- wallet/budget when known;
- relevant refinery/mining/goal alerts;
- route progress and immediate follow-up action.

### Starmap

Keep Starmap focused on geography and navigation. It should visualize the shared `RoutePlan`, including:

- current/last-known location;
- planned stops;
- highlighted current-to-next route leg;
- compact current/from-to/next information.

Starmap should not create an independent route-planning truth.

### Cargo Hauling

Expand inherited hauling support toward operational planning:

- reject or warn on contract combinations that exceed usable ship capacity;
- combine contracts sharing origins/destinations;
- order pickups/drop-offs;
- support configurable complexity;
- distinguish direct hauls from multi-contract/multi-stop plans;
- support partial pickup/drop-off workflows where needed;
- establish cargo-grid/container planning groundwork.

Suggested complexity levels:

- **Simple** — direct A -> B, no split stops;
- **Moderate** — multiple compatible contracts sharing origins/destinations;
- **Complex** — mixed pickup/drop-off routes;
- **Advanced** — partial handling plus optional commodity trading.

### Trade

Retain the existing Trade organization while expanding each workflow.

**Planner** should become a real multi-hop planner that can consider:

- starting point and desired destinations;
- active ship/capacity;
- wallet/investment constraints;
- stock/demand and data freshness;
- container compatibility;
- expected profit, ROI, profit/SCU, and estimated route cost/time;
- already-planned hauling stops and route deviation.

**Sell Load** should answer: **“I have this cargo; how should I liquidate it?”**

It should support mixed loads and strategies such as:

- maximum return;
- fastest liquidation;
- fewest stops;
- best one-stop sale;
- balanced;
- best sale along the current route.

Manual cargo entry is acceptable before automatic inventory observation exists.

**Market** should retain current buy/sell/stock information and add availability/fullness presentation when provider data supports it. Qualitative provider status such as empty/low/medium/high/full can be shown even when a reliable absolute station-cap denominator is unavailable.

### Initial visible NEXT

Expose the already-established deterministic `NEXT` contract in Operations and the overlay during this phase.

The first visible experience can remain hauling/route oriented. Later phases add more candidate action types without changing the presentation contract unnecessarily.

Exit criteria:

- the player can define/confirm an active ship and usable cargo capacity;
- canonical cargo state exists independently from contract obligations;
- one shared ordered route can contain multiple operational stops;
- Hauling and Trade can contribute to that route;
- Operations displays the current operation and a useful deterministic next action;
- Starmap and overlay reflect the same route/NEXT state.

## 0.3 — NavLink Vision

Goal: create one reusable local screen-understanding platform instead of implementing unrelated OCR pipelines for each game screen.

Planned foundation:

- Windows screen capture with explicit capture scopes;
- reusable regions of interest (ROI);
- layout/template recognition where practical;
- OCR preprocessing and normalization;
- per-field confidence and validation;
- fixture-based regression testing across resolutions/UI scales;
- a development/helper workflow for capturing screenshots, annotating regions, recording expected values, and replaying recognition locally;
- no network dependency for OCR itself.

The first major consumer should be the **commodity terminal** because it exercises row detection, canonical matching, confidence review, and local provider-cache updates.

Commodity-terminal Vision should support:

- commodity rows;
- buy/sell prices;
- visible stock/demand/quantity/status fields;
- canonical matching against cached provider/reference catalogs;
- side-by-side review of uncertain values;
- immediate local cache updates after confirmation;
- optional explicit UEX submission through documented APIs;
- never silently submit low-confidence or unreviewed values.

This Vision foundation is also intended for later Refinery and ASOP readers.

## 0.4 — Mining & Refinery Intelligence

Goal: turn mining/refining features into deterministic operational tools, then use NavLink Vision to reduce manual entry.

### Mining

Build a deterministic **crackability calculator** before automating its inputs.

Manual inputs can include:

- rock mass;
- resistance;
- instability;
- composition;
- active mining ship/head/modules;
- gadgets/consumables.

Outputs should include:

- crackable / marginal / unlikely assessment;
- useful power-margin/context calculations where supported;
- recommended module/gadget changes;
- value and goal context from cached market/blueprint data.

Then expand mining Vision to populate reliable visible inputs automatically.

The inherited RS Decoder should evolve into a contextual assistant capable of representing multiple simultaneous signatures, probable matches, and confidence.

### Refinery

Keep manual refinery entry available while adding OCR through the shared Vision infrastructure.

Refinery recognition may identify, where reliably visible:

- location/refinery;
- work-order materials;
- refining method;
- yield/quantity;
- cost;
- completion time/state.

Recognized values must be reviewable/correctable and should enter shared refinery state rather than becoming UI-only data.

Exit criteria:

- crackability calculations are deterministic and unit tested without OCR;
- mining observations can contribute useful value/goal context;
- refinery Vision reuses the common capture/ROI/fixture infrastructure;
- recognized mining/refinery state can contribute candidates or constraints to `NEXT`.

## 0.5 — Cross-domain NEXT intelligence

Goal: deepen the recommendation engine after the operational state and planners are proven.

Candidate inputs include:

- current location and active ship;
- usable cargo capacity;
- wallet/reserved cash;
- active hauling contracts/objectives;
- confirmed/inferred carried cargo;
- shared route plan;
- refinery jobs;
- mining observations and crackability/value context;
- blueprint/shopping-list goals;
- cached commodity prices, stock/demand/status, and freshness;
- user preferences, exclusions, and complexity/risk constraints.

Candidate outputs include:

- next destination/action;
- contract cargo to collect/deliver;
- optional commodity purchase that fits remaining capacity;
- recommended liquidation path for carried goods;
- whether a mining target contributes to current goals;
- whether buying a required material is more efficient than mining it;
- refinery collection/sale actions;
- alternative route strategies;
- expected run value and key assumptions.

Recommendations remain deterministic, explainable, and testable. AI can later summarize/explain results but must not be the only core decision engine.

## 0.6 — Ships & Operations refinement

Goal: deepen mature workflows after the shared operational loop is stable.

Potential work:

- full ship **Loadout Calculator** with component compatibility and performance calculations;
- multiple named saved loadouts per owned ship;
- ASOP-terminal Vision for ship location/status and rental timers when reliable;
- deeper cargo-grid/container placement and load ordering;
- earnings/run/session history;
- operation profiles such as hauling, trading, mining, salvage, or mixed runs;
- richer Mission Guides with structured stages, maps/screenshots, prerequisites, and tips;
- richer blueprint/material acquisition planning;
- import/export/backup of local user state;
- optional crew/group/network improvements after single-user state is solid;
- optional local-network/mobile companion after desktop workflows are mature.

## R&D / NavLink Labs

These ideas are valuable but must not block the critical operational roadmap:

- rotatable 3D ship viewer when usable/legal model assets are available;
- curated ship floor plans;
- experimental game-screen-assisted ship interior reconstruction/floor-plan generation;
- advanced visual scene understanding beyond deterministic layout/OCR techniques;
- other experimental mapping or spatial reconstruction tools.

Automated interior reconstruction is expected to be a separate research effort, not a dependency for the Ships module.

## Long-term principles

- New work should strengthen shared state, planning, observation, or presentation of the current operation.
- Do not make every module its own source of truth.
- Build calculations/planners correctly before automating their inputs.
- Prefer reusable Vision infrastructure over one-off OCR pipelines.
- Keep core use local-first and useful during provider outages.
- Preserve provenance, freshness, and confidence when they affect trust.
- Do not automate gameplay input or cross the security boundary defined in `SECURITY.md`.
- Treat experimental 3D/AR-style ideas as R&D until the operational core is mature.
