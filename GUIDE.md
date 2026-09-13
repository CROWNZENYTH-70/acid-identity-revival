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

## The provided mission file

`mission/tutorial_0001.bin` is 100% ours (authored by `mission/Program.cs`,
not extracted). Contents: Firenze_santacroce_sunny, player spawn at
(-50, 5, -50), 3 live enemies (papal guard, guard captain, crossbowman),
crowd off. Place it at:

```
/storage/emulated/0/ACID_Revival/AcierData/Missions/tutorial_0001.bin
```

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
- GuardC `15c667c9-...` → `enemy_crossbowman_medici`

Plus a null-check in `NpcStatsHelper` so lone NPCs (no formation parent)
don't abort stats init. To add/change an enemy: pick a live `Enemy_` name
from the 127 `NpcData` defs, add an alias row, add an `Npc(...)` line.

## Spawn coordinates

(-50, 5, -50) is the locked spawn — ground level, room to move, guards
together. Found by probing; Y=5 grabs streets, Y=40 grabs rooftops.

## Device data layout (3 locations — read this, it matters)

You were right to flag this: the game does NOT read everything from shared
storage. Verified live via adb. There are three locations:

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

## Contributing

Pick an open issue, discuss in Discussions, keep PRs to `patch/` +
`mission/` + docs. Stay anonymous-safe: no real emails, no personal paths,
no game binaries. See Legal in README.
