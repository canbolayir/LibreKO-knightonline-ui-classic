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
  raises `Native.MemberMissing`. Then it returns a default value instead of throwing. `Has`, `HasMethod` and
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
- **`NativeWindows.OnDescendantAdded(root, action)`** listens to `SceneTree.NodeAdded`. It describes rows, cards and
  cells the client adds later (mail rows, store cards, trade rows, recipe rows, merchant signs, whisper rows). It runs
  before the skin's deferred styling.
- **HUD and layout.** `NativeHud` hides or extends HUD elements that have no public `HudPart` (the mail,
  achievements, attendance and Power-Up Store icons, and the familiar bar). It also carries the whisper styler.
  `NativeLayout` recreates three things the fork had: the legacy resize grip, chat/Info seam snapping and
  `content_open_anchor`. `NativeUi` provides the Classic count badge (`CountBadge`), ungrouped money digits
  (`GroupDigits`, through `PlainDigits`) and the per-window attention styler.
- **Game services (`NativeGame.cs`).** `INativeGame` holds the services the skins need beyond the public plugin API:
  - character panel and NPC portrait;
  - chat history, item links and nearby players;
  - hotbar selection, run/sit/attack commands and minimap visibility;
  - inventory `MoveAmount`, `TransferToInventorySlot` and `ConfirmDrop`.

  C# 14 extension members put these services on `IGameWindows`, `IGameChat`, `IGameHotbar`, `IGameCommands`,
  `IGameMap` and `IGameInventory`, so skin code reads like the old fork API. `ClientGame` is the runtime source.
  Each member falls back to what the public API already offers when native support is absent. For example,
  `MoveAmount` moves the whole stack when the client has no `MoveBetween(int,int,int)`. The preview replaces
  `NativeGame.Source` with fixture data.
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
| HUD, chat, whispers (`NativeHudChat`, `NativeChat`, `NativeLayout`, `NativeHud`) | Classic chat and Info, history format, timestamps and custom colours, private-message exclusion, `ChatSystem.Info` and server notices as yellow System lines, bubble colours, Shift-click item links, nearby players, whisper styling and compose target, chat colours editor and palette, legacy resize grip, chat/Info seam snapping, hidden HUD icons, taskbar service toggles, notification count, hotbar selection, command state, gold income/expense lines in Info. | `fix/chat-whisper-state`: an existing whisper window comes to the front when it is reopened or receives a whisper, and a click focuses the top-most window. On bare upstream the window can stay behind another conversation, and a click can focus the window underneath. `fix/escape-order`: Escape closes chat colours, the mail reader, mail compose and nine more windows before it opens the game menu, and it closes the shop after its amount prompt. |
| Character and NPC (`NativeCharacterPanel`, `NativeCharacterNpc`, `NativeNpc`, `NativeQuestLog`, `NativeParty`, `NativeSkills`, `NativeEvents`) | Character Report, Quest, Clan and Friend pages through `CharacterPanelBridge`; Knights Management; NPC dialog portraits and quest captions; Quest Details reward rows; preset label wording; party roster, click-to-target and top-right docking; seek-party board; skill weapon requirement and mastery trees; temple-event leave confirmation. | `fix/quest-rewards`: the "Selected reward" and "Received rewards" states in Quest Details. On bare upstream `_questRewardSelection`, `_questRewardOptionTitle` and `_questRewardOptionBox` are reported missing once, and the rows always list the reward options. `fix/stat-presets`: preset totals and validation from the class base. The plugin's "Allocated X / Y    Remaining Z" text works either way. |
| Inventory and storage (`NativeInventory`, `NativeStorage`, `VipVaultPinPrompt`) | Classic inventory; identity-checked destroy; magic-bag right-click target; warehouse with 24-cell pages and amount prompt; VIP and clan vault pages; in-vault rearrangement, which the plugin sends itself; Classic VIP PIN prompt; clan coin prompt. | `feat/magic-bag-amount`: part of a stack between bag and magic bag. On bare upstream a partial amount moves the whole stack. `fix/storage-transfers`: VIP and clan quantity prompts with merge-aware destinations (`VipWhPending`/`ClanWhPending` `Count`/`Merge`), merging into existing stacks, `StorageTransferBusy`, and stale-reply guards. On bare upstream vault transfers are whole-stack with no prompt, and the busy state comes from the in-flight fields. |
| Vendor, trade, merchant (`NativeVendor`, `NativeTrade`, `NativeMerchant`) | Vendor buy/sell confirmations; compact 24-cell catalogue pages; drag payload; count badges; gold log; Classic exchange request layer, final approval with offer snapshot, and gold prompt; merchant advert step; amount prompt metas; release activation; drop onto a listing slot; wanted-sale gold cap; Silver/Copper signs. | `fix/vendor-exchange-validation`: `SellSlot(abs, count, itemId)` re-checks the item (bare upstream falls back to the 2-argument `SellSlot`); untradable items are refused through `ItemSlot.IsTradable` (skipped on bare upstream); `_exConfirmedByPartner` locks a confirmed partner's offer (on bare upstream `ex_partner_locked` stays false and one member is reported missing). `fix/merchant-stack-sync`: stall purchases keep both inventories in step with the server. The plugin does not depend on it. |
| Magic Anvil and NPC services (`NativeAnvil`, `AnvilPlacementRules`, `NativeServices`) | Anvil selection window, placement rules, embedded 28-cell bag, Cancel/Back, busy, compound, error and scan metas, count badges, the `IsTradable` rule (mirrored from `Flag`/`UniqueId`); warp, seal, Chaotic Generator (28-cell bag and drop target), item combination quantity, recipe book, redistribution, Classic repair mode. | `fix/anvil-session`: `UpgradeInteractionLocked`, the preview gate, the confirmation snapshot and the retained-item result, plus a server that echoes the requested upgrade type on refusal. On bare upstream the bench lock falls back to `_upgradeSession`, the pending result and the scan tween, and upstream's own anvil bugs remain. `fix/npc-service-requests`: repair applies to the confirmed item and plays its sound only after the acknowledgement; the seal uses a `Notice.Confirm` confirmation with an in-flight guard and identity checks; the redistribution request (`_reset`). On bare upstream redistribution reads `_resetKind`, and seal and repair keep upstream's flow. |
| Market and communication (`NativeMarket`, `NativeCommunication`) | Mail inbox, reader and compose; messenger and chat rooms; Market Price with the Classic chart background; merchant search; Power-Up Store with compact-screen and touch layout; Akara's Altar. The plugin also works around two upstream bugs: the chat-room log hang at the 101st line, and empty states that never return. It also repairs the cached Market Price "no history" reply every frame. | `fix/auction-replies`: bid and retract replies apply to the submitted lot; busy locking; tab pressed state kept in sync; `_auctionPlaceBid`, `_auctionRefresh`, `_auctionRowButtons`. On bare upstream Place Bid and Refresh are found by structure, and `auction_row_action` goes on every row button. `fix/mail-claims`: claims fill existing stacks, the unread badge follows reads, and compose cannot send twice. `fix/chat-room-lists` and `fix/market-price-no-history` fix the two bugs in the client and make the plugin's workarounds redundant but harmless. |
| Identity and appearance (`NativeIdentity`, `NativeEquipView`, `NativeLook`, `NativeAppearance`, `NativeRebirth`, `NativeCape`) | Name change with Cancel, clan creation names, transformation, council tax rate, equipment inspection rebuild, gender change (Classic editor and preview framing), rebirth (Total column, plugin-owned confirmation), cape service (preview, pages, cost confirmation). | `fix/beauty-shop`: the Classic Beauty Shop needs `_changeHairApply` and `_changeHairInFlight`; without them the preparer stops and the client's own window stays. The branch also adds the server's in-game Beauty Shop handler, `LookEditor.SetLocked` (bare upstream disables the editor buttons instead) and `_faceSteps`/`_hairSteps` (bare upstream finds the steppers by structure). `fix/nation-transfer`: the Classic Confirm calls `World.SendNationTransfer(int, IReadOnlyList<NationTransferPick>)` and uses `_transferRevision`, `_transferNotice`, `_transferCompleted`, `SetTransferLocked` and `CancelTransferConfirmation`, which only this branch has. On bare upstream the plugin does not take over the transfer window, so the client's own window handles the transfer. The server side saves the transfer in one step. `fix/clan-requests`: cape replies are matched to their request; `CapeResetEvent` closes the picker on disconnect (on bare upstream it is reported missing once); the clan fee confirmation (`_clanCreateNotice`, reworded by the plugin) is shown, while bare upstream creates the clan without it. `fix/rebirth-requests`, `fix/beauty-shop`, `fix/clan-requests`: `Send*` methods return `bool`, so a refused send is detected. On bare upstream they return `void`, and a call counts as sent. |
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

