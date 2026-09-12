# SC-navLink Project Definition

This document defines the product boundary, core project rules, terminology, and documentation authority for SC-navLink.

## Purpose

SC-navLink is a native Windows, local-first operations companion for Star Citizen.

It combines live game state, cached community data, OCR-assisted observations, and deterministic planning in one coherent workflow.

The core product question is:

> **Given my current operation and the best available data, what is the best next action?**

SC-navLink must help answer this question without forcing the player to coordinate many unrelated tools.

## Current phase

SC-navLink is in the **0.1 Foundation** phase.

The `foundation/0.1` branch is the integration branch for this phase.

GitHub issues define current task status and acceptance criteria. This document defines product intent and long-lived project rules.

The next architecture task is Issue #3, which introduces the shared `GameState` model.

## Product goals

SC-navLink has these primary goals:

- Maintain one shared picture of the current player operation.
- Combine hauling, cargo, trading, mining, refining, blueprints, and related economic state.
- Use cached community data without making an online service a hard dependency.
- Give recommendations that account for current location, ship, cargo, contracts, budget, and goals.
- Show the source, age, confidence, or inference status of data when those details affect trust.
- Keep recommendations explainable and testable.
- Preserve useful inherited Nexus behavior during the transition.
- Keep the desktop application useful when external providers are unavailable.

## Primary product scope

The planned core product includes these areas:

- live session and shard state;
- current location and active ship state;
- usable cargo capacity and carried cargo state;
- hauling contracts and combined pickup or delivery planning;
- commodity market and trade data;
- route and trade planning;
- mining observations and value context;
- refinery jobs and value context;
- blueprint and shopping-list goals;
- OCR-assisted terminal observations;
- the `NEXT` recommendation model;
- full desktop views and a concise game overlay.

Later work can add related economic workflows when they support the same shared operation model.

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

`GameState` is the planned canonical model for the current live operation.

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

## Recommendation policy

The first `NEXT` implementations must use deterministic rules or scoring.

Each recommendation must be explainable from its inputs, assumptions, and data freshness when those details affect the result.

An AI model is not required for the core recommendation path.

A later AI feature can explain or summarize deterministic results without becoming the only decision engine.

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
- a major change to project scope.

Update the related canonical document in the same pull request when the accepted decision changes that document.