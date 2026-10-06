# NPC services integration and compatibility

## Required fork

This plugin requires [canbolayir/LibreKO](https://github.com/canbolayir/LibreKO). The unmodified ZeusAFK/LibreKO client does not provide all required Classic bridges and interaction APIs. The theme is only one part of the integration: bag quantities, inventory synchronization and other protocol behavior also require the matching fork server. Build against the fork's client assembly and deploy compatible client/server packages together. The manifest version field does not verify the repository or API set.

Latest verified client/server revision: [5a52bbc](https://github.com/canbolayir/LibreKO/commit/5a52bbc82c0909cda927fb581b4bdb47a1ba01bc). Verified plugin implementation: [2e4f047](https://github.com/canbolayir/LibreKO-knightonline-ui-classic/commit/2e4f047c15691371fcf0039131b2c4f2e7aae27d). Later documentation changes do not alter that runtime implementation.

## Features from upstream 4772e7a

| Feature | Resulting behavior |
| --- | --- |
| Kaira and Hemes exchanges | Store packages, coupons and cosmetic certificates can be exchanged through their NPC flows. |
| Makeup Artist Kelly | Gender/race, face and hair changes use the Gender Change item, with a rotating model preview. |
| Grand Merchant Kaishan | A Nation Transfer Certificate moves the whole account to the other nation. Clan membership, king status and nation wars remain checked. |
| Menissiah's official list | Lists merchants in the current zone/room, with selling/buying filters, whisper and movement to the seller. The existing local merchant transaction flow handles purchases/sales. |
| Character rename | Requires the Scroll of Identity and NPC entry; character-select rename is refused. The live name and nameplate refresh after success. Rename/report hotkey entries are removed upstream. |
| Clan rename | The Clan Name Change Scroll is consumed when used. |
| Maestro HP/MP potions | Use requires at least 100,000 coins and enough money for the configured item price; the configured restoration price is charged. |
| Transformations | Compatible wings, fairies, talismans, emblems, glove effects, clan gauntlets and capes remain on humanoid transformations and are dropped for incompatible forms. |

Kelly, nation transfer and merchant search retain upstream native styling. Their Classic redesign is deliberately deferred; existing Classic screens retain their current composition and artwork.

## Local resolution and retained behavior

The merge conflict in the rename success handler was resolved by keeping both upstream live name refresh and local Info status routing. A separate commit registers the three new panels in the existing Escape stack and sends non-combat service notices to Info. Nation transfer retains its in-flight cancellation guard.

Bag amount prompts and stack merging, vendor confirmations, explicit final trade approval, merchant quantities and pending guards, mail partial claims/unread badges, chat docking and fixed private-message origins remain in place. Enter confirms a trade quantity; it does not accept the final trade. Custom Moradon content remains excluded, and this upstream commit adds no database migrations.

## Commits and verification

- [32975ce](https://github.com/canbolayir/LibreKO/commit/32975ce): preserve upstream history with a merge and resolve the rename conflict.
- [472b7b8](https://github.com/canbolayir/LibreKO/commit/472b7b8): connect new native service panels to Escape and Info.
- [5a52bbc](https://github.com/canbolayir/LibreKO/commit/5a52bbc): document integration and retained behavior.
- [2e4f047](https://github.com/canbolayir/LibreKO-knightonline-ui-classic/commit/2e4f047): add source-driven native service preview fixtures; no Classic runtime layout change.

915 client, 2,113 game and 16 login tests passed: 3,044 total. Source-driven Godot checks cover six native service captures and Escape callbacks, 40 inventory renders, 82 merchant renders, 28 trade renders, 20 PUS/mail renders, NPC controls, chat docking and mail badges. The eight Human/Karus Character Report, Quest, Clan and Friend pages retain identical baseline pixels and checked rendered bounds. See the [sanitized verification summary](upstream-4772e7a-verification.json).

These domain/protocol and UI fixture checks do not replace a live two-player transaction test or guarantee compatibility with arbitrary future upstream revisions.