**Build-time binding.** `InventoryWindow` calls `_game.Inventory.MoveAmount(...)`. Against a client that has
`IGameInventory.MoveAmount` (`feat/magic-bag-amount`), that call compiles to the interface member. Against bare
upstream it compiles to the plugin's extension. Install a build made against the same kind of client it will run on.

## 3. Reflected client members

These tables are the maintenance checklist. Every name was checked against the plugin source as a string literal
under `src/Native`, and against the `classic-integration` client source. A *(branch)* note marks a member that
exists only with that fix branch. The plugin feature-detects it or degrades as described in section 2. "Kind" uses:
F field, P property, M method, E event or delegate backing field, T nested type. `World` is the owner unless a row
says otherwise. Public API used directly, such as `Net.I` events, `UiTheme`, `ItemData` and `HudWindow`, is not
listed. Preview-only members are listed at the end.

### Shared

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

### HUD, chat, whispers

| Member | Kind | Used for |
| --- | --- | --- |
| `ChatSystem._store`, `PublishedMax`, static `RenderText`, `AddEntry`, `StatusNotice` (set), `PluginTyping`, `IsActive`, `_bubbles` (`Bubble.Label`), `Panel`, `SetChannel`, `Open` | F/P/M | Classic history, System lines, bubble colours, link rule, channel requests |
| `ChatSystem+ChatEntry.Raw` and `Seq`, `Time`, `Category`, `Type`, `Name`, `Nation`, `Gm`, `Text`, `Bbcode` | M/P | Reading and adding entries |
| `_whisperLayer`, `_whispers` (`WhisperChat.Name/Window/Input/Log/Blink`), `_worldReady`, `_hudEditMode`, `_self` | F | Whisper preparation, compose target, attention styler |
| `_nearbyListed`, `_nearbyRows`, `_nearbyFirstPoll`, `RebuildNearby()` | F/M | Nearby players |
| `_chatPalette`, `_chatPaletteSlot`, `_chatColorsDraft` | F | Chat colours palette placement and focus |
| `ShowChatItemTip`, `ShowPlayerMenuByName` | M | Link tooltip, player menu |
| `_mailIconButton`, `_trophy`, `_trophyBlink`, `_attendanceGift`, `_attendanceGiftBlink`, `_powerUpStoreIcon`, `QueueDockLayout()` | F/M | Hidden HUD icons |
| `_achClaimablePerTab`, `ClaimableAttendanceCount()` | F/M | Notification count (with public `Net.MailUnread`) |
| `_mailShown`/`ToggleMail`, `_achShown`/`ToggleAchievements`, `_attendanceShown`/`ToggleAttendance`, `_pusShown`/`ToggleShoppingMall`, `_fullMapShown`/`ToggleFullMap`, `_genieShown`/`ToggleGenie`/`CloseGenie` | F/M | Taskbar window toggles |
| `_hotSelected`, `_hotbar`, `SelectHotAbs(int)` | F/M | Hotbar selection |
| `_running`, `_selfSitting`, `_autoAttack`, `ToggleRunMode()`, `ToggleAutoAttack()`, `StartCameraHalfTurn()`, `ToggleEsc(bool?)` | F/M | Commands |
| `_miniMap` | F | `MiniMapVisible` |
| `HudLayout._corner`, `_resizing`, `_dragging`, `_dragMoved`, `_resizeOriginMouse`, `_resizeOriginPosition`, `_resizeOriginSize`, `Frozen`, `MinimumSize`, `Place(Vector2,Vector2)` | F/P/M | Legacy grip, seam snapping |

