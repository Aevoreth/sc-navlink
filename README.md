# SC-navLink

**Know your next move.**

SC-navLink is an experimental, native Windows operations companion for **Star Citizen**. It is being developed as a local-first application that combines live game state, hauling contracts, mining and refinery information, blueprint goals, community market data, OCR-assisted observations, and route planning into one coherent workflow.

The long-term goal is not to become a launcher for a collection of unrelated utilities. SC-navLink should maintain one shared picture of the player's current operation and use it to answer a practical question:

> **Given where I am, what I am carrying, what contracts and goals I have, and the best data currently available, what should I do next?**

## Project status

SC-navLink is currently in the **0.1 Foundation** phase.

The repository was initialized from the full Git history of [Nexus](https://github.com/T3SoD/NexusApp), the MIT-licensed Star Citizen companion by T3SoD. The inherited application still contains Nexus names, assets, namespaces, installer metadata, and UX while the SC-navLink foundation is established. Those elements will be migrated deliberately rather than through one large mechanical rename.

The inherited Nexus baseline already provides substantial functionality that SC-navLink can build upon:

- read-only `Game.log` session tracking;
- automatic hauling-contract detection and consolidation;
- Windows-native OCR support for mining RS values;
- an in-game desktop overlay;
- refinery tracking;
- mining/resource reference data;
- blueprint ownership, requirements, and material planning;
- local SQLite-backed state;
- optional cached UEX sell-price data;
- a .NET 10 / WPF Windows desktop foundation and automated tests.

SC-navLink's first work is to preserve that useful foundation while introducing a shared game-state model, cleaner provider boundaries, a fuller locally cached market-data layer, and the first native trade/market workflows.

## Planned operating model

```text
Star Citizen
    |
    +-- Game.log -------------------+
    |                               |
    +-- Screen capture / OCR -------+--> Shared Game State
    |                               |      - location / shard
    |                               |      - ship / capacity
    |                               |      - cargo
    |                               |      - contracts
    |                               |      - wallet
    |                               |      - mining scans
    |                               |      - refinery jobs
    |                               |      - blueprint goals
    |                               |
    +-------------------------------+
                                    |
                  +-----------------+-----------------+
                  |                 |                 |
                 UEX          SC Wiki / data       Local DB
                  |                 |                 |
                  +-----------------+-----------------+
                                    |
                              Planner / NEXT
                                    |
                            Desktop + Overlay
```

External data should enhance SC-navLink rather than become a hard dependency. Game/session state and cached reference data remain local, and stale-but-known data should remain visible with its age clearly marked when an online service is unavailable.

## Major planned areas

### Live operations

SC-navLink will consume read-only game-log events and locally observed state to track the current session, active contracts, cargo movements, purchases/sales where they can be inferred reliably, refinery work, blueprint unlocks, and related economic activity.

### Hauling and cargo

Active hauling contracts should become one consolidated operation: what to collect, how much, where to deliver it, how much capacity remains, and which stop should be next.

### Market and trading

A dedicated data layer will use documented community APIs such as UEX, cache normalized market/reference data locally, expose freshness, and calculate trade opportunities using the player's actual route, ship capacity, budget, cargo, and active obligations.

Rather than only answering "what is the best trade route?", the eventual planner should be able to answer questions such as:

> You are already flying from A to B for these contracts. What profitable cargo can fit in your unused space without disrupting the run?

### Terminal vision

Planned OCR-assisted terminal capture will read visible commodity-terminal information, validate recognized rows against locally cached catalogs, assign confidence, allow review of uncertain values, update the local cache, and optionally submit confirmed observations to a community data provider through its documented API.

No market observation should be silently submitted upstream.

### Mining and refining

The inherited mining tools will be expanded toward contextual scan recognition, value estimation, refinery recommendations, shopping/blueprint relevance, and concise overlay guidance.

### Blueprints and resources

Blueprint requirements should participate in route decisions. If a current goal needs a resource, NavLink should eventually help decide whether it makes more sense to mine it, buy it, collect it along another route, or defer it.

### `NEXT`

`NEXT` is the planned cross-module recommendation layer. It should begin as deterministic, testable scoring/rules rather than an opaque AI dependency.

A future overlay might reduce a complicated session to something like:

```text
SC-navLink // NEXT

PICK UP
32 SCU Titanium [Contract]

OPTIONAL BUY
48 SCU Laranite
Est. profit +27.4k

THEN
Everus Harbor
```

## Game-integration and anti-cheat boundary

SC-navLink is designed to remain fully external to Star Citizen.

The project permits read-only access to files intentionally written by the game, standard Windows screen capture/OCR, ordinary desktop overlays, and public/community APIs.

The project does **not** intend to use game-process memory reading/writing, DLL injection, packet manipulation, game-file modification, automated gameplay input, or anti-cheat evasion mechanisms.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development boundary.

## Clean-room policy

SC-navLink may study other Star Citizen tools to understand public behavior and useful workflows, but incompatible source code is not copied, translated, mechanically ported, or incorporated.

In particular, SC-DataRunner / SC-DataRunnerNet may serve as a behavioral reference for commodity-terminal OCR and community-data submission workflows. SC-navLink's implementation of those ideas must be independently written from public API documentation, public data formats, original research, and our own tests.

See [ATTRIBUTION.md](ATTRIBUTION.md) for details.

## Roadmap

The current roadmap is maintained in [docs/ROADMAP.md](docs/ROADMAP.md).

Current sequence:

1. **0.1 — Foundation:** project identity, build/test baseline, shared game state, provider boundaries, local UEX cache, native Market view.
2. **0.2 — Trade & Route Planner:** combine contracts, capacity, budget, market data, and existing routes.
3. **0.3 — Terminal Vision:** capture/OCR/validation/local update/optional UEX submission.
4. **0.4 — Mining Vision:** richer scan recognition and contextual value/resource guidance.
5. **0.5 — NavLink NEXT:** cross-module recommendation engine and concise operational overlay.
6. **0.6+ — Operations Refinement:** refinery optimization, richer blueprint acquisition planning, cargo-grid awareness, history, and additional economic workflows.

## Development

The inherited technical baseline is:

- C# / .NET 10;
- WPF, Windows-only;
- CommunityToolkit.Mvvm;
- Microsoft.Data.Sqlite;
- Windows.Media.Ocr / WinRT OCR;
- xUnit tests;
- self-contained `win-x64` distribution.

During the early transition:

- `main` remains the stable project baseline;
- `foundation/0.1` is the integration branch for the first milestone;
- focused changes should be developed on short-lived branches and reviewed before integration where practical.

The first coding priority is to verify the inherited build/tests, then introduce the shared state and provider/cache architecture without breaking existing Nexus functionality.

## Naming

- Product/project: **SC-navLink**
- Repository/package identifier: **sc-navlink**
- Internal shorthand where context is clear: **NavLink** or **navLink**

## Attribution and license

SC-navLink is derived from [Nexus](https://github.com/T3SoD/NexusApp) and preserves its Git history and MIT attribution. See [ATTRIBUTION.md](ATTRIBUTION.md) and [LICENSE](LICENSE).

The original Nexus copyright and MIT permission notice must remain with copies or substantial portions of the inherited software.

## Disclaimer

SC-navLink is an unofficial fan-made project and is not affiliated with, endorsed by, sponsored by, or otherwise connected to Cloud Imperium Games or Roberts Space Industries.

Star Citizen, Roberts Space Industries, Cloud Imperium, and related names and marks are the property of their respective owners.
