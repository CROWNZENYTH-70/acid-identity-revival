# Contributor Guide

## What this is

An offline revival kit for Assassin's Creed Identity (Android). The game's
servers are dead. This kit patches the client to boot offline and authors
playable missions with its own data formats. Proven on-device: boot to
Animus hub, or straight into Firenze with 3 guards that fight back.

What it is not: a remake, a server emulator, or a content pack. No Ubisoft
files live here or ever will.

## You bring (never in this repo)

- Your own AC Identity APK + its `AcierData` cache.
- `apktool`, `apksigner`, `keytool`, `dotnet` 8 SDK (apktool/keytool need Java).
- An Android device with an `ACID_Revival/` folder on shared storage.

## Project layout

- `patch/` — Cecil patcher. Reads clean `Managed/` DLLs, writes patched
  `Assembly-CSharp.dll` + `SharedBaseLib.dll`. One source, two boot modes
  via CLI flag (`hub` default, `freeroam`).
- `mission/` — mission author. Builds `tutorial_0001.bin` with the game's
  own protobuf contracts. Ships a ready-made `tutorial_0001.bin`.
- `README.md` — build steps. This file — how it all works.

## The provided mission files

`mission/tutorial_0001.bin` is 100% ours (authored by `mission/Program.cs`,
not extracted). Contents: Firenze_santacroce_sunny, player spawn at
(-50, 5, -50), 3 live enemies (papal guard, guard captain, crossbowman),
crowd density 40 / max 20. Place it at:

```
 /storage/emulated/0/ACID_Revival/AcierData/Missions/tutorial_0001.bin
```

`mission/missions/` holds 16 more self-authored missions, one per env —
player-only spawn at origin (navmesh snap lands it), no guards unless
rebuilt without `--no-guards` — plus 3 patrol versions
(`test_patrolv1/v2/v3.bin`): santacroce sunny at the locked spawn with
3 walking guards, and `test_patrolpair.bin` (two guards, one formation,
walking together — see “Patrols”). To test a scene on-device without adb:
copy the test file over `tutorial_0001.bin` in the Missions folder
(exact name), launch, and restore afterwards (keep `tutorial_0001.GOOD.bak`
safe — `tutorial_0001.bin` hash changes every rebuild, same content).

Rebuild any of them with the mission CLI:

```
dotnet run --project mission -p:ManagedDir=<Managed> -- <out.bin> [scene] [region] [age] [mood] [--no-guards]
```

Defaults (`tutorial_0001.bin` + santacroce sunny) reproduce the main
mission with its locked spawn and 3 guards.

No other cache file is modified — GameDB, bundles, dicts stay byte-identical.
Rebuild it any time with step 4 of the README; each build gets a fresh root
GUID (same content, different hash — normal).

## Boot modes

- `hub`: tutorial lookup returns null, boot falls through to
  `BuildDefinition.AnimusMission` (AnimusGlobe). Settings only — boot proof.
- `freeroam`: `SetupAnimusFile` scene AnimusGlobe → Firenze_santacroce_sunny,
  mission type hub → mission. Boots to loading screen → spawn → fight.

## How enemies work

Mission guids you write are NOT live NPC keys — they exist in GameDB as
other item types. The patcher aliases 3 mission guids to live definitions
at `GameDB.FindGameIdFromGuid` entry:

- GuardA `520620a5-...` → `enemy_papal_medici`
- GuardB `2d2ca21b-...` → `enemy_guardcaptain_medici`
- GuardC `d91147f6-...` (unused guid) → `enemy_crossbowman_medici`.
  Note: GuardC was moved off `15c667c9-...` because that guid doubles as
  crowd pool slot #0 — aliasing it hijacked a civilian lookup.

Plus a null-check in `NpcStatsHelper` so lone NPCs (no formation parent)
don't abort stats init. To add/change an enemy: pick a live `Enemy_` name
from the 127 `NpcData` defs, add an alias row, add an `Npc(...)` line.

## Patrols (verified on-device)

Each guard node carries two components — formation FIRST, spawn second
(`MissionPhase` runs `OnPreActivate` in component order, so the patrol
entity must exist before the spawn pre-creates the NPC) — plus waypoint
children forming the beat. `NpcStatsHelper` reads the formation off the
spawn's own GameObject → `Npc_PatrolId` → patrol member; at `OnActivate`
the patrol builds its route and warps members on, then walks it via the
turn-update Approacher. Guards detect and break off to attack, then return —
unlike the old full-idle statues.

- `test_patrolv1.bin`: first walking proof (3 short 2-point beats).
- `test_patrolv2.bin`: varied shapes — GuardA square loop (`Circeling`),
  GuardB 3-point beat with a `WalkFast` leg, GuardC brisk beat with a 3s
  standing-watch halt. GuardA's square hit the map east wall (x=-34).
- `test_patrolv3.bin` (current): same as v2 with GuardA's loop recentered
  to (-47,-49), 10m square clear of the wall. All 3 beats verified walking.
