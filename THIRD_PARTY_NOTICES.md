# Sources

- C# layout and gameplay adapters: ZeusAFK/LibreKO-knightonline-ui-chaos, AGPL-3.0.
  https://github.com/ZeusAFK/LibreKO-knightonline-ui-chaos
- DXT reader: ZeusAFK/LibreKO-Assets, AGPL-3.0; copied `bakers/dxt_to_png.py`.
  https://github.com/ZeusAFK/LibreKO-Assets
- UIF format reference: Open-KO/KnightOnline, `src/N3Base/N3UI*.cpp`.
  https://github.com/Open-KO/KnightOnline
- UI artwork/layout input: user's local Knight Online installation. Original game artwork retains
  its original ownership; the code license does not relicense these game assets.
- Mail envelope artwork: existing `main_ui_01.png` from ZeusAFK/LibreKO-knightonline-ui-chaos,
  copied unchanged as `assets/textures/classic_mail_chaos.png`. The normal image and notification
  frame sequence follow `assets/layouts/co_hpbar_us.json`. Original game artwork retains its ownership.
  https://github.com/ZeusAFK/LibreKO-knightonline-ui-chaos

- 2.x-era window art in `assets/myko`: layouts and textures of a 2.x-era Knight Online client UI
  (UIF window overrides for the Power Up Store, quest NPC menu and viewer, status bar menu,
  notice/premium bars and the notice content box), decoded from loose UIF/DXT files and
  converted to the plugin's layout JSON format with the DXT reader above. `assets/myko/index.json`
  lists each file with its SHA-256. Original game artwork retains its original ownership; the code
  license does not relicense these game assets.

See LICENSE for the adapter code's AGPL-3.0 terms.

- Hunt icon: user-supplied visual, reduced to transparent 32/64-pixel PNGs.
  The two preview fixtures are visual inputs, not captured account/game state.
  The code license does not relicense third-party artwork.

- Status bar icons: glyphs from game-icons.net, https://game-icons.net, licensed under CC BY 3.0
  (https://creativecommons.org/licenses/by/3.0/). Recoloured (nation gradient, outline, shadow, state variants)
  by `tools/make_hud_icons.py` into `assets/textures/hud-icons`; the unmodified sources are in
  `tools/source/game-icons`.
  - "Scroll unfurled" by Lorc (Quests), https://game-icons.net/1x1/lorc/scroll-unfurled.html
  - "Cut diamond" by Lorc (Power-Up Store), https://game-icons.net/1x1/lorc/cut-diamond.html
  - "Trophy cup" by Delapouite (Achievements), https://game-icons.net/1x1/delapouite/trophy-cup.html
  - "Calendar" by Delapouite (Daily Attendance), https://game-icons.net/1x1/delapouite/calendar.html
  - "Magic lamp" by Lorc (Genie), https://game-icons.net/1x1/lorc/magic-lamp.html
  - "Knight banner" by Delapouite (Events), https://game-icons.net/1x1/delapouite/knight-banner.html
  - "Laurel crown" by Lorc (Daily Rank), https://game-icons.net/1x1/lorc/laurel-crown.html
  - "Hourglass" by Lorc (Event Schedule), https://game-icons.net/1x1/lorc/hourglass.html
  - "Envelope" by Lorc (Mail), https://game-icons.net/1x1/lorc/envelope.html
- Discord corner icon: the Discord mark from Simple Icons, https://simpleicons.org, CC0 1.0 Universal, as the LibreKO
  client ships it (`Client/assets/ui/icons/brand/discord.svg`), rasterised to `tools/source/game-icons/simpleicons_discord.png`.
  Discord and the Discord mark are trademarks of Discord Inc.; the mark only links to the LibreKO Discord server.
  - "Envelope" by Lorc (Mail), https://game-icons.net/1x1/lorc/envelope.html
