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

## Device cache layout

The patched build reads game data from shared storage (not app-private):

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

The 7 metadata files come from your own cache copy (e.g. the IA
`AC-Identity.zip`) — we never modify them. Large asset bundles
(environments, outfits) resolve through the game's cache manager at boot;
as long as the cache copy is complete, no manual bundle placement is needed.
The stock IA instructions say extract to `/Android/data/` — for THIS build,
the `AcierData/` folder must land at the `ACID_Revival/` path above instead.

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
