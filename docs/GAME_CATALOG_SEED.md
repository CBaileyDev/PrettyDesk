# PrettyDesk — Seed Game Catalog

This is the first list of games for v1, with **detection hints** and an **art-direction seed** for each one.

> ⚠️ **Every executable name and Steam AppID below is UNVERIFIED and must be checked before it ships.**
> They come from memory and public knowledge, and games rename executables in patches (DX12 builds, engine upgrades, remasters).
> Confidence: **H** = very likely correct, **M** = plausible but check carefully, **L** = guess.

## How to verify (implementing agent + owner)
1. **Steam games:** the SteamDB app page → *Configuration* → *Launch* options lists the exact executables. The AppID is in the store URL.
2. **Non-Steam games:** use PCGamingWiki (the *Game data → Executable* sections, plus install-folder notes) and the launcher's install folder.
3. **Ground truth:** the owner launches the game with PrettyDesk's debug build. *Settings → Advanced → Detection log* shows the foreground process name for the current window. Confirm it, then set `verified: true` with the date in `content/catalog.src.json`.
4. Never ship a rule that matches a **launcher or anti-cheat service** (e.g. `steam.exe`, `EpicGamesLauncher.exe`, `RiotClientServices.exe`, `Battle.net.exe`, `EADesktop.exe`, `vgc.exe`, `EasyAntiCheat.exe`, `BEService.exe`). The global `excludeExeNames` list in the catalog MUST contain these.

---

## 1. Detection seed

Tier 1 covers the most-played and most setup-culture-relevant games. Do these first, in both the art track (A2) and verification.

