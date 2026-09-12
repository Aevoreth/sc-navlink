# SC-navLink Decision Record

This file records accepted project decisions that have long-term effects.

A decision entry records context, the accepted choice, and its consequences.

`PROJECT.md` and the other canonical documents remain authoritative for current rules.

## D-001: Use SC-navLink as the project identity

**Date:** 2026-09-11  
**Status:** Accepted

### Context

The project needs a distinct identity from Nexus and other Star Citizen companion tools.

### Decision

Use **SC-navLink** as the product and project name.

Use **sc-navlink** for the repository and package identifier.

Use **NavLink** or **navLink** as internal shorthand when the context is clear.

### Consequences

The rebrand can occur in controlled steps. Inherited Nexus names can remain until a safe migration exists.

Issue #6 tracks the planned product rebrand.

## D-002: Derive the project from Nexus

**Date:** 2026-09-11  
**Status:** Accepted

### Context

Nexus already provides a useful native Windows foundation for several required Star Citizen workflows.

### Decision

Use the MIT-licensed Nexus repository as the project base and preserve its Git history.

Keep required Nexus attribution and the MIT license notices.

### Consequences

SC-navLink can reuse and change inherited Nexus code under the MIT license.

The project can evolve the application without rebuilding every inherited feature from the start.

## D-003: Keep SC-navLink local-first and Windows-native

**Date:** 2026-09-11  
**Status:** Accepted

### Context

The project needs direct Windows OCR, a desktop overlay, local state, and reliable operation during provider outages.

### Decision

Keep the current Windows-native .NET 10 and WPF foundation for the early project phases.

Keep core operational state and cached provider data on the local computer.

### Consequences

The core application remains usable without a required cloud account.

A future platform change needs a separate architecture decision.

## D-004: Use SC-DataRunner only as a behavioral reference

**Date:** 2026-09-11  
**Status:** Accepted

### Context

SC-DataRunner demonstrates useful commodity-terminal OCR and community-data workflows.

Its source is not the implementation base for SC-navLink.

### Decision

Use SC-DataRunner and SC-DataRunnerNet only to understand public behavior and workflow ideas.

Write SC-navLink implementations independently from public API documentation, public formats, original research, observations, and tests.

### Consequences

Do not copy, translate, mechanically port, or incorporate incompatible SC-DataRunner source code.

`ATTRIBUTION.md` and `CONTRIBUTING.md` contain the detailed clean-room policy.

## D-005: Keep the application external to Star Citizen

**Date:** 2026-09-11  
**Status:** Accepted

### Context

SC-navLink needs useful live context without unsafe or anti-cheat-sensitive integration methods.

### Decision

Use external and read-only integration methods.

Allowed methods include `Game.log`, visible-screen capture, OCR, desktop overlays, local files, and documented public or community APIs.

Do not use memory access, code injection, packet manipulation, game-file modification, automated gameplay input, or anti-cheat evasion.

### Consequences

New integration features must fit the boundary in `SECURITY.md` and `PROJECT.md`.

A boundary change needs an explicit project decision before implementation.

## D-006: Introduce one shared GameState before new cross-module features

**Date:** 2026-09-11  
**Status:** Accepted

### Context

Hauling, trading, mining, refining, cargo, blueprints, and `NEXT` need the same operational facts.

Independent state copies would create conflicts and make recommendations unreliable.

### Decision

Create one observable `GameState` model outside WPF views and view models.

Use it for shared session, location, ship, cargo, contract, wallet, mining, refinery, and goal state.

### Consequences

Issue #3 is the next architecture task after the inherited baseline verification.

At least one inherited consumer will migrate first as a proof of the model.

## D-007: Put provider APIs behind a local cache and domain boundary

**Date:** 2026-09-11  
**Status:** Accepted

### Context

SC-navLink needs community market data without excessive requests or fragile direct HTTP dependencies in the UI.

### Decision

Use provider interfaces, normalized domain models, and SQLite-backed local caching.

Views and view models must read provider data through domain or cache services.

Cache entries must expose data freshness when age affects the result.

Keep the last known good data when a provider refresh fails.

### Consequences

Issue #4 implements the first independent UEX provider and cache layer.

Issue #5 builds the first native Market view on that layer.

## D-008: Build NEXT as deterministic logic before AI

**Date:** 2026-09-11  
**Status:** Accepted

### Context

The recommendation engine needs stable tests, clear explanations, and predictable behavior.

### Decision

Build the first `NEXT` model with deterministic rules or scoring.

Represent the recommendation, its reason, its assumptions, and relevant data freshness or confidence.

Do not require an AI model for the core recommendation path.

### Consequences

Issue #7 defines the initial `NEXT` domain model and hauling-only rule set.

A later AI feature can summarize or explain deterministic results.

## D-009: Rebrand in controlled steps

**Date:** 2026-09-11  
**Status:** Accepted

### Context

The inherited application contains many Nexus names, namespaces, assets, paths, and package references.

A large mechanical rename would create unnecessary risk during foundation work.

### Decision

Rebrand user-visible identity and packaging in focused work.

Do not combine bulk namespace changes with unrelated architecture changes.

Preserve compatible local-data migration when names or paths change.

### Consequences

Issue #6 tracks the first rebrand pass.

Inherited technical names can remain during the transition.

## D-010: Define a documentation authority model

**Date:** 2026-09-12  
**Status:** Accepted

### Context

The project needs durable context that remains useful across chats, issues, branches, and future contributors.

### Decision

Use `PROJECT.md` as the canonical product charter and documentation map.

Use specialized documents as the source of truth for their stated topics.

Use GitHub issues for task status and acceptance criteria.

Use this file to record the reasons for high-impact accepted decisions.

### Consequences

A durable rule change must update its canonical document.

A decision entry records history but does not override the current canonical rule.