### Character and NPC

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
| static `NoWeaponNpcIds`, `WantedClasses`, `AttachWeapons`; `World+Ent` `IsNpc`, `Dead`, `Name`, `Level`, `ModelId`, `NpcType`, `NpcId`, `Gear`, `Body` | F/M/P | NPC portrait factory |
| `_presetStatPointsLbl` | F | Preset label wording |
| `_partyMembersBox`, `_dockOrder`, `Selectable`, `Select` | F/M | Party roster, click-to-target, docking |
| `_seekListBox`, `_seekRegisterBtn`, `_seeking`, `_seekPageLbl`, `_seekTotal`, `_seekPage`, static `SeekZoneName` | F/M | Seek-party board |
| `_inZoneLeaveAsk`, `OnInZoneLeaveConfirmed`, static `IsTempleEventZone` | F/M | Event leave confirmation |

### Inventory and storage

| Member | Kind | Used for |
| --- | --- | --- |
| `MoveBetween(int,int,int)` *(feat/magic-bag-amount)* | M | Partial moves |
| `_moveInFlight`, `_bagPairing`, `BagPairing<>._openedByCompanion`, `_invDelSlot`, `_invDelItemId`, `RefuseItemInUse(int,int)`, `ConfirmDeleteItem()` | F/M | Embedded bag pairing, identity-checked destroy |
| `_warehouse`, `_whCells`, `_whStatus`, `_whAmount`, `_whShown`, `_whMoney`, `_whInFlight` | F | Warehouse |
| `WithdrawSlot(int)`, `DepositSlot(int,int)`, `AskWithdraw(int,int)`, `CanDropOnWarehouse(int,Variant)`, `DropOnWarehouse(int,Variant)` | M | Warehouse transfers |
| `_vipWh`, `_vipWhPage`, `_vipWhPageLbl`, `_vipWhStatus`, `_vipWhExpiryLbl`, `_vipWhExpirySec`, `_vipWhShown`, `_vipWhInFlight`, `_vipWhPending`, `_vipWhPinDlg`, `_vipWhPinPrompt`, `_vipWhPinEdit` | F | VIP vault and PIN prompt |
| `_clanWh`, `_clanWhPage`, `_clanWhPageLbl`, `_clanWhStatus`, `_clanWhShown`, `_clanWhLoaded`, `_clanWhMoney`, `_clanWhInFlight`, `_clanWhPending`, `_clanWhGoldInput` | F | Clan warehouse |
| `RefreshVipWarehouse()`, `RefreshClanWarehouse()`, `VipDepositSlot(int)`, `VipWithdrawSlot(int)`, `ClanWhDepositSlot(int)`, `ClanWhWithdrawSlot(int)`, `ClanWhGoldTransfer(bool)`, `SubmitVipPin()` | M | Vault transfers, coins, PIN |
| `World+VipWhPending`, `World+ClanWhPending` (`Op`, `InvAbs`, `VipIdx`/`WhIdx`; `Count`, `Merge` *(fix/storage-transfers)*) | T | Quantity transfers |
| `StorageTransferBusy` *(fix/storage-transfers)* | P | Busy state. Falls back to the in-flight fields. |
| `Net._conn` and its `Send(Packet)` | F/M | In-vault rearrangement (op 4) |