| Tier | id | Display name | Launcher(s) | exeNames (verify!) | Steam AppID | Conf. | Notes |
|---|---|---|---|---|---|---|---|
| 1 | `cs2` | Counter-Strike 2 | Steam | `cs2.exe` | 730 | H | |
| 1 | `valorant` | VALORANT | Riot | `VALORANT-Win64-Shipping.exe` | — | H | `VALORANT.exe` is a bootstrapper; match only Shipping. Kernel anti-cheat (Vanguard): QA must confirm there are no warnings (FR-DET-3). |
| 1 | `lol` | League of Legends | Riot | `League of Legends.exe` | — | H | The lobby client is `LeagueClientUx.exe`. Default is in-match only; OWNER-DECISION on whether the lobby counts. |
| 1 | `fortnite` | Fortnite | Epic | `FortniteClient-Win64-Shipping.exe` | — | H | |
| 1 | `minecraft` | Minecraft | Launcher / MS Store | `Minecraft.Windows.exe`, `javaw.exe` | — | M | `javaw.exe` is ambiguous: require `windowTitleContains: ["Minecraft"]`. Works with modded launchers (Prism, CurseForge). |
| 1 | `roblox` | Roblox | Roblox | `RobloxPlayerBeta.exe` | — | H | |
| 1 | `apex` | Apex Legends | Steam / EA | `r5apex.exe`, `r5apex_dx12.exe` | 1172470 | M | DX12 build has a different exe |
| 1 | `overwatch2` | Overwatch 2 | Battle.net / Steam | `Overwatch.exe` | 2357570 | H | |
| 1 | `rocket-league` | Rocket League | Epic (Steam legacy) | `RocketLeague.exe` | 252950 | H | |
| 1 | `gta5` | Grand Theft Auto V | Steam / Rockstar / Epic | `GTA5.exe`, `GTA5_Enhanced.exe` | 271590, 3240220 | M | The Enhanced edition's AppID and exe are M confidence |
| 1 | `marvel-rivals` | Marvel Rivals | Steam | `Marvel-Win64-Shipping.exe` | 2767030 | M | Strict IP care, see art seed |
| 1 | `cod` | Call of Duty (HQ) | Steam / Battle.net | `cod.exe` | 1938090 | M | The HQ launcher-era titles (BO6, BO7, Warzone) share this. Older titles use other exes; skip them for v1. |
| 1 | `dota2` | Dota 2 | Steam | `dota2.exe` | 570 | H | |
| 1 | `r6siege` | Rainbow Six Siege X | Steam / Ubisoft | `RainbowSix.exe`, `RainbowSix_Vulkan.exe` | 359550 | M | The exe may have changed with the Siege X update |
| 1 | `cyberpunk` | Cyberpunk 2077 | Steam / GOG / Epic | `Cyberpunk2077.exe` | 1091500 | H | |
| 1 | `elden-ring` | Elden Ring | Steam | `eldenring.exe` | 1245620 | H | Nightreign is a separate entry: `nightreign.exe`, 2622380, M |
| 1 | `genshin` | Genshin Impact | HoYoPlay / Epic | `GenshinImpact.exe`, `YuanShen.exe` | — | H | `YuanShen.exe` is the CN build |
| 1 | `pubg` | PUBG: Battlegrounds | Steam | `TslGame.exe` | 578080 | H | |
| 1 | `helldivers2` | Helldivers 2 | Steam | `helldivers2.exe` | 553850 | H | |
| 1 | `bg3` | Baldur's Gate 3 | Steam / GOG | `bg3.exe`, `bg3_dx11.exe` | 1086940 | H | |
| 2 | `rdr2` | Red Dead Redemption 2 | Steam / Rockstar | `RDR2.exe` | 1174180 | H | |
| 2 | `destiny2` | Destiny 2 | Steam | `destiny2.exe` | 1085660 | H | |
| 2 | `hsr` | Honkai: Star Rail | HoYoPlay / Epic | `StarRail.exe` | — | H | |
| 2 | `the-finals` | THE FINALS | Steam | `Discovery.exe` | 2073850 | H | |
| 2 | `poe2` | Path of Exile 2 | Steam / Standalone | `PathOfExile.exe`, `PathOfExileSteam.exe`, `PathOfExile_x64.exe`, `PathOfExile_x64Steam.exe` | 2694490 | M | Several exe names exist across builds |
| 2 | `diablo4` | Diablo IV | Battle.net / Steam | `Diablo IV.exe` | 2344520 | H | |
| 2 | `wow` | World of Warcraft | Battle.net | `Wow.exe`, `WowClassic.exe` | — | H | Classic could get its own pack later |
| 2 | `mh-wilds` | Monster Hunter Wilds | Steam | `MonsterHunterWilds.exe` | 2246340 | H | |
| 2 | `palworld` | Palworld | Steam / Xbox | `Palworld-Win64-Shipping.exe` | 1623730 | H | The Xbox/MS Store build may differ |
| 2 | `stardew` | Stardew Valley | Steam / GOG | `Stardew Valley.exe`, `StardewModdingAPI.exe` | 413150 | H | SMAPI (modded) launches a different exe |
| 2 | `terraria` | Terraria | Steam | `Terraria.exe` | 105600 | H | tModLoader is a separate AppID (1281930, M) |
| 2 | `rust` | Rust | Steam | `RustClient.exe` | 252490 | H | |
| 2 | `tarkov` | Escape from Tarkov | BSG Launcher / Steam | `EscapeFromTarkov.exe` | — | H (exe) | Steam AppID unknown; verify |
| 2 | `silksong` | Hollow Knight: Silksong | Steam / GOG | `Hollow Knight Silksong.exe` | 1030300 | M | |
| 2 | `wukong` | Black Myth: Wukong | Steam / Epic | `b1-Win64-Shipping.exe` | 2358720 | H | |
| 2 | `sea-of-thieves` | Sea of Thieves | Steam / Xbox | `SoTGame.exe` | 1172620 | H | |
| 2 | `lethal-company` | Lethal Company | Steam | `Lethal Company.exe` | 1966720 | H | |
| 2 | `hades2` | Hades II | Steam / Epic | `Hades2.exe` | 1145350 | M | |
| 2 | `deadlock` | Deadlock | Steam | `deadlock.exe` | 1422450 | M | |
| 2 | `forza-h5` | Forza Horizon 5 | Steam / Xbox | `ForzaHorizon5.exe` | 1551360 | H | Add Forza Horizon 6 when it releases and is verified |
| 2 | `warframe` | Warframe | Steam / Standalone | `Warframe.x64.exe` | 230410 | H | |
| 2 | `osu` | osu! | Standalone | `osu!.exe` | — | H | Stable and lazer both use `osu!.exe` (verify lazer) |
| 2 | `ets2` | Euro Truck Simulator 2 | Steam | `eurotrucks2.exe` | 227300 | H | |
| 3 | `bf6` | Battlefield 6 | Steam / EA | `bf6.exe` | — | L | Verify everything |
| 3 | `arc-raiders` | ARC Raiders | Steam | — | 1808500 | L | Exe unknown; verify |

**Global `excludeExeNames`:**
`steam.exe`, `steamwebhelper.exe`, `EpicGamesLauncher.exe`, `EpicWebHelper.exe`, `RiotClientServices.exe`, `RiotClientUx.exe`, `LeagueClient.exe`, `LeagueClientUx.exe`, `Battle.net.exe`, `Agent.exe`, `EADesktop.exe`, `EABackgroundService.exe`, `UbisoftConnect.exe`, `upc.exe`, `GalaxyClient.exe`, `HoYoPlay.exe`, `vgc.exe`, `vgtray.exe`, `EasyAntiCheat.exe`, `EasyAntiCheat_EOS.exe`, `BEService.exe`, `javaw.exe` (unless a title rule is present).

