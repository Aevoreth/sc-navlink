# SC-navLink Project Definition

This document defines the product boundary, core project rules, terminology, and documentation authority for SC-navLink.

## Purpose

SC-navLink is a native Windows, local-first operations companion for Star Citizen.

It combines live game state, cached community/reference data, OCR-assisted observations, and deterministic planning in one coherent workflow.

The core product question is:

> **Given my current operation and the best available data, what is the best next action?**

SC-navLink should reduce the need to coordinate separate hauling, trading, mining, refinery, cargo, blueprint, ship, and routing tools manually.

## Current phase

SC-navLink is completing the **0.1 Foundation** phase and preparing the **0.2 Operations & Route Core** phase.

The `foundation/0.1` branch remains the integration branch until the foundation is formally closed out.

GitHub issues define current task status and acceptance criteria. `docs/ROADMAP.md` defines milestone sequencing. This document defines product intent and long-lived project rules.

The immediate planning focus after foundation closeout is to connect **Ships -> active ship/capacity -> CargoState -> RoutePlan -> NEXT -> Operations/Starmap/Overlay**.

## Product model

SC-navLink is best understood as three cooperating layers.

### Operational truth

The application maintains the best available picture of the current operation, including facts such as:

- session/shard;
- current/last-known location;
- active ship and usable cargo capacity;
- wallet/budget when known;
- hauling contracts and objectives;
- carried cargo;
- mining observations;
- refinery jobs;
- blueprint/material goals;
- operation/route state.

### Planning

Deterministic planners interpret operational truth and cached reference/provider data to answer questions such as:

- which contract combinations fit the active ship;
- which pickup/drop-off order is practical;
- which commodity trade fits the planned route;
- where a mixed cargo load should be sold;
- whether a rock is practical to crack with the current mining setup;
- whether a refinery/blueprint/material action should be prioritized.

### Presentation

The same shared operation is presented at different levels of detail:

- **Operations** — mission-control summary: “Everything Live, in one place.”
- **Starmap** — geographic/route visualization;
- **module views** — detailed editors, browsers, and calculators;
- **overlay** — concise immediate guidance while playing.

Presentation surfaces must not become competing owners of operational truth.

## Core module intent

The existing and planned navigation modules have these intended roles.

### Operations

Operations is the high-level live dashboard for the current session.

It should summarize, rather than duplicate, detailed module workflows. Its central responsibilities include:

- primary `NEXT` action;
- current and next location;
- active ship and cargo utilization;
- active hauling/trade route summary;
- wallet/budget when known;
- relevant mining/refinery/goal alerts;
- immediate follow-up actions.

### Starmap

Starmap visualizes the player's current/last-known location and the shared route.

It can show current-to-next legs, ordered stops, and compact from/to context, but should not maintain an independent route-plan truth.

### Mission Guides

Mission Guides currently provide curated graphical/location guidance.

A later structured version can add prerequisites, stages, maps/screenshots, and tips for supported missions. This is useful but not on the critical planning path.

### RS Decoder

RS Decoder is a local screen/OCR mining-signature tool.

Its OCR path should remain local. It should support multiple simultaneous scan signatures rather than collapsing a scan region to one value.

Later mining work can add probable resource/node matches, confidence, crackability/value context, and goal relevance.

### Refinery

Refinery tracks refinery jobs and related economic context.

Manual entry remains valid. Later OCR should be implemented through the shared NavLink Vision platform, not through a completely separate screen-reader stack.

### Mining Codex

Mining Codex is the reference/catalog surface for mineable materials and relevant characteristics.

It can later incorporate crackability/value/equipment context while remaining distinct from live observations.

### Blueprint Library

Blueprint Library tracks known/needed blueprints and material goals.

Its source data must be complete enough that goal-aware recommendations do not operate from a silently incomplete catalog.

### Network

Network/Blueprint Sharing is currently a blueprint-exchange workflow, not a requirement for live multi-user synchronization.

Real-time crew/group synchronization can be considered later after single-user state is reliable.

### Cargo Hauling

Cargo Hauling manages contracts/objectives and evolves into an operational planner that understands:

- ship capacity;
- combined contracts;
- ordered pickups/drop-offs;
- complexity limits;
- partial handling where required;
- container/cargo-grid planning.

Contract obligations are not the same thing as confirmed carried cargo.

### Trade

Trade retains three conceptual workflows:

