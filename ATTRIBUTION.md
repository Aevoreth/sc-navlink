# Attribution

SC-navLink is an independent, unofficial Star Citizen companion application.

## Nexus

SC-navLink is derived from the open-source Nexus companion application by T3SoD. The SC-navLink repository was initialized from the Nexus Git history so that the original authorship and development history remain intact.

Original project: https://github.com/T3SoD/NexusApp

Nexus is licensed under the MIT License. Its original copyright and permission notice remain in `LICENSE` and must be retained in copies or substantial portions of the inherited software.

SC-navLink builds on Nexus features and infrastructure including, where retained or adapted, its Windows desktop application foundation, `Game.log` session tracking, mining utilities, refinery tracking, blueprint data and workflows, hauling support, overlay infrastructure, OCR-related functionality, local persistence, and optional market-data integration.

New SC-navLink contributions remain attributable to their respective contributors under the repository's MIT license unless a file explicitly states otherwise.

## Clean-room reference policy

SC-navLink may study other Star Citizen community applications to understand publicly observable behavior, workflows, interoperability requirements, and user needs. Source code from projects with licenses that are not compatible with SC-navLink's MIT-licensed codebase must not be copied, translated, mechanically ported, or incorporated into SC-navLink.

In particular, SC-DataRunner / SC-DataRunnerNet may be used as a behavioral reference for concepts such as commodity-terminal capture, OCR-assisted market-data collection, validation workflows, and UEX submission. SC-navLink implementations of those capabilities must be written independently from public API documentation, public data formats, original research, and our own tests.

When practical, design notes for such work should identify the public documentation or independently observed behavior used to derive the implementation.

## External data sources

SC-navLink may integrate with community and public data providers such as UEX and the Star Citizen Wiki. Those services retain their own terms, attribution requirements, trademarks, data ownership, and API policies. Their data is not relicensed by this repository.

## Star Citizen disclaimer

SC-navLink is an unofficial fan-made project and is not affiliated with, endorsed by, sponsored by, or otherwise connected to Cloud Imperium Games or Roberts Space Industries.

Star Citizen, Roberts Space Industries, Cloud Imperium, and related names and marks are the property of their respective owners.
