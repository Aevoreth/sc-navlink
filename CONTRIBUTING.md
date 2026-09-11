# Contributing to SC-navLink

SC-navLink is a Windows-native, local-first Star Citizen operations companion. The project is derived from Nexus and is licensed under the MIT License.

## Project goals

SC-navLink should help a player answer one practical question: **what should I do next?**

The application is intended to combine local game state, public/community data, OCR-assisted observations, and deterministic planning into one coherent workflow for hauling, trading, mining, refining, blueprints, cargo, and related economic gameplay.

## Safety and game-integration boundary

SC-navLink must remain external to Star Citizen.

Allowed approaches include:

- read-only access to `Game.log` and other plain-text game files that are intentionally written to disk;
- standard Windows screen capture;
- OCR and computer-vision analysis of pixels visible to the player;
- global hotkeys and ordinary desktop UI/overlay behavior;
- public/community APIs and locally cached reference data.

Do not introduce:

- game-process memory reading or writing;
- DLL/code injection;
- packet interception or manipulation;
- modification of Star Citizen game files;
- automated game input intended to play the game for the user;
- mechanisms designed to evade Easy Anti-Cheat or other anti-cheat systems.

## Licensing and clean-room development

The inherited Nexus code is MIT-licensed and its original attribution must be preserved.

Do not copy, translate, mechanically port, or incorporate source from projects whose licenses are incompatible with SC-navLink's MIT-licensed codebase.

SC-DataRunner / SC-DataRunnerNet may be studied as a behavioral reference only. Features inspired by it must be implemented independently from public UEX documentation, public data formats, independently observed application behavior, and SC-navLink's own research and tests.

If a contribution is substantially informed by an external specification or API, document that source in code comments or project documentation when useful.

## Architecture principles

- **Local first:** the app should remain useful when external services are unavailable.
- **One shared state:** modules should consume a common model of current location, ship, cargo, contracts, wallet, mining/refinery state, and goals rather than inventing separate copies.
- **Cache external data:** UI interactions should primarily query local storage. Network refreshes should be deliberate, rate-limited, and respect provider guidance.
- **Show freshness:** community market data must display age/freshness instead of implying guaranteed accuracy.
- **Deterministic before AI:** route and recommendation features should begin with transparent scoring/rules that can be tested and explained.
- **Confidence-aware OCR:** uncertain OCR results should be reviewed rather than silently treated as truth or submitted upstream.
- **No silent external writes:** submissions to community services such as UEX should be explicit and reviewable by the user.
- **Keep business logic out of WPF views:** planning, pricing, route scoring, parsing, and state transitions should be unit-testable independently of the UI.

## Branch and pull-request workflow

During early development:

- `main` remains the stable inherited/project baseline;
- `foundation/0.1` is the integration branch for the first SC-navLink milestone;
- focused work should use short-lived branches and merge through pull requests where practical.

Prefer small commits with descriptive messages. Avoid combining large namespace/file moves with unrelated behavioral changes.

## Testing

Changes to parsers, calculations, caching, route planning, state inference, or recommendation logic should include or update automated tests.

For OCR-related features, keep representative fixtures/screenshots where licensing and privacy allow, and test confidence/error handling as well as successful recognition.

## Data and privacy

SC-navLink should avoid collecting personal information unless a feature explicitly requires it. Local session data should remain local by default. Any network transmission should be visible, documented, and limited to what the selected feature requires.

## Naming

- Product/project name: **SC-navLink**
- Repository/package identifier: **sc-navlink**
- Internal shorthand where context is clear: **NavLink** or **navLink**