- **Planner** — multi-hop commodity/operation planning;
- **Sell Load** — intelligent liquidation of cargo already on hand;
- **Market** — local cached market catalog with source/freshness/availability context.

Trade planning should be able to optimize around an operation that already exists rather than ignoring hauling stops and route intent.

### Ships

Ships is a planned core module, not merely a later reference browser.

Its initial tabs are:

- **Ship Browser** — ship catalog/reference, flyable/concept filtering, role filters, cargo/price/location data;
- **My Hangar** — pledged, in-game purchased, and rented ships, including active-ship selection and rental/location state;
- **Loadout Calculator** — component simulation and named saved loadouts, with deeper implementation allowed to land after the initial Ships foundation.

The Ships module provides the canonical user-facing path for active ship and usable-capacity state.

## Product goals

SC-navLink has these primary goals:

- Maintain one shared picture of the current player operation.
- Combine hauling, cargo, trading, mining, refining, blueprints, ships, and related economic state.
- Use cached community/reference data without making an online service a hard dependency.
- Give recommendations that account for current location, ship, cargo, contracts, budget, route, and goals.
- Show the source, age, confidence, or inference status of data when those details affect trust.
- Keep recommendations explainable and testable.
- Preserve useful inherited Nexus behavior during the transition.
- Keep the desktop application useful when external providers are unavailable.
- Build useful manual planners/calculators before requiring OCR automation.
- Reuse screen-understanding infrastructure across terminal, refinery, ASOP, and mining workflows.

## Primary product scope

The planned core product includes these areas:

- live session and shard state;
- current location and active ship state;
- ship catalog and user hangar state;
- usable cargo capacity and carried cargo state;
- cargo-grid/container planning where reliable data exists;
- hauling contracts and combined pickup/delivery planning;
- commodity market and trade data;
- multi-stop route and trade planning;
- mixed-load liquidation planning;
- mining observations, crackability, and value context;
- refinery jobs and value context;
- blueprint and shopping-list goals;
- OCR/computer-vision-assisted observations;
- the `NEXT` recommendation model;
- Operations and Starmap presentation;
- concise game overlay guidance.

Later work can add related economic workflows when they support the same shared operation model.

## Experimental / R&D scope

Some ideas fit the project but are intentionally separated from the critical roadmap because they are higher-risk research tasks.

Examples include:

- rotatable 3D ship models when usable/legal assets exist;
- curated ship floor plans;
- automatic ship-interior/floor-plan reconstruction from game screen capture;
- advanced visual scene reconstruction beyond deterministic screen-layout/OCR techniques.

These features can be explored under an R&D/NavLink Labs track without blocking core Ships, cargo, route, or NEXT development.

## Explicit non-goals

SC-navLink does not have these goals:

- automate gameplay input;
- read or write Star Citizen process memory;
- inject code into Star Citizen;
- intercept or change game network traffic;
- modify Star Citizen game files;
- evade Easy Anti-Cheat or another anti-cheat system;
- require a cloud account for core local use;
- depend on an opaque AI model for core planning;
- copy incompatible third-party source code;
- silently submit observations to a community data service;
- become a launcher for unrelated Star Citizen utilities.

## Shared state and data truth

`GameState` is the canonical model for the current live operation as domains are migrated into it.

Views must not become independent owners of live operational state.

Services can publish state changes through the shared state layer. Consumers can observe the same state without keeping conflicting copies.

Operational data can include provenance when the source affects trust.

Useful provenance classes include:

- direct game-log observation;
- user-confirmed value;
- OCR observation;
- inferred value;
- cached provider value;
- bundled reference value.

OCR-derived data can include a confidence value. Cached provider data can include fetch or observation timestamps.

A recommendation is an interpretation of known state. A recommendation is not a confirmed game-state fact.

Inferred state must remain distinguishable from confirmed state when that difference affects a player decision.

External market data must not erase the last known good cache when a refresh fails.

## Cargo truth model

SC-navLink must not collapse three different cargo concepts into one model:

1. **Cargo obligations** — contract/objective requirements.
2. **Actual/inferred cargo state** — what is believed to be aboard the active ship.
3. **Cargo placement plan** — where containers should be placed on a cargo grid.

A contract objective is not evidence by itself that cargo is physically aboard the ship.

Manual confirmation, OCR, and deterministic inference can all contribute to cargo state, but provenance must remain visible to the planning layer.

## Route truth model

The application should converge on one shared ordered `RoutePlan` for the current operation.