### Vendor, trade, merchant

| Member | Kind | Used for |
| --- | --- | --- |
| `_vendorCells`, `_vendorCellIds`, `_vendorCatalogue` (set), `_vendorCompanion`, `_vendorEntries`, `_vendorFooter`, `_vendorGroup`, `_vendorNpcId`, `_vendorPanel`, `_vendorSelected`, `_vendorShown`, `_vendorBuy`, `_tradePrompt`, `_tradeInFlight`, `_moveQueue`, `LoyaltyShop`, `VendorWallet`, `VendorCurrency`, `Floaters` | F/P | Vendor metas, compact pages, confirmations |
| `BuyAmount`, `BuyRoom`, `CanBuy`, `CarriedWeight`, `InMainBag`, `SelectVendorItem`, `ShowVendorPage`, `PluginLogAdd` | M | Vendor actions, gold log |
| `SellSlot(int,int,int)` *(fix/vendor-exchange-validation)*, otherwise `SellSlot(int,int)` | M | Sell |
| `ItemSlot.IsTradable` *(fix/anvil-session, fix/vendor-exchange-validation)* | P | Untradable refusal |
| `BagCompanion.Take`, `BagCompanion.IntoBag` (init) | P | Rerouting the bag companion through the confirmation |
| `Net.GoldChangeEvent` | E | Gold log (handler prepended) |
| `_exPanel`, `_exMineList`, `_exTheirsList`, `_exBagList`, `_exWaitLayer`, `_exAmountLayer`, `_exAmountSpin`, `_exAmountIcon`, `_exAmountName`, `_exAmountHint`, `_exAmountMax`, `_exAmountSlot`, `_exAmountShown`, `_exAskDialog`, `_exConfirmBtn`, `_exGoldEdit`, `_exStatus`, `_exShown`, `_exRequestPending`, `_exConfirmedByMe`, `_exAddInFlight`, `_exMyOffer`, `_exTheirOffer`, `_exMyGoldOffer`, `_exTheirGoldOffer`, `_exPartnerName`; `ExOfferItem.ItemId/Count/Dura/SourceAbs` | F/P | Exchange names, metas, request layer, final approval, gold prompt |
| `_exConfirmedByPartner` *(fix/vendor-exchange-validation)* | F | Partner lock |
| `AnswerExchangeRequest`, `OnExchangeConfirm`, `ConfirmExchangeAmount`, `CloseExchangeAmount`, `OnAddGold`, `SetExStatus` | M | Exchange actions |
| `_mctLayer`, `_amountLayer`, `_amountAccept`, `_amountConfirmBtn`, `_amountCount`, `_amountCountRow`, `_amountPrice`, `_amountPriceFixed`, `_amountMarketRow`, `_amountMarketHint`, `_marketPricePanel`, `_marketPriceShown` | F | Merchant amount prompt metas |
| `_sellAdvert`, `_sellStallPanel`, `_myStall`, `_myStallSrc`, `_wantedItems`, `_stalls`, `_stallSignLayer`, `_sellStallCells`, `_sellBagCells`, `_shopCells`, `_shopBagCells`, `_wishCells`, `_wantedCells`, `_wantedBagCells` | F | Merchant windows and signs |
| `AcceptAmount`, `AskStallPrice`, `CloseAmountPrompt`, `ConfirmSellStall`, `SellToWanted`, `SetSellStatus`, `SetWantedStatus`, `StageStallItem`, `UnstageStallItem`, `RefreshStallSign`, static `FreeStallSign` | M | Merchant actions |
| `MerchantCell.Index`, `OnActivate` (set), `OnDropFrom` (set), `Item`, `_count`; `Stall.Sign`, `SignCells`, `IsBuying` | F/P | Release activation, slot drops, sign rebuild |