- `test_patrolpair.bin`: paired proof — GuardA+GuardB share one formation
  node (formation FIRST, then two spawns) on the verified B line beat,
  walking together. Verified on-device.
- Main mission (`mission/tutorial_0001.bin`, rebuilt): all 3 guards walk
  the v3 beats instead of idling — square loop, 3-point beat with fast
  leg, brisk beat + 3s watch halt.

Authoring: `PatrolNpc(...)` in `mission/Program.cs` (`Military`,
`BackAndForth`/`Circeling`, per-waypoint idle/speed); paired:
`PatrolPair(...)` — formation + N spawns on one node, waypoint children.
All component/node IDs unique mission-wide (spawns 2–4, formations
12–14, waypoints 22–30; pair uses formation 12, spawns 2–3, waypoints
22–24). Rebuild: same CLI, output name `test_patrol` triggers the locked
spawn (`test_patrolpair` for the pair probe).

## Objectives (verified on-device)

`test_objective.bin` is the gate: 3 guards + a primary `ObjectiveKill`
(`SlayGuards`, ID 40, targets = spawn IDs 2/3/4, minimum 3). Verified:
tracker shows “Eliminate the guards ({count} remaining)”, counts down per
kill, completes at 0. Raw description strings pass through unmapped; the
main mission now ships the same objective on its walking sentries.

Known behavior: completion plays the intro and follows the hub return
path, which is server-dead offline → spinloop. Use Retry for a clean
replay. A clean offline end-of-mission flow is an open contributor item.

## Spawn coordinates

(-50, 5, -50) is the locked spawn — ground level, room to move, guards
together. Found by probing; Y=5 grabs streets, Y=40 grabs rooftops.

## Device data layout (3 locations — read this, it matters)

The game does NOT read everything from shared storage. Verified live via adb. There are three locations:

**1. Shared metadata (our patch target) — you place this:**
```
/storage/emulated/0/ACID_Revival/
  boot.log                 # created on launch; real errors only
  AcierData/
    AssetBundleDict_AndroidDXT.xml
    AssetBundleDict_AndroidETC2.xml
    AssetBundleDict_AndroidGeneric.xml
    FirstContact_Android.xml
    GameDB_AndroidDXT.bin
    GameDB_AndroidETC2.bin
    GameDB_AndroidGeneric.bin
    Missions/
      tutorial_0001.bin    # ours — ship or rebuild, see above
```
Only ~800K. The patcher redirects `BuildDefinition.get_Url` here, so the
GameDB / FirstContact / dict lookups resolve locally. We never modify these
7 files — only `Missions/tutorial_0001.bin` is ours. They come from the SAME
dump, not a separate source: dumps store downloads as UID-named files and
the name<->uid mapping lives in `files/cache/catalogue.bin`. Recover them
with the bundled tool (stdlib only, no deps):

```
python3 tools/extract_metadata.py <path-to>/files/cache ./AcierData
```

It parses the catalogue's URL table, copies out the 7 files, and prints a
per-file report (`[catalogue]` = resolved from your dump,
`[known-uid]` = IA-revision fallback). Verified: 6/6 outputs byte-identical
(md5) to a working install, and FirstContact resolves via catalogue on a
pristine dump. If it reports anything MISSing, your dump is incomplete —
first boot dies at `FetchData`.

**2. App-external bundle cache (the real 1.4G) — you must push this:**
```
/storage/emulated/0/Android/data/com.ubisoft.assassinscreed.identity/
  files/cache/             # 105 UID-named bundle files + catalogue.bin (~1.4G)
  files/localstorage.json  # profile/progress state (tiny, game-written)
  files/track/tracking.bin # analytics queue (tiny, game-written)
  cache/UnityShaderCache/  # 329 compiled shaders (~8.6M, game-written)
```
The cache manager (`Manager.BaseDir = SavedDataDir + "/cache/"`) reads
bundles ONLY from here. Push your own cache copy's bundle files to
`files/cache/` via adb — nothing in this repo fetches them, and the shared
folder above can never substitute for them.

**High-quality envs — ALL LIVE ON DEVICE (17/17 boot, zero crashes):**
every dumped High bundle was swapped under its scene's Low cache UID with
the catalogue size fields patched (see procedure below) — Santacroce sunny,
stormy and tutorial, palazzo daytime AND nighttime, both Forlis, both
Monteriggionis, both Sant'Angelos, both Colosseums, plus the 4 Animus
scenes. Palazzo *nighttime* High was missing from the MEGA catalogue's URL
table but its bytes were in the same RAR unmapped (`2b03ed47-...`,
92997260 bytes = dict size byte-exact, UnityRaw Nov-2016,
strings-confirmed, verified booting on-device with lit-window detail) —
same swap procedure, Low UID `0994de5d-...` (verify size 70099692
on-device first; catalogue sizes `ec a2 2d 04 …` → `8c 06 8b 05 …`). Method (no patch change — the game keeps
requesting the Low URL): overwrite the Low bundle file with the High bytes
under the SAME cache UID, then patch the two LE64 size fields in
`files/cache/catalogue.bin` for that entry, because `Manager.Verify`
checks size (not content) and deletes unregistered files. Back up the Low
file first (one `cp` on device). UIDs in `tools/high_env_uids.txt`.
Source: a MEGA dump (`com.ubisoft.assassinscreed.identity.rar`) holding
the full 16-file High set — individual files are retrievable without
downloading it whole: each file's byte range walks via the RAR header
table, and a single-file RAR (signature + main head + file header/data +
ENDARC) extracts with 7z.