A route can contain contract pickups/drop-offs, trade buys/sells, optional stops, and other operational actions.

Operations, Starmap, Hauling, Trade, `NEXT`, and the overlay should consume the same route rather than maintain incompatible route copies.

## NEXT policy

`NEXT` is a continuous coordination layer, not a one-time milestone feature.

Its domain contract should remain stable enough that new modules can contribute candidate actions without forcing presentation surfaces to be redesigned for every new gameplay domain.

The first implementations use deterministic rules or scoring.

Each recommendation must be explainable from its inputs, assumptions, and relevant data freshness/confidence.

An AI model is not required for the core recommendation path.

A later AI feature can explain or summarize deterministic results without becoming the only decision engine.

## Vision / OCR policy

Screen-derived features should use a shared **NavLink Vision** foundation where practical.

The shared foundation can provide:

- Windows capture;
- reusable regions of interest;
- preprocessing;
- OCR;
- layout/template detection;
- confidence/validation;
- fixture capture and replay;
- developer annotation/debug tooling.

Individual domain readers can then define what fields mean on a commodity terminal, refinery screen, ASOP terminal, or mining screen.

OCR itself should remain local unless a future feature explicitly documents and asks for an external processing service.

Recognized values must be reviewable when confidence is insufficient for safe automatic use.

## Architecture guardrails

The inherited technical foundation uses C#, .NET 10, WPF, CommunityToolkit.Mvvm, SQLite, Windows OCR, and xUnit.

The project can change this stack only through a deliberate architecture decision.

Business rules must remain outside WPF views when practical. State transitions, parsing, scoring, caching, and planning must remain unit-testable.

UI code must read provider data through domain or cache services. A view must not make direct provider HTTP calls.

External providers must use clear interfaces. Provider-specific response types must not become the shared product domain model.

Network refreshes must use caching, request coalescing, and provider-aware rate limits where applicable.

Cached data must expose freshness when age can affect a player decision.

SC-navLink must remain external to Star Citizen. `SECURITY.md` defines the detailed security and integration boundary.

## External source policy

SC-navLink is derived from the MIT-licensed Nexus project. The repository preserves the inherited history and required attribution.

The project can study another application to understand public behavior or workflow.

The project must not copy, translate, mechanically port, or incorporate incompatible source code.

SC-DataRunner and SC-DataRunnerNet are behavioral references only.

Features inspired by those tools must use public documentation, public formats, independent observation, original research, and SC-navLink tests.

Public or community APIs must be accessed through their documented interfaces.

## Naming

Use these names consistently:

- Product and project: **SC-navLink**
- Repository and package identifier: **sc-navlink**
- Internal shorthand: **NavLink** or **navLink** when the context is clear

Inherited Nexus names can remain temporarily where a safe migration has not occurred.

## Documentation authority

Use each document as the source of truth for its stated area:

- `PROJECT.md`: product purpose, scope, core rules, naming, and documentation authority.
- `CONTRIBUTING.md`: contributor workflow and engineering contribution rules.
- `SECURITY.md`: security controls and the game-integration boundary.
- `ARCHITECTURE.md`: the architecture that the current code implements.
- `docs/ROADMAP.md`: milestone goals and planned sequence.
- `docs/DECISIONS.md`: accepted high-impact project decisions and their reasons.
- `docs/CURSOR-HANDOFF.md`: development-context summary for continuing implementation in Cursor; it is supplemental and must defer to canonical documents and code when they differ.
- GitHub issues: task status, task scope, and acceptance criteria.
- GitHub pull requests: reviewed implementation changes and discussion.
- Automated tests: executable expectations for implemented behavior.
- The code: the final source of truth for behavior that is currently implemented.

`docs/DECISIONS.md` records why a decision exists. It does not override a current canonical document.

If documents conflict, use the document with authority for that topic. Correct the stale document in the same work when practical.

A proposed change is not a project rule only because an issue or discussion contains it.

A durable change to a core rule must update the applicable canonical document.

## Change control

Record a decision when a change affects a long-lived project boundary or architecture rule.

Examples include:

- a change to the game-integration boundary;
- a change to the clean-room policy;
- a change to the primary application stack;
- a change to the shared-state model;
- a change to provider or caching policy;
- a change to the core recommendation model;
- a major change to project scope;
- a change to shared route/cargo truth boundaries;
- a decision to create a reusable cross-domain Vision platform.

Update the related canonical document in the same pull request when the accepted decision changes that document.