### Magic Anvil and NPC services

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

### Market and communication

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

### Identity and appearance

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
| `Net.CapeResetEvent` *(fix/clan-requests)* | E | Close the picker on disconnect |
| `Net.SendCapeBuy`, `SendChangeHair`, `SendRebirthStatChange` | M | Called reflectively: they return `void` upstream and `bool` on the fix branches |
| `LookPreview._pivot`, `_model` (set) | F | `LookFraming` |
| `LookEditor._races`, `_faceLbl`/`_hairLbl` (set), `_colour`, `_raceButtons`; `_locked`, `_faceSteps`/`_hairSteps`, `SetLocked` *(fix/beauty-shop)* | F/M | Editor names, 1-based display, lock |

### Familiar

| Member | Kind | Used for |
| --- | --- | --- |
| `_petBagCells`, `_petNameLbl`, `_petLevelLbl`, `_petHpBar`, `_petMpBar`, `_petExpBar`, `_petSatBar`, `_petAttackBtn`, `_petDefendBtn`, `_petLootBtn`, `_petFeedBtn`, `_petDismissBtn`, `_petStatus`, `_itemTipPanel` | F | Familiar window names and details |
| `UsePetSkill(int)`, `SkillTooltip(Skill)`, `Now()`, `PetCooldown(int,double)`, `MyPetEntity()` | M | Skills, cooldowns, portrait |
| `_petHatchTabs`, `_petHatchPage`, `_petTransformPage`, `_petHatchName`, `_petHatchBtn`, `_petHatchStatus`, `_petHatchEggPick`, `_petTransformPetPick`, `_petTransformScrollPick`, `_petHatchInFlight`, `_petHatchShown`, `_petHatchNpc`, `_petHatchSlot`, `_petTransformSlot`, `_petScrollSlot`, `_petHatchLayer` | F | Trainer state (the native fields stay authoritative) |
| `IsPetEgg`, `IsFamiliarItem`, `IsTransformScroll`, `IsValidPetName`, `RefreshPetPickLooks`, `SetPetHatchStatus`, `ClosePetHatch` | M | Trainer rules and actions |
| `WhisperChat.Window`, `ServiceTabs._buttons` | F | Keyboard yielding, tab lock |
| `Net.SendPetHatch`, `SendPetTransform` | M | `bool` on fix/familiar-replies, `void` upstream |
| `HudLayout._moveGripOverlay`; bar recognised by the type names `PetSkillCell` and `HotGrip` | F | Familiar bar grip and default position |

### Preview-only members

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
- Server: partial in-bag splits, if returning one material to a chosen cell is wanted.

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
   Both builds must have 0 errors. The only known warning is CS8604 in `ClassicVendorSkin`. A failure here means a
   public API type changed.
2. **Check reflected names statically.** Extract every string literal passed to `Native.*` under `src/Native`.
   Check each identifier against the new client's `Client/src`. Compare method arity too, because overloads change
   between upstream and the fix branches (`MoveBetween`, `SellSlot`, `SendNationTransfer`). Every name in section 3
   must exist on `classic-integration`. Only the names marked with a branch may be absent upstream.
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
   - character: `character-audit` (with `inventory-parent-audit`), `details-audit`, `skill-audit`, `party-audit`;
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
