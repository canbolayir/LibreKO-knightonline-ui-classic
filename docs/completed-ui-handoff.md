# Completed UI handoff — 2026-10-08

This source requires the matching `canbolayir/LibreKO` fork. It is not a standalone skin for the unmodified ZeusAFK client. Build against the fork revision linked in the README, deploy its matching client and server, and enable only one UI theme. Existing version numbers do not identify the required API set.

## Completed since the warehouse release

| Area | Native behavior retained or repaired |
| --- | --- |
| NPC services | Repair in the real inventory, original hammer cursor and acknowledged sound; warp rows; seal/keypad; stat and mastery reset; fragment exchange; item combination and recipe inspection. |
| Communication | Mail inbox, read and compose, attachments and partial claims; messenger, chat rooms and chat colours. Mail uses a compatible service composition because the available original references do not contain matching mail semantics. |
| Market | Merchant search, market history, Power Up Store cart and gifting, and all four native auction tabs. Confirmation freezes transaction details. |
| Identity and appearance | Name change, clan creation, transformation, equipment inspection, gender, beauty and account nation transfer. Race content controls available appearance choices; pending edits are guarded. |
| Progression and clan | Rebirth stages and allocations, nation/siege rate controls, cape patterns, colours, prices and requirements. |
| Familiar | Ability, equipment and skills pages; native skill bar; hatch and transformation inventory; food and equipment result handling; cached portraits for all currently seeded forms. |

These are editable C# layouts and adapters around native controls. Original UIF data remains a visual/art reference; entire imported UIF windows do not replace the declarative component architecture.

## Important client/server fixes

- Beauty requests now reach a server coordinator and accepted appearance changes persist.
- Nation transfer validates and saves the account coherently. Old logout state cannot overwrite the newly transferred nation/position; failed persistence restores the in-memory certificate state.
- Rebirth rejects invalid, dead and busy requests while retaining existing requirements and costs.
- Familiar equipment movement freezes the original item identities. A refreshed familiar stat sheet arriving before the movement acknowledgement cannot cause a second, incorrect swap.
- Feeding and trainer results are validated before inventory changes; malformed or duplicate packets cannot apply an optimistic item loss.
- Transport generations discard callbacks from old connections. Reconnect/character selection resets pending services and queued inventory requests.
- Trainer Enter/Escape is routed before global chat activation while preserving active chat and unrelated text editing.

No protocol transaction ID was added to legacy service replies. An identical delayed refusal within the same connection can remain ambiguous. The server remains authoritative.

## Verification and release boundaries

The final reviewed source passed **1,101 client tests** and **2,327 game-server tests**. Familiar verification includes **19,818 native checks**, **202 captured states**, all eight Human/Karus parent pages plus selection variants, 16 actual parent bounds/geometry records, and NPC portrait regressions.

The reconnect audit uses real framed TCP with isolated version/login/character-selection/MYINFO fixtures and native World reconstruction. It uses a registered scene alias and an unbaked flat zone; it is not a production-account play session. Existing stale character resource UIDs resolve by resource path; successful final logs contain no errors.

Evidence is retained in the development workspace under `research/pet-classic-audit/verification.json`, `manual-visual-review.json`, `installation.json`, and the earlier per-feature review folders. These files are outside this repository and are not automatically included in a Git clone. The matching client/plugin DLL and PDB files were hash-verified in both local client and benchmark installations. No running game or server was restarted; an existing process may still be using the previous build.

Topic commits form a dependency series. The final pair is verified; an intermediate commit is not an independently approved release pair. Local Moradon additions are excluded from the source publication.

## Work still pending

The remaining-window inventory tracks **27 unverified windows** after Familiar and hatching. King/election and siege administration, events and minigames, achievements/attendance/rank/genie, global map and fortune still need specific design and interaction review. Pregame, settings and miscellaneous overlays require separate audits. Generic Classic framing does not mean a screen has been finished. GM tools are outside the current Classic scope.

Continue by researching semantic references, adapting native controls, testing input and transactions, then inspecting final source-driven Godot captures. Review all eight parent pages after shared changes and install only the verified matching pair. Do not replace original art with improvised panels or commit/push without the user's authorization for that step.
