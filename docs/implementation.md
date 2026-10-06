# Implementation and commit guide

This is a topic-organized import of the existing verified implementation. Feature modules share the UI kit and plugin entry; apply the complete series. Commit bodies explain why/how and distinguish final verification from per-commit testing.

## Native client changes

### 01. feat(plugins): expose Classic character, HUD and interaction contracts

[ebf3a65b40](https://github.com/canbolayir/LibreKO/commit/ebf3a65b40ece0875699823a4205976c8aa7acf6)

Classic presentation must use live game services rather than copy their state or replace native gameplay rules.

Add backward-compatible default interface members for character pages, portraits, notifications, bag quantities, chat links, commands, minimap visibility and hotbar selection. Expose native header/attention styling and stable window IDs.

### 02. fix(chat): preserve Classic channels and route service notices to Info

[a09a8f5f80](https://github.com/canbolayir/LibreKO/commit/a09a8f5f804747b3a5cc6ba7e73fafb797ff0b64)

The upstream chat rework must retain Classic channel colors and service messages while keeping private messages in their own windows.

Format escaped Classic lines, publish after storing entries, filter history, expose nearby players without a duplicate native card, and distinguish gold income/expense in Info. Route noncombat service notices through ChatStatusNotice.

### 03. fix(skills): describe the actual equipped weapon requirement

[42c467340a](https://github.com/canbolayir/LibreKO/commit/42c467340ae16c6abae2feb69bd88ba79d67ef3f)

Displayed weapon text must agree with casting checks, including unrestricted weapon skills.

Map ItemGroup to readable equipment requirements and test weapon groups and mastery pages.

### 04. fix(preview): isolate viewport particles and show elemental weapon glow

[ce6474d75b](https://github.com/canbolayir/LibreKO/commit/ce6474d75bada87fce31795113e27a52bcd5a0c8)

Inventory portraits must retain weapon effects without sharing emitters with the live world or leaking pooled nodes.

Key particle pools by viewport and effect part, forget emitters on exit, share lazy weapon-glow lookup and apply glow alongside item shine in character previews.

### 05. fix(presets): display and validate base stats plus allocated points

[97d4f4f96c](https://github.com/canbolayir/LibreKO/commit/97d4f4f96c3e766fd3e45e22a53a1609e6077118)

Zero-based totals are impossible in the game and the earlier conservative cap rejected valid class allocations.

Centralize creation/redistribution class-family bases; keep saved allocations, display actual totals, disable invalid steps and validate total/cap/remaining points before sending.

### 06. fix(quests): retain chosen rewards and expose cached NPC portraits

[3be3f6d2be](https://github.com/canbolayir/LibreKO/commit/3be3f6d2be6c24518a995af5b26194aa2e993270)

Reward choices should not reset on refresh or imply that an unavailable choice can be selected; dialogue portraits should follow the actual speaking NPC.

Cache pending choices and received receipts per quest, distinguish available options from received rewards, tag status and selection for the theme, and expose an isolated appearance-keyed NPC model factory using the retained talk target.

### 07. feat(character): bridge live report, quest, clan and friend pages

[58e0025322](https://github.com/canbolayir/LibreKO/commit/58e00253228f1cd75ec40bc357f72c3ce090a42e)

The Classic four-tab window needs real native data/actions and must not refetch a quest log when opening the selected detail.

Add CharacterPanelBridge rows/actions with permission checks; share live clan management, track selected pages and donations, and allow opening a main window without resetting its selection.

### 08. fix(items): unify stack badges and expose reusable quantity controls

[3d37660da7](https://github.com/canbolayir/LibreKO/commit/3d37660da7690ccade71e589ebd1047c7cafc91d)

A stackable item with count one must be labeled consistently across inventory, store and warehouse, and commerce needs shared numeric controls.

Add a common count-badge rule, fixed full-rect bottom-right label bounds, tradability flags, optional ungrouped numeric entry, public quantity confirmation and trade-row item metadata.

### 09. fix(vendor): confirm purchases and sales with authoritative checks

[901f776507](https://github.com/canbolayir/LibreKO/commit/901f776507f7cfa49a6989de67249a0531c3b5a4)

Classic vendor interactions need explicit approval, compact original catalogue pages, full-stack sale defaults and stale-item protection.

Use compact catalogue paging, guard busy/dead/move states, ask quantities only when useful, reuse buy/sell confirmation data, revalidate item and quantity before sending, and expose sellability/drag metadata to the theme.

### 10. fix(inventory): synchronize bag amounts, stacks and zone snapshots

[1b0cef9d4b](https://github.com/canbolayir/LibreKO/commit/1b0cef9d4b075bc78daa5b628e2403208b909c39)

Whole-stack right-click transfers and stale swap-only snapshots could split existing stacks or resurrect removed bags after teleporting.

Plan bag-to-grid merges before free slots, queue requested portions, apply confirmed merge/partial moves to LastEnter, append optional amount to the wire and validate it on the server. Reuse guarded item destruction and cover snapshots, bag transfers and server amount rules.

### 11. feat(party): expose member state and original party docking

[09aeb190cb](https://github.com/canbolayir/LibreKO/commit/09aeb190cbb5b5c9c60470e3d9d0186f62356f91)

Classic party rendering needs authoritative HP/MP, selection, leader/status data and seeking state without changing native invitation rules.

Attach structured member/list metadata, preserve tooltips and right-click leader actions, support member selection/private messages, detach obsolete rows immediately and dock the Classic party at the screen edge.

### 12. fix(trade): require explicit final approval and validate offer state

[6e6ae33445](https://github.com/canbolayir/LibreKO/commit/6e6ae334453dbedab8f2eb6e6be8f300b74be320)

Trade must keep all twelve slots accessible and never finalize merely because Enter was used for quantity input.

Add themed request/final layers and explicit decide action; returning No preserves the offer. Validate quantity and tradability, prevent edits while pending or partner-locked, consolidate stack slot counting and publish authoritative metadata/tooltips.

### 13. fix(merchant): synchronize purchases and validate listing interactions

[026d28c023](https://github.com/canbolayir/LibreKO/commit/026d28c023783f7f4f1e5604e7c1c6ba9f47f528)

Merchant quantity changes must follow server stack notifications, not a guessed single-item mutation; stale prompts must not send to a different stall.

Track pending purchase/sale identity, validate balance/coin cap/capacity/price, use chosen listing slots, support right-click and drag without double activation, seed full sale quantities, keep advert approval and expose Buying/Selling sign state. Notify both inventories and seller weight on the server.

### 14. fix(mail): refresh unread badges and fill partial stacks safely

[a853150123](https://github.com/canbolayir/LibreKO/commit/a85315012312b26e8ab52cb90e46bff7fc1dfc53)

Successful reads must clear notifications even after selection changes, and partial claims must use existing stack capacity without duplication.

Refresh unread count on successful ACK, update cached read state only on success, fill available space up to 9999 and retain the undelivered attachment remainder. Add a full-inventory repeated-claim regression test.

### 15. fix(store): fit the upstream Power-Up Store on compact screens

[47997b0181](https://github.com/canbolayir/LibreKO/commit/47997b01811f3c2076eda43e189e055413451532)

New cart and gift features must fit an 800x600 Classic client and preserve the existing Info notice route.

Keep category/cart controls for responsive width changes, switch search/sort tools to vertical layout on compact screens and route purchase results through ChatStatusNotice.

### 16. feat(hud): connect Classic bridges, chat docking and private focus

[16a1e1bbdc](https://github.com/canbolayir/LibreKO/commit/16a1e1bbdcd1cc22c97c7f414a8bf851056199d8)

Theme replacements must control native services consistently while retaining map heading, fixed chat tabs, independent dragging and predictable private-message focus.

Wire all Classic contracts to live services, preserve shoppingmall identity after upstream changes, expose HUD notification/hotbar state, snap adjacent chat/Info resize within 12 pixels with drag detachment, integrate whisper styling/focus and keep minimap heading tied to resolved movement. Extend Escape ordering for modal states.

### 17. perf(client): support Mobile rendering and reproducible diagnostics

[2fa0ecff1f](https://github.com/canbolayir/LibreKO/commit/2fa0ecff1f4c72cebab4fda311048e622b952206)

Local performance work uses Mobile/D3D12 and needs explicit unsupported-feature handling plus reproducible source/content diagnostics.

Set Mobile defaults and threading policy, disable unavailable SSAO/fog controls, allow mounting existing content without extraction or overriding source files, and add renderer/character-selection smoke commands.

### 18. chore(godot): retain stable C# resource identifiers

[8e97bc54f3](https://github.com/canbolayir/LibreKO/commit/8e97bc54f323d1ea5fad57dc764c8b8b3d993a70)

Plugin and new source scripts should retain their Godot resource identity across clean checkouts.

Track generated source UID sidecars only; exclude build/cache files and import-only line-ending noise.

## Plugin source groups

### 01. chore: establish Classic plugin packaging and licensed import tools

Keep the Classic theme independently buildable and preserve the license/provenance of its adapter and reference tooling.

Add the Godot C# project, plugin manifest, installation script, ignore rules, AGPL license, attribution and loose UIF/DXT import tools. Build caches and compiled assemblies are excluded.

### 02. feat(artwork): preserve nation-specific Classic layouts and atlases

Original Human/Karus geometry and artwork are the visual reference, while composed screens remain editable.

Import layout/reference metadata and texture atlases with ownership attribution; retain the local theme settings and supplied hunt icon alongside matching equipment/costume/mail textures.

### 03. feat(layout): add editable Classic geometry and text primitives

Pixel alignment and consistent fonts/counts must be shared rather than hand-coded differently on each screen.

Provide layout nodes/views, drawn text, item slots/count bounds, clipped drag previews, Classic frames, design geometry, UI kit and reusable scroll rails.

### 04. feat(character): compose report, quest, clan and friend pages

Original four-page compositions must present native live state and preserve selection, permission and disabled behavior.

Add declarative page rectangles/router, live rows, embedded details, titles/presets and native dialog adapters without replacing complete screens with imported UIF templates.

### 05. feat(npc): compose fixed speech, cached portraits and quest cards

NPC menus must fit the viewport, keep navigation stable and show long speech in its own scroll area.

Cache one-shot NPC portraits, place name/level beside fixed speech, page long lists, use two-column hunt/collect/reward cells, distinguish quest states and retain classic choice markers and item tooltips.

### 06. feat(skills): align mastery controls and readable descriptions

The skill window must retain the original proportions with readable shared bold text and correct requirement labels.

Separate named skill geometry from controls, keep long descriptions accessible and route mastery/skill interactions through the game bridge.

### 07. feat(inventory): unify equipment, costume and bag geometry

Equipment, bag and costume slots must share exact border/count placement and form one contiguous nation-specific composition.

Compose original frames and Chaos icon atlases, embed a reversible costume toggle, align coins/weight, clip preview effects inside the rounded model area and use guarded Classic item destruction.

### 08. feat(vendor): reuse Classic quantity and approval compositions

Buy/sell and bag quantity dialogs should share original frame/button artwork while presenting quantities and incoming/outgoing money clearly.

Create one named quantity layout and confirmation frame; align native vendor catalogue/inventory cells, remove theme search chrome, retain dragging/right-click/tooltips and wire Enter quantity confirmation and Escape cancellation.

### 09. feat(party): adapt native member and seeking states to Classic rows

Human and Karus party controls need readable active/disabled states and native member actions.

Skin live party/seek rows, show member bars/status/tooltips and preserve selection, leader actions and party/private requests.

### 10. feat(trade): compose twelve slots and explicit final approval

Every trade slot must be visible without scrolling; final approval must remain an explicit mouse action.

Add named twelve-slot grid/request/quantity/final-approval geometry with matching original frames and bold fonts. Keep partner-confirmed state aligned and No returning to the unchanged offer.

### 11. feat(merchant): compose Classic stalls, prompts and Silver/Copper signs

Selling and Buying must stay distinguishable while preserving the original interaction language and modern native merchant features.

Align catalogue/bag grids and totals, adapt search/advert/price/quantity/approval states, reuse original frame/button geometry and use the selected Silver Selling and Copper Buying signs with coherent hover/pressed/disabled states.

### 12. feat(hud): preserve Classic chat, Info, whispers and command artwork

Chat filters, Info and HUD controls must retain original nation ink, fixed tab widths and native modern chat features.

Compose shared bold chat/private text and editable channel options, fixed channel strip, right-side Info controls, linked resizing, stable PM placement, original status/target/minimap/taskbar and contiguous rotating hotbars.

### 13. feat(plugin): register composed Classic windows and native store styling

One plugin entry must connect the feature modules and keep new upstream store/mail buttons consistent with nation styling.

Register themed native windows/HUD replacements, retain declarative composition and skin selected/disabled PUS/mail buttons using Classic textures and readable Human/Karus ink.

### 14. test(preview): retain source-driven Human/Karus regression harnesses

Visual and interaction verification must be reproducible without logging in or restarting an active game.

Add demo native bridges and audits for character/details, NPC/portraits, presets, skills, inventory, vendor, party, trade, merchant, chat docking, mail wire and upstream store states, including control-bound assertions.
## Upstream NPC service fixtures

The `npc-services-audit` preview route renders the upstream Kelly, nation transfer and merchant search panels without adding Classic framing or replacing their layout. It loads the existing character content packs, checks viewport bounds and exercises their registered Escape callbacks. The upstream gender fixture uses an El Morad Rogue and nation transfer targets Karus; running under both nation contexts intentionally retains the shared native service style. Existing Classic artwork and runtime layouts are unchanged by this fixture addition. Final integration captures and regression records are retained under the workspace's `research/upstream-4772e7a-audit` directory.
