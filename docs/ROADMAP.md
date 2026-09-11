# SC-navLink Roadmap

SC-navLink is intended to become a native, local-first Star Citizen operations companion that combines game-log state, OCR-assisted observations, community data, and route/recommendation logic into one coherent application.

The roadmap intentionally prioritizes a useful, stable foundation before advanced OCR and recommendation features.

## 0.1 — Foundation

Goal: establish SC-navLink as an independent Nexus-derived project with a clean architecture and a first native market-data workflow.

Planned work:

- preserve Nexus history and MIT attribution;
- establish SC-navLink project identity, documentation, contribution policy, and release conventions;
- verify the inherited application builds and tests cleanly before functional changes;
- introduce a shared observable `GameState` for session/location/ship/cargo/contracts/wallet/refinery/mining/blueprint context;
- define provider abstractions for market data, game/reference data, wiki data, and optional submissions;
- implement a dedicated UEX client using public API documentation;
- add SQLite-backed caching with provider-specific freshness/TTL rules;
- expose data age and stale/offline state in the UI;
- create the first SC-navLink-native Market view for commodity/location/price browsing;
- create an initial `NEXT` model that can at least consume current location and known hauling stops;
- retain inherited Nexus functionality during the transition wherever practical.

Exit criteria:

- a clean Windows build is produced from SC-navLink;
- inherited tests are green or any known inherited failures are explicitly documented;
- UEX market data can be refreshed, cached, queried locally, and viewed natively;
- the app remains usable when UEX is unavailable by falling back to cached data;
- no new functionality depends on copying incompatible third-party source code.

## 0.2 — Trade & Route Planner

Goal: combine active gameplay state with market opportunities instead of functioning as a standalone trade calculator.

Planned work:

- represent current ship, usable cargo capacity, wallet/budget, current location, carried cargo, and active hauling obligations;
- calculate buy/sell opportunities from cached market data;
- score routes by expected profit, ROI, profit/SCU, estimated time, required capital, stock/demand, container compatibility, and route deviation;
- combine optional commodity trades with already-planned contract pickup/drop-off stops;
- show why a recommendation was chosen;
- support manual constraints such as reserve cash, max investment, excluded commodities, preferred systems, and desired risk level;
- begin a compact overlay presentation for the next recommended stop/action.

## 0.3 — Terminal Vision

Goal: turn commodity-terminal observations into locally useful market data and optional community submissions.

Planned work:

- Windows screen-capture service with explicit capture scopes;
- terminal/layout detection;
- OCR for commodity rows, buy/sell prices, stock/demand, quantities, and related visible fields;
- canonical matching against cached UEX/reference catalogs;
- per-field confidence scores and validation rules;
- side-by-side review of uncertain values;
- update SC-navLink's local market cache immediately from confirmed observations;
- optional, explicit submission to UEX using its documented data-submission API;
- never submit low-confidence/unreviewed data silently;
- diagnostics and fixtures for OCR regressions across resolutions/UI scales.

SC-DataRunner may inform desired behavior and workflow only; implementation remains independent and clean-room as described in `ATTRIBUTION.md` and `CONTRIBUTING.md`.

## 0.4 — Mining Vision

Goal: evolve inherited RS-value recognition into a contextual mining assistant.

Planned work:

- improve scan-region capture and detection resilience;
- recognize RS signature plus additional visible mining metrics where reliable;
- identify probable resources/node counts and confidence;
- estimate raw/refined value using cached market data;
- surface relevant shopping-list/blueprint materials;
- recommend whether a rock is worth investigating based on the player's goals;
- integrate refinery bonuses/yields and expected downstream value;
- present concise mining guidance in the overlay.

## 0.5 — NavLink NEXT

Goal: create the cross-module recommendation engine that answers **what should I do next?**

Inputs may include:

- current location and ship;
- usable cargo capacity;
- wallet/reserved cash;
- active contracts and objectives;
- inferred/confirmed cargo state;
- refinery jobs;
- current mining observations;
- blueprint/shopping-list goals;
- cached commodity prices, stock/demand, and data age;
- user preferences and exclusions.

Outputs should be deterministic, explainable recommendations such as:

- next destination;
- contract cargo to collect/deliver;
- optional commodity purchases that fit remaining capacity;
- where to sell carried goods;
- whether a mining target contributes to current goals;
- whether buying a material is more efficient than mining it;
- expected run value and key assumptions.

The first recommendation engine should use transparent rules/scoring, not an opaque AI dependency.

## 0.6 — Operations Refinement

Potential work after the core loop is proven:

- refinery collection and sale optimization;
- richer blueprint/material acquisition planning;
- cargo-grid/container-size awareness and load ordering;
- earnings/run/session history;
- configurable operation profiles (hauling, trading, mining, mixed run);
- better stale-data/confidence visualization;
- import/export/backup of local state;
- additional economic gameplay support where data permits, including salvage-related planning;
- optional local-network/mobile companion after the desktop workflow is mature.

## Long-term principles

The roadmap is not a promise that every possible feature belongs in SC-navLink. New work should reinforce the core experience rather than turning the application into a launcher for unrelated utilities.

A feature is a particularly good fit when it can contribute to the shared game state or improve the quality of a contextual recommendation.