**3. App-private (`/data/user/0/...`) — nothing to do:**
Only `files/`, `cache/`, `shared_prefs/` (~913B prefs xml), `code_cache/`.
Tiny Unity/player state. Unreadable over adb (package is not debuggable) —
no root needed, leave it alone; the game manages it.

The stock IA instructions ("extract to `/Android/data/`") remain correct
for the bundle cache (location 2). What changed for THIS build: the 7
metadata files must ALSO exist at the `ACID_Revival/AcierData/` path
(location 1) instead of only inside the app folders.

## Critical rule: always patch pristine files

The patcher expects CLEAN `Managed/` input. Never run it on an
already-patched `apk_dec` — stacking hub on freeroam (or any double patch)
produces a boot that hangs at 100% (`CombatController.LoadMission` nulls on
missing player). If a build misbehaves, re-decode from your original APK.

## Logs

Only real errors land at `/storage/emulated/0/ACID_Revival/boot.log`
(marker + Unity exceptions). No per-file spam. If reporting a bug, attach
the full log plus which APK (hub/freeroam) and mission were used.

## Crowd: what lives offline

DB census (parsed entries, not string counts): 83 HumanoidDefs + 127
NpcDefs, zero key overlap. All 10 original pool bodies have HumanoidDef
rows (5 female + 5 male: `crowd_civilian_male_01/02/03`,
`crowd_civilian_rich_male_01/02`); none of them have NpcData rows, so
crowd bodies only spawn via the native crowd path, never as mission NPCs
(verified: plain-spawn minis crash at 50% load, patrol mini spinloops).
The patcher pool is 11 live bodies (mercenary + 3 females + 5 males +
2 courtesans, all verified walking on-device), interleaved so males fall
inside the spawnable range at forced quality 0.8. Bad picks are
null-skipped, crowd stays enabled InProgress; the mission sets
density 40 / max 20.

## Parcour + tracker guards (patcher-controlled)

- `ParcourHelper.CheckCivilianCover`: 7 null-guards (Patrol,
  PatrolDefinition ×2, Definition ×2, PatrolComponent, Formation) + null
  NavMeshAgent guard on `CheckCivilianCovers`. Two calls sit inside the
  `CanApproachPatrol` argument list, so their guards pop the 2 pending
  args too — patch-time asserts (7 guards / 2 nested) fail loud if the
  compiler output ever changes. (The naive version shipped an
  `InvalidProgramException` caught on-device; depth-aware version verified
  clean in `boot.log`.)
- `OptionsMenuData.UpdateMissionTracker`: swallow-guard — custom missions
  ship no objectives, which NRE'd on every options refresh.

## Env test matrix (all 17 boot on-device, zero crashes)

| Mission file | Scene | Notes |
|---|---|---|
| `test_animus_globe.bin` | AnimusGlobe | hub scene as mission |
| `test_tut01/02/03.bin` | TutorialScene 01–03 | particle-void arenas |
| `test_palazzo_day.bin` | Firenze palazzo daytime | High |
| `test_palazzo_night.bin` | Firenze palazzo nighttime | High, verified live |
| `test_santacroce_tut.bin` | Firenze santacroce tutorial | High, liveliest street |
| `test_santacroce_stormy.bin` | Firenze santacroce stormy | High |
| `test_forli_dusk.bin` / `test_forli_siege.bin` | Forli dusk / siege | High |
| `test_monte_day.bin` / `test_monte_night.bin` | Monteriggioni day / night | High |
| `test_roma_overcast.bin` / `test_roma_stormy.bin` | Sant'Angelo overcast / stormy | High |
| `test_colo_aft.bin` / `test_colo_foggy.bin` | Colosseum afternoon / foggy | High |

Test missions spawn the player at origin (navmesh snap decides landing —
rooftops happen) with no tuning. Known cosmetic: a floating civilian was
seen once in roma_stormy (spawn off-navmesh, harmless).

## Contributing

**Status: done, in maintenance.** The revival goal is met; the maintainer
is not planning further work. Contributions welcome within scope:

- Objectives (escort / interact / timer, chaining, multi-mission arcs).
- Text story (briefings, annotations) — no voices exist to reuse.
- Staging, crowd tuning, offline end-of-mission flow, tooling/docs.

Out of scope: open cities, social stealth, acted story, score, servers,
new maps/assets. Pick an open issue, discuss in Discussions, keep PRs to
`patch/` + `mission/` + `tools/` + docs. This repo does not contain any game binaries. See Legal in README.
