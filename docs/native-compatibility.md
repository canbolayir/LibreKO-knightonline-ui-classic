# Native compatibility

The Classic UI runs on the unmodified [ZeusAFK/LibreKO](https://github.com/ZeusAFK/LibreKO) client. Everything the
former fork added to the client for this plugin (native control names, state metas, character/chat/HUD bridges,
Classic-only behaviour and the Classic preview fixtures) is recreated in the plugin's compatibility layer under
`src/Native`. Client and server correctness fixes that cannot live in a plugin are proposed upstream as 23 separate
branches. The plugin does not require them. On bare upstream it runs with upstream's existing bugs. Where a fix
adds something the plugin can use, the plugin detects it at runtime.

This page covers:

1. how the layer works;
2. what works on bare upstream and what each fix branch adds;
3. every client member the plugin reaches by reflection, as a checklist for upstream renames;
4. known differences from the former fork build;
5. optional client hooks that would remove reflection;
6. how to verify the plugin after an upstream update.

## 1. How the compatibility layer works

- **Validated reflection (`Native.cs`).** Reflection on non-public client members exists only in `src/Native`, and
  nearly all of it goes through `Native`: `Get`, `TryGet`, `Set`, `Call`, `TryCall`, `Subscribe`, `Unsubscribe`,
  `Has`, `HasMethod` and `ClientType`. Lookups walk base types and are cached. When a member is missing, `Native`
  writes `native member unavailable: <Type>.<member>` once per member, adds it to `Native.MissingMembers` and
  raises `Native.MemberMissing`. Then it returns a default value instead of throwing. A member that exists but no
  longer has the type `Get`/`TryGet` read it as (a container that became another container type, a field that
  changed width) is reported the same way, once, as `native member type changed: <Type>.<member> is <Actual>, read
  as <Requested>`; a null value of a compatible type is not a mismatch. `Has`, `HasMethod` and
  `TryCall` do not report, so they are used for feature detection. Two cases run outside `Native`:
  - The anvil binds the client's private `OnUpgradeOpen` with `Delegate.CreateDelegate`.
  - Nested records such as `World+BagCompanion` and `World+VipWhPending` are created with `Activator`.
- **Window preparers (`NativeWindows`).** Each window group registers preparers per window id
  (`NativeWindows.Prepare`) or per pattern (`PrepareMatching`, for example the per-player whisper windows).
  - `NativeSetup.Register` runs from `Plugin.Initialize` before any skin registers. Each group adds its own
    `Register*` partial method in its own `Native*.cs` or `Familiar*.cs` file.
  - Preparers and skins both defer to the end of the frame that built the window, so the preparer always runs first.
  - A preparer names the native controls a skin looks for. It mirrors native state into the metas the skin reads.
    It builds the additions the skin expects (embedded bags, confirmation layers, extra buttons). Last, it sets the
    skin's gate meta, for example `classic_mail_controls`.
  - If a required member is missing, the preparer returns without setting the gate. The skin then leaves the window
    as the client built it. The NPC service skins (`ClassicServiceSkin`) apply the generic Classic frame
    (`ClassicSkin.Apply`) instead. A rename upstream therefore costs one window its Classic composition. It does
    not break the game.
- **`NativeWindows.Sync(window, action)`** adds a `native_sync` node. The node runs the action every frame while the
  window is visible. It uses the lowest process priority, so it runs before the skin draws. It mirrors busy, pending,
  selected and page state, and repairs text the client rewrites.
- **Closed windows do not tick.** The client builds every window when the World starts, so a skin that processed
  while its window was closed worked every frame for the whole session. Skins and syncs use `Layout/ShownOnly.Gate`:
  the node processes only while its window (or the node itself) is visible in the tree, and when the window opens its
  sync runs once more as a deferred call, which Godot flushes before it draws that frame. The window's own signal fires
  before its children's, so `native_sync` catches up before the skins. A `_Process` that acts on the game (sends,
  cancels, lets a timer expire) keeps ticking instead: the roulette landing, the anvil scan clock, the trade tick,
  the repair sync, the transaction hide edge, the trade approval cancel and the repair hammer.
- **Per-frame code creates no Godot names.** A string passed to a meta, theme, node-path or group API creates a
  finalizable `StringName`/`NodePath` per call, which made every .NET GC pause long. Per-frame methods use cached names
  (`Layout/ThemeNames`, per-file `HotNames`), resolve named controls once (`Layout/ControlIndex`) and apply theme
  overrides only on change (`ThemeNames.SetColor`/`SetConstant`). `tools/check-hot-literals.ps1` reports a literal in
  per-frame code.
- **`NativeWindows.OnDescendantAdded(root, action)`** listens to `SceneTree.NodeAdded`. It describes rows, cards and
  cells the client adds later (mail rows, store cards, trade rows, recipe rows, merchant signs, whisper rows). It runs
  before the skin's deferred styling. All scene-tree watchers share one `node_added` connection (`NativeHooks.WatchTree`).
- **HUD and layout.** `NativeHud` hides or extends HUD elements that have no public `HudPart` (the mail,
  achievements, attendance and Power-Up Store icons, and the familiar bar). It also carries the whisper styler.
  `NativeLayout` recreates three things the fork had: the legacy resize grip, chat/Info seam snapping and
  `content_open_anchor`. `NativeUi` provides the Classic count badge (`CountBadge`), ungrouped money digits
  (`GroupDigits`, through `PlainDigits`) and the per-window attention styler.
- **Game services (`NativeGame.cs`).** `INativeGame` holds the services the skins need beyond the public plugin API:
  - character panel and NPC portrait;
  - chat history, item links and nearby players;
  - hotbar selection, run/sit/attack commands and minimap visibility;
  - inventory `TransferToInventorySlot` and `ConfirmDrop` (part of a stack moves through the client's own
    `IGameInventory.MoveAmount`).

  C# 14 extension members put these services on `IGameWindows`, `IGameChat`, `IGameHotbar`, `IGameCommands`,
  `IGameMap` and `IGameInventory`, so skin code reads like the old fork API. `ClientGame` is the runtime source.
  Each member falls back to what the public API already offers when native support is absent. For example,
  `ConfirmDrop` uses the public `Drop` when the client has no destroy queue. The preview replaces
  `NativeGame.Source` with fixture data.
- **Live enable and disable (`NativeHooks`, `NativeSetup.Unregister`).** Upstream switches UI themes from Settings >
  Plugins without a restart. On disable the client calls `Plugin.Shutdown`, removes every `context.Ui` registration
  and its leftover `PluginGame` handlers, and rebuilds the World in place. The plugin assembly stays loaded, and a
  re-enable creates a new `Plugin` instance, so static state survives. The plugin therefore records every hook it
  attaches to an object it does not own in `NativeHooks`:
  - scene-tree watchers (`NativeHooks.WatchTree`: the window, content-anchor, familiar-bar and merchant-sign
    watchers and every `OnDescendantAdded`);
  - handlers on `Net` events (`NativeHooks.Listen`), including every `Native.Subscribe`;
  - handlers that replace one of the World's own handlers (`NativeHooks.Replace`: anvil open, nation transfer open)
    or run ahead of it (the prepended mail-list and gold handlers);
  - the `Game.Windows.Source` wrapper.

  Each hook is released once, by its caller, when its owner (normally the World) leaves the tree, or on shutdown;
  on shutdown a replaced World handler is restored while that World is still in the tree. `Plugin.Shutdown` then
  calls `NativeSetup.Unregister`: it releases all hooks, removes the plugin delegates still attached to the
  client's game API (the Classic HUD and windows of the World that is about to be rebuilt), sweeps `Net` and the
  tree's `node_added` for unrecorded plugin handlers (a warning names them; the audit expects none), and clears the
  static registries (`NativeWindows`, `NativeHud`, `NativeOverlays`, `NativeLayout`, `FamiliarBar`,
  `NativeGame.Source`, `NativeChat`, `NativeQuestLog`, `NativeCommunication`, `NativeHudChat`, `Native` reports and
  `MemberMissing` listeners). It also frees plugin nodes outside the World (the developer check, dialogs on the
  root), the NPC portrait cache, the straight-art cache, the character page router and the cached NPC heading font,
  and resets `Plugin.Kit`. `Plugin.Active` guards code that can run after shutdown (deferred preparers, `_ExitTree`
  of Classic nodes freed by the rebuild). `plugin-toggle-audit` drives the host path on a full native World:
  native start, enable, disable, re-enable, disable, re-enable.
- **Preview fixtures (`preview/Fixtures`).** The fork's `World.UiPreview.*Classic*` partials are rebuilt in the
  preview project through `Native`: `PreviewFixtures` plus one partial file per group. The preview's `PluginUi` runs
  no window extenders, so fixtures and audits call the group preparer (`NativeServices.Prepare`,
  `NativeTrade.Prepare` and so on) before they apply a skin. Fixtures that stand in for a server reply call the
  client handler first and then raise the `Net` event, in the same order as the game.

## 2. Feature support

"Bare upstream" means `upstream/main` with no fix branch. `classic-integration` is upstream plus all 23 branches.
On `classic-integration`, behaviour and rendering match the former fork build except for the differences in
section 4.

### Per window group

| Group | Works on bare upstream | Needs a fix branch |
| --- | --- | --- |
| HUD, chat, whispers (`NativeHudChat`, `NativeChat`, `NativeLayout`, `NativeHud`) | Classic chat and Info, history format, timestamps and custom colours, private-message exclusion, `ChatSystem.Info` and server notices as yellow System lines, bubble colours, Shift-click item links, nearby players, whisper styling and compose target, chat colours editor and palette, legacy resize grip, chat/Info seam snapping, hidden HUD icons, taskbar service toggles, notification count, hotbar selection, command state, gold income/expense lines in Info. | `fix/chat-whisper-state`: an existing whisper window comes to the front when it is reopened or receives a whisper, and a click focuses the top-most window. On bare upstream the window can stay behind another conversation, and a click can focus the window underneath. `fix/escape-order`: Escape closes chat colours, the mail reader, mail compose (hidden with its draft kept, as with its X; only Cancel clears it) and nine more windows before it opens the game menu, and it closes the shop after its amount prompt. |
| Character and NPC (`NativeCharacterPanel`, `NativeCharacterNpc`, `NativeNpc`, `NativeQuestLog`, `NativeParty`, `NativeSkills`, `NativeEvents`) | Character Report, Quest, Clan and Friend pages through `CharacterPanelBridge`; Knights Management; NPC dialog portraits and quest captions; Quest Details reward rows; preset label wording; party roster, click-to-target and top-right docking; seek-party board; skill weapon requirement and mastery trees; temple-event leave confirmation. | `fix/quest-rewards`: the "Selected reward" and "Received rewards" states in Quest Details. On bare upstream `_questRewardSelection`, `_questRewardOptionTitle` and `_questRewardOptionBox` are reported missing once, and the rows always list the reward options. `fix/stat-presets`: preset totals and validation from the class base. The plugin's "Allocated X / Y    Remaining Z" text works either way. |
| Inventory and storage (`NativeInventory`, `NativeStorage`, `VipVaultPinPrompt`) | Classic inventory; identity-checked destroy; magic-bag right-click target; warehouse with 24-cell pages and amount prompt; VIP and clan vault pages; in-vault rearrangement, which the plugin sends itself; Classic VIP PIN prompt; clan coin prompt. | `feat/magic-bag-amount`: part of a stack between bag and magic bag. On bare upstream a partial amount moves the whole stack. `fix/storage-transfers`: VIP and clan quantity prompts with merge-aware destinations (`VipWhPending`/`ClanWhPending` `Count`/`Merge`), merging into existing stacks, `StorageTransferBusy`, and stale-reply guards. On bare upstream vault transfers are whole-stack with no prompt, and the busy state comes from the in-flight fields. |
| Vendor, trade, merchant (`NativeVendor`, `NativeTrade`, `NativeMerchant`) | Vendor buy/sell confirmations; compact 24-cell catalogue pages; drag payload; count badges; gold log; Classic exchange request layer, final approval with offer snapshot, and gold prompt; merchant advert step; amount prompt metas; release activation; drop onto a listing slot; wanted-sale gold cap; Silver/Copper signs. | `fix/vendor-exchange-validation`: `SellSlot(abs, count, itemId)` re-checks the item (bare upstream falls back to the 2-argument `SellSlot`); untradable items are refused through `ItemSlot.IsTradable` (skipped on bare upstream); `_exConfirmedByPartner` locks a confirmed partner's offer (on bare upstream `ex_partner_locked` stays false and one member is reported missing). `fix/merchant-stack-sync`: stall purchases keep both inventories in step with the server. The plugin does not depend on it. |
| Magic Anvil and NPC services (`NativeAnvil`, `AnvilPlacementRules`, `NativeServices`) | Anvil selection window, placement rules, embedded 28-cell bag, Cancel/Back, busy, compound, error and scan metas, count badges, the `IsTradable` rule (mirrored from `Flag`/`UniqueId`); warp, seal, Chaotic Generator (28-cell bag and drop target), item combination quantity, recipe book, redistribution, Classic repair mode. | `fix/anvil-session`: `UpgradeInteractionLocked`, the preview gate, the confirmation snapshot and the retained-item result, plus a server that echoes the requested upgrade type on refusal. On bare upstream the bench lock falls back to `_upgradeSession`, the pending result and the scan tween, and upstream's own anvil bugs remain. `fix/npc-service-requests`: repair applies to the confirmed item and plays its sound only after the acknowledgement; the seal uses a `Notice.Confirm` confirmation with an in-flight guard and identity checks; the redistribution request (`_reset`). On bare upstream redistribution reads `_resetKind`, and seal and repair keep upstream's flow. |
| Market and communication (`NativeMarket`, `NativeCommunication`) | Mail inbox, reader and compose; messenger and chat rooms; Market Price with the Classic chart background; merchant search; Power-Up Store with compact-screen and touch layout; Akara's Altar. The plugin also works around two upstream bugs: the chat-room log hang at the 101st line, and empty states that never return. It also repairs the cached Market Price "no history" reply every frame. | `fix/auction-replies`: bid and retract replies apply to the submitted lot; busy locking; tab pressed state kept in sync; `_auctionPlaceBid`, `_auctionRefresh`, `_auctionRowButtons`. On bare upstream Place Bid and Refresh are found by structure, and `auction_row_action` goes on every row button. `fix/mail-claims`: claims fill existing stacks, the unread badge follows reads, and compose cannot send twice. `fix/chat-room-lists` and `fix/market-price-no-history` fix the two bugs in the client and make the plugin's workarounds redundant but harmless. |
| Identity and appearance (`NativeIdentity`, `NativeEquipView`, `NativeLook`, `NativeAppearance`, `NativeRebirth`, `NativeCape`) | Name change with Cancel, clan creation names, transformation, council tax rate, equipment inspection rebuild, gender change (Classic editor and preview framing), rebirth (Total column, plugin-owned confirmation), cape service (preview, pages, cost confirmation). | `fix/beauty-shop`: the Classic Beauty Shop needs `_changeHairApply` and `_changeHairInFlight`; without them the preparer stops and the client's own window stays. The branch also adds the server's in-game Beauty Shop handler, `LookEditor.SetLocked` (bare upstream disables the editor buttons instead) and `_faceSteps`/`_hairSteps` (bare upstream finds the steppers by structure). `fix/nation-transfer`: the Classic Confirm calls `World.SendNationTransfer(int, IReadOnlyList<NationTransferPick>)` and uses `_transferRevision`, `_transferNotice`, `_transferCompleted`, `SetTransferLocked` and `CancelTransferConfirmation`, which only this branch has. On bare upstream the plugin does not take over the transfer window, so the client's own window handles the transfer. The server side saves the transfer in one step. `fix/clan-requests`: cape replies are matched to their request; a cape reply for another clan raises `CapeResetEvent` (upstream 7ef0f8e): the picker unlocks the same draft and shows text 16810, while a disconnect, recognised by `PetResetEvent`/`RebirthResetEvent` following in the same reset, closes the picker; the clan fee confirmation (`_clanCreateNotice`, reworded by the plugin) is shown, while bare upstream creates the clan without it. `fix/rebirth-requests`, `fix/beauty-shop`, `fix/clan-requests`: `Send*` methods return `bool`, so a refused send is detected. On bare upstream they return `void`, and a call counts as sent. |
| Familiar (`NativePet`, `FamiliarWindow`, `FamiliarTrainer`, `FamiliarBar`, `FamiliarSkillButton`, `FamiliarPortraitView`) | Familiar window (details, 2×8 skills, 3D portrait), trainer (hatching and transformation with confirmation and keyboard handling), familiar bar (names, grip, default position, slot frames, tooltips). | `fix/familiar-replies`: replies are matched to the request and never applied twice; `SendPetHatch`/`SendPetTransform` return `bool` (on bare upstream a live connection counts as sent); reset on disconnect. |

The nation-transfer behaviour on bare upstream follows from the code paths above. No audit runs on bare upstream.

### Branch index

| Branch | Plugin relationship |
| --- | --- |
| `feat/magic-bag-amount` | Feature-detected: `MoveBetween(int,int,int)`, `IGameInventory.MoveAmount`. Without it a partial amount moves the whole stack. |
| `fix/anvil-session` | Feature-detected: `UpgradeInteractionLocked`, `ItemSlot.IsTradable`. Fixes the anvil session and the server's preview refusal type. |
| `fix/auction-replies` | Feature-detected: `_auctionPlaceBid`, `_auctionRefresh`, `_auctionRowButtons`. |
| `fix/beauty-shop` | Required for the Classic Beauty Shop. Optional for the editor lock and stepper names. |
| `fix/chat-room-lists` | Makes the plugin's chat-room and messenger workaround redundant. |
| `fix/chat-whisper-state` | Behaviour only: whisper raise and top-most click focus. |
| `fix/clan-requests` | Feature-detected: `CapeResetEvent`, `_clanCreateNotice`, `bool SendCapeBuy`. |
| `fix/escape-order` | Behaviour only: Escape entries for 12 windows and the shop/amount-prompt order. |
| `fix/familiar-replies` | Feature-detected: `bool SendPetHatch`/`SendPetTransform`. |
| `fix/inventory-move-snapshots` | Core correctness. No plugin interaction. |
| `fix/magic-bag-stacks` | Core correctness. The plugin keeps its own magic-bag target rule. |
| `fix/mail-claims` | Behaviour only. |
| `fix/market-price-no-history` | Makes the plugin's per-frame no-history repair redundant. |
| `fix/merchant-stack-sync` | Behaviour only. |
| `fix/nation-transfer` | Required for the Classic nation transfer window; without it the client's own window is used. |
| `fix/npc-service-requests` | Feature-detected: `_reset`. Behaviour for repair and seal. |
| `fix/preview-effects` | Weapon glow and emitters in character previews. Not reflected. |
| `fix/quest-rewards` | Feature-detected: `_questRewardSelection`, `_questRewardOptionTitle`, `_questRewardOptionBox`. |
| `fix/rebirth-requests` | Feature-detected: `bool SendRebirthStatChange`. Server validation. |
| `fix/stat-presets` | Behaviour only. |
| `fix/storage-transfers` | Feature-detected: `VipWhPending`/`ClanWhPending` `Count` and `Merge`, `StorageTransferBusy`. |
| `fix/transport-generations` | Core correctness. A reply decoded before a connection loss is now applied. |
| `fix/vendor-exchange-validation` | Feature-detected: `SellSlot(int,int,int)`, `ItemSlot.IsTradable`, `_exConfirmedByPartner`. |

**Build-time binding.** `InventoryWindow` and `NativeStorage` call `IGameInventory.MoveAmount(...)`, the client's
facade from `feat/magic-bag-amount` (upstream since 9d0e0bb). The plugin's extension of the same name and its
reflected `MoveBetween(int,int,int)` were removed on ff78eef, where the facade shadowed them. The stack split also
binds `ItemSlotView.PicksAmount`, `StackDial` and `DragLayer.ChosenAmount` at build time, and the Classic party
list binds the party facade's `MemberBuffs`, `PingMs`, `LevelPercent` and `WarBoardCooldown` (with `PartySignal`
and `TextWrap`), so a build needs an upstream client from ff78eef on. Install a build made against the client it
will run on.

**Verified upstream.** The current plugin is verified against upstream `fa3cfc3` (bare and `classic-integration-fa3c`,
which has no extra branch). It still builds against `ff78eef`, where the fa3cfc3 paths find their members missing
and keep the earlier ones (the whisper log, input and "Send", the room's named controls, `_bifrostRemaining` and
`_isEventRegistered`). The plugin facades it uses instead of reflection where they cover the same members:
`IGameInventory` (`Move`, `MoveAmount`, tooltips, slots), `IGameParty` (rows, pick, member menu, row order, row
meter, re-invite, war board mode and cooldown, member buffs, ping and level progress, user vote), `IGameUserVote`,
`IGameRepurchase`, `IGameUserInfo`, `IGameChat.History`, `IGameWhispers.History`, `IGameWindows`, `IGameCommands`
and `IGameCharacter`. The ff78eef changes are in the sections "Changed shapes", "Stack split and storage amounts",
"Party HUD and War Board" and "Chat after room lines left the main chat on upstream ff78eef"; the fa3cfc3 changes
are in "Changed shapes on upstream fa3cfc3" and the four sections after it.

## 3. Reflected client members