---

## 2. Art-direction seeds (input for `inspiration:` blocks, see ART_DIRECTION.md §5)

These are starting points. The agent expands each into a full `inspiration` block and four prompts. The **Avoid** column always also includes: game title, character, place and faction names, logos, text, UI/HUD, official character designs and recreations of official key art.

| id | Genre / setting (translate, don't copy) | Palette seed | Motif ideas | Avoid (extra) |
|---|---|---|---|---|
| `cs2` | Grounded modern tactical; sun-bleached desert towns, Mediterranean/North-African sandstone, industrial yards | #D9A441 #C8B08A #3E5C76 #1B1F24 | arched doorways, long dawn shadows, dust motes, tiled rooftops, wooden crates | soldiers, weapons, graffiti, recognizable map layouts |
| `valorant` | Stylized near-future tactical; clean sunlit plazas mixing Venetian, Moroccan and Japanese architecture with sleek tech; soft painterly 3D | #FF4655 #0F1923 #ECE8E1 #BDBCB7 | crisp geometric shadows, canal city at dusk, rooftop over neon-lit bay, minimal red accent line | agents, ability effects, the V emblem |
| `lol` | Painterly high fantasy of rival realms; mystic river valley through ancient jungle, crystal light, rune-carved ruins | #C89B3C #0A1428 #0AC8B9 #1E2328 | glowing turquoise river, gold-trimmed stone ruins, floating spirit lights, twilight canopy | champions, exact map layout, crests |
| `fortnite` | Bright cartoon adventure island; playful stylized nature, purple storm wall on the horizon | #4CC9F0 #9B5DE5 #FEE440 #00BB77 | rolling candy-green hills, chunky stylized trees, distant storm, floating island at sunset | skins, bus, llama, emotes |
| `minecraft` | Voxel/blocky sandbox world; cube-shaped terrain and trees, square sun and moon | #7CB342 #8D6E63 #4FC3F7 #2E2E2E | blocky cliff waterfall, voxel village at dusk, cube clouds, lantern-lit cave glow | mobs, block-face creatures, player figures, exact official textures |
| `roblox` | Toy-like low-poly building worlds; bright plastic materials, floating obstacle platforms in the sky | #00A2FF #F2F3F3 #FFB000 #393B3D | floating platform paths, studded plastic surfaces, low-poly islands, soft studio light | avatars, the tilted-square logo |
| `apex` | Sci-fi frontier battle royale; arid canyons with colossal derelict industrial machinery, alien flora, crashed ships | #DA292A #F2C14E #3B4A54 #121417 | lone zipline tower at sunset, canyon with giant machinery, desert outpost in storm light | legends, weapons, drop-ship branding |
| `overwatch2` | Optimistic near-future; bright global cities (Mediterranean, East-Asian, Nordic) with sleek tech | #F99E1A #218FFE #FFFFFF #43484C | sunny futuristic plaza, cherry blossoms with clean tech, coastal city with hover-rail | heroes, payloads, the circle emblem |
| `rocket-league` | Neon sports arena at night; stadium floodlights, hex-pattern field, boost-trail light streaks | #0070F3 #FF7A00 #0A0E1A #C0C8D8 | empty glowing arena, giant ball on center line, light trails in the air | cars, team crests, any branding |
| `gta5` | Sun-soaked West-Coast metropolis; palm boulevards, hills, coastline, neon nights | #F7B733 #FC4A1A #2E86AB #1B1B2F | palm silhouettes at sunset, freeway interchange at dusk, pier lights, desert highway | characters, the loading-screen comic style, sign text |
| `marvel-rivals` | Comic-book multiverse city; dramatic skyline split by glowing portals, bold cel-shaded painting | #E23636 #F5C518 #1F4E99 #121212 | portal over skyline, floating city fragments, comic-halftone sky | **ANY** superhero, costume, emblem or shield shape (strict) |
| `cod` | Modern military realism; desolate urban district at dawn, haze, distant helicopter silhouettes | #556B2F #B08D57 #3A3F44 #0B0C0D | smoky dawn skyline, rain-soaked compound, night-vision-green dusk | soldiers in focus, weapons, insignia |
| `dota2` | High-fantasy war of two realms; lush golden radiance versus scorched crimson corruption, with a river between them | #C9A55A #3C6E47 #8E2B2B #1A1418 | river dividing green and red lands, ancient tree, crumbling tower at dusk | heroes, the map layout, the emblem |
| `r6siege` | Tactical close quarters; suburban house at night with dramatic light, barricades, dust in light shafts | #F7B500 #2F3640 #8C99A6 #0D0F12 | light shafts through a broken wall, rain on a dark house, drone's-eye floor glow | operators, gadgets, logos |
| `cyberpunk` | Neon megacity future-noir; rain-slick streets, towering arcologies, yellow/cyan neon | #FCEE0A #00F0FF #FF003C #0A0A12 | rain reflections, monorail through towers, lone vending glow, smog sunset | characters, readable signs (glyphless neon only), brand names |
| `elden-ring` | Dark-fantasy ruined kingdom; a colossal glowing golden tree on the horizon, misty fields, ruined castles | #D4AF37 #6B6B47 #2C2A26 #0E0D0C | golden tree over fog, lone ruined bridge, graveyard of giant swords in mist | knights, bosses, exact castle designs |
| `genshin` | Anime cel-shaded fantasy open world; floating islands, windmills on green hills, lantern-lit mountain town | #4FB3BF #F2D398 #8DC26F #2A3B4C | windmill hills at golden hour, lantern festival on water, crystal lake | characters, the seven statues |
| `pubg` | Realistic battle royale; Eastern-European countryside, abandoned military base, wheat fields, cargo plane | #C8A951 #6B7A3A #A9B4BF #1C1F22 | distant cargo plane over wheat, parachutes at dawn (tiny), lonely farmhouse | characters, the pan, crates with branding |
| `helldivers2` | Satirical militaristic sci-fi; hostile alien planets, orbital strike light beams, burning skies | #FFE710 #F2F2F2 #2B2B2B #C1272D | orbital beam on the horizon, bug-hive wasteland, drop pod trails at dusk | soldiers, propaganda text, emblems |
| `bg3` | High-fantasy CRPG; gothic city spires, moonlit forests, campfire under stars, arcane purple glow | #6B4C9A #C9A15A #2B3A2E #120E14 | campfire beneath starfield, arcane-lit ruins, city of spires at dusk | characters, tentacled creatures, the logo |
| `rdr2` | American frontier, 1899; misty mountains, golden plains, bayou swamp, campfire | #A33B20 #D9B26F #4F5D2F #1C1A17 | tiny lone rider silhouette, plains at dawn, foggy bayou, snowy pass | faces, logos, the poster red background style |
| `destiny2` | Mythic sci-fi; golden-age space architecture, ring-planet vistas, rusted colony ships in snow | #E9E5DA #3A6EA5 #D4A84B #0F1218 | ruined launch site in snow, ring-planet sunrise, ancient vault in jungle | the giant white sphere, Guardians, ships by design |
| `hsr` | Anime space fantasy; a train through starry space, retro-futuristic stations, snowy steampunk city | #F4D06F #6C5CE7 #A0E7E5 #121225 | train window to galaxy, snowy brass city, starlit platform | characters, the exact train design |
| `the-finals` | Virtual game-show arena; sleek glass cityscape mid-destruction, studio light rigs | #E9FF00 #FF2E63 #2B2D42 #EDF2F4 | floating debris frozen in time, neon-lit cash-out vault glow, studio spotlights | contestants, sponsor text |
| `poe2` | Grim dark fantasy; ruined ancient temples, cursed swamps, blood-red moon, torchlit stone | #8A1C1C #C2A878 #2F3A33 #0B0A09 | blood moon over ruins, torchlit stair into dark, drowned temple | characters, the logo |
| `diablo4` | Gothic horror; snowy mountain monastery, cathedral ruins, crimson skies, bleak moors | #8B0000 #C8B79A #3D3B3A #0A0807 | cathedral silhouette in blizzard, lone lantern on a moor | demons, the logo, named villains |
| `wow` | Stylized high-fantasy MMO; floating lands, glowing elven forest at night, colossal stone fortress | #F8B700 #148B9C #3C5A2B #1A1A2E | moonwell glow in a forest, floating isles at dawn, harbor city at sunset | races/characters, faction crests |
| `mh-wilds` | Hunting adventure in wild ecosystems; windswept desert plains, lightning storms, oases | #D6A35C #7E9F5B #4F6D8F #22201C | storm over plains, a distant unidentifiable giant silhouette, oasis at dawn | official monsters, hunters, weapons |
| `palworld` | Cheerful survival open world; lush grasslands, floating ruins, volcanic islands | #7FC8A9 #F9E784 #5DA9E9 #2E3A23 | grassy cliffs over sea, volcanic isle at sunset, overgrown ruins | creatures, the logo |
| `stardew` | Cozy pixel-art farm valley; seasonal farm, small town, mines glow | #8CC152 #F6BB42 #A0522D #2C3E50 | pixel farm at dawn, autumn orchard, snowy cabin at night (**pixel-art style**, `upscaler: x4plus-anime`) | characters, exact sprites |
| `terraria` | 2D pixel adventure; layered biome cross-section, glowing caverns, night sky | #6AB04C #3B3B98 #F0932B #130F40 | surface-to-underworld cross-section, mushroom-glow cave, starry surface (pixel art) | bosses, characters, exact sprites |
| `rust` | Brutal survival; overgrown abandoned industrial island, rusted radar dish, cold coast | #B7410E #6B705C #A5A58D #1E1E1E | rusted dish in fog, campfire on cold beach, overgrown monument | players, logos |
| `tarkov` | Gritty realism; snowy North-European urban decay, foggy forest, abandoned shoreline resort | #5B6057 #9A8F7A #2E3437 #0F1112 | fog over abandoned resort, snowy checkpoint, dead forest at dusk | soldiers, gear, logos |
| `silksong` | Hand-painted 2D gothic insect kingdom; ivory-silver spires, silk threads, bell towers, moss | #E9E4D8 #B23A48 #2D3142 #0D0E14 | silk threads catching light, bell citadel in mist, mossy grotto (**2D hand-painted**) | the protagonist, characters |
| `wukong` | Chinese mythology action; misty karst mountains, ancient temples, autumn maples, golden clouds | #C79A3A #8E2C1F #5A6B4E #121212 | temple on a karst peak, maple valley in mist, golden cloud sea | the monkey-king figure, the staff, characters |
| `sea-of-thieves` | Stylized pirate adventure; turquoise seas, painterly islands, galleon silhouettes at sunset | #1FB5AD #F2C14E #4B3F72 #0E1B2C | lone ship at golden hour, lantern-lit island at night, storm at sea | characters, the skull cloud, emblems |
| `lethal-company` | Lo-fi sci-fi horror; abandoned industrial moon facility, foggy dusk, orange floodlights | #E2552C #2E3B32 #A8A27F #0B0C0B | lone floodlit facility in fog, ship ramp glow, retro terminal glow (no text) | creatures, readable text |
| `hades2` | Bold painterly Greek-myth underworld; moonlit crossroads, starlit temples, purple-green mystic glow | #7B5EA7 #3FB27F #E0B354 #0E0B16 | moon over ruined temple, glowing crossroads, starfield over a dark sea | characters, the logo |
| `deadlock` | Occult noir city; 1920s art-deco metropolis, streetcars, fog lamps, mystic glow | #C8A96B #3F6E6A #6B2E2E #121314 | deco skyline in fog, rain-slick streetcar line, occult glyph-free glow | heroes, readable signs |
| `forza-h5` | Open-world road festival in Mexico; volcano at golden hour, jungle roads, desert highways | #F25F5C #FFE066 #247BA0 #1C1C1C | empty road to a volcano, festival lights at dusk, coastal highway | branded cars, logos, sign text |
| `warframe` | Space-ninja sci-fi; organic-tech orbiters, gas-giant vistas, ancient gold-white palatial architecture | #E0C589 #2E6F95 #C1C7CF #0A0C10 | gas giant from an orbiter window, golden ruins in the void | frames/characters, logos |
| `osu` | Rhythm game, abstract; glowing concentric circles, pink gradients, beat pulses | #FF66AA #FFFFFF #2A2A3A #111118 | concentric light rings, pulse waves in dark, soft pink aura | the logo composition, text |
| `ets2` | European road trip; empty highway through alpine valley at dawn, coastal road, autumn countryside | #F2A541 #4E8098 #8BA888 #1B1F22 | road into mountains at dawn, rain on a highway at dusk, sunflower field road | branded trucks, road-sign text |
| `bf6` | Modern large-scale war; collapsing skyline, dust storm, dramatic weather | #E6C34A #6F7A80 #2B2F33 #0C0D0F | storm over a damaged skyline, smoke-lit sunset | soldiers, logos |
| `arc-raiders` | Retro-futurist post-apocalyptic surface; lush overgrown ruins, colossal machines far off, 1970s sci-fi | #F4A259 #5B8E7D #BC4B51 #1F2421 | overgrown ruins with distant giant machine silhouette, 70s-poster sky | the official machine designs, characters |