These tables are the maintenance checklist: every client member the plugin reaches by name (`Native.Get`,
`Native.Set`, `Native.Call`, `Native.Has` and the fixtures' helpers), grouped by the type that declares it, with
the files that use it. They are produced by resolving each string-literal member path against a build of the
client, so a path that no longer resolves is a rename to follow. "Kind" uses F field, P property, M method,
E event and T nested type. `WorldRoot` members are mostly the features and systems a path starts from
(`Achievement.AchPanel` is `WorldRoot.Achievement`, then `AchievementFeature.AchPanel`). Public API used
directly, such as `UiTheme`, `ItemData` and `HudWindow`, is not listed.

### Runtime

| Type | Members | Used in |
| --- | --- | --- |
| `WorldRoot` | `Achievement` P, `AchievementTrophy` P, `Attendance` P, `Avatar` P, `BagCompanions` P, `BasicAttackSystem` P, `BattleEvent` P, `Bifrost` P, `BorderDefenseWar` P, `Bounty` P, `CameraSystem` P, `CapeWindow` P, `Challenge` P, `ChangeHair` P, `Chaos` P, `CharacterSealWindow` P, `Chat` P, `ChatHost` P, `ChatNotices` P, `ChatReactions` P, `ChatRoom` P, `Clan` P, `ClanBattle` P, `ClanCreate` P, `ClanPoints` P, `ClanPremium` P, `ClanWarehouse` P, `CollectionRace` P, `CommandList` P, `DailyRankWindow` P, `DeathDialog` P, `DisguiseWindow` P, `DockHudRects` M, `EntityLooksSystem` P, `EntitySystem` P, `EquipView` P, `Events` P, `EventTitlesWindow` P, `Exchange` P, `ExpSealWindow` P, `FishingHall` P, `Forces` P, `Fortune` P, `Friends` P, `GatherSystem` P, `GenderChangeWindow` P, `Genie` P, `GenieHammer` P, `GeniePotions` P, `GenieScrolls` P, `GenieSub` P, `GenieTown` P, `GlobalMap` P, `Hotbar` P, `Hotkeys` P, `InventoryWindow` P, `ItemCombineWindow` P, `ItemSmashWindow` P, `ItemTooltip` P, `KillSplash` P, `KingElection` P, `KingTreasury` P, `KnightRoyaleWindow` P, `Log` P, `Loot` P, `LootFilterWindow` P, `Lottery` P, `Mail` P, `MailCompose` P, `MailIcon` P, `MainWindows` P, `Map` P, `MarketPrice` P, `Merchant` P, `MerchantBuy` P, `MerchantSearch` P, `MerchantStalls` P, `Messenger` P, `MsgOut` P, `NameChange` P, `NationChannels` P, `NationTransfer` P, `Nereid` P, `NpcDialogWindow` P, `Party` P, `PartyInvites` P, `Pet` P, `PetHatch` P, `PetSkillBar` P, `PieceChange` P, `PlayerMenu` P, `PluginBridge` P, `pluginHud` F, `PowerUpStoreGift` P, `Premium` P, `Quest` P, `QuestNotifications` P, `QuestTracker` P, `Rank` P, `RankerMessageWindow` P, `Rebirth` P, `Repair` P, `Report` P, `Repurchase` P, `ResetConfirm` P, `Roulette` P, `ScenerySystem` P, `Seal` P, `SeekParty` P, `Selection` P, `ShoppingMall` P, `Shout` P, `Siege` P, `SiegeTowerSystem` P, `SkillCastSystem` P, `SkillPreset` P, `SoccerSystem` P, `SpecialAuctionWindow` P, `TimedItemsWindow` P, `TitlePicker` P, `TopIcons` P, `Tournament` P, `TownRecall` P, `Upgrade` P, `UpgradeNotice` P, `UserInfo` P, `UserVoteWindow` P, `VanguardWindow` P, `Vendor` P, `VipWarehouse` P, `Warehouse` P, `Warp` P, `Whispers` P, `WorldReady` P, `ZoneAbility` P, `ZoneId` P | ClassicGenieQuick.cs, ClassicLotterySkin.cs, ClassicTournamentSkin.cs, ClassicWorldOverlays.cs, FamiliarTrainer.cs, FamiliarWindow.cs, NativeAnvil.cs, NativeAppearance.cs, NativeBoards.cs, NativeCape.cs, NativeCharacterPanel.cs, NativeChat.cs, NativeChatReactions.cs, NativeChatRoom.cs, NativeCommandList.cs, NativeCommunication.cs, NativeContextMenus.cs, NativeEquipView.cs, NativeEventWindows.cs, NativeEvents.cs, NativeGenieCommands.cs, NativeGenieQuick.cs, NativeHudChat.cs, NativeIdentity.cs, NativeInventory.cs, NativeItems.cs, NativeKing.cs, NativeMapFortune.cs, NativeMarket.cs, NativeMerchant.cs, NativeNpc.cs, NativeNpcOpening.cs, NativeOverlays.cs, NativeParty.cs, NativePremium.cs, NativeProgression.cs, NativeQuestHud.cs, NativeQuestKey.cs, NativeQuestLog.cs, NativeRebirth.cs, NativeRepurchase.cs, NativeServices.cs, NativeSiege.cs, NativeSocial.cs, NativeStorage.cs, NativeTrade.cs, NativeUserVote.cs, NativeVendor.cs |
| `AchievementFeature` | `achCategoryBox` F, `achClaimablePerTab` F, `AchHideClaimed` P, `achList` F, `achListRow` F, `achScroll` F, `achSearch` F, `AchStatus` P, `achSummary` F, `achSummaryPage` F, `achSummaryTabButton` F, `achTabButtons` F, `achTabs` F, `achTitleBanner` F, `achVisible` F, `Shown` P, `Tab` P, `ToggleAchievements` M, `ViewingSummary` P | NativeHudChat.cs, NativeProgression.cs |
| `AchievementTrophyFeature` | `badge` F, `Trophy` P | NativeHudChat.cs |
| `AttendanceFeature` | `bonusRow` F, `ClaimableCount` M, `countLabel` F, `Gift` P, `giftBadge` F, `Grid` P, `notice` F, `shown` F, `slots` F, `states` F, `Toggle` M | NativeHudChat.cs, NativeProgression.cs |
| `BagCompanionFeature` | `Pairing` P, `RefreshFit` M, `RefuseItemInUse` M | NativeAnvil.cs, NativeInventory.cs, NativeServices.cs, NativeStorage.cs, NativeTrade.cs, NativeVendor.cs |
| `BasicAttackSystem` | `AutoAttack` P | NativeHudChat.cs |
| `BattleEventFeature` | `battleeventBanner` F, `BattleeventBoard` P, `battleeventResult` F | NativeOverlays.cs |
| `BifrostFeature` | `banner` F, `bar` F, `inZoneLeaveAsk` F, `inZoneLeaveBanner` F, `inZoneLeaveBtn` F, `inZoneLeaveTitleLbl` F, `joinModal` F, `joinModalBar` F, `joinModalBtn` F, `joinModalCloseBtn` F, `joinModalCount` F, `joinModalStatus` F, `joinModalTimer` F, `joinModalTitle` F, `OnInZoneLeaveConfirmed` M, `SecondsLeft` M, `timerLbl` F, `titleLbl` F, `UrgentSecs` static F | NativeEvents.cs, NativeOverlays.cs |
| `BorderDefenseWarFeature` | `BdwScoreBanner` P | NativeOverlays.cs |
| `BountyFeature` | `listBox` F, `rewardEdit` F, `targetEdit` F | NativeBoards.cs |
| `CameraSystem` | `StartCameraHalfTurn` M | NativeHudChat.cs |
| `CapeFeature` | `blueSlider` F, `buyBtn` F, `bVal` F, `CellTip` M, `choice` F, `chosenLbl` F, `Close` M, `current` F, `greenSlider` F, `gVal` F, `hint` F, `layer` F, `NeedName` static M, `noteLbl` F, `OnResult` M, `pattern` F, `previewing` F, `priceLbl` F, `redSlider` F, `reqLbl` F, `requestInFlight` F, `rVal` F, `SetStatus` M, `shown` F, `status` F, `Swatch` static M, `ticket` F, `Toggle` M | NativeCape.cs |
| `CapeSession` | `CapeResetEvent` E | NativeCape.cs |
| `ChallengeFeature` | `askDialog` F | NativeOverlays.cs |
| `ChangeHairFeature` | `beautyConfirm` F, `BeautyEditor` P, `BeautyInFlight` P, `BeautyPreview` P, `CloseHairPalette` M, `hairPalettePicker` F, `hairPaletteTarget` F, `status` F | NativeAppearance.cs |
| `ChaosFeature` | `NestTimerPanel` P | NativeOverlays.cs |
| `CharacterSealFeature` | `CsCodePage` P, `CsCodeStars` P, `csKeys` F, `CsSealingPage` P, `csSelectOk` F, `CsSelectPage` P, `CsSelectPrompt` P, `CsSelectTitle` P, `csSheetAchievements` F, `csSheetCells` F, `csSheetGold` F, `csSheetLevel` F, `csSheetName` F, `csSheetStats` F, `csSlotButtons` F, `CsSocket` P, `CsTransGold` P, `CsTransHeadline` P, `csTransShown` F, `FitAt` M, `Picked` P, `TakeFromBag` M | NativeItems.cs, NativeServices.cs |
| `ChatHostFeature` | `colorsDraft` F, `nearbyFirstPoll` F, `NearbyRows` P, `palette` F, `paletteSlot` F, `RebuildNearby` M | NativeChat.cs |
| `ChatNoticesFeature` | `orderMessage` F | NativeOverlays.cs |
| `ChatReactionsFeature` | `ReactionLayer` P | NativeChatReactions.cs |
| `ChatRoomFeature` | `roomBlink` F, `roomChannelsButton` F, `RoomComposer` P, `roomConfigButton` F, `roomDeafen` F, `roomJoin` F, `roomLayer` F, `roomLeaderBox` F, `roomLeaderHandOver` F, `roomLeaderKick` F, `roomMembersColumn` F, `roomMembersToggle` F, `roomMic` F, `roomRosterEmpty` F, `roomRosterRows` F, `RoomThread` P, `roomVoiceGear` F, `roomVoiceLeave` F, `roomVoiceRow` F, `roomVolume` F | NativeChatRoom.cs |
| `ClanBattleFeature` | `panel` F | NativeOverlays.cs |
| `ClanCreateFeature` | `messageLabel` F, `nameInput` F, `notice` F | NativeIdentity.cs |
| `ClanFeature` | `allianceAsk` F, `allianceClans` F, `allianceNotice` F, `ApplyMy` M, `AskConfirm` M, `confirmAsk` F, `Content` P, `ctxMember` F, `disbandAsk` F, `EnsureLoaded` M, `inviteAsk` F, `LeaveAsk` P, `memberMenu` F, `members` F, `OnAllianceButton` M, `OnLeave` M, `OnMemberMenuAction` M, `OpenMemo` M, `RefreshTab` M, `removeAsk` F, `ShowTab` M, `status` F | NativeCharacterPanel.cs, NativeContextMenus.cs |
| `ClanPointsFeature` | `Toggle` M | NativeCharacterPanel.cs |
| `ClanPremiumFeature` | `chip` F, `dot` F, `inClan` F, `label` F | NativePremium.cs, NativeQuestHud.cs |
| `ClanWarehouseFeature` | `amountPrompt` F, `AskWithdraw` M, `CanDropOn` M, `CanWithdraw` P, `cells` F, `ClanBagFit` M, `companion` F, `depositBtn` F, `DepositSlot` M, `DropOn` M, `money` F, `page` F, `shown` F, `slots` F, `status` F, `TakeFromBag` M, `TurnPage` M, `withdrawBtn` F | NativeStorage.cs |
| `CollectionRaceFeature` | `crCollectBlock` F, `crCollectBox` F, `crCompleteBanner` F, `crCompleteTitle` F, `crCompletingLabel` F, `crHuntBlock` F, `crHuntBox` F, `crRewardsBlock` F, `crRewardsBox` F, `crTimerDigits` F, `crWinnersLabel` F, `crWinnersRow` F | NativeEventWindows.cs |
| `CombatLogFeature` | `Notice` M | NativeCharacterPanel.cs, NativeServices.cs |
| `CommandListFeature` | `cmdShown` F, `entries` F, `FillEntries` M, `Groups` P, `UnavailableColour` static F | NativeCommandList.cs |
| `DailyRankFeature` | `drBlink` F, `drIcon` F, `OnIcon` M, `Window` P | NativeHudChat.cs |
| `DeathDialogFeature` | `Panel` P | NativeOverlays.cs |
| `DisguiseFeature` | `formRows` F, `groupRows` F, `note` F | NativeIdentity.cs |
| `EntityLooksSystem` | `AttachWeapons` M, `Dress` M, `MobIndex` P, `ResolveMobScene` M, `SelfGear` M | FamiliarWindow.cs, NativeCape.cs, NativeNpc.cs |
| `EntitySystem` | `Ents` P | NativeCharacterPanel.cs, NativeNpc.cs |
| `EntityView` | `Body` F, `Dead` F, `Gear` F, `IsNpc` F, `Level` F, `ModelId` F, `Name` F, `NpcId` F, `NpcType` F, `Race` F, `Size` F | NativeCharacterPanel.cs, NativeNpc.cs |
| `EquipViewFeature` | `pending` F, `RequestEquipmentView` M, `shown` F, `status` F | NativeEquipView.cs |
| `EventsFeature` | `alert` F, `iconButton` F, `Toggle` M, `Window` P | NativeHudChat.cs |
| `EventTitlesFeature` | `TitleColumn` P | NativeOverlays.cs |
| `ExchangeFeature` | `addInFlight` F, `amountHint` F, `AmountIcon` P, `amountLayer` F, `amountMax` F, `amountName` F, `amountShown` F, `amountSlot` F, `amountSpin` F, `AnswerRequest` M, `askDialog` F, `CloseAmount` M, `ConfirmAmount` M, `confirmBtn` F, `ConfirmedByMe` P, `ConfirmedByPartner` P, `goldEdit` F, `MyGoldOffer` P, `MyOffer` P, `OfferSlot` M, `OnAddGold` M, `OnConfirm` M, `Panel` P, `PartnerName` P, `requestPending` F, `SetStatus` M, `shown` F, `status` F, `TheirGoldOffer` P, `TheirOffer` P, `waitLayer` F | NativeTrade.cs |
| `ExpSealFeature` | `amountLabel` F, `JarRect` P, `offButton` F, `onButton` F, `percent` F, `Pick` P | NativeItems.cs |
| `FishingHallFeature` | `fishHallList` F | NativeBoards.cs |
| `ForcesFeature` | `angerBar` F, `joinBtn` F, `leaveBtn` F, `pointsLbl` F, `rankLbl` F, `stateLbl` F | NativeBoards.cs |
| `FortuneFeature` | `cards` F, `face` F, `Final` P, `lines` F, `name` F, `okButton` F, `Pick` P, `reveal` F, `spin` F, `Stage` P, `stars` F | NativeMapFortune.cs |
| `FrameClock` | `Now` static M | FamiliarWindow.cs |
| `FriendsFeature` | `DoFriendAdd` M, `EnsureLoaded` M, `friendAddInput` F, `FriendDetailLine` static M, `friends` F, `friendStatus` F | NativeCharacterPanel.cs |
| `GameCursor` | `applied` static F, `enabled` static F, `Peek` static M | ClassicRepairHammer.cs |
| `GatherSystem` | `gatherChip` F | NativeOverlays.cs |
| `GenderChangeFeature` | `confirm` F, `Editor` P, `Preview` P, `status` F | NativeAppearance.cs |
| `GenieFeature` | `autoParty` F, `claimBtn` F, `Close` M, `cureCurse` F, `deadline` F, `genieParty` F, `hideRange` F, `hpThreshold` F, `huntInPlace` F, `modes` F, `monsterList` F, `mpThreshold` F, `Panel` P, `petPercent` F, `petPotion` F, `Points` P, `ptCode` F, `range` F, `recoverySpin` F, `RequestPending` P, `returnHome` F, `RunLabel` P, `shown` F, `SkillPicker` P, `slots` F, `start` F, `status` F, `tipLabel` F, `Toggle` M, `ToggleRun` M | NativeGenieCommands.cs, NativeGenieQuick.cs, NativeHudChat.cs, NativeProgression.cs |
| `GenieHammerFeature` | `hammerStatus` F, `hammerThreshold` F | NativeProgression.cs |
| `GeniePotionsFeature` | `hpSelect` F, `mpSelect` F, `petSelect` F, `PotionPicker` P | NativeProgression.cs |
| `GenieScrollsFeature` | `autoScrolls` F, `scrollButtons` F, `scrollPicker` F, `scrollStatus` F | NativeProgression.cs |
| `GenieSubFeature` | `RefreshSub` M, `Sub` P, `ToggleFromSub` M | ClassicGenieQuick.cs, NativeCommandList.cs, NativeGenieCommands.cs, NativeOverlays.cs |
| `GenieTownFeature` | `other` F, `SpiritOfItem` static F | NativeGenieCommands.cs, NativeProgression.cs |
| `GlobalMapFeature` | `list` F, `NationName` static M | NativeEquipView.cs, NativeMapFortune.cs |
| `HotbarFeature` | `hotLoadedFromServer` F, `HotSelected` P, `SelectHotAbs` M, `Slots` P, `Usable` M | NativeHudChat.cs |
| `HotkeysFeature` | `hotkeys` F, `padHotkeys` F | NativeQuestKey.cs |
| `HudLayout` | `defaultPosition` F, `dragging` F, `dragMoved` F, `Frozen` P, `id` F, `MinimumSize` P, `Place` M, `ReapplyDefault` M, `resizeGrip` F, `resizeOriginMouse` F, `resizeOriginPosition` F, `resizeOriginSize` F, `resizing` F | FamiliarBar.cs, NativeCharacterNpc.cs, NativeHudChat.cs, NativeLayout.cs |
| `HudWindow` | `dock` F, `header` F | NativeHudChat.cs, NativeUi.cs |
| `InventoryFeature` | `AskDeleteItem` M, `CarriedWeight` M, `ConfirmDeleteItem` M, `HideDeletePrompt` M, `InvDelPanel` P, `invDelQueue` F, `MoveBetween` M, `moveInFlight` F, `moveQueue` F, `SlotAt` M | NativeAnvil.cs, NativeInventory.cs, NativeVendor.cs |
| `ItemCombineFeature` | `button` F, `footer` F, `result` F, `shown` F, `slots` F, `strip` F | NativeServices.cs |
| `ItemSlotView` | `countLabel` F, `DoubleClicked` E, `lockIcon` F, `RightClicked` E | NativeStorage.cs, NativeUi.cs, NativeVendor.cs |
| `ItemSmashFeature` | `cancelBtn` F, `message` F, `results` F, `shown` F, `SmashBagFit` M, `socket` F, `startBtn` F, `stopBtn` F, `subMessage` F, `TakeFromBag` M | NativeServices.cs |
| `ItemTooltipFeature` | `Hide` M, `Show` M, `ShowItem` M, `TipPanel` P | FamiliarTrainer.cs, FamiliarWindow.cs, NativeAnvil.cs, NativeChat.cs, NativeEquipView.cs, NativeServices.cs, NativeStorage.cs, NativeTrade.cs |
| `ItemUpgradeSession` | `UpgradeOpenEvent` E | NativeAnvil.cs |
| `KillSplashFeature` | `killSplash` F | NativeOverlays.cs |
| `KingElectionFeature` | `ballotText` F, `boxOpen` F, `busy` F, `nominateText` F, `NomineeEdit` P, `options` F, `planBusy` F, `planCount` F, `PlanEdit` P, `senatorsBox` F, `senatorsLabel` F, `speechLower` F, `speechUpper` F, `voteBusy` F, `voteRows` F | NativeKing.cs |
| `KingTreasuryFeature` | `boxOpen` F, `nationIntroEdit` F, `nationTaxBusy` F, `nationTaxNotice` F, `nationTaxNotice2` F, `nationTaxOptions` F, `nationTaxRateBusy` F, `nationTaxRateValue` F, `StepNationTaxRate` M | NativeIdentity.cs, NativeKing.cs |
| `KnightRoyaleFeature` | `areaBanner` F, `RoyaleBoard` P | NativeOverlays.cs |
| `LookEditor` | `colourPicker` F, `raceButtons` F | NativeAppearance.cs, NativeLook.cs |
| `LookPreview` | `PlaceCamera` M | NativeLook.cs |
| `LootFeature` | `cells` F, `Close` M, `Panel` P, `prompt` F | NativeOverlays.cs |
| `LootFilterFeature` | `ruleItem` F, `ruleMenu` F | NativeContextMenus.cs |
| `LotteryFeature` | `buyBtn` F, `Close` M, `costLabel` F, `myTicketsLabel` F, `playerHeader` F, `Render` M, `reqIcon` F, `rewardsBox` F, `soldLabel` F, `State` P, `statusLabel` F, `timerLabel` F, `winnersLabel` F | ClassicLotterySkin.cs, NativeEventWindows.cs |
| `MailComposeFeature` | `AttachmentDrop` P, `Attachments` P, `attachRows` F, `attachTitle` F, `BodyInput` P, `bodyRemaining` F, `composeStatus` F, `Gold` P, `Recipient` P, `recipientSuggestions` F, `sendBtn` F, `SubjectInput` P | NativeCommunication.cs |
| `MailFeature` | `claimBtn` F, `deleteBtn` F, `list` F, `listScroll` F, `mails` F, `readAttachments` F, `readAttachmentScroll` F, `readAttachmentTitle` F, `readBody` F, `readMeta` F, `readSubject` F, `selectedId` F, `shown` F, `status` F, `Toggle` M, `unreadOnlyToggle` F, `unreadPill` F | NativeCommunication.cs, NativeHudChat.cs |
| `MailIconFeature` | `IconButton` P | NativeHudChat.cs |
| `MainWindowsFeature` | `characterPages` F, `Hide` M, `IsOpen` M, `layer` F, `QueueDockLayout` M, `ShowCharacterPage` M, `SyncState` M, `Windows` P | NativeAnvil.cs, NativeCharacterPanel.cs, NativeHudChat.cs, NativeServices.cs, NativeStorage.cs, NativeTrade.cs |
| `MapFeature` | `fullShown` F, `MiniMap` P, `ToggleFull` M | NativeHudChat.cs |
| `MarketPriceFeature` | `ClearMarketPriceChart` M, `marketPriceCache` F, `marketPriceChart` F, `marketPriceItem` F, `marketPriceName` F, `MarketPriceNoHistoryText` static F, `MarketPricePanel` P, `MarketPriceSearch` P, `marketPriceShown` F, `marketPriceSlot` F, `marketPriceStatus` F, `marketPriceTrades` F, `marketPriceUpdated` F, `SetMarketPriceStatus` M | NativeMarket.cs, NativeMerchant.cs |
| `MerchantBuyFeature` | `SellToWanted` M, `SetWantedStatus` M, `wantedBagCells` F, `wantedCells` F, `wantedItems` F, `wishCells` F | NativeMerchant.cs |
| `MerchantFeature` | `AcceptAmount` M, `amountAccept` F, `amountConfirmBtn` F, `amountCount` F, `amountCountRow` F, `AmountLayer` P, `amountMarketHint` F, `amountMarketRow` F, `AmountPrice` P, `amountPriceFixed` F, `AskStallPrice` M, `CloseAmountPrompt` M, `ConfirmSellStall` M, `myStall` F, `myStallSrc` F, `SellAdvert` P, `sellBagCells` F, `SellStallCells` P, `SellStallPanel` P, `ServiceLayer` P, `SetSellStatus` M, `shopBagCells` F, `shopCells` F, `StageStallItem` M, `UnstageStallItem` M | NativeMerchant.cs, NativeServices.cs |
| `MerchantSearchFeature` | `search` F, `searchEdit` F, `searchLines` F, `searchLoading` F, `searchPages` F, `searchScope` F, `searchStatus` F | NativeMarket.cs |
| `MerchantStallsFeature` | `FreeStallSign` static M, `RefreshStallSign` M, `Stalls` P, `stallSignLayer` F | NativeMerchant.cs |
| `MessengerFeature` | `msgrList` F, `msgrTextInput` F, `msgrToInput` F | NativeCommunication.cs |
| `MessengerSession` | `MessengerListEvent` E | NativeCommunication.cs |
| `ModelAnimation` | `ForceDoubleSidedOnce` static M, `ModelNodeName` static F | FamiliarWindow.cs, NativeNpc.cs |
| `ModelNodes` | `AttachBuiltInPlugs` static M | NativeNpc.cs |
| `MsgOutFeature` | `msgOut` F | NativeOverlays.cs |
| `NameChangeFeature` | `btn` F, `clan` F, `Close` M, `edit` F, `hint` F, `status` F | NativeIdentity.cs |
| `NationChannelsFeature` | `ncPrivateRows` F, `ncPrivateTab` F, `ncPublicRows` F, `ncPublicTab` F, `ncPubOpen` F, `ncRefresh` F, `ncSearchEdit` F | NativeSocial.cs |
| `NationTransferFeature` | `CancelTransferConfirmation` M, `Open` M, `SelectTransferCharacter` M, `Send` M, `SetTransferLocked` M, `SetTransferStatus` M, `transfer` F, `transferCompleted` F, `transferConfirm` F, `transferEditor` F, `transferHeader` F, `transferInFlight` F, `transferLayer` F, `transferList` F, `transferNotice` F, `TransferPanel` P, `transferPicks` F, `transferPreview` F, `transferRevision` F, `transferRows` F, `transferShown` F, `transferStatus` F | NativeAppearance.cs |
| `NationTransferSession` | `Candidates` P, `NationTransferOpenEvent` E | NativeAppearance.cs |
| `NereidFeature` | `FlagBoard` P, `ScoreBoard` P, `Strip` P | NativeOverlays.cs |
| `Notice` | `request` F | NativeAppearance.cs, NativeIdentity.cs |
| `NpcDialogFeature` | `Close` M, `menuBox` F, `Panel` P, `QuestContent` P, `questRewardChoice` F, `shown` F | NativeAppearance.cs, NativeNpc.cs, NativeNpcOpening.cs |
| `PartyFeature` | `hudMenu` F | NativeContextMenus.cs |
| `PartyInvitesFeature` | `inviteAskDialog` F, `OpenInvitePrompt` M | NativeOverlays.cs, NativeParty.cs |
| `PetFeature` | `attackBtn` F, `bagCells` F, `defendBtn` F, `dismissBtn` F, `expBar` F, `feedBtn` F, `hpBar` F, `levelLbl` F, `lootBtn` F, `mpBar` F, `nameLbl` F, `satBar` F, `status` F | FamiliarWindow.cs |
| `PetHatchFeature` | `btn` F, `Close` M, `eggPick` F, `inFlight` F, `IsPetEgg` static M, `IsValidPetName` static M, `layer` F, `NameInput` P, `npc` F, `page` F, `petScrollSlot` F, `petTransformPage` F, `petTransformPetPick` F, `petTransformScrollPick` F, `petTransformSlot` F, `RefreshPetPickLooks` M, `SetStatus` M, `shown` F, `slot` F, `status` F, `tabs` F | FamiliarTrainer.cs |
| `PetSkillBarFeature` | `MyPetEntity` M, `PetCooldown` M, `UsePetSkill` M | FamiliarWindow.cs |
| `PieceChangeFeature` | `busy` F, `message` F, `Place` M, `position` F, `resultSockets` F, `shown` F, `socket` F, `spinning` F, `startBtn` F, `stopBtn` F, `subMessage` F, `talkBtn` F | NativeServices.cs |
| `PlayerMenuFeature` | `InviteToParty` M, `ShowByName` M | NativeCharacterPanel.cs, NativeChat.cs |
| `PluginBridgeFeature` | `LogAdd` M | NativeVendor.cs |
| `PluginGame` | `inventory` F | NativeInventory.cs |
| `PluginUi` | `HudHidden` M | NativeChat.cs |
| `PowerUpStoreCartView` | `cartEmpty` F, `cartLines` F, `cartScroll` F, `cartStatus` F, `cartTitle` F, `CartWidth` static F, `checkout` F, `clearCart` F, `giftBanner` F, `giftButton` F, `giftInfo` F, `giftName` F, `shortfall` F, `totalRow` F, `WellMargin` static F | NativeMarket.cs |
| `PowerUpStoreCatalogView` | `cards` F, `cashValue` F, `Category` P, `categoryList` F, `CategoryWidth` static F, `grid` F, `gridEmpty` F, `gridScroll` F, `gridTitle` F, `hoveredCard` F, `searchInput` F, `sortPick` F | NativeMarket.cs |
| `PowerUpStoreDetailsView` | `detailsAdd` F, `detailsBox` F, `detailsBuy` F, `detailsCategory` F, `detailsDescription` F, `detailsIcon` F, `detailsName` F, `detailsPrice` F, `detailsStatus` F | NativeMarket.cs |
| `PowerUpStoreGiftFeature` | `giftBox` F, `giftCard` F, `giftCardInfo` F, `giftCardName` F, `giftCardWarning` F, `giftList` F, `giftListTitle` F, `giftPickPane` F, `GiftSearch` P, `giftStatus` F | NativeMarket.cs |
| `PowerUpStoreModel` | `Cart` P, `Catalog` P, `Categories` P, `Pending` P | NativeMarket.cs |
| `PremiumFeature` | `Chip` P, `ClanBonusChip` P, `clanBonusMarks` F, `dot` F, `label` F | NativePremium.cs, NativeQuestHud.cs |
| `PriceChart` | `AxisFontSize` static F, `AxisWidth` static F, `BarEdge` static F, `BarFill` static F, `BarHover` static F, `BarWidth` static F, `Baseline` static F, `bottom` F, `Column` M, `DayFontSize` static F, `days` F, `hoverDay` F, `hoverPart` F, `MarkerHalf` static F, `MarkerHeight` static F, `MaxColor` static F, `Midline` static F, `MinColor` static F, `Plot` P, `PlotPadding` static F, `top` F, `ValueY` M | NativeMarket.cs |
| `QuantityPrompt` | `Confirm` M | NativeUi.cs |
| `QuestFeature` | `abandonBtn` F, `AbandonSelected` M, `completeBtn` F, `CompleteSelected` M, `detailBody` F, `detailEmpty` F, `filter` F, `InFilter` M, `kind` F, `QuestsInView` M, `RefreshDetail` M, `rewardBox` F, `rewardOptionBox` F, `rewardOptionTitle` F, `rewardTitle` F, `Selected` P, `SetFilter` M, `SetKind` M, `ToggleTrackSelected` M, `trackBtn` F | NativeCharacterPanel.cs, NativeQuestLog.cs |
| `QuestNotificationsFeature` | `notificationIndex` F, `QuestNotifications` P | NativeNpc.cs |
| `QuestRewardSelection` | `Chosen` M | NativeQuestLog.cs |
| `QuestRows` | `ItemIcon` static M, `ItemRow` static M | NativeQuestLog.cs |
| `QuestTrackerFeature` | `trackerBox` F, `trackerCount` F, `trackerFold` F, `trackerHeader` F, `TrackerPanel` P | NativeQuestHud.cs |
| `RankerMessageFeature` | `Paper` P | NativeOverlays.cs |
| `RankFeature` | `ladderElMorad` F, `ladderKarus` F, `ladderOwnElMorad` F, `ladderOwnKarus` F | NativeProgression.cs |
| `RebirthFeature` | `addBtns` F, `bonusLbls` F, `btn` F, `inFlight` F, `layer` F, `levelLbl` F, `OnPressed` M, `pick` F, `pickLbls` F, `pointsLbl` F, `RefreshUI` M, `removeBtns` F, `resetMode` F, `SetStatus` M, `shown` F, `status` F | NativeRebirth.cs |
| `RepairFeature` | `All` M, `Close` M, `CostAt` M, `footer` F, `InFlight` P, `IsRepairable` M, `queue` F, `RepairableSlots` M, `shown` F, `TakeFromBag` M | NativeServices.cs |
| `ReportFeature` | `eligible` F, `fileBtn` F, `gateLbl` F, `nextBtn` F, `pageLbl` F, `prevBtn` F, `ReasonEdit` P, `status` F, `TargetEdit` P, `voteList` F | NativeBoards.cs |
| `RepurchaseFeature` | `rbBuy` F, `rbEmpty` F, `rbNames` F, `rbPrices` F, `RbScroll` P, `rbTimes` F | NativeRepurchase.cs |
| `ResetConfirmFeature` | `reset` F | NativeServices.cs |
| `RouletteFeature` | `coins` F, `coinsLabel` F, `OnSpinPressed` M, `resultLabel` F, `spinBtn` F | NativeEventWindows.cs |
| `ScenerySystem` | `ClockLabel` P | NativePremium.cs |
| `SealFeature` | `codeField` F, `codeRow` F, `confirm` F, `FitAt` M, `gold` F, `headline` F, `Pad` P, `prompt` F, `shown` F, `socket` F, `TakeFromBag` M, `waiting` F | NativeServices.cs |
| `SeekPartyFeature` | `fullMoonRow` F, `seekEntries` F, `seekFilter` F, `seekFilters` F, `seekHeading` F, `seeking` F, `seekListBox` F, `seekPage` F, `seekPageLbl` F, `seekRegisterBtn` F, `SeekShown` P, `seekStatusLbl` F, `seekTotal` F, `SeekZoneName` static M, `ToggleSeek` M, `WantedClasses` static F, `wantedClassOpt` F, `wantedMsgInput` F | NativeHudChat.cs, NativeParty.cs |
| `SelectionFeature` | `SelectedId` P | NativeCharacterPanel.cs |
| `SelfAvatar` | `Body` F, `Dead` F, `Running` F, `Sitting` F, `Visual` F | FamiliarTrainer.cs, FamiliarWindow.cs, NativeAppearance.cs, NativeCape.cs, NativeChat.cs, NativeHudChat.cs, NativeRebirth.cs, NativeVendor.cs |
| `ServiceTabs` | `buttons` F | FamiliarTrainer.cs |
| `ShoppingMallFeature` | `AddDetailsToCart` M, `CartView` P, `CatalogView` P, `DetailsView` P, `FitPowerUpStore` M, `modal` F, `Model` P, `OpenDetails` M, `overlay` F, `shown` F, `Toggle` M | NativeHudChat.cs, NativeMarket.cs |
| `ShoutFeature` | `label` F | NativeOverlays.cs |
| `SiegeFeature` | `apply` F, `cancel` F, `challengerRows` F, `chosen` F, `defenderRows` F, `fee` F, `period` F, `scheduleRows` F, `signedUp` F, `taxRatePrompt` F, `taxRateValue` F | NativeIdentity.cs, NativeSiege.cs |
| `SiegeTowerSystem` | `DropOff` P, `TowerButton` P | NativeOverlays.cs |
| `SkillCastSystem` | `SelfTransformModel` M | NativeHudChat.cs |
| `SkillPresetFeature` | `presetStatPointsLbl` F, `TogglePreset` M | NativeCharacterPanel.cs, NativeQuestLog.cs |
| `SoccerSystem` | `soccerBoard` F | NativeOverlays.cs |
| `SpecialAuctionFeature` | `auctionBidList` F, `AuctionCheckInput` P, `auctionChecks` F, `auctionClock` F, `auctionCurrent` F, `auctionGold` F, `auctionLog` F, `auctionLotSlots` F, `AuctionMillions` P, `auctionMinimum` F, `auctionPages` F, `auctionPlaceBid` F, `auctionRefresh` F, `auctionRowButtons` F, `auctionSchedule` F, `auctionSelectedName` F, `auctionStatus` F, `auctionTabButtons` F, `auctionTotal` F, `auctionWinList` F, `auctionWords` F, `NoHistoryText` static F | NativeMarket.cs |
| `StatBar` | `frac` F | ClassicEventFamily.cs |
| `TempleSignUpBoard` | `Current` P, `Registered` P, `SecondsLeft` M | NativeOverlays.cs |
| `TimedItemsFeature` | `tiHideToday` F, `tiNames` F, `tiPlaces` F, `tiRows` F, `tiScroll` F, `tiSlots` F, `tiTimes` F | NativeItems.cs |
| `TitlePickerFeature` | `Toggle` M | NativeCharacterPanel.cs |
| `TopIconBadge` | `Blink` P | NativeHudChat.cs |
| `TopIconsFeature` | `Icon` M, `Place` M, `powerUpStoreIcon` F | NativeHudChat.cs, NativePremium.cs |
| `TournamentFeature` | `countLbl` F, `regBtn` F, `registered` F, `roundLbl` F, `stateLbl` F, `unregBtn` F | ClassicTournamentSkin.cs, NativeEventWindows.cs |
| `TownRecallFeature` | `dialog` F | NativeOverlays.cs |
| `UpgradeFeature` | `accessoryResultSocket` F, `accessorySockets` F, `anvilBench` F, `anvilCompanion` F, `anvilFooter` F, `AnvilId` P, `btn` F, `ClearSocket` M, `ClearSockets` M, `Close` M, `DismissConfirm` M, `InteractionLocked` P, `IsTarget` static M, `itemIds` F, `itemResultSocket` F, `itemSockets` F, `layer` F, `OnBenchChanged` M, `OpenAnvilBench` M, `pendingResult` F, `PlacementError` M, `positions` F, `scanTween` F, `Session` P, `ShowAnvilPrompt` M, `shown` F, `sockets` F | NativeAnvil.cs |
| `UpgradeNoticeFeature` | `label` F | NativeOverlays.cs |
| `UserInfoFeature` | `RequestInformation` M | NativeCharacterPanel.cs |
| `UserVoteFeature` | `askShown` F | NativeUserVote.cs |
| `VanguardFeature` | `Layer` P, `wantedOpen` F, `wantedToggle` F | ClassicWorldOverlays.cs, NativeOverlays.cs |
| `VendorFeature` | `buy` F, `BuyAmount` M, `BuyRoom` M, `CanBuy` M, `Catalogue` P, `CellIds` P, `Cells` P, `Close` M, `companion` F, `Currency` P, `entries` F, `footer` F, `group` F, `LoyaltyShop` P, `Panel` P, `selected` F, `SelectItem` M, `SellSlot` M, `shown` F, `ShowPage` M, `TradePrompt` P, `Wallet` P | NativeAnvil.cs, NativeVendor.cs |
| `VipWarehouseFeature` | `amountPrompt` F, `AskWithdraw` M, `CanDropOnVault` M, `cells` F, `clearPinBtn` F, `companion` F, `DepositSlot` M, `DropOnVault` M, `expiryLbl` F, `IsVaultKey` static M, `keyCell` F, `open` F, `OpenVaultFromBag` M, `pinDlg` F, `pinEdit` F, `pinPrompt` F, `pinSet` F, `setPinBtn` F, `shown` F, `Status` P, `StoredItems` P, `SubmitPin` M, `TakeFromBag` M, `VaultKeys` static F, `VipBagFit` M | NativeInventory.cs, NativeSkills.cs, NativeStorage.cs |
| `WarehouseFeature` | `AmountPrompt` P, `AskWithdraw` M, `CanDropOn` M, `Cells` P, `companion` F, `depositBtn` F, `DepositSlot` M, `DropOn` M, `Money` P, `shown` F, `Status` P, `StorageTransferBusy` P, `StoredItems` P, `TakeFromBag` M, `WhBagFit` M, `withdrawBtn` F | NativeStorage.cs |
| `WarpFeature` | `desc` F, `goldLbl` F, `image` F, `levelsLabel` F, `rows` F, `scroll` F, `Selected` P, `status` F, `travel` F | NativeServices.cs |
| `WeaponMounts` | `WeaponWearer` static M | NativeNpc.cs |
| `WhispersFeature` | `OpenWhisperWith` M, `whisperLayer` F, `Windows` P | FamiliarTrainer.cs, NativeCharacterPanel.cs, NativeChat.cs, NativeParty.cs |
| `ZoneAbilityFeature` | `ZoneabilityChip` P | NativeOverlays.cs |

### Preview only

The preview fixtures and audits also reach client internals that the plugin never touches at runtime. An upstream
rename there breaks an audit, not the game.

| Type | Members | Used in |
| --- | --- | --- |
| `WorldRoot` | `BuffSystem` P, `BuildEscapeOrder` M, `ChaosSkillWindow` P, `ClassChange` P, `CreatePluginAdapters` M, `CreatePluginGenie` M, `EntityStatesSystem` P, `escapeOrder` F, `ExpBar` P, `GameMenu` P, `HudLauncher` P, `loadingLayer` F, `MyId` P, `NpcInteraction` P, `PartyCommander` P, `Progression` P, `QuestReceipt` P, `QuestTarget` P, `StatusHud` P, `WarBoard` P | AnvilAudit.cs, ChatRoomAudit.cs, ClassicServicesAudit.cs, CommandFunctionAudit.cs, DetailsAudit.cs, EventWindowsAudit.cs, ExchangeAudit.cs, GenieAudit.cs, InventoryAudit.cs, MailAudit.cs, MapFortuneAudit.cs, MerchantAudit.cs, NpcOpeningAudit.cs, NpcPortraitAudit.cs, NpcServicesAudit.cs, PartyAudit.cs, PetWorldReconnectAudit.cs, PluginToggleAudit.cs, PreviewFixtures.cs, RepairAudit.cs, SkillAudit.cs, SkillRulesAudit.cs, StorageAudit.cs, UpstreamIntegrationAudit.cs, UserVoteAudit.cs, VendorAudit.cs |
| `AchievementSession` | `ApplyList` M, `ApplySummary` M, `Clear` M | ProgressionFixtures.cs |
| `AttendanceSession` | `NoteAutoOpened` M | ProgressionFixtures.cs |
| `BasicAttackSystem` | `AutoTargetId` P, `BasicAttackRange` M, `StopAutoAttack` M | GenieAudit.cs |
| `BattleEventFeature` | `battleeventBannerLbl` F, `battleeventResultLbl` F, `BuildUi` M | WorldOverlaysAudit.cs |
| `BifrostFeature` | `BuildUi` M, `CloseJoinModal` M, `OnDisband` M, `OnInZoneLeavePressed` M, `OnJoinResult` M, `OnTempleSignUps` M, `OnTime` M, `ShowSignUp` M | EventWindowsAudit.cs, ForgottenTempleAudit.cs, UpstreamNpcAudit.cs |
| `BorderDefenseWarFeature` | `BuildBdwScoreBanner` M, `HudLayer` P | WorldOverlaysAudit.cs |
| `BountyFeature` | `OnPost` M, `panel` F | EventsBAudit.cs, EventsBFixtures.cs |
| `BuffSystem` | `ContinuanceSkill` P, `TextCastingFailed` static F | SkillRulesAudit.cs |
| `Cape` | `table` static F | CapeAudit.cs |
| `CapeFeature` | `Dispose` M, `UpdateGate` M | CapeAudit.cs, IdentityAppearanceFixtures.cs |
| `CapeSession` | `Pending` P | CapeAudit.cs |
| `ChallengeFeature` | `BuildPrompt` M, `requestPending` F | WorldOverlaysAudit.cs |
| `ChangeHairFeature` | `Close` M, `Dispose` M, `hairPalette` F, `Open` M, `ShowBeautyLook` M | BeautyAudit.cs, IdentityAppearanceFixtures.cs |
| `ChangeHairSession` | `Request` P | IdentityAppearanceFixtures.cs |
| `ChaosSkillWindowFeature` | `chaosSkillIcons` F, `ChaosSkillPages` P, `ChaosSkillPageTick` M, `ChaosSkillsPerPage` static F, `FlipChaosSkillPage` M | EventWindowsAudit.cs, SkillRulesAudit.cs |
| `CharacterSealFeature` | `BuildTrans` M, `BuildWindow` M, `Close` M, `Layer` P, `OnSealedAchievements` M | ItemWindowsAudit.cs, NpcOpeningAudit.cs |
| `ChatHostFeature` | `BuildColors` M, `CloseColors` M, `colorsLayer` F, `ColorsWindow` P, `OpenColors` M | ChatColoursAudit.cs, HudChatFixtures.cs |
| `ChatReactionsFeature` | `Build` M, `Dispose` M, `OpenReactionBar` M, `OpenReactionDetails` M | ChatRoomAudit.cs, SocialAudit.cs |
| `ChatRoomFeature` | `Build` M, `BuildPeerPopup` M, `ChatRoomNotice` M, `Dispose` M, `RoomConfig` P, `roomConfigError` F, `roomConfigKeep` F, `roomConfigName` F, `RoomVoiceSettings` P, `StartBlink` M | ChatRoomAudit.cs, SocialAudit.cs |
| `ChatThreadView` | `log` F, `older` F, `PillOf` M, `ReactButtonOf` M | ChatRoomAudit.cs, SocialPromptsAudit.cs |
| `ClanBattleFeature` | `BuildBanner` M, `label` F | WorldOverlaysAudit.cs |
| `ClanCreateFeature` | `Build` M, `Close` M, `Submit` M | IdentityAudit.cs, NpcOpeningAudit.cs |
| `ClanFeature` | `BuildMineView` M, `dutyLbl` F, `fundLbl` F, `nameLbl` F, `noticeLbl` F, `OnAllianceList` M, `OnDonationList` M, `RenderMembers` M, `standingLbl` F, `tab` F | DetailsAudit.cs |
| `ClanPointsFeature` | `Build` M, `PointsShown` P | DetailsAudit.cs |
| `ClanPremiumFeature` | `BuildChip` M, `RefreshChip` M | PremiumQuestAudit.cs |
| `ClanPremiumSession` | `ClanPremiumActive` P, `ClanPremiumEndsAt` P | PremiumQuestAudit.cs |
| `ClanSession` | `ClanOnlineCount` P, `MyClan` P, `SeedPreview` M | CapeAudit.cs, DetailsAudit.cs, IdentityAppearanceFixtures.cs, StorageAudit.cs, UserVoteAudit.cs |
| `ClanWarehouseFeature` | `BuildPanel` M, `layer` F, `OnResult` M, `Panel` P, `Refresh` M | StorageAudit.cs |
| `ClassChangeFeature` | `BuildPanel` M, `OnNpc` M | ClassicServicesAudit.cs, NpcOpeningAudit.cs |
| `CollectionRaceFeature` | `OnClose` M, `OnCompleted` M, `OnCrTimerTick` M, `OnProgress` M, `Show` M | EventsAAudit.cs |
| `CombatLogFeature` | `log` F, `MaxLines` static F | CommandFunctionAudit.cs, SkillRulesAudit.cs |
| `CommandListFeature` | `Toggle` M, `Window` P | CommandFunctionAudit.cs |
| `Config` | `ChatColors` static P, `HotbarLocked` static P, `LegacyQuestFallback` static P, `SettingsSavePath` static P, `WindowsPath` static M | ChatColoursAudit.cs, CommandFunctionAudit.cs, QuestDetailAudit.cs, SkillRulesAudit.cs |
| `DailyRankFeature` | `drPicker` F, `LoadBoards` M | DetailsAudit.cs, EventWindowsAudit.cs |
| `DeathDialogFeature` | `ArmRespawnButton` M, `BuildDeathDialog` M, `Hide` M, `Layer` P, `MarkRespawnPending` M, `OnExpLoss` M, `pick` F, `RefreshRows` M, `RefreshText` M, `scrolls` F, `Show` M | WorldOverlaysAudit.cs |
| `DevelopSession` | `RebirthPending` P | RebirthAudit.cs |
| `DisguiseFeature` | `Dispose` M, `groups` F, `PickGroup` M | UpstreamNpcAudit.cs |
| `EntityStatesSystem` | `sitRequestPending` F | CommandFunctionAudit.cs |
| `EventsFeature` | `TickEventClocks` M, `tickIn` F | EventWindowsAudit.cs, HudButtonsAudit.cs |
| `EventTitlesFeature` | `Refresh` M | MykoHudAudit.cs |
| `ExchangeFeature` | `Abort` M, `BuildPanel` M, `layer` F, `OfferSlotAmount` M, `OnAddResult` M, `OnAgree` M, `OnCancel` M, `OnOtherAdd` M, `OnOtherDecide` M, `OnRequest` M, `Open` M, `ShowWait` M | ExchangeAudit.cs, VendorTradeMerchantFixtures.cs |
| `ExchangeSession` | `ExchangeOtherAddEvent` E | ExchangeAudit.cs |
| `ExpBarFeature` | `layer` F | PluginToggleAudit.cs |
| `ExpSealSession` | `ExpSealState` P | ItemWindowsAudit.cs |
| `FishingHallFeature` | `fishHallPanel` F | EventsBFixtures.cs |
| `ForcesFeature` | `panel` F | EventsBFixtures.cs |
| `FortuneFeature` | `Build` M, `Open` M, `PickCard` M, `Reveal` M | MapFortuneAudit.cs, UpstreamNpcAudit.cs |
| `FramedConn` | `Connected` F, `stream` F | QuestDetailAudit.cs |
| `FxParticles` | `shared` F | InventoryAudit.cs |
| `GameCursor` | `kind` static F | ClassicServicesAudit.cs, RepairAudit.cs |
| `GameMenuFeature` | `battleTimer` F, `exitArmed` F, `shown` F, `Toggle` M | CommandFunctionAudit.cs |
| `GameSession` | `NpcQuest` P, `QuestBook` P, `Self` P, `UserOption` P, `Vendor` P | BeautyAudit.cs, CommandFunctionAudit.cs, DetailsAudit.cs, FamiliarFixtures.cs, GenieAudit.cs, IdentityAppearanceFixtures.cs, IdentityAudit.cs, InventoryAudit.cs, MarketCommunicationFixtures.cs, MykoNpcAudit.cs, NpcIntegrationAudit.cs, NpcOpeningAudit.cs, NpcPortraitAudit.cs, PremiumQuestAudit.cs, PresetAudit.cs, SkillAudit.cs, SkillRulesAudit.cs, VendorAudit.cs |
| `GatherSystem` | `BuildGatherChip` M | EventOverlaysAudit.cs |
| `GenderChangeFeature` | `shown` F | NpcServicesAudit.cs |
| `GenieFeature` | `Genie` P, `Monsters` P, `RefreshMonsters` M, `RefreshSlots` M | GenieAudit.cs |
| `GenieSession` | `GenieRunning` P | CommandListAudit.cs, ProgressionFixtures.cs, SkillRulesAudit.cs |
| `GlobalMapFeature` | `Dispose` M, `Toggle` M | MapFortuneAudit.cs |
| `HotbarFeature` | `ChaosBattleLocked` P, `HotPage` P, `layer` F, `LocalPath` M, `SkillItemHeld` M | EventWindowsAudit.cs, PluginToggleAudit.cs, SkillRulesAudit.cs |
| `HotkeysFeature` | `keyActions` F | SkillAudit.cs, SkillRulesAudit.cs |
| `HudLauncherFeature` | `layer` F | PluginToggleAudit.cs |
| `HudLayout` | `ApplySavedLayout` M, `BeginDrag` M, `BeginResize` M, `CycleBackgroundOpacity` M | ChatDockAudit.cs, PetBarPlacementAudit.cs |
| `IconVeilView` | `wash` F | RepairAudit.cs |
| `InventoryFeature` | `BuildPanel` M, `moveCur` F, `OnItemMoveResult` M, `OnItemRemoveResult` M, `RefreshUI` M | AnvilAudit.cs, AnvilServiceFixtures.cs, ClassicServicesAudit.cs, ExchangeAudit.cs, InventoryAudit.cs, MerchantAudit.cs, NpcOpeningAudit.cs, PetEquipmentAudit.cs, PetFoodAudit.cs, StorageAudit.cs, UpstreamNpcAudit.cs, VendorAudit.cs |
| `ItemCombineFeature` | `AskCombineCount` M, `OpenCombineRecipeBook` M, `SelectCombineCategory` M, `SelectCombineRecipe` M | ClassicServicesAudit.cs, UpstreamNpcAudit.cs |
| `ItemData` | `items` static F, `pieces` static F, `sell` static F | ClassicServicesAudit.cs, ExchangeAudit.cs, HuntIconOptions.cs, MerchantAudit.cs, MerchantPalette.cs, MykoNpcAudit.cs, NpcDesignAudit.cs, NpcIntegrationAudit.cs, VendorAudit.cs |
| `ItemSlotView` | `Clicked` E | WorldOverlaysAudit.cs |
| `ItemsSession` | `PendingMove` P | PetEquipmentResultAudit.cs |
| `ItemTooltipFeature` | `Build` M, `tipLayer` F, `Visible` P | ChatRoomAudit.cs, ExchangeAudit.cs, MailClassicAudit.cs, MerchantAudit.cs, MykoNpcAudit.cs, NpcIntegrationAudit.cs, NpcOpeningAudit.cs, StorageAudit.cs, WorldRootPreviewMembers.cs |
| `KeyBinds` | `Changed` static E | HudButtonsAudit.cs |
| `KillSplashFeature` | `Splash` M | EventOverlaysAudit.cs |
| `KingElectionFeature` | `OnSchedule` M | KingNationAudit.cs |
| `KnightRoyaleFeature` | `EnsureAreaBanner` M | EventOverlaysAudit.cs |
| `LoginNet` | `I` static P | CommandFunctionAudit.cs, GenieAudit.cs, PetWorldReconnectAudit.cs, PluginToggleAudit.cs, SkillRulesAudit.cs |
| `LookPreview` | `model` F, `pivot` F | BeautyAudit.cs, CapeAudit.cs, GenderAudit.cs, NationTransferAudit.cs |
| `LootFeature` | `BuildPrompt` M, `BuildWindow` M, `Entries` P, `layer` F, `openBundleId` F, `RefreshWindow` M | WorldOverlaysAudit.cs |
| `LotteryFeature` | `Open` M | WorldRootPreviewMembers.cs |
| `MailComposeFeature` | `OnBodyChanged` M, `OnSendResult` M, `OpenCompose` M, `RefreshRecipientSuggestions` M, `SendComposed` M | MailClassicAudit.cs |
| `MailFeature` | `OnList` M, `OnRead` M | MailAudit.cs, MailClassicAudit.cs |
| `MainWindowsFeature` | `Show` M | QuestDetailAudit.cs |
| `MarketPriceFeature` | `Build` M, `BuildMarketPricePanel` M, `CloseMarketPrice` M, `OnMarketPrice` M, `OpenMarketPrice` M, `RequestMarketPriceWindow` M, `SelectMarketPriceItem` M | MarketPriceAudit.cs, MerchantAudit.cs, UpstreamNpcAudit.cs |
| `MerchantBuyFeature` | `BuildBuyPanels` M, `CloseWantedStall` M, `CloseWishList` M, `OnBuyList` M, `OnBuySold` M, `OnWishItemPicked` M, `OnWishSlotClicked` M, `OpenWishList` M, `RefreshWantedStall` M, `RefreshWishList` M | MerchantAudit.cs |
| `MerchantFeature` | `BuildAmountPrompt` M, `BuildPanels` M, `BuyFromStall` M, `CloseSellStall` M, `CloseShop` M, `MenuShown` P, `OnBuy` M, `OnGold` M, `OnItemAdd` M, `OnList` M, `OnOpenResult` M, `RefreshShop` M | AnvilServiceFixtures.cs, CommandFunctionAudit.cs, MerchantAudit.cs |
| `MerchantSearchFeature` | `OnSearchLoaded` M, `OnSearchOpen` M, `OnSearchRows` M, `searchShown` F | MerchantSearchAudit.cs, NpcServicesAudit.cs |
| `MessengerFeature` | `Dispose` M, `DoSend` M | CommunicationAudit.cs |
| `NameChangeFeature` | `Open` M | IdentityAudit.cs |
| `NationChannelsFeature` | `Build` M, `Dispose` M, `NationShown` P, `OnNationRooms` M, `OnNationSearch` M, `RefreshNationButtons` M | ChatRoomAudit.cs, CommandFunctionAudit.cs, CommunicationAudit.cs, SocialAudit.cs |
| `NationChatSession` | `PendingRoom` P | ChatRoomAudit.cs |
| `NationTransferFeature` | `Dispose` M | NationTransferAudit.cs |
| `NationTransferSession` | `Pending` P | NationTransferAudit.cs |
| `Notice` | `onConfirm` F | NationTransferAudit.cs |
| `NpcDialogFeature` | `Begin` M, `Build` M, `BuildNpcDialog` M, `End` M | AnvilAudit.cs, DetailsAudit.cs, ExchangeAudit.cs, MerchantAudit.cs, MykoNpcAudit.cs, NpcIntegrationAudit.cs, NpcOpeningAudit.cs, NpcPortraitAudit.cs, VendorAudit.cs |
| `NpcInteractionFeature` | `RangeTick` M | NpcPortraitAudit.cs |
| `NpcQuestSession` | `LastNpcId` P, `LastNpcName` P, `TalkingNpcId` P | MykoNpcAudit.cs, NpcIntegrationAudit.cs, NpcOpeningAudit.cs, NpcPortraitAudit.cs, VendorAudit.cs |
| `PartyCommanderFeature` | `commanderUserHidden` F | PartyAudit.cs |
| `PartyFeature` | `BuildHud` M, `FollowHighlight` M, `MenuKick` static F, `Pick` P, `RefreshUI` M | ContextMenuAudit.cs, PartyAudit.cs |
| `PartyInvitesFeature` | `BuildPrompts` M, `dialogLayer` F, `invitePending` F | PartyAudit.cs, WorldOverlaysAudit.cs |
| `PartySession` | `PartyCommanderId` P, `status` F | PartyAudit.cs |
| `PartyWarBoardFeature` | `Build` M, `meterToggleSentAt` F, `OnWarBoard` M, `Panel` P, `warBoardSentAt` F, `WarBoardTick` M | PartyAudit.cs, UserVoteAudit.cs |
| `PetFeature` | `Close` M, `Open` M, `Panel` P, `RefreshUI` M, `SetStatus` M, `shown` F | PetEquipmentAudit.cs, PetFoodAudit.cs, PetPlateAudit.cs, PetPortraitAudit.cs, PetSkillAudit.cs, PetWorldReconnectAudit.cs |
| `PetHatchFeature` | `OnFailed` M, `Open` M, `Reset` M | PetAudit.cs |
| `PetSession` | `PendingFeed` P, `PendingIncubation` P | PetFoodAudit.cs, PetReplyAudit.cs |
| `PetSkillBarFeature` | `CanFinishPetSkill` M, `HidePetBar` M, `OnPetBarSummoned` M, `PetBarSkills` P, `petSkillGeneration` F, `PetSkillReadyAt` P, `RefreshPetBar` M | PetBarAudit.cs, PetBarPlacementAudit.cs, PetSkillAudit.cs |
| `PieceChangeFeature` | `BuildPanel` M, `npcId` F, `PieceChangeTick` M, `RefreshActions` M, `RefreshBackpack` M, `StartSpin` M, `StopSpin` M | ClassicServicesAudit.cs |
| `PluginGame` | `Attach` M, `AttachGenie` M, `AttachSocial` M, `AttachUserInfo` M, `AttachUserVote` M, `AttachWarp` M, `hotbar` F, `RaiseGenie` M, `RaiseHotbar` M, `RaiseHotbarReady` M, `RaiseInventory` M, `RaiseLogLine` M, `RaiseUserInfo` M, `RaiseUserVote` M | AnvilAudit.cs, ClassicServicesAudit.cs, ExchangeAudit.cs, InventoryAudit.cs, MerchantAudit.cs, PartyAudit.cs, PartyVoteFixtures.cs, PetEquipmentResultAudit.cs, Preview.cs, RepairAudit.cs, SkillAudit.cs, SkillRulesAudit.cs, StorageAudit.cs, UpstreamIntegrationAudit.cs, UserVoteAudit.cs, VendorAudit.cs |
| `PluginInfo` | `Enabled` P, `Instance` P, `State` P | PluginToggleAudit.cs |
| `PowerUpStoreCartView` | `RenderCart` M | StoreAudit.cs |
| `PowerUpStoreGiftFeature` | `OnRecipient` M | StoreAudit.cs |
| `PremiumFeature` | `OnStatus` M | PremiumQuestAudit.cs |
| `ProgressionFeature` | `OnGoldChange` M | VendorAudit.cs |
| `ProgressSession` | `SelfAbnormal` P | EventWindowsAudit.cs, SkillRulesAudit.cs |
| `QuantityPrompt` | `max` F | VendorAudit.cs |
| `QuestBook` | `ApplyKillCounts` M, `ApplyLog` M, `ApplyObjectives` M, `ApplyStrings` M, `ApplyView` M, `Entries` P, `Kills` P, `Objectives` P, `RewardSelection` P, `Strings` P, `Tracked` P, `Views` P | CharacterNpcFixtures.cs, DetailsAudit.cs, PremiumQuestAudit.cs |
| `QuestFeature` | `BuildLog` M, `BuildRow` M, `RefreshLog` M, `Select` M | DetailsAudit.cs, PremiumQuestAudit.cs, QuestDetailAudit.cs |
| `QuestNotificationsFeature` | `Dismiss` M, `EnsureNotificationWindow` M, `RefreshNotification` M | PremiumQuestAudit.cs |
| `QuestReceiptFeature` | `OnReceipt` M, `ReceiptWindow` P | DetailsAudit.cs |
| `QuestTargetFeature` | `EnsureTargetWindow` M | DetailsAudit.cs |
| `QuestTrackerFeature` | `RefreshTracker` M | PremiumQuestAudit.cs |
| `RebirthFeature` | `Dispose` M, `OpenPicker` M, `OpenReset` M | RebirthAudit.cs |
| `RepairFeature` | `Build` M, `Dispose` M, `OnResult` M, `Open` M | AnvilServiceFixtures.cs, ClassicServicesAudit.cs, NpcOpeningAudit.cs, RepairAudit.cs |
| `RepairSession` | `RepairNpcEvent` E | NpcOpeningAudit.cs |
| `ReportFeature` | `OnResult` M, `panel` F | EventsBAudit.cs, EventsBFixtures.cs |
| `RepurchaseFeature` | `Dispose` M, `On` M, `Open` M | RepurchaseAudit.cs |
| `ResetConfirmFeature` | `CancelReset` M | ClassicServicesAudit.cs, NpcOpeningAudit.cs |
| `RoomMediaSession` | `HideRoomPanel` M | ChatRoomAudit.cs |
| `RouletteFeature` | `Toggle` M | EventsAAudit.cs |
| `SealFeature` | `AskConfirm` M, `BuildWindow` M, `Close` M, `PushDigit` M, `TakeSocket` M | ClassicServicesAudit.cs, NpcOpeningAudit.cs |
| `SeekPartyFeature` | `ApplySeekBoard` M, `BuildSeekPanel` M, `fullMoonAutoEntry` F, `SeekBoardKind` P, `seekLayer` F, `SeekPanel` P, `UpdateSeekStatus` M | PartyAudit.cs |
| `SelfAvatar` | `KoX` F, `KoY` F, `KoZ` F | GenieAudit.cs |
| `SelfSession` | `Class` P, `Face` P, `Hair` P, `Inventory` P, `LastEnter` P, `Race` P, `SeedPreviewEnter` M, `Sheet` P, `TransformSkill` P | AnvilAudit.cs, BeautyAudit.cs, ChatRoomAudit.cs, ClassicServicesAudit.cs, CommandFunctionAudit.cs, CommunicationAudit.cs, DetailsAudit.cs, EventOverlaysAudit.cs, EventWindowsAudit.cs, EventsAFixtures.cs, ExchangeAudit.cs, FamiliarFixtures.cs, GenderAudit.cs, GenieAudit.cs, IdentityAppearanceFixtures.cs, IdentityAudit.cs, InventoryAudit.cs, ItemWindowsAudit.cs, MarketCommunicationFixtures.cs, MerchantAudit.cs, MykoHudAudit.cs, NpcOpeningAudit.cs, NpcServicesAudit.cs, PetEquipmentResultAudit.cs, PremiumQuestAudit.cs, PresetAudit.cs, ProgressionAudit.cs, QuestDetailAudit.cs, RepairAudit.cs, RepurchaseAudit.cs, SkillAudit.cs, SkillRulesAudit.cs, StorageAudit.cs, UpstreamIntegrationAudit.cs, UpstreamNpcAudit.cs, VendorAudit.cs |
| `ShoppingMallFeature` | `OnCatalog` M, `OnOpen` M, `TickTimers` M | StoreAudit.cs, StoreMykoAudit.cs |
| `ShoutFeature` | `BuildBanner` M, `layer` F | WorldOverlaysAudit.cs |
| `Shutdown` | `Releases` static F | InventoryAudit.cs |
| `SiegeFeature` | `BuildWindows` M, `guardShown` F, `officeShown` F, `OpenGuard` M, `OpenOffice` M, `OpenTaxRate` M, `taxListShown` F | CouncilRateAudit.cs, SiegeAudit.cs, UpstreamNpcAudit.cs |
| `SkillCastSystem` | `CanCastWithGear` M, `InSkillRange` M, `PendingCasts` P | GenieAudit.cs, SkillRulesAudit.cs |
| `SkillPresetFeature` | `BuildPresetSkillBlock` M, `BuildPresetSlotBar` M, `BuildPresetStatBlock` M, `presetPanel` F, `presetShown` F, `presetSlot` F, `RefreshPresetUI` M, `SelectPresetSlot` M | DetailsAudit.cs, PresetAudit.cs |
| `SpecialAuctionFeature` | `auctionClickedAt` F, `auctionToday` F, `BuildPanel` M, `Close` M, `EnsureAuctionTable` M, `OnAuctionBid` M, `OnAuctionClaim` M, `OnAuctionCollect` M, `OnAuctionLog` M, `OnAuctionMyInfo` M, `OnAuctionToday` M, `Open` M, `RefreshAuctionBalance` M, `RefreshAuctionToday` M, `RenderAuctionSchedule` M, `ShowAuctionTab` M, `TickSpecialAuction` M | AuctionAudit.cs, UpstreamNpcAudit.cs |
| `StatusHudFeature` | `Root` P | PluginToggleAudit.cs |
| `TitlePickerFeature` | `BuildPanel` M, `BuildRow` M, `panel` F, `Shown` P | DetailsAudit.cs |
| `TournamentFeature` | `Toggle` M | EventsAAudit.cs |
| `TownRecallFeature` | `Build` M | WorldOverlaysAudit.cs |
| `UpgradeFeature` | `Confirm` M, `OnResult` M, `RefreshActions` M, `Send` M, `SetAnvilBench` M, `StageAnvil` M | AnvilAudit.cs, AnvilServiceFixtures.cs, WorldRootPreviewMembers.cs |
| `UpgradeNoticeFeature` | `BuildBanner` M, `layer` F | WorldOverlaysAudit.cs |
| `UserInfoFeature` | `Build` M, `OnInformation` M | DetailsAudit.cs |
| `UserOptionSession` | `PartyRequestsBlocked` P, `TradeRequestsBlocked` P | CommandFunctionAudit.cs |
| `UserVoteFeature` | `askUntil` F, `askWindow` F, `Build` M, `notice` F, `OnAsk` M, `select` F, `ShowVoteNotice` M, `UserVoteTick` M | UserVoteAudit.cs |
| `VendorFeature` | `BuildPanel` M, `OnGold` M, `OnTradeResult` M, `Open` M, `RefreshDetail` M | NpcOpeningAudit.cs, VendorAudit.cs |
| `VendorSession` | `TradeInFlight` P | VendorAudit.cs |
| `VipWarehouseFeature` | `BuildPanel` M, `expiresAt` F, `inFlight` F, `layer` F, `OnResult` M, `Panel` P, `Refresh` M, `ShowPinDialog` M | NpcOpeningAudit.cs, StorageAudit.cs |
| `Vitals` | `Mp` P | GenieAudit.cs |
| `WarehouseFeature` | `AskGoldTransfer` M, `Build` M, `BuildPanel` M, `Close` M, `Dispose` M, `layer` F, `OnResult` M, `Panel` P, `Refresh` M, `WithdrawInto` M | NpcOpeningAudit.cs, StorageAudit.cs |
| `WarehouseSession` | `WarehouseNpcEvent` E | NpcOpeningAudit.cs |
| `WarpFeature` | `Close` M, `SelectRow` M | ClassicServicesAudit.cs |
| `WhispersFeature` | `AddWhisperPost` M, `Build` M, `BuildWhisperOptions` M, `Dispose` M, `GetOrCreateWhisper` M, `OnDirectPosted` M, `OnWhisperLookChanged` M, `RefreshWhisperOptions` M | ChatRoomAudit.cs, SocialAudit.cs, SocialPromptsAudit.cs |
| `WindowHost` | `RaiseHidden` M, `RaiseShown` M | QuestDetailAudit.cs |
| `WorldPreview` | `AgilityNecklace` static F, `AuctionPreviewDay` static F, `AuctionPreviewSeconds` static F, `BuildChaosGuide` M, `BuildChaosLadder` M, `BuildChaosSkillPage` M, `BuildCharacterSeal` M, `BuildChatRoom` M, `BuildClanBoards` M, `BuildClanBonus` M, `BuildClanHandover` M, `BuildCombineRecipeBook` M, `BuildCommander` M, `BuildCommandList` M, `BuildDailyRank` M, `BuildEvents` M, `BuildEventTitles` M, `BuildExpSeal` M, `BuildFortune` M, `BuildItemCombine` M, `BuildItemSmash` M, `BuildKing` M, `BuildLootFilter` M, `BuildNationChat` M, `BuildNereid` M, `BuildNestTimer` M, `BuildOrderMessage` M, `BuildPowerUpStore` M, `BuildPremium` M, `BuildQuestTracker` M, `BuildRankBoard` M, `BuildRankerPaper` M, `BuildRepair` M, `BuildRoyaleScore` M, `BuildSeal` M, `BuildSiege` M, `BuildSiegeTower` M, `BuildSiegeWar` M, `BuildSoccer` M, `BuildTimedItems` M, `BuildUpgrade` M, `BuildVanguard` M, `BuildWarp` M, `BuildWarRanking` M, `BuildWarResult` M, `BuildWhisper` M, `DetachPreviewControl` static M, `ElfMetalEarrings` static F, `ExitMenu` M, `MailEntries` static M, `PreviewClan` static M, `PreviewHpPotion` static F, `PreviewItem` static M, `PreviewMpPotion` static F, `PreviewUpgradedWeapon` static F, `PusPreviewMail` static M, `ShadowPiece` static F, `ShowSealKeypad` M, `StrengthNecklace` static F | AnvilServiceFixtures.cs, ChatRoomAudit.cs, ClassicServicesAudit.cs, CommandListAudit.cs, EventOverlaysAudit.cs, EventWindowsAudit.cs, EventWindowsFixtures.cs, IdentityAppearanceFixtures.cs, ItemWindowsAudit.cs, KingNationFixtures.cs, MapFortuneFixtures.cs, MarketCommunicationFixtures.cs, MykoHudAudit.cs, PremiumQuestAudit.cs, RepairAudit.cs, SiegeFixtures.cs, SkillRulesAudit.cs, SocialFixtures.cs, SocialPromptsAudit.cs, UpstreamIntegrationAudit.cs |
| `ZoneAbilityFeature` | `BuildChip` M, `zoneabilityTypeLbl` F | WorldOverlaysAudit.cs |
| `ZoneSession` | `EventTitlesChangedEvent` E | MykoHudAudit.cs |

### Notes by window group

The notes below were written window group by window group while the plugin was ported. Their member names are
the client's names before it split its world scene into features (`_trophy` is now `AchievementTrophy.Trophy`);
the tables above are the current names. The behaviour they describe still holds.

#### Shared

| Member | Kind | Used for |
| --- | --- | --- |
| `PluginGame._windows`, `PluginGame._inventory`, proxy `Source`, `PluginGameBridge._w` | F/P | Find the World behind the plugin services. Install the `NativeServiceWindows` decorator. |
| `PluginUi.HudHidden` | M (internal) | Chat item-link guard |
| `HudLayout._defaultPosition` (get/set), `ReapplyDefault` | F/M | Content anchors, party docking, familiar bar default position |
| `HudWindow.Closed` | E | Find the World that owns a fixture window (familiar) |
| `ItemSlotView._count`, `_icon`, `RightClicked`, `DoubleClicked` | F/E | Count badge, drag preview, vendor rerouting |
| `QuantityPrompt.Confirm`, `Notice._request` (`DialogRequest`) | F | Prompt wiring, confirmation wording |
| `Inv`, `Sheet`, `Chat`, `_selfDead`, `_ents`, `_myId`, `_selectedId`, `_zone`, `_mainWindows` | F/P | Common state |
| `ShowItemTooltip(int,ItemSlot,string)`, `HideItemTooltip()`, `MoveBetween(int,int)`, `HideMainWindow(string)`, `CombatNotice(string)` | M | Common actions |
| `ResolveMobScene(int)`, static `ForceDoubleSidedOnce(Node,string)`, `ModelNodeName` | M/F | NPC and familiar portrait models |

#### HUD, chat, whispers

| Member | Kind | Used for |
| --- | --- | --- |
| `ChatSystem._store`, `PublishedMax`, static `RenderText`, `AddEntry`, `StatusNotice` (set), `PluginTyping`, `IsActive`, `_bubbles` (`Bubble.Label`), `Panel`, `SetChannel`, `Open` | F/P/M | Classic history, System lines, bubble colours, link rule, channel requests |
| `ChatSystem+ChatEntry.Raw` and `Seq`, `Time`, `Category`, `Type`, `Name`, `Nation`, `Gm`, `Text`, `Bbcode` | M/P | Reading and adding entries |
| `_whisperLayer`, `_whispers` (`WhisperChat.Name/Window/Input/Log/Blink`), `_worldReady`, `_hudEditMode`, `_self` | F | Whisper preparation, compose target, attention styler |
| `_nearbyListed`, `_nearbyRows`, `_nearbyFirstPoll`, `RebuildNearby()` | F/M | Nearby players |
| `_chatPalette`, `_chatPaletteSlot`, `_chatColorsDraft` | F | Chat colours palette placement and focus |
| `ShowChatItemTip`, `ShowPlayerMenuByName` | M | Link tooltip, player menu |
| `_mailIconButton`, `_trophy`, `_trophyBlink`, `_attendanceGift`, `_attendanceGiftBlink`, `_powerUpStoreIcon`, `_genieIcon`, `_eventsIcon`, `_drIcon`, `QueueDockLayout()`, `PlaceTopIcons()` | F/M | Hidden HUD icons (the status bar's letter row opens their windows) |
| `_achClaimablePerTab`, `ClaimableAttendanceCount()`, `_eventsAlert`, `_drBlink` | F/M | Notification count (with public `Net.MailUnread`); events and daily rank give an attention state |
| `_mailShown`/`ToggleMail`, `_achShown`/`ToggleAchievements`, `_attendanceShown`/`ToggleAttendance`, `_pusShown`/`ToggleShoppingMall`, `_fullMapShown`/`ToggleFullMap`, `_genieShown`/`ToggleGenie`/`CloseGenie`, `_eventsPanel`/`ToggleEvents`, `_drWindow`/`OnDailyRankIcon` | F/M | Taskbar and status-bar window toggles |
| `_mainWindows["Character"]` `_dock`/`DockTo`, `_geniePanel` `Layout._defaultPosition`/`ReapplyDefault()`, `_pluginHud` | F/M | The docked Character window and the Genie window's first spot stay clear of the Classic status bar, letter row and minimap |
| `_hotkeys`, `_padHotkeys` (entries for `KeyBinds.Get`/`GetPad(KeyAction.Quests)`, re-applied on `KeyBinds.Changed`) | F | The "Quest Journal" key (F10) opens the Quest list like Q; without them it opens Quest Details as before |
| `_hotSelected`, `_hotbar`, `SelectHotAbs(int)` | F/M | Hotbar selection |
| `HotbarUsable(SkillData.Skill,double)`, `SelfTransformModel()`, `_hotbar`, `_zone` | M/F | Skill bar dimming (`NativeHudChat.HotbarUsable`): the client's own "usable now" test (MP, stamina, weapon, anger, buff, coins, learned, consumed items, group cooldown), plus the zone-tree and command-form gates it applies when an entry fires. Missing: the bar dims from item counts only. |
| `_hotLoadedFromServer` | F | Passive skills that old bars still hold are cleared through `IGameHotbar.Drop(abs, 0, -1)` only after the client has applied the saved bar (`NativeHudChat.HotbarLoaded`), outside Chaos and with the Genie idle: on a bar change, on a Genie change, and on the slot-state tick when the bar becomes editable. Missing: nothing is cleared; clicks on them are still refused. |
| static `VipVaultKeys` | F | A bag double-click leaves VIP vault keys to the client's right-click like other items it does not put on the skill bar (`NativeSkills.BagClickAddsToHotbar`). |
| Public: `HotbarAssign.CanAdd`/`CanDrag`, `SkillLearn.For`, `IGameHotbar.Ready`, `HotSlotOverlay`, `UpgradeBadge`, `IGameGenie.Cell`/`SetCell`, `IGameCommands.Run(KeyAction.Skills/Presets)` | API | Skill rules of the Classic skill window and skill bar (`NativeSkills`): right-click and drag follow the client's own skill-window rules (the war shouts of both nations stay: the plugin adds the El Morad 230013/230014 that `CanAdd` lacks before upstream's fix), learn states, ready flash, upgrade badges; passive skills are cleared from Genie cells; the taskbar Skill button and the Skill Preset button run the client's keys |
| `_running`, `_selfSitting`, `_autoAttack`, `ToggleRunMode()`, `ToggleAutoAttack()`, `StartCameraHalfTurn()`, `ToggleEsc(bool?)` | F/M | Commands |
| `_miniMap` | F | `MiniMapVisible` |
| `_clanBonusChip`, `_clanBonusMarks` (3277df5) | F | Clan online-bonus chip named `clan_bonus_chip` and skinned as a Classic plaque ("Clan" and the O/X marks); hidden while the 2.x top-right group shows its clan bonus bar (`NativeQuestHud`, `NativePremium.HideClientChips`) |
| Public: `IGameTarget.QuestDrops` (`GameTargetDrop`) | API | Quest-item rows under the Classic target frame (`TargetFrame`); replaces nothing (the native `_targetDropRows` sit in the replaced `HudPart.TargetFrame`) |
| Public: `IGameMap.NpcKind`, `NpcKindPresent`, `PickNpcKind`, `MiniMap.Blip.Diamond`, `NpcFinder.KindCaptions` | API | Classic NPC finder over the status minimap (`ClassicNpcList`) and its gold diamonds above the frame art (`MapCanvas.Markers`) |
| Public: `GameLogLine.Kind` | API | Info log tabs All/Status/Item/System (`LogWindow`), the client's `TabbedLog` tabs |
| Public: `PremiumNames.Name`, `PremiumText`/`PremiumFallback`, `Net.ClanOnlineCount`, `Net.NoClanBonus`, `ClanBonusCard` | API | Premium names from the text table in the approved title case (`NativePremium.ClassicCase`, "Gold Premium: 2d 4h" on the plaque, time and benefits in its tooltip/box); clan bonus marks and text (`NativePremium.BonusOf/BonusLines`) |
| `HudLayout._corner`, `_resizing`, `_dragging`, `_dragMoved`, `_resizeOriginMouse`, `_resizeOriginPosition`, `_resizeOriginSize`, `Frozen`, `MinimumSize`, `Place(Vector2,Vector2)` | F/P/M | Legacy grip, seam snapping |

#### Character and NPC

| Member | Kind | Used for |
| --- | --- | --- |
| `_selfRace`, `_selfClass`, `_stTitleBtn`, `_characterPages`, `_clanContent`, `_mainLayer`, `_escapeStack`, static `CharacterPageWidth`, `World+CharacterPage`, `World+ClanTab` | F/T | Character panel bridge, clan details composition |
| `ShowCharacterPage`, `ShowClanTab`, `ToggleTitlePicker`, `TogglePreset`, `ToggleClanPoints`, `RequestUserInformation`, `SyncMainWindowState` | M | Page actions |
| `_clanInviteAsk`, `_clanDisbandAsk`, `_clanLeaveAsk`, `_clanRemoveAsk`, `_clanAllianceAsk`, `_clanConfirmAsk`, `_clanMemberMenu`, `_clanStatus`, `_clanMembers`, `_allianceNotice`, `_allianceClans`, `_ctxMember` | F | Clan page rows, status and dialogs |
| `OnLeaveClan`, `AskClanConfirm`, `OnAllianceButton`, `OnMemberMenuAction`, `ApplyMyClan`, `EnsureClanLoaded`, `RefreshClanTab`, static `StatusName` | M | Clan actions |
| `_friendStatus`, `_friends`, `_friendAddInput`, `EnsureFriendsLoaded`, `DoFriendAdd`, `OpenWhisperWith`, `InvitePlayerToParty`, static `FriendDetailLine` | F/M | Friend page |
| `_questFilter`, `_questKind`, `_questViews`, `_questTracked`, `_questSelected`, `_questTrackBtn`, `_questAbandonBtn`, `_questCompleteBtn` | F | Quest page |
| `SetQuestFilter`, `SetQuestKind`, `ToggleTrackSelectedQuest`, `AbandonSelectedQuest`, `CompleteSelectedQuest`, `RefreshQuestDetail`, `QuestsInView`, `QuestFacts`, `QuestName`, `QuestJournal`, `QuestStateOf`, static `StateName` | M | Quest page |
| `_questRewardChoice`, `_questRewardBox`, `_questRewardTitle`, `QuestRewards`, `QuestRewardName`, `QuestItemRow` | F/M | Quest Details reward rows, NPC reward choice |
| `_questRewardSelection` (`Received`, `Chosen`), `_questRewardOptionTitle`, `_questRewardOptionBox` *(fix/quest-rewards)* | F/M | Chosen and received reward state |
| `_npcDialogShown`, `_npcPanel`, `_npcTalkId`, `_vendorNpcId`, `_npcQuestContent`, `_questNotifications`, `_questNotificationIndex` | F | NPC dialog portrait, quest captions |
| `Net.WarehouseNpcEvent`, `Net.RepairNpcEvent`, `_npcMenuBox` (+ `_npcPanel`) | E/F | NPC opening windows (`NativeNpcOpening` marks the client's Inn Hostess and Blacksmith choices; `ClassicNpcOpeningChoice` shows them on `co_inn_us` / `co_smith_us`) |
| static `NoWeaponNpcIds`, `WantedClasses`, `AttachWeapons`; `World+Ent` `IsNpc`, `Dead`, `Name`, `Level`, `ModelId`, `NpcType`, `NpcId`, `Gear`, `Body` | F/M/P | NPC portrait factory |
| `_presetStatPointsLbl` | F | Preset label wording |
| `_partyMembersBox`, `_dockOrder`, `Selectable`, `Select` | F/M | Party roster, click-to-target, docking |
| `_seekListBox`, `_seekRegisterBtn`, `_seeking`, `_seekPageLbl`, `_seekTotal`, `_seekPage`, static `SeekZoneName` | F/M | Seek-party board |
| `_inZoneLeaveAsk`, `OnInZoneLeaveConfirmed`, static `IsTempleEventZone` | F/M | Event leave confirmation |

#### Inventory and storage

| Member | Kind | Used for |
| --- | --- | --- |
| `IGameInventory.MoveAmount(int,int,int)` | API | Partial moves (bag, magic bags, the storages' embedded bag) |
| `_moveInFlight`, `_bagPairing`, `BagPairing<>._openedByCompanion`, `_invDelSlot`, `_invDelItemId`, `RefuseItemInUse(int,int)`, `ConfirmDeleteItem()` | F/M | Embedded bag pairing, identity-checked destroy |
| `_warehouse`, `_whCells`, `_whStatus`, `_whAmount`, `_whShown`, `_whMoney`, `_whInFlight` | F | Warehouse |
| `WithdrawSlot(int)`, `DepositSlot(int,int,int?)`, `AskWithdraw(int,int,int?)`, `CanDropOnWarehouse(int,Variant)`, `DropOnWarehouse(int,Variant)` | M | Warehouse transfers |
| `_vipWh`, `_vipWhPage`, `_vipWhPageLbl`, `_vipWhStatus`, `_vipWhExpiryLbl`, `_vipWhExpirySec`, `_vipWhShown`, `_vipWhInFlight`, `_vipWhPending`, `_vipWhPinDlg`, `_vipWhPinPrompt`, `_vipWhPinEdit` | F | VIP vault and PIN prompt |
| `_clanWh`, `_clanWhPage`, `_clanWhPageLbl`, `_clanWhStatus`, `_clanWhShown`, `_clanWhLoaded`, `_clanWhLoading` *(upstream 7ef0f8e, optional)*, `_clanWhMoney`, `_clanWhInFlight`, `_clanWhPending`, `_clanWhGoldInput` | F | Clan warehouse |
| `RefreshVipWarehouse()`, `RefreshClanWarehouse()`, `VipDepositSlot(int)`, `VipWithdrawSlot(int)`, `ClanWhDepositSlot(int)`, `ClanWhWithdrawSlot(int)`, `ClanWhGoldTransfer(bool)`, `SubmitVipPin()` | M | Vault transfers, coins, PIN |
| `World+VipWhPending`, `World+ClanWhPending` (`Op`, `InvAbs`, `VipIdx`/`WhIdx`; `Count`, `Merge` *(fix/storage-transfers)*) | T | Quantity transfers |
| `StorageTransferBusy` *(fix/storage-transfers)* | P | Busy state. Falls back to the in-flight fields. |
| `Net._conn` and its `Send(Packet)` | F/M | In-vault rearrangement (op 4) |

#### Vendor, trade, merchant

| Member | Kind | Used for |
| --- | --- | --- |
| `_vendorCells`, `_vendorCellIds`, `_vendorCatalogue` (set), `_vendorCompanion`, `_vendorEntries`, `_vendorFooter`, `_vendorGroup`, `_vendorNpcId`, `_vendorPanel`, `_vendorSelected`, `_vendorShown`, `_vendorBuy`, `_tradePrompt`, `_tradeInFlight`, `_moveQueue`, `LoyaltyShop`, `VendorWallet`, `VendorCurrency`, `Floaters` | F/P | Vendor metas, compact pages, confirmations |
| `BuyAmount`, `BuyRoom`, `CanBuy`, `CarriedWeight`, `InMainBag`, `SelectVendorItem`, `ShowVendorPage`, `PluginLogAdd` | M | Vendor actions, gold log |
| `SellSlot(int,int,int)` *(fix/vendor-exchange-validation)*, otherwise `SellSlot(int,int)` | M | Sell |
| `ItemSlot.IsTradable` *(fix/anvil-session, fix/vendor-exchange-validation)* | P | Untradable refusal |
| `BagCompanion.Take`, `BagCompanion.IntoBag` (init) | P | Rerouting the bag companion through the confirmation |
| `World.ShowItemTooltip(int, ItemSlot, string note = "", bool shopGoods = false)` *(3277df5)* | M | The client's catalogue hover passes `shopGoods: true` itself and shows its "Purchasing Price" line (tariff included, red above the wallet); the Classic catalogue cell only forwards the hover, the plugin's former "Buying Price" line is gone. Sell prices follow `ItemData.SellPrice`, which applies the premium quarter (`ItemData.PremiumSeller`). |
| `_rbNames`, `_rbPrices`, `_rbTimes` (`Label[10]`), `_rbBuy` (`Button[10]`), `_rbScroll`, `_rbEmpty` *(3277df5)* | F | Buy-back window (`repurchase`, `NativeRepurchase`): names the client's rows, captions and Refresh for `ClassicRepurchaseSkin`; Buy keeps the client's confirmation, expired notice and request. The row states (too dear, expired) and the close come from the facade `IGameRepurchase` (`Rows`, `Close`; its `Buy` is not used because it skips the confirmation). Missing members leave the window on the generic Classic frame. |
| `Net.GoldChangeEvent` | E | Gold log (handler prepended) |
| `_exPanel`, `_exMineList`, `_exTheirsList`, `_exBagList`, `_exWaitLayer`, `_exAmountLayer`, `_exAmountSpin`, `_exAmountIcon`, `_exAmountName`, `_exAmountHint`, `_exAmountMax`, `_exAmountSlot`, `_exAmountShown`, `_exAskDialog`, `_exConfirmBtn`, `_exGoldEdit`, `_exStatus`, `_exShown`, `_exRequestPending`, `_exConfirmedByMe`, `_exAddInFlight`, `_exMyOffer`, `_exTheirOffer`, `_exMyGoldOffer`, `_exTheirGoldOffer`, `_exPartnerName`; `ExOfferItem.ItemId/Count/Dura/SourceAbs` | F/P | Exchange names, metas, request layer, final approval, gold prompt |
| `_exConfirmedByPartner` *(fix/vendor-exchange-validation)* | F | Partner lock |
| `AnswerExchangeRequest`, `OnExchangeConfirm`, `ConfirmExchangeAmount`, `CloseExchangeAmount`, `OnAddGold`, `SetExStatus` | M | Exchange actions |
| `_mctLayer`, `_amountLayer`, `_amountAccept`, `_amountConfirmBtn`, `_amountCount`, `_amountCountRow`, `_amountPrice`, `_amountPriceFixed`, `_amountMarketRow`, `_amountMarketHint`, `_marketPricePanel`, `_marketPriceShown` | F | Merchant amount prompt metas |
| `_sellAdvert`, `_sellStallPanel`, `_myStall`, `_myStallSrc`, `_wantedItems`, `_stalls`, `_stallSignLayer`, `_sellStallCells`, `_sellBagCells`, `_shopCells`, `_shopBagCells`, `_wishCells`, `_wantedCells`, `_wantedBagCells` | F | Merchant windows and signs |
| `AcceptAmount`, `AskStallPrice`, `CloseAmountPrompt`, `ConfirmSellStall`, `SellToWanted`, `SetSellStatus`, `SetWantedStatus`, `StageStallItem`, `UnstageStallItem`, `RefreshStallSign`, static `FreeStallSign` | M | Merchant actions |
| `MerchantCell.Index`, `OnActivate` (set), `OnDropFrom` (set), `Item`, `_count`; `Stall.Sign`, `SignCells`, `IsBuying` | F/P | Release activation, slot drops, sign rebuild |

#### Magic Anvil and NPC services

| Member | Kind | Used for |
| --- | --- | --- |
| `_upgradeItemIds`, `_upgradePositions`, `_upgradeSockets`, `_itemSockets`, `_accessorySockets`, `_itemResultSocket`, `_accessoryResultSocket`, `_anvilFooter`, `_upgradeBtn`, `_upgradeLayer`, `_anvilBench`, `_upgradeShown`, `_upgradeSession`, `_upgradePendingResult`, `_upgradeScanTween`, `_upgradeAnvilId`, `_anvilCompanion` | F | Bench state, names, metas, embedded bag |
| `UpgradeInteractionLocked` *(fix/anvil-session)* | P | Bench lock. Falls back to session, pending result and tween. |
| `IsUpgradeTarget(int)`, `UpgradePlacementError(int)`, `ClearUpgradeSocket(int)`, `ClearUpgradeSockets()`, `RefreshBagFit()`, `OnUpgradeBenchChanged()`, `DismissUpgradeConfirm()`, `ShowAnvilPrompt()`, `CloseUpgrade()`, `OpenAnvilBench(AnvilBench)`, `CloseVendor()`, `SlotAt(int)` | M | Staging, Cancel/Back, selection window |
| `OnUpgradeOpen` (delegate on `Net.UpgradeOpenEvent`) | M | Replaced by the Classic selection window. If binding fails, the client's NPC menu stays. |
| `World+BagCompanion` (record constructor), `World+BagFit`, `World+AnvilBench` | T | Anvil bag companion |
| `_warpScroll`, `_warpImage`, `_warpLevels`, `_warpDesc`, `_warpGoldLbl`, `_warpStatus`, `_warpTravel`, `_warpRows`, `_warpSelected` | F | Warp names, selection |
| `_sealSocket`, `_sealHeadline`, `_sealPrompt`, `_sealCodeRow`, `_sealCodeField`, `_sealPad`, `_sealGold`, `_sealConfirm`, `_sealBagCells` | F | Seal names |
| `_pieceMessage`, `_pieceSubMessage`, `_pieceStartBtn`, `_pieceStopBtn`, `_pieceTalkBtn`, `_pieceSocket`, `_pieceResultSockets`, `_pieceBackpackGrid`, `_piecePosition`, `_pieceShown`, `_pieceBusy`, `_pieceSpinning`, `PlacePiece(int)` | F/M | Chaotic Generator |
| `_itemCombineStrip`, `_itemCombineFooter`, `_itemCombineResult`, `_itemCombineButton`, `_itemCombineSlots`, `_itemCombineShown`, `_amountCount` | F | Item combination |
| `_reset` (`Pending`, `Kind`) *(fix/npc-service-requests)*, otherwise `_resetKind` | F | Redistribution pending |
| `_repairShown`, `_repairInFlight`, `_repairQueue`, `_repairFooter`, `RepairTakeFromBag(int)`, `RepairAll()`, `CloseRepair()`, `IsRepairable(int)`, `RepairCostAt(int)`, `RepairableSlots()` | F/M | Classic repair mode |

#### Storage, items and services on upstream cd63f98

These members replace the rows above where the 2619 review renamed or removed them (`_vipWhPage`,
`_vipWhPageLbl`, `_vipWhExpirySec`, `_clanWhPageLbl`, `_clanWhLoaded`, `_clanWhLoading`, `_clanWhGoldInput`,
`ClanWhGoldTransfer(bool)`, `ClanWhDepositSlot`, `ClanWhWithdrawSlot`, `VipWithdrawSlot`, `_invDelSlot`,
`_invDelItemId`, `_sealBagCells`, `_pieceBackpackGrid`, `Net._conn`). The plugin no longer sends vault packets or
sets the vaults' pending state itself; every transfer runs through the client's handlers below.

| Member | Kind | Used for |
| --- | --- | --- |
| `_vipWhCells`, `_vipKeyCell`, `_vipWhOpen`, `_vipWhExpiresAt`, `_vipWhPinSet`, `_vipSetPinBtn`, `_vipClearPinBtn`, `_vipWhAmount` | F | VIP vault: Classic 4 x 12 pages over the 48-slot grid, key plaque, lock, PIN actions, amount prompt |
| `VipTakeFromBag(int)`, `VipBagFit(int)`, `AskVipWithdraw(int,int,int?)`, `VipWithdrawInto(int,int,int?)`, `CanDropOnVipVault(int,Variant)`, `DropOnVipVault(int,Variant)` | M | VIP transfers, key use and in-vault moves |
| `_clanWhCells`, `_clanWhDepositBtn`, `_clanWhWithdrawBtn`, `_clanWhAmount`, `TurnClanWhPage(int)` | F/M | Clan bank page, coin buttons with the client's rights, amount and gold prompts |
| `ClanTakeFromBag(int)`, `ClanBagFit(int)`, `AskClanWithdraw(int,int,int?)`, `ClanWithdrawInto(int,int,int?)`, `CanDropOnClanWh(int,Variant)`, `DropOnClanWh(int,Variant)` | M | Clan bank transfers and in-bank moves (rights from `ClanCanWithdraw`) |
| `_whDepositBtn`, `_whWithdrawBtn`, `WhTakeFromBag(int)`, `WhBagFit(int)`, `WithdrawInto(int,int,int?)` | F/M | Warehouse coin buttons, deposit and take-out from the embedded bag |
| `StorageTransferBusy` | P | Busy state of every storage |
| `_invDelQueue` (`DestroyQueue`), `_invDelPanel`, `AskDeleteItem(int)`, `ConfirmDeleteItem()`, `HideDeletePrompt()` | F/M | Bulk destroy through the Classic destroy prompt |
| `OpenVipVaultFromBag()` | M | VIP Vault shortcut in the inventory drawer |
| `Net.GoldChangeLogEvent`, `_tradeInFlight` | E/F | Gold log: shop trades and server gold notices are logged by the client only |
| `_sealCompanion`/`SealTakeFromBag(int)`, `SealBagFit(int)`, `_sealShown`, `_sealWaiting` | F/M | Seal: Classic bag and socket drop instead of the docked inventory |
| `_piecePosition`, `PlacePiece(int)` (`_pieceCompanion` left docked but closed) | F/M | Chaotic Generator: Classic bag rebuilt from the carried items |
| `_smashSocket`, `_smashResults`, `_smashMessage`, `_smashSubMessage`, `_smashStartBtn`, `_smashStopBtn`, `_smashCancelBtn`, `_smashShown`, `_smashSlot`, `SmashTakeFromBag(int)`, `SmashBagFit(int)` | F/M | Disassemble (`itemsmash`) |
| `_csSocket`, `_csTransHeadline`, `_csTransGold`, `_csTransShown`, `_csBagSlot`, `CharacterSealTakeFromBag(int)`, `CharacterSealBagFit(int)` | F/M | Character Seal scroll / Cypher Ring transfer (`char_seal_trans`) |
| `_csSealingPage`, `_csSelectPage`, `_csCodePage`, `_csSelectTitle`, `_csSelectPrompt`, `_csSelectOk`, `_csSlotButtons`, `_csKeys`, `_csCodeStars`, `_csPicked` | F | Character Seal pages (`char_seal`) |
| `_csSheetName`, `_csSheetLevel`, `_csSheetStats`, `_csSheetGold`, `_csSheetCells`, `_csSheetAchievements` | F | Cypher Ring sheet (`char_seal_sheet`) |
| `_expSealJar`, `_expSealPercent`, `_expSealAmount`, `_expSealOn`, `_expSealOff`, `_expSealPick`; `Net.ExpSealState` | F/P | EXP Seal (`exp_seal`) |
| `_tiRows`, `_tiSlots`, `_tiNames`, `_tiPlaces`, `_tiTimes`, `_tiScroll`, `_tiHideToday` | F | Temporary Item List (`timed_items`) |
| `_lootFilterWindow` body controls (named by order) | - | Loot Filter (`loot_filter`) |
| `PluginUi.ReplaceExitMenu` (`ExitMenuRequest`) | API | Classic exit menu (`co_exitmenu`) with the temporary item list |

#### Market and communication

| Member | Kind | Used for |
| --- | --- | --- |
| `_mailList`, `_mailListScroll`, `_mailUnreadOnly`, `_mailUnreadPill`, `_mailStatus`, `_mails`, `_mailSelectedId`, `_mailReadSubject`, `_mailReadMeta`, `_mailReadBody`, `_mailReadAttachmentTitle`, `_mailReadAttachmentScroll`, `_mailReadAttachments`, `_mailClaimBtn`, `_mailDeleteBtn` | F | Inbox and reader names, row metas |
| `_mailTo`, `_mailToSuggest`, `_mailSubject`, `_mailBodyRemaining`, `_mailBody`, `_mailGold`, `_mailAttachTitle`, `_mailDropZone` (`MailDropZone._hint/_idle/_hot`), `_mailAttachRows`, `_mailSendBtn`, `_mailComposeStatus`, `_mailAttachments` | F | Compose names, drop zone styling |
| `_msgrList`, `_msgrToInput`, `_msgrTextInput`, `_chatRoomList`, `_chatRoomNameInput`, `_chatRoomStatus`, `_chatRoomLogScroll`, `_chatRoomLog`, `_chatRoomSayInput`, `_chatRoomCurrentId` | F | Messenger and chat rooms |
| `Net.MessengerListEvent`, `Net.ChatRoomListEvent` | E | Handler prepended to detach old rows |
| `_marketPriceSlot`, `_marketPriceName`, `_marketPriceTrades`, `_marketPriceChart`, `_marketPriceUpdated`, `_marketPriceStatus`, `_marketPriceSearch`, `_marketPriceItem`, `_marketPriceCache`, `ClearMarketPriceChart()`, `SetMarketPriceStatus(string)`, const `MarketPriceNoHistoryText` | F/M | Market Price names, no-history repair |
| `ItemSearchPanel._query`, `_pick`, `_group`, `_level`, `_summary`, `_results`, `_tabButtons` | F | Item search names |
| `PriceChart._days`, `_top`, `_bottom`, `_hoverDay`, `_hoverPart`, `Plot`, `Column(int)`, `ValueY(long)`; constants `PlotPadding`, `BarWidth`, `MarkerHalf`, `MarkerHeight`, `AxisWidth`, `AxisFontSize`, `DayFontSize`, `BarFill`, `BarHover`, `BarEdge`, `MaxColor`, `MinColor`, `Midline`, `Baseline` | F/M | `NativeChartBackground` repaint |
| `_merchantSearchEdit`, `_merchantSearchScope`, `_merchantSearchStatus`, `_merchantSearchPages`, `_merchantSearchLines` (`MerchantSearchLine.Root/Whisper/Move/View/Icon/Name/Price`), `_merchantSearch`, `_merchantSearchLoading` | F | Merchant search names |
| The 41 `_pus*` fields of `StoreControls`; `_pusCards` (`PusCardView.Card/Entry`), `_pusCart`, `_pusCatalog`, `_pusCategories`, `_pusCategory`, `_pusHoveredCard`, `_pusModal`, `_pusShown`, `_pusSortPick`, `_pusGiftList`; `FitPowerUpStore()`; constants `PusCategoryWidth`, `PusCartWidth`, `PusWellMargin` | F/M | Power-Up Store names, cards, compact layout |
| The 15 `_auction*` fields of `AuctionFields`; `_auctionTabButtons`, `_auctionPages`, `_auctionLotSlots`; const `SpecialAuctionNoHistoryText` | F | Akara's Altar names, tabs, lots |
| `_auctionPlaceBid`, `_auctionRefresh`, `_auctionRowButtons` *(fix/auction-replies)* | F | Structural fallback without them |

#### Social on upstream cd63f98 (nation chat, party HUD, trade grids, boards, prompts)

Upstream removed the chat-room window, the party window and the trade row lists; the `_chatRoom*`,
`ChatRoomListEvent`, `_partyMembersBox` and `_exMineList/_exTheirsList/_exBagList` rows above no longer apply.

| Member | Kind | Used for |
| --- | --- | --- |
| Plugin seams `FrameWindow`, `ReplaceHud(HudPart.Party)`, `ReplacePlayerMenu`, `ReplaceTextInput`; `PluginGame.Party` (`IGameParty`) | API | Classic social frame, party list, player menu, text prompt |
| `_ncRefresh`, `_ncPublicTab`, `_ncPrivateTab`, `_ncPublicRows`, `_ncPrivateRows` | F | Nation chat names, refresh caption |
| `ToggleSeekParty()`, `_seekShown`, `OpenInvitePrompt()`, `ToggleRowMeter()`, `ToggleWarBoard()`, `_warBoardRowShares`, `_partyHudMenu`, `Net.PartyRowMeterMode`, `Net.PartyCommanderId` | F/M | Party list actions, row meter, member menu (Classic context menu), taskbar party board |
| `_wantedClassOpt`, `_wantedMsgInput`, `_seekFilters`, `_fullMoonRow`, `_seekHeading`, `_seekStatusLbl`, `_seekEntries`, `_seekFilter`, `_zone` | F | Party board filter panel, Full Moon Rift actions, row descriptions after the client filter |
| `_exMyOffer`, `_exTheirOffer` (`ExOfferItem.SourceAbs/ItemId/Count/Dura`), `Inv`, `OfferSlot(int,int?)`, `ShowItemTooltip(int, ItemSlot, string)`, `_bagPairing._openedByCompanion`, `HideMainWindow(string)`, `_exStatus`, `Net.ExchangeOtherAddEvent` | F/M/E | Classic trade grids, offering, docked inventory closed, approval withdrawn on partner change |
| `ChatSystem.RenderText/PlainText/ItemOf` (static), whisper header buttons, `_whisperLayer` popups | M | Whisper item links and emoticons, header actions, emoticon and size popups |
| `_cmdGroups`, `_cmdEntries`, `_cmdShown`, `FillCommandEntries(CommandGroup)`, static `CommandUnavailableColour`; `CommandCatalog.Groups`, `CommandEntry.Id` | F/M | Classic command list (`NativeCommandList`): reads the client's rows per group, shows the Classic groups in the client's own list and `_cmdShown`, so the client's `RunListedCommand` runs the activated row. Missing members leave the window on its original art with the client's own two lists. |
| `_escapeStack`, `HudWindow.DockTo(Func<Vector2>)` (public), `DockHudRects()`, `_genieSub` | F/M | Command list: an Escape entry ahead of the main windows (the client registers none for `_cmdWindow`), and the right-middle default position beside the right-side HUD; a dragged position still wins and a layout reset returns to it |
| `ToggleGenie()`, `ToggleGenieFromSub()` (fallback `ToggleGenieRun()`), `_genieShown`, `_genieRequestPending`, `_geniePoints`, `_genieDeadline`, `_genieRunLabel`, `_geniePanel` (its "Use Spirit of Genie" button), `CountInBackpack(int)`, static `SpiritOfGenieItem`, `Net.GenieRunning` | F/M | Genie rows of the command list (`NativeGenieCommands`): each runs the client's own action and is greyed while the client's state refuses it, with the client's status line as the reason. Missing members drop the Genie group. |
| `ChatSystem._input` | F | Prefill the client opens the chat with ("/PM ", "/RoyalOrder ") taken into the Classic chat input (`NativeChat.TakeTyped`) |

#### Party, user vote, user card and clan page on upstream 3277df5

The party list, war board, user vote windows and character card use the plugin facades the client added in
3277df5; the reflected `ToggleRowMeter()`, `ToggleWarBoard()` and `_warBoardRowShares` of the cd63f98 social row
above are no longer read.

| Member | Kind | Used for |
| --- | --- | --- |
| `IGameParty.RowMeterMode`, `RowMeterShare`, `CycleRowMeter`, `ToggleWarBoard`, `MoveRow`, `AutoRejoin`, `AutoRejoinTip`, `ToggleAutoRejoin`, `CommanderBarHidden`, `ShowCommanderBar`; `GamePartyMember.HpLook` | API | Classic party list: row meter, War Board action, Move up / Move down arrows in the client's row order, automatic re-invite, commander bar restore, HP look art |
| `HudPart.WarBoard`; `IGameParty.WarBoardOpen`, `WarBoardMode`, `WarBoardAlpha`, `WarBoardRows`, `SetWarBoardMode`, `SetWarBoardAlpha` | API | Classic Party War Board (`ClassicWarBoard`); the client keeps the requests, their cooldown and the ranking |
| `_warBoardCool` (`Label`) | F | 3277df5 only: the client's 5 s tab cooldown, mirrored every frame. No longer read since ff78eef, which exposes `IGameParty.WarBoardCooldown` (see below) |
| `IGameParty.UserVoteHere`, `UserVote(id)`; `IGameUserVote` (`Title`, `SelectPrompt`, `Reasons`, `CostLine`, `AskQuestion`, `AskReason`, `AskSeconds`, `AskSecondsLeft`, `NoticeTitle`, `NoticeLines`, `Send`, `CancelSelect`, `Answer`, `CloseNotice`); `UserVote` constants | API | Classic `user_vote`, `user_vote_ask`, `user_vote_notice` on the message box (`ClassicUserVoteSkin`); the client's member menu offers User Vote in Juraid and answers Draw when the countdown ends |
| `IGameUserVote` proxy `Source` (one or more facade proxies) → bridge `_w`; `_userVoteAskShown` (`UserVoteAsk.Reason`) | F | Only when the client's text table lacks the reason texts (43790-43793, it returns "Reason n" or an empty reason line): the reasons and a received vote's listed reason show English captions (`NativeUserVote`); the client's text is used whenever present |
| `IGameUserInfo.Card` (`GameUserCard`, `GameRankRibbon`, `GameUserRankRow`), `Changed`; `UserInfoCard.PodiumPlaces`; resource `res://assets/ui/daily_rank/medal_{n}.png` | API | Classic character card (`ClassicUserInfoCard`); the client's card body stays hidden in the window, its Confirm button below the Classic card |
| `_fullMoonRow` buttons (Enter, Party Board, Auto Entry) | F | Named `seek_full_moon_enter/shout/auto` for the Classic party board's Full Moon row |
| `OpenClanMemo()` | M | Clan memo from the clan page's own-row menu and the Knights Management Memo action |
| `Net.ClanOnlineCount`, `Net.NoClanBonus`, `ClanBonusCard` (public) | API | Clan online bonus line on the Classic clan page |

#### Identity and appearance

| Member | Kind | Used for |
| --- | --- | --- |
| `_nameChangeEdit`, `_nameChangeBtn`, `_nameChangeStatus`, `CloseNameChange` | F/M | Name change |
| `_clanCreateMessage`, `_clanCreateName`; `_clanCreateNotice` *(fix/clan-requests)* | F | Clan creation, fee wording |
| `_disguiseGroupRows`, `_disguiseFormRows`, `_disguiseNote` | F | Transformation |
| `_siegeTaxRatePrompt`, `_nationTaxRateValue`, `_siegeTaxRateValue`, `_nationTaxRateBusy`, `StepNationTaxRate` | F/M | Council tax rate |
| `_equipViewPending`, `_equipViewShown`, `_equipViewStatus`, `RequestEquipmentView`, static `NationName` | F/M | Equipment inspection |
| `_genderPreview`, `_genderEditor`, `_genderStatus`, `_genderConfirm`, `_selfFace`, `_selfHair` | F | Gender change |
| `_changeHairStatus`, `_changeHairShown`, `CloseChangeHair`, `SetChangeHairStatus`, `OnChangeHairResult`; `_changeHairApply`, `_changeHairInFlight` *(fix/beauty-shop)* | F/M | Beauty Shop |
| `_transferPreview`, `_transferEditor`, `_transferStatus`, `_transferConfirm`, `_transferHeader`, `_transferList`, `_transferLayer`, `_transferPanel`, `_transferRows`, `_transferPicks`, `_transferNation`, `_transferInFlight`, `_transferShown`, `CloseNpcDialog`, `OpenNationTransfer`, `SelectTransferCharacter`, `SetTransferStatus` | F/M | Nation transfer window |
| `_transferRevision`, `_transferNotice`, `_transferCompleted`, `SetTransferLocked`, `CancelTransferConfirmation`, `SendNationTransfer(int, IReadOnlyList<>)`; `Net._nationTransferCandidates` *(fix/nation-transfer)*; `Net._nationTransferTarget`; `Net.NationTransferOpenEvent` (handler replaced) | F/M/E | Transfer confirmation, open guard |
| `_rebirthPick`, `_rebirthBonusLbls`, `_rebirthPickLbls`, `_rebirthAddBtns`, `_rebirthRemoveBtns`, `_rebirthBtn`, `_rebirthStatus`, `_rebirthLevelLbl`, `_rebirthPointsLbl`, `_rebirthLayer`, `_rebirthInFlight`, `_rebirthSent`, `_rebirthShown`, `SetRebirthStatus`, `RefreshRebirthUI`, `OnRebirthStatResult` | F/M | Rebirth |
| `_capeR/G/B`, `_capeRVal/GVal/BVal`, `_capeTicket`, `_capeBuyBtn`, `_capeHint`, `_capeStatus`, `_capeChosenLbl`, `_capeReqLbl`, `_capePriceLbl`, `_capeLayer`, `_capeChoice`, `_capePattern`, `_capeCurrent`, `_capeShown`, `_capeRequestInFlight`, `_capePreviewing`, `_selfVisual`, `SelfGear` | F/P | Cape service |
| `SetCapeStatus`, `CloseCape`, `ToggleCape`, `OnCapeResult`, `CapeCellTip`, statics `CapeNeedName`, `CapeSwatch`, `DressCape` (8 args) | M | Cape service |
| `Net.CapeResetEvent` | E | Unlock the draft after a released cape request |
| `Net.PetResetEvent` *(fix/familiar-replies)*, `Net.RebirthResetEvent` *(fix/rebirth-requests)* | E | Tell a disconnect from a cape release: close the picker |
| `RebirthWire.Busy` *(fix/rebirth-requests)* | F | Result reported for a refused local rebirth send (legacy `0` without it) |
| `Net.SendCapeBuy`, `SendChangeHair`, `SendRebirthStatChange` | M | Called reflectively: they return `void` upstream and `bool` on the fix branches |
| `_capeNoteLbl` *(3277df5)*; public `Domain.CapeShop` (`VoucherAllowed`, `ConfirmText`, `ContCost`, `Fill`, `Amount`, texts 10313/10319/10321/10322/15713), `Cape.CapeDef.Castle` | F | Cape service on 3277df5: the client's "Now equipped" or description becomes the cape name's tooltip and "Now equipped" the price line of the worn cape; the voucher is offered for castle capes only, with the client's refusal (10322); the Classic confirmation uses the client's purchase wording (10316/10318, Clan CONT and Noah lines; 10321 for the voucher). The client's own `SelectCape` (run on a clan change) no longer overwrites the Classic grade lines: the gate rewrites them every frame. `_capeContLbl` stays in the client's hidden column (the Classic price line carries the CONT cost). `IGameCapeShop` is not used: it has no pages, dye or preview, and its `Buy` shows the client's confirmation instead of the Classic one. |
| `LookPreview._pivot`, `_model` (set) | F | `LookFraming` |
| `LookEditor._races`, `_faceLbl`/`_hairLbl` (set), `_colour`, `_raceButtons`; `_locked`, `_faceSteps`/`_hairSteps`, `SetLocked` *(fix/beauty-shop)* | F/M | Editor names, 1-based display, lock |

#### Familiar

| Member | Kind | Used for |
| --- | --- | --- |
| `_petBagCells`, `_petNameLbl`, `_petLevelLbl`, `_petHpBar`, `_petMpBar`, `_petExpBar`, `_petSatBar`, `_petAttackBtn`, `_petDefendBtn`, `_petLootBtn`, `_petFeedBtn`, `_petDismissBtn`, `_petStatus`, `_itemTipPanel` | F | Familiar window names and details |
| `UsePetSkill(int)`, `SkillTooltip(Skill)`, `Now()`, `PetCooldown(int,double)`, `MyPetEntity()` | M | Skills, cooldowns, portrait |
| `_petHatchTabs`, `_petHatchPage`, `_petTransformPage`, `_petHatchName`, `_petHatchBtn`, `_petHatchStatus`, `_petHatchEggPick`, `_petTransformPetPick`, `_petTransformScrollPick`, `_petHatchInFlight`, `_petHatchShown`, `_petHatchNpc`, `_petHatchSlot`, `_petTransformSlot`, `_petScrollSlot`, `_petHatchLayer` | F | Trainer state (the native fields stay authoritative) |
| `IsPetEgg`, `IsFamiliarItem`, `IsTransformScroll`, `IsValidPetName`, `RefreshPetPickLooks`, `SetPetHatchStatus`, `ClosePetHatch` | M | Trainer rules and actions |
| `WhisperChat.Window`, `ServiceTabs._buttons` | F | Keyboard yielding, tab lock |
| `Net.SendPetHatch`, `SendPetTransform` | M | `bool` on fix/familiar-replies, `void` upstream |
| `HudLayout._moveGripOverlay`; bar recognised by the type names `PetSkillCell` and `HotGrip` | F | Familiar bar grip and default position |

#### World overlays

`NativeOverlays` prepares the overlays without a window id once the world is attached (`BecameAvailable`). It names
the controls below and mirrors the countdown urgency and registration state into metas; the client keeps every
control, callback, timer and position rule. All members exist on bare upstream.

| Member | Kind | Used for |
| --- | --- | --- |
| `_joinModal`, `_joinModalTitle`, `_joinModalTimer`, `_joinModalBar`, `_joinModalStatus`, `_joinModalCount`, `_joinModalBtn`, `_joinModalCloseBtn`, `_bifrostBanner`, `_bifrostTitleLbl`, `_bifrostTimerLbl`, `_bifrostBar`, const `BifrostUrgentSecs`; `_bifrostRemaining`, `_isEventRegistered` (clients before fa3cfc3, see the fa3cfc3 section) | F | Temple event registration message box and countdown plaque |
| `_inZoneLeaveBanner`, `_inZoneLeaveTitleLbl`, `_inZoneLeaveBtn` | F | In-zone leave plaque |
| `_deathPanel`, `_inviteAskDialog`, `_challengeAskDialog`, `_townRecallDialog` | F | Death notice and confirmation prompts on the message box art |
| `_upgradeNoticeLabel`, `_shoutLabel`, `_battleeventBanner`, `_battleeventResult`, `_clanBattlePanel`, `_lootPrompt` | F | Outlined broadcast lines |
| `_zoneabilityChip`, `_battleeventBoard`, `_bdwScoreBanner` | F | Zone chip and war score plaques |
| `_lootPanel`, `_lootCells`, `CloseLoot()` | F/M | Loot box: the client's twelve cells on two dropped-item grids, plus a close button |
| `_lootRuleMenu`, `_lootRuleItem`, `_clanMemberMenu`, `_ctxMember`, `PopupMenu.id_pressed` | F | Loot item, party member and clan member right-click menus shown as the Classic context menu; rows emit the client's own `id_pressed` |

Overlays the client builds later are prepared through their fields four times a second (`NativeOverlays.PrepareLater`):

| Member | Kind | Used for |
| --- | --- | --- |
| `_soccerBoard` | F | Soccer score strip (war score plaque with close) |
| `_nestTimerPanel` | F | Monster Stone and Chaos timer toast (outlined text) |
| `_royaleBoard`, `_genieSub` | F | Knight Royale sign-up board and genie quick panel (event frame) |
| `_eventTitleColumn`, `_gatherChip`, `_nereidFlagBoard`, `_nereidScoreBoard` | F | Information plaques |
| `_nereidStrip`, `_wantedToggle`, `_wantedOpen`, `_vanguardLayer`, `_zone` | F | Mini-map plates and Wanted buttons re-hosted at the top right (the Classic HUD hides the client mini map) |
| `_areaBanner`, `_msgOut`, `_orderMessage`, `_killSplash` | F | Outlined Arial Bold banners |
| `_towerButton`, `_dropOff` | F | Siege tower Board button (moved under the Classic target frame) and drop-off |
| `_rankerPaper` | F | Ranker paper buttons and text |

#### Events, rankings and progression

| Member | Kind | Used for |
| --- | --- | --- |
| `_ladderKarus`, `_ladderElMorad`, `_ladderOwnKarus`, `_ladderOwnElMorad`; `LadderRow.Rank`, `Mark`, `Name`, `Clan`, `Points`, `Bonus` | F | PK ladder (`rank`) columns |
| `_achTitleBanner`, `_achSearch`, `_achStatus`, `_achCategoryBox`, `_achListRow` | F | Achievement title banner, filters and category column |
| `_genieReturnHome`, `_genieHuntInPlace`, `_genieHideRange`, `_genieCureCurse`, `_genieOther` | F | Genie Others page |
| `_genieSkillPicker` (`PopupMenu`, `AboutToPopup`) | F | The client's Genie skill picker drops passive skills before it opens (`NativeSkills.FilterGenieSkillPicker`), as the skill window refuses to drag them. |
| `_genieSlots` (`Button[28]`: Attack 0-7, Recovery 8-15, Support 16-27), `_genieModes` (`Dictionary<GenieMode, CheckButton>`), `_genieParty` (`CheckBox[12]`), `_genieRecovery`, `_geniePetPotion`, `_geniePetSelect`, `_geniePetPercent`, `_genieHammerThreshold`, `_genieHammerStatus` *(3277df5)* | F | Genie window (`NativeProgression.PrepareGenie`): the three skill columns (8 + 8 + 12 cells, a party check under each support cell), the mode toggles named by `GenieMode` (`genie_mode_attack`, `_basicattack`, `_combo`, `_recovery`, `_support`, `_leadertarget`, `_hppotion`, `_mppotion`), the recovery percent, the Familiar potion, the 11 "Set Others" rows (`GenieTownReturn.OtherRows`, hammer rows included) and the hammer threshold. The removed `_genieAutoHammer`, `_genieSkills` and the Combo hint are no longer read. |
| `_genieSub` rows (`HudPart.GenieSub` seam, 3277df5) | F | The Classic quick panel keeps skinning the client's own panel (now on its own layer); the third monster row has no action button and keeps the row height of the others. |
| `StatBar._frac` | F | Original progress bar mirroring the vanguard countdown |

The event, ranking and war windows of the event-window family (`ClassicEventFamily.WindowIds`) are restyled from their
own controls and need no reflected member.

#### HUD on the bundled 2.x art (`assets/myko`)

Used only when the layouts are found (bundled in `assets/myko`, see `src/LocalAssets.cs`); without them nothing below is read.

| Member | Kind | Used for |
| --- | --- | --- |
| `_premiumChip`, `_clanPremiumChip`, `_clanPremiumInClan`, `_clockLabel`, `PlaceTopIcons` | F/M | Top-right group: clan premium bar and the time box; the client's chips and clock are hidden while the group shows them (the premium chip stays as an invisible spacer so the client's remaining top icons keep clear) (`NativePremium`) |
| `Net.GmListEvent` (`Action<IReadOnlyList<string>>`) | E | Not in the client yet. When a client adds it, the GM List bar follows it (`NativeGmList.Attach`); until then the list is empty |

#### Chat rooms, voice and chat categories on upstream 3277df5

The nation chat rooms open a group-chat style room window (`chat_room`). The Classic room (`ClassicChatRoomSkin`,
`ClassicChatRoomLayout`, preparer `NativeChatRoom`) frames the client's own room through `FrameWindow("chat_room")`;
posts, voice notes, the voice call, the roster, the popups and every callback stay native. No plugin facade covers
the room or `VoiceChat`; the plugin subscribes no room or voice event (it reads the client's controls and tweens).

| Member | Kind | Used for |
| --- | --- | --- |
| Plugin seam `FrameWindow("chat_room")`, `WindowFrame.SetDragHandle/Close/TitleChanged`, `HudWindow.SetMinimized/Minimized/MinimizedChanged/Body` | API | Classic frame, compact whisper bar when minimized, title `Name (n/max)` |
| `_roomScroll`, `_roomLog`, `_roomOlder`, `_roomComposerRow`, `_roomInput`, `_roomRecord`, `_roomSend`, `_roomRecordStrip`, `_roomRecordClock`, `_roomRecordLevel`, `_roomVoiceRow`, `_roomMembersToggle`, `_roomConfigButton`, `_roomJoin`, `_roomDeafen`, `_roomVolume`, `_roomMic`, `_roomVoiceGear`, `_roomVoiceLeave`, `_roomMembersColumn`, `_roomRosterRows`, `_roomRosterEmpty`, `_roomLeaderBox`, `_roomLeaderHandOver`, `_roomLeaderKick`, `_roomChannelsButton`, `HudWindow._header` (door and channel-list header icons) | F | Names for the skin and the audit; all must exist or the room keeps the plain Classic frame around the client's content |
| `_roomLayer` (popups: `_roomConfig`, `_roomVoiceSettings`, `_roomPeerPopup`, `_roomMemberMenu`, `_roomEmoticons`) | F | Classic menu panel on the popups as the client creates them |
| `_roomBlink` (`Tween`) | F | The minimized blink becomes the whisper attention blink on the compact bar |
| Roster row `StyleBoxFlat` (`UiTheme.Row` colours: `RowHover` selected, `7fd98b` speaking) | Look | Classic row state mirrored each frame |
| `_ncPubOpen`, `_ncPriOpen`, `_ncMembersTitle`, `_ncMembersCount`, `_ncMemberRows` | F | Nation chat Open buttons, members title and summary in the body (fa3cfc3 removed the private Open and the members window; only `_ncPubOpen` is read) |
| `ChatCategory.NoahKnights/ChatRoom/All`, `ChatType.NoahKnights` (34); `IGameCharacter.NoahKnight` (`ChatSystem.ChatEntry.Place` is no longer read since ff78eef, see below) | F/API | The Classic chat masks with `ChatCategory.All` (saved filters migrate with `chat.filter_rev` 2 like the client's `ChatPrefs`); Noah Knights tab (`\|`) only for Noah Knights, who lose Clan and Alliance. `IGameChat.Lines` carries rendered BBCode only, so the Classic chat keeps reading `_store` for its own format |
| `IGameCommands.Run(KeyAction.ToggleRun/AutoAttack)`, `ShowExitMenu()` | API | Replace the reflected `ToggleRunMode`, `ToggleAutoAttack`, `ToggleEsc(true)`; `StartCameraHalfTurn` has no key action and stays reflected |
| `WhisperLinks.Linkify/IsWebLink` | M | Whisper notices with web links open them, as the client does |
| `EmoticonPicker.InputButton` (the `TextureButton` in `_roomInput`) | F | Named `room_emoticons` and set to the inline emoticon size inside the Classic composer field |

`chat_room`, `user_vote`, `user_vote_ask`, `user_vote_notice` and `repurchase` are excluded from the generic
`ClassicSkin` window extender in `Plugin.cs`; their skins no longer undo its theme and stray frame. Item links and
emoticons in room posts need the installed content (`items.json`, `assets/ui/emoticons/sheet.png`), which
`chat-room-audit` mounts from `build/client/source-content/knightonline.pck`.

#### Changed shapes on upstream ff78eef

The plugin compiles unchanged on ff78eef; these reflected members changed shape at run time. Each call adapts to
the member it finds, so older clients keep the previous path.

| Member | Kind | Used for |
| --- | --- | --- |
| `World+BagCompanion.IntoBag` (`Func<int, int, int?, bool>`: the amount picked with the mouse wheel; before `Func<int, int, bool>`) | P/T | `NativeVendor` reroutes buys through the Classic approval and `NativeAnvil` builds the anvil companion; the delegate shape comes from `Native.MemberType`, the amount is ignored (the approval asks the count, anvil items move whole) |
| `WithdrawInto`, `VipWithdrawInto`, `ClanWithdrawInto` `(int, int, int? amount)`; `AskWithdraw`, `AskVipWithdraw`, `AskClanWithdraw`, `DepositSlot`, `OfferSlot` gained an optional `int? amount` | M | Storage drops into the bag pass the drag's `DragLayer.AmountKey` amount (null = whole stack); the optional ones bind through `Native.Method`'s default filling |
| `AttachWeapons(Node3D, int[], HandWearer, int npcType)`, static `WeaponWearer(bool npc, bool statue, int race, int npcId, int size, int modelId)`, static `AttachBuiltInPlugs(Node3D, string)`, `_mobIndex`; `World+Ent.Race`, `Size`; `NoWeaponNpcIds` removed | M/F | NPC portrait: weapons by the model's plug slots and scale, the model's own built-in weapons on a freshly loaded model; the unarmed list is read only where it still exists |
| `LookEditor._races` (now always in the editor, hidden without a race choice: the hair shop container leak fix) | F | Still named `look_races`; the Beauty Shop editor has it, invisible, so the Classic skin and `beauty-audit` treat a hidden list as no race choice |

Character previews: on ff78eef the skeleton's cached global bone pose of a `CharacterPreview` model can stay at the
bind pose while the animated idle pose is shown. Framed on that cache, the gender change, Beauty Shop, nation
transfer and cape service previews fitted the outstretched bind-pose arms and showed the character a sixth
smaller. `LookFraming` and `InventoryPortrait` now compose the bone poses from the animated local poses
(`LookFraming.ShownBonePoses`), which restores the approved framing.

#### Stack split and storage amounts on upstream ff78eef

Upstream 115b830 splits a dragged stack with the mouse wheel: the client's `StackDial` steps 1/5/10/50/100 per four
quick notches, the drag icon shows the count (white for the whole stack, `UiTheme.GoldBright` for a part) and the
drag data carries it under `DragLayer.AmountKey`. On desktop the client then no longer asks for a quantity in the
storages (`AsksStackAmount` is touch only), so a whole stack moved at once. The Classic UI keeps the original
quantity prompt and adds the wheel:

- **Classic dial** (`Layout/ClassicStackDial`). A Classic bag cell (`ItemSlot.PicksAmount`: the inventory window,
  the trade's bag and the vendor's trading inventory) arms the client's `StackDial` on the Classic drag icon
  (`IconDragPreview`), shows the count in the cell's count type and corner, and publishes the amount like the
  client's own cells. While a stack is dragged the wheel belongs to the dial (`_Input`, handled before the cells,
  so the storage pages do not turn). `ItemSlot.OnDropAmount` hands the picked amount to the drop.
- **Rule.** A part picked with the wheel moves at once. The whole stack (no wheel turn, or the wheel back at the
  whole stack) keeps the original Classic flow: the storages and the trade ask the count in the client's prompt,
  the magic bags ask in the Classic prompt, and a bag-to-bag move takes the whole stack. Bag-to-bag parts are
  offered, as upstream allows (`ItemMove.SplitsAcross` includes `InventoryToInventory`); the client refuses a split
  it cannot make. A part moved inside a storage is refused by the client ("Only whole stacks move inside the
  storage."). A part dropped on the vendor's catalogue still opens the client's sale prompt (the client's sale
  takes no amount).
- **Storages** (`NativeStorage`). The stored and embedded-bag cells set `ItemSlotView.PicksAmount`, so the client
  arms its own dial on them; the plugin gives that drag count the Classic count type and corner (one deferred
  restyle of the client's label on the drag icon). The whole-stack prompt opens the client's `_whAmount`,
  `_vipWhAmount` or `_clanWhAmount` (Classic quantity skin) with the client's texts, and the chosen count goes to
  the client again; every refusal stays the client's.

| Member | Kind | Used for |
| --- | --- | --- |
| `ItemSlotView.PicksAmount` (init), `DragLayer.AmountKey`, `DragLayer.ChosenAmount(Dictionary)`, `StackDial` (`Applies`, `Turn`, `Amount`, `Whole`), `UiTheme.GoldBright`, `Platform.TouchUi` | API | Wheel dial on the Classic and storage cells, amount of a drop |
| `DragLayer` node `DragLayer` (root `CanvasLayer`) and its `DragLayerRoot`; the drag icon's count `Label` | Node | Classic type for the client's drag count on storage cells (skipped when the layout differs) |
| `DepositSlot`, `VipDepositSlot`, `ClanDepositSlot` `(int abs, int target, int? amount)` | M | Commit a stored count (prompt or wheel part) |
| `AskWithdraw`, `AskVipWithdraw`, `AskClanWithdraw` `(int index, int target, int? amount)` | M | Commit a taken-out count; the client refuses a locked vault or missing clan rights |
| `DropOnWarehouse`, `DropOnVipVault`, `DropOnClanWh` `(int, Variant)`; `WhTakeFromBag`, `VipTakeFromBag`, `ClanTakeFromBag` `(int)` | M | Whatever does not ask: singles, wheel parts, vault keys, refusals, moves inside the storage |
| `_whShown`, `_vipWhShown`, `_clanWhShown`, `ClanCanWithdraw`, static `IsVipVaultKey(int)` | F/P/M | Prompt guards: a stale prompt does nothing, refusals stay the client's |
| `_whCompanion`, `_vipCompanion`, `_clanCompanion`; `BagCompanion.Take`, `IntoBag` (`Func<int, int, int?, bool>`, init) | F/P | A right-click or drop in the Classic bag window while a storage is open takes the same route |
| `OfferSlot(int abs, int? amount)` | M | Trade: a wheel part is offered without the prompt; the whole stack still asks (`OfferSlot(int)` on older clients) |

#### Party HUD and War Board on upstream ff78eef

Upstream rewrote its party HUD (class diamond with a level gauge, ping signal, buff row, drag reordering, a settings
popup) and removed its row meter switch ("the damage/healing switch lives on the war board only"). The Classic party
list keeps the original cells and shows the new member data through the party facade; nothing new is reflected.

| Member | Kind | Used for |
| --- | --- | --- |
| `IGameParty.MemberBuffs(id)` (`GamePartyBuff`: `SkillId`, `Icon`, `Tooltip`) | API | Buff icons on each cell's name line, 14 px in the client's order, ending under the bars; the name gives way (ellipsis) down to two icons, further buffs are not shown (the client also shows one line). Each icon has the client's buff tooltip; without art the skill's initial on a dark Classic cell (the client draws the bare letter) |
| `IGameParty.PingMs(id)`; `PartySignal` (`Level`, `Arcs`, `Tooltip`, `Unknown`, `NoSignal`) | API | Connection glyph in the arrow column beside the name line (three arcs in the client's colours, struck through without a signal, the client's "Ping: N ms" tooltip); the member tooltip's last line |
| `IGameParty.LevelPercent(id)` | API | "Level progress N.N%" in the member tooltip (the own row: the experience percent) |
| `GamePartyMember.Picked` (= the client's `PartyHighlight`) | API | The green selection frame follows the client's highlight: the picked member, or the party member who is the world target; targeting someone outside the party clears it. The client raises `Changed` when it moves |
| `IGameParty.WarBoardCooldown` | API | The War Board tab cooldown beside the Classic tabs, refreshed on `Changed` (raised whenever the shown seconds change); replaces the per-frame `_warBoardCool` mirror and the board's World accessor |
| `IGameParty.RowMeterMode`, `RowMeterShare`, `CycleRowMeter`; `Config.PartyRowMeter` (default now off) | API | Kept: the Classic "Meter" action still cycles Off, Damage, Healing. The client forces the row meter off at World build only while its own party HUD is shown (`KeepRowMeterOff` is skipped when a plugin replaces `HudPart.Party`), so the replacement keeps it. A live plugin disable leaves the client's row meter in the chosen mode until the player relogs (upstream has no switch for it any more) |
| `IGameParty.AutoRejoinTip` (long official texts 44730/44731) | API | Re-invite tooltip wrapped to the client's buff tooltip width (`TextWrap.Wrap`, 44 letters); the party list and the war board use the original tooltip look (co_tooltip_us) |

`MoveRowTo` (drag reordering) is not used: the Classic cells keep the Move up / Move down arrows (`MoveRow`).

#### Chat after room lines left the main chat on upstream ff78eef

Chat room lines stay in the room window: the client no longer subscribes `RoomChatEvent` in `ChatSystem`
(`OnRoomChat` is gone), and it removed the `\` prefix, `ChatRoomChannel`, `UseRoomChannel`, `NotInChatRoomText`
and `ChatEntry.Place`. `ChatCategories.Each` no longer lists `ChatCategory.ChatRoom`, although the bit stays in
`ChatCategory.All` and in the General tab. Chat rooms are open to both nations: the client dropped its
`NationMismatch` check before joining. The plugin subscribes no room event and reads no new member.

| Member | Kind | Used for |
| --- | --- | --- |
| `ChatSystem+ChatEntry.Place` (removed) | F | No longer read. `NativeChat` read it for every new entry, which logged "native member unavailable" once on ff78eef; `ClassicChatFormat.Line` lost its `place` (`[ Zone ]`) argument |
| `ChatPrefixes` (no `\`) | API | The Classic chat's own prefix list drops `\`, so `\text` on a tab is sent with the tab's prefix instead of bypassing it |
| `ChatCategories.Each` (no `ChatRoom`), `ChatCategory.All` | API | The Chat Settings list the client's categories (ten, without "Chatroom") and place the options under the last category row (`ChatWindow.SettingsOptionsTop`); the panel is 28 px shorter. The Classic mask stays `ChatCategory.All`, and the `chat.filter_rev` 2 migration keeps adding the chat room bit like the client's `ChatPrefs`; no line of that category reaches the chat any more |
| `ChatType.ChatRoom` (33) | API | `ClassicChatFormat.Channel` keeps its `room` tag and ink, because the client keeps the type, its category and its colour slot; no chat log line or bubble carries it |
| `NationRoom.Nation`, `NationRow` nation marks (`assets/ui/nation_chat/karus.png`, `elmorad.png`) | Look | Rooms of either nation are listed and joined through the unchanged Classic nation chat skin; `chat-room-audit` checks a room of the other nation in the list, its password prompt, and its room window opened by the server's reply |

#### Changed shapes on upstream fa3cfc3

Upstream fa3cfc3 (events on the USKO timetable, a new events window, chat reactions, whispers on the room thread)
changed these members. The two compile breaks and the one run-time break are fixed in place; the window changes are
covered by the four sections that follow (events and sign-ups, chat room, whispers and the Private tab, skill bar).
On a client before fa3cfc3 the new members are missing: each path checks for them and keeps the earlier one.

| Member | Kind | Used for |
| --- | --- | --- |
| `Net.WhisperLine` (removed), `Net.WhisperHistory(name)` now returns `IReadOnlyList<RoomPost>` | API | The whisper line restyle reads the `IGameWhispers.History` facade, which keeps `GameWhisperLine(Mine, Notice, Text)` on both clients (a voice note reads as "Voice note (m:ss)") |
| `TempleEventType` moved from `LibreKO.Network` to `LibreKO.Domain` | API | Preview only (`ForgottenTempleAudit`) |
| `World.DressCape` is an instance method (it hides capes in the Chaos dungeon) | M | The cape shop dresses the character's own body through the world instance, which binds the static and the instance shape |
| `World+WhisperChat.Log`, `Scroll`, `Input` fields replaced by `Thread` (`ChatThreadView`) and `Composer` (`ChatComposer`) | F | Whisper skin on the thread and composer (section "Whispers and the Channel list Private tab on upstream fa3cfc3") |
| `_roomScroll`, `_roomLog`, `_roomOlder`, `_roomComposerRow`, `_roomInput`, `_roomRecord`, `_roomSend`, `_roomRecordStrip`, `_roomRecordClock`, `_roomRecordLevel` replaced by `_roomThread` and `_roomComposer` | F | Chat room skin rebound to the shared thread and composer (section "Chat room on the shared thread and composer, with reactions, on upstream fa3cfc3") |
| `_ncPriJoin`, `_ncPriLeave`, `_ncPriOpen`, `_ncPriChange`, `_ncPriPersons`, `_ncSearch`, `_ncMembers*`, `OpenNationSearch`, `NationChatRules.Summary`, window ids `nation_chat_members`, `nation_chat_search` (removed) | F/M | The Private tab is a search row (`_ncSearchEdit`, Search, Create room); members live in the room window |
| `_bifrostRemaining`, `_isEventRegistered`, `_eventType`, `EndBifrostEvent` (removed): sign-ups are `Net.SignUps` (`TempleSignUpBoard`: `Current`, `Registered`, `SecondsLeft`) | F | The Classic registration and countdown read the urgent and registered state from them (section "Events window and temple sign-ups on upstream fa3cfc3") |

#### Events window and temple sign-ups on upstream fa3cfc3

Upstream rewrote the events window (`events`) as a card list and keeps temple sign-ups per event, so two can be
open at once. The Classic event family composes the new window from its own controls (`ClassicEventSchedule`, found
by the shape the client builds; no member is reflected) and the registration reads the new sign-up board.

| Member | Kind | Used for |
| --- | --- | --- |
| `Net.SignUps` (`TempleSignUpBoard`: `Current`, `Registered`, `SecondsLeft(type, utc)`), `World.BifrostSecondsLeft(utc)` | P/M | `NativeOverlays.SignUpState`: the shown sign-up's seconds (with none open, the plain Bifrost countdown), registered = `Registered == Current != None`, registered elsewhere = another open sign-up, read only while the box or the plaque is shown. The registration box and the countdown plaque turn urgent (red bar and ink) and registered (Ready ink, Cancel); registered elsewhere shows the client's "You are registered for ..." in the muted ink with Close alone. Clients without `Net.SignUps` keep reading `_bifrostRemaining` and `_isEventRegistered` |
| Events window: the filter row (two `ButtonGroup` tabs and the "Only my level" toggle), the 640-pixel `ScrollContainer` list, the cards (`PanelContainer` column: glyph tile, name and level band, description, status with Join or Open, time chips, weekday pills), the shift and empty labels | Shape | Cards on the quest inset (lit while live), glyphs on the nation inventory cell, time chips and weekdays as small insets, tabs on the chat tab art, the check on the chat check socket, Join/Open as the service button. The status ink the client rewrites every second is mapped to the event inks; the list takes its cards' height up to the client's 470 pixels, so loading and empty are a 668x144 box. Every native callback stays (tab and filter `Toggled`, Join to `ShowSignUp(type)`, Open to `ToggleLottery`) |
| `UiTheme.UnderlineTabButton` underline (`ColorRect` child) | Look | Drawn transparent; the chat tab art shows the chosen page |
| `_joinModalBtn` font colour (the client recolours Join and Cancel per state) | Look | `ClassicButtonCaption` keeps the native text transparent, so only the art caption draws (fixes a doubled "Cancel"/"Join"). It clears the client's colour inside the button's own `ThemeChanged` (raised by `AddThemeColorOverride`), so no frame draws the native text |
| `World.FormatModalTitle` (`ToUpper()` with the system culture) | Look | On a Turkish system "Juraid" becomes "JURAİD"; the Classic registration title keeps the English capitals |

The whole event family now gives its windows the original tooltip look (co_tooltip_us), and its restyle skips
tooltip popups. The Chaos skill page keeps the client's dimming (`ChaosSkillIcon.Modulate`, cube skills and the
Chaos blink); `event-windows-audit` checks it. The former 516-pixel body width of `events` is gone, so on clients
before fa3cfc3 the old events window keeps its natural width (`ClassicEventFamilyLayout.BodyWidth` stays as the
declarative per-window hook).

#### Chat room on the shared thread and composer, with reactions, on upstream fa3cfc3

The room's log and composer are the client's shared controls (`ChatThreadView`, `ChatComposer`), also used by the
whispers. `NativeChatRoom.Prepare` names them and their parts for the skin and the audit; the Classic room restyles
the thread rows in place (the thread keeps its rows to refresh reactions and voice notes). The reaction popups live
on the World's own `_reactionLayer` (CanvasLayer 80), shared by rooms and whispers; `NativeChatReactions.Attach`
watches it through one tracked hook (`World._reactionLayer (reaction popups)`, released with the World or at
shutdown, every handler guarded by `Plugin.Active`) and `ClassicReactionStyle` dresses each popup on the Classic menu
panel as the client creates it. The client keeps building, sizing, placing and wiring every control; no facade
covers the thread, the composer or the reactions, and the plugin subscribes no room, voice or reaction event.

| Member | Kind | Used for |
| --- | --- | --- |
| `_roomThread` (`ChatThreadView : ScrollContainer`), its `_log` and `_older` | F | Named `room_log_scroll`, `room_log`, `room_older`: the Classic log field and scroll bar, the post rows dressed as they arrive, the Classic "Load older messages" |
| `_roomComposer` (`ChatComposer : HBoxContainer`), its `_field`, `Input` (property), `_record`, `_send`, `_strip`, `_clock`, `_level` | F | Named `room_composer_row`, `room_field`, `room_input`, `room_record`, `room_send`, `room_record_strip`, `room_record_clock`, `room_record_level`: Classic field, cells, recording strip and meter. The composer widens its input style for the emoticon button each time it enters the tree; the Classic field keeps its own room |
| `ChatThreadView` row shape: `MarginContainer` > `HBoxContainer` (alignment = side) > react slot `CenterContainer` > `Button` and stack `VBoxContainer` > bubble `PanelContainer`, pill holder `MarginContainer` > `HBoxContainer` > pill `PanelContainer`; day chips `MarginContainer` > `CenterContainer` > `PanelContainer`; notices `CenterContainer` (a `RichTextLabel` when they carry web links); `NoteRow` play `Button`, `VoiceWaveformView`, `NoteDownloadRing` | Look | `ClassicChatRoomSkin.StylePost`: Classic bubbles and sender colours as before, the react button in the original item cell (`ReactCell`, no longer flat so the cell shows), the reaction pill as a Classic chip (`Pill`; the client swaps its own hover style in on enter and exit, so the Classic one follows), Classic day chips and notices. `ReactCell` and `Pill` are public for the whisper skin |
| The composer's `EmoticonPicker` (now the composer's own child) | F | Watched like the room layer; dressed synchronously before the client sizes and places it. The whisper composer's picker takes the same `ClassicReactionStyle` look |
| `World._reactionLayer` with `ReactionBar`, `ReactionDetails` and the reaction `EmoticonPicker` (matched by type name; they exist since fa3cfc3) | F | `ClassicReactionStyle`: opaque Classic menu panel with the client's padding; the bar's picks on the Classic menu hover and the current reaction in the gold selection frame (re-marked on `AboutToPopup` after the client's `ShowAbove`; the client's picks are flat, so its own marks never showed); the card's heading, filter chips (chosen chip in the gold selection frame; the count in Arial Bold, a Unicode emoji glyph at the client's 14 px), gold rule, Classic scroll bar and member rows (initial in the original item cell in the sender colour, Arial Bold name, "Click to remove" in the muted ink, Classic list hover) dressed again on every fill; the pickers' tabs as Classic toggles and the emoji on the Classic hover |
| `_roomConfigKeep`, `_roomPushToTalk`, `_roomPeerMute` (`UiTheme.FlatCheck`, per-control SVG check icons since fa3cfc3) | F/Look | `ClassicChatRoomSkin.StylePopup` puts the original `{nation}_chat_us` check box (`btn_check_normal`) on every popup check; "Keep the room" keeps the client's autowrap inside the card |
| `_roomVolume`, `_roomPeerVolume` (`VoiceLevels.MaxVolume` 2, "up to 200%") | Look | Classic slider unchanged; the range and the tooltip stay the client's |
| `_ncPriJoin` (removed; a double-click on a row enters) | F | Preview only: `chat-room-audit` enters rooms of both nations by double-click |

#### Whispers and the Channel list Private tab on upstream fa3cfc3

Whispers run on the room's thread view and composer (`ChatThreadView`, `ChatComposer`): voice notes, reactions and
90-day history. The Classic PM (`ClassicWhisperSkin`, `ClassicWhisperThread`, `ClassicWhisperLayout`, preparer
`NativeWhispers`) finds both in the window body by type name before it moves anything. On older clients, and for
the HUD fixtures, the previous scroll, input and "Send" path stays. No plugin facade covers the thread or the
composer, and the plugin subscribes no thread, composer or voice event (it connects only to the nodes' own signals).

| Member | Kind | Used for |
| --- | --- | --- |
| `ChatThreadView` (`ScrollContainer`): `_log`, `_older`, `_rows` (`PostRow.Root` keyed by `RoomPost`); public `Pinned`, `ShowReactButton`, `ScrollToEnd`, `OlderRequested` | F | The thread fills the original log area. Its rows are restyled in place as they enter `_log` (once per batch, at the end of the frame) and are never replaced, because the thread refreshes reactions and voice notes through them. `_rows` maps a row to its post for the line's text, ink and time. "Load older messages" is the Classic action button |
| Row tree of `BuildPost`, `BuildNote`, `BuildNotice` and `DayChip` | Look | The restyle walks it: the line `HBoxContainer` (end-aligned for own posts) holds the react slot and the stack; the stack holds the bubble (column with the text and time, or the note: play `Button`, `VoiceWaveformView`, clock `Label`) and the pill holder. Result: no bubble or time stamp (the time is the line's tooltip), own lines white on the right, the partner cyan on the left, notices yellow, the react button a title-bar icon in a 14 px cell beside the first line, reactions on a gold-line chip, a voice note as a play cell, waveform and clock on one line, day markers as small chips. A row whose tree changes keeps the client's look |
| `ChatComposer` (`HBoxContainer`): `Input`, `_field`, `_strip`, `_record`, `_send`, and the `EmoticonPicker` it adds as its own child | F | The field (with the input) sits in the original edit field, and the composer still hides it while a note records. The input's 20 px emoticon button (`EmoticonPicker.InputButton`) becomes a 12 px button on the dark band of the original edit art, level with the send and record cells. The record button sits in a cell that mirrors the send cell, and the recording strip covers the edit band. The original send cell runs `_send` and is off while the composer hides it; its art has no disabled image, so it takes the Classic cells' disabled tint (0.55). The picker has the room picker's look (`ClassicReactionStyle`) |
| `World+WhisperChat.Composer` (`Input` is now a property that reads it) | F | Compose target, focus, and the "after sending, leave the input" hook (`_send.Pressed`, `Input.TextSubmitted`). Older clients keep `Input`, `Log` and the "Send" caption |
| `WhisperChat.HeaderIcons` (hidden while minimized) | Look | The Classic title-bar actions follow it across minimize and restore |
| `Config.WhisperSize` (`BubbleMax`, the thread's minimum size) | Look | The original frame keeps its log area (pinned every frame) and lines wrap at the log width |
| `_ncSearchEdit` (Private tab: room name, Search, Create room), rows with the "Double-click to enter" tooltip | F | Named `nation_search_edit`. The generic social restyle draws the row: the Classic edit field, the action buttons and 22 px rows. `_ncPriOpen` and the members preparer are gone, and `nation_chat_members` and `nation_chat_search` left `ClassicSocialLayout.WindowIds` |

#### Skill bar and the Chaos lock on upstream fa3cfc3

In the Chaos dungeon, fa3cfc3 refuses every skill while the character blinks after a respawn
(`ChaosBattle.BattleLocked`: zone 85 and `Net.SelfAbnormal == RespawnBlink.Blinking`, read as `ChaosBattleLocked` by
`HotbarUsable` and `SkillsLocked`). Cube skills stay dimmed until their item is in the bag (`SkillItemHeld`), on the
skill bar and on the Chaos skill page. The plugin needs no change for this: it reads the same members as before.

| Member | Kind | Used for |
| --- | --- | --- |
| `HotbarUsable(SkillData.Skill,double)`, which now includes `ChaosBattleLocked` and `SkillItemHeld` | M | The Classic bar dims from the client's own test (`NativeHudChat.HotbarUsable`). So while the character blinks, every slot is dimmed, and after the blink a cube skill stays dimmed with a red 0 until its item is in the bag. A click still reaches the client's `FireHotSlot`, which refuses it with "Casting failed", as on the native bar. If the member is missing, the bar dims from item counts only. |
| `chaos_skill` page: `ChaosSkillIcon.SetUsable` sets `Modulate` to `HotSlotCell.DimColor` | — | The Classic event family restyles the frame, the type and the buttons. It leaves the icons and their tint to the client. |

`skill-rules-audit` checks this for both nations, against the client's World, using the members below. These
members are used by the preview only:
- `ChaosBattleLocked`, `SkillItemHeld`, `HotSlotCell.DimColor` and `Net.SelfAbnormal`
- `BuildChaosSkillPageUiPreview`, `ChaosSkillPageTick`, `FlipChaosSkillPage`, `ChaosSkillPages`, `ChaosSkillsPerPage`,
  `_chaosSkillIcons` and `ChaosSkillIcon.Skill`
- `_combatLog`, `TextCastingFailed`, `SystemText`, `_pendingCasts` and `_continuanceSkill`

#### Preview-only members

The preview fixtures also reach client internals that the plugin never touches at runtime. Examples are the
`Build*Panel`/`Build*Window` builders, the `On*` reply handlers, `Net.SeedPreviewEnter`/`SeedPreviewPet`, the
`ChatSystem` constructor with `Build`/`DetachNetwork`, `_itemTipLayer`, and the `Net` event backing fields used
to deliver replies. An upstream rename there breaks an audit, not the game. The full lists are in the group port
reports (see section 6).

## 4. Known differences from the former fork build

These differences remain on `classic-integration`. Bare-upstream degradations are listed in section 2.

**Chat and HUD**

- Service notices that `World` code sends through `CombatNotice` (upgrade results, warp travel, Beauty Shop
  success, cape updates and others) appear in Info as status lines, as upstream does. The fork had rerouted about
  40 call sites to the chat. `ChatSystem.Info` and server notices still reach the Classic chat as System lines.
- Auto-run continues while the player types in the Classic chat. This matches upstream's native chat: the plugin
  reports typing through upstream's `SetTyping`, and upstream does not stop movement for it.
- Upstream publishes a chat entry before it stores it. `ChatWindow` therefore re-reads the history one deferred call
  later, coalescing bursts. There is no visible difference.
- The minimap and full-map heading follow upstream's camera yaw. The fork's travel-direction heading was not ported.
- Upstream still builds its own nearby-players card inside the hidden chat panel. It stays invisible and the
  plugin rebuilds its rows from it. On touch UI there is no card, so `NearbyPlayers` is empty.
- A native whisper row, or the whisper fade tween, can exist for one frame before the plugin replaces or kills it.
  In practice this happens before the frame is drawn.
- The client's own combat-log panel gets no gold line, because the plugin's gold log handler runs first. Only
  Classic Info shows gold income and spending, and Classic hides that panel.

**Character and NPC**

- The Escape entry for the clan details window is inserted before the main windows, not directly after clan points.
- The preset label is rewritten one frame after each native refresh.
- Party docking is reapplied on visibility, resize and viewport change. Party no longer takes part in the
  right-hand dock stack, as in the fork.

**Inventory, storage, anvil, services**

- A partial bag-to-grid move goes to one destination (`MoveBetween(from, to, amount)`). The fork's multi-step plan
  (fill several stacks, then one free cell) is not reproduced. An amount that does not fit is refused by the client.
- Returning a staged anvil scroll stack to another bag cell releases the socket and leaves the stack where it is.
  The fork moved one scroll on the client only. The server refuses partial in-bag splits, so the fork ended in the
  same state.
- Repair All checks the cost of the first damaged item only. Later items are refused by the server, and the queue
  is cleared.
- The in-vault rearrangement (op 4) is written by the plugin to `Net._conn`. The client only knows about it through
  the plugin's in-flight flag.
- Storage drag previews keep Godot's default grab point and scale. The anvil uses the Classic `IconDragPreview`.
- The warehouse's embedded bag cells read the live inventory every frame. The fork could show a stale count until
  the next warehouse refresh.

**Vendor, trade, merchant**

- `MoveBetween` does not refuse while a vendor reply is pending. The Classic shop's bag cells refuse moves while a
  modal is open, and the separate inventory windows are closed while the shop is open.
- The client still pops its exchange request dialog. The plugin hides it in the same event dispatch, before it is
  drawn.
- Compact vendor pages replace `_vendorCatalogue`. Buy packets keep the source line and list.

**Identity and appearance**

- The player menu always sends an equipment-view request. The plugin applies only the reply with the requested name,
  but it cannot refuse a second request while one is pending.
- Toggling the cape window while a purchase is pending hides it and closes it at the end of the frame. A second
  toggle in that same frame closes it instead of being ignored.
- Nation transfer replaces the client's `OnNationTransferOpen` handler. When the plugin ignores a list it did not
  ask for, it restores Net's cached candidates.

**Familiar**

- Calling `OpenPetHatch` while the trainer is open resets the name and the staged items. The trainer restores the
  staged items on the next frame, but not the name.
- A native familiar refresh that has no Net event (for example a death state change) reaches the details on the
  next tick.
- Item tooltip panels become mouse-transparent only after a familiar cell has shown them.

**Market and store**

- The compact Power-Up Store layout (below 960 px) and touch centring exist only in the plugin's store preparer,
  under the Classic store skin. The fork had them in core for every theme. The legacy `upstream-integration-audit`
  puts the generic frame on the store at 800×600, so its `pus-compact` check fails. `store-audit` covers the real
  case and passes.
- Godot names unnamed nodes `@Class@N`. The plugin names native controls after the client adds them, so these
  counters in audit check strings differ from the fork's. Normalise them before you compare check lists.

## 5. Optional client hooks

None of these is required. Each would replace a reflection or a workaround.

**Windows and dialogs**

- A window action and state registry: `IGameWindows.Invoke("anvil", "reset" | "back")`,
  `("repair", "all" | "close" | "take")`, and a state bag (busy, compound, error, scan outcome, pending,
  selected). It would replace the per-frame meta mirroring.
- One interception point for confirmations and prompts: vendor confirm, exchange request, final and amount, merchant
  open, rebirth, cape and nation-transfer submit. This should include a submit entry point that does not ask again.
- `ui.ReplaceNpcMenu(...)` for the anvil selection window.
- `SetBodySuppressed("repair")` for the repair catalogue.
- A `HudWindow` VIP PIN dialog (`vipwarehouse_pin`).
- A wildcard `ExtendWindow("whisper_*")`, plus a row builder or row metas.
- `PluginUi.EscapeCloses(Func<bool>, Action)`.
- Buy-back close (3277df5): the `repurchase` header X hides the window before its `Closed` handler runs, so
  `CloseRepurchase` returns early and never sends `RepurchaseReply.Close`. The plugin closes through
  `IGameRepurchase.Close()`. Fixed upstream in 26e13b5 (#163, on ff78eef: the close follows the buy-back session);
  the plugin's facade close is equivalent there and stays for 3277df5 clients.
- `HudWindow` keep-position and dock-override flags.
- `HudWindow.AttentionChanged`.
- A replaceable resize grip with `Resizing`/`ResizeEnded` events, or seam snapping as a `HudLayout` option.
- Stable names, or public accessors, on `LookEditor` and `LookPreview`. On `LookEditor` the plugin uses the rows,
  steppers, value labels, colour button and colour availability. On `LookPreview` it uses the camera, the model and
  a gear-aware `Show`. Both also need `RemoveChild` before `QueueFree`.
- A `PriceChart.Background` style box.
- `UpgradeSocket.CanDrop`/`Dropped` and an "embeds full inventory" option for piece change.

**Inventory and items**

- On `IGameInventory`:
  - bag-companion access (`Companion`, `CompanionBusy`, `Fit`, `ReservedCount`, `CloseCompanion`) and a "do not
    open the inventory" attach option;
  - `ConfirmDrop` and `TransferToInventorySlot`.
- An `IGameStorage` read model with `Deposit`, `Withdraw`, `Move`, `Page` and `PageSize`.
- `Net.SendVipWarehouseStore`/`SendClanWhStore` with op-4 acknowledgement handling.
- `IGameItemRules.PlacementFilter` for anvil sockets.
- In `ItemSlotView`: the Classic count badge, the full-cell count layout, and a drag preview that keeps icon size
  and grab point.
- `MoneyEdit.GroupDigits`.
- A `quantityOnly` flag on the amount prompt.
- `MoveBetween` refusing while a vendor reply is pending.
- Server: partial in-bag splits. Upstream since 115b830 (ff78eef): the item move carries an amount and
  `ItemMove.SplitsAcross` includes bag-to-bag moves; the Classic bag cells offer them through the wheel dial.

**Chat, HUD, notices**

- A `SystemNotice` → `GameLogKind.System` classification for World-side notices.
- Movement that treats plugin typing like native typing (`!Chat.IsActive || Chat.PluginTyping`).
- Store-before-publish in `ChatSystem.AddEntry`, or structured entry events.
- A gold-change log hook, or `GoldIncome`/`GoldExpense` log kinds.
- `PluginUi.ExtendHud(HudPart.FamiliarBar, ...)` with default position and drag-handle options.
- A native-slot tooltip factory.

**Content and requests**

- An event (or names) for `ShowQuestView`, quest notification captions, and quest-log reward refreshes.
- A name on `_clanContent` and an `OpenClanTab` API.
- `IGameModels.BuildNpc(modelId)` for portraits.
- A `PetSheetShown` notification.
- An `OpenPetHatch` that keeps an open draft.
- Client request guards the fork had:
  - one outstanding equipment-view request;
  - Net ignoring a nation-transfer candidate list while an account is open;
  - `ToggleCape` refusing while a request is in flight.
- `Send*` methods returning `bool` upstream. The fix branches already do this.

**Preview correctness**

- `World.UpdateInventoryTooltip` using `_itemTipPanel.GetViewport()`.
- `MouseFilter = Ignore` on item tooltip containers.
- A shared coin maximum constant.

## 6. Verifying after an upstream update

1. **Build the plugin twice.** Build once against a bare upstream client and once against `classic-integration`
   (upstream plus the remaining fix branches, rebased):
   `dotnet build KnightOnlineUiClassic.csproj -c Release -p:LibreKOClientDir=<folder with LibreKO.dll>`.
   Both builds must have 0 errors. The known warnings are CS0108 in `ClassicEventsSkin` and `ClassicWorldOverlays`
   and CS8604 in `ClassicVendorSkin`. A failure here means a public API type changed.
2. **Check reflected names statically.** Extract every string literal passed to `Native.*` under `src/Native`.
   Check each identifier against the new client's `Client/src`. Compare method arity too, because overloads change
   between upstream and the fix branches (`MoveBetween`, `SellSlot`, `SendNationTransfer`). Every name in the section 3
   tables must exist on `classic-integration`. Only the names marked with a branch may be absent upstream.
3. **Run the plugin unit tests:** `dotnet test tests/AnvilRules.Tests` and `dotnet test tests/ClassicChatFormat.Tests`.
   The plugin csproj excludes `tests/**`.
4. **Build the preview against `classic-integration`:**
   `dotnet build preview/Preview.csproj -p:LibreKOClientDir=...`. The audits use APIs from the fix branches
   (for example `bool SendPetSkill` and the `ushort` move amount), so the preview targets the integration client.
5. **Run every audit mode for both nations.** Use `godot --path preview -- <mode>` for Human and append `karus`
   for Karus. Set `LIBREKO_AUDIT_CLIENT_PACK` to the client's content pack and `LIBREKO_AUDIT_OUTPUT_DIR` to a fresh
   folder per mode. The modes are the routes in `preview/Preview.cs`:
   - preview variants: `preview`, plus `horizontal`, `hotbar-check`, `detail` and `native-reference` combinations;
   - HUD and chat: `hud-audit`, `chat-colours-audit`;
   - character: `character-audit` (with `inventory-parent-audit`), `details-audit`, `skill-audit`, `skill-rules-audit`,
     `party-audit`;
   - NPC: `npc-integration-audit` (with `npc-portrait-regression`), `npc-portrait-audit`, `npc-design-audit`,
     `hunt-icon-options`, `upstream-npc-audit`, `forgotten-temple-audit`;
   - items and storage: `inventory-audit`, `storage-audit`;
   - trade: `vendor-audit`, `trade-audit`, `merchant-audit`, `merchant-palette`;
   - anvil and services: `anvil-audit`, `classic-services-audit`, `npc-services-audit`;
   - market and communication: `mail-classic-audit`, `communication-audit`, `market-price-audit`,
     `merchant-search-audit`, `store-audit`, `auction-audit`;
   - identity and appearance: `identity-audit`, `council-rate-audit`, `equipview-audit`, `gender-audit`,
     `beauty-audit`, `nationtransfer-audit`, `rebirth-audit`, `cape-audit`;
   - familiar: `pet-audit`, `pet-portrait-audit`, `pet-connection-audit`, `pet-keyboard-audit`,
     `pet-world-reconnect-audit`;
   - legacy: `upstream-integration-audit`.

   Some audits are known to fail on the fork baseline too, with the same messages:
   - `details-audit` and `npc-portrait-audit`: a stale `ClassicNpcDialogue` → `ClassicDetailPanel` cast;
   - `npc-services-audit`: "gender not registered as open in Escape stack";
   - `preview-hotbar-check` and `preview-horizontal-multi-detail`: the F4 hotbar check;
   - `upstream-integration-audit`: `pus-compact` (section 4).
6. **Compare with the previous accepted run.** The audit runner and comparison scripts live in the maintainer's
   workspace, not in this repository. Any equivalent works:
   - Launch both nations per mode with a timeout and record exit codes.
   - Normalise `@Class@N` in check lists, then require the same checks in the same order.
   - Compare images against the previous run. Use two runs of the baseline as a noise mask.
   - Known noise: real desktop cursor hover and tooltip position, text caret, 3D preview and cloth or particle
     variation, animation timing, random rewards, and mail clock stamps.
   - Search the logs for `native member unavailable`. On `classic-integration` it must not appear.
7. **Review the result visually.** Check the eight Character Report, Quest, Clan and Friend pages for Human and
   Karus, plus selection and disabled states. They must stay pixel-identical. Then install the verified build into
   each client's plugin folder.

The per-group port reports and fix-branch reports were written during the port. They record parity runs, reviewed
image differences and draft pull request texts. They are kept in the maintainer's workspace under
`research/upstream-plugin-port/` (`port/*.md`, `fixes/*.md`).
