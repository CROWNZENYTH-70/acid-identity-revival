# AC Identity — Offline Revival Kit

Offline patcher + mission builder for Assassin's Creed Identity (Android).
Boot past the dead servers into the Animus hub or straight into a playable
Firenze mission with working enemy AI, combat, and death.

Proven on-device: boot → spawn → 3 guards fight back → killable, plus a
walking crowd (3 women + 1 mercenary) and the High-quality Santacroce env
at a steady 58–59 fps. No servers.

See `GUIDE.md` for how it works, where the mission file goes, and how to
contribute. A ready-made `mission/tutorial_0001.bin` (our authoring) ships
in the repo — copy it to the device path in step 5, or rebuild your own.

## Demo

- Hub boot (Animus): https://github.com/CROWNZENYTH-70/acid-identity-revival/releases/download/demo-v1/demo-hub.mp4
- Tutorial loading → Firenze fight (3 guards): https://github.com/CROWNZENYTH-70/acid-identity-revival/releases/download/demo-v1/demo-fight.mp4

## What works

- Offline boot (local profile, local data URL, no Play Games sign-in)
- Animus hub boot (default) or direct-to-mission boot (`freeroam` flag).
  Note: the hub itself has nothing working except Settings — it is a
  boot proof, not gameplay. Playable content is the Firenze mission.
- Custom `tutorial_0001.bin` mission: player spawn + 3 live enemies
  (papal guard, guard captain, crossbowman) with precache → behavior tree →
  stats → attack
- 60 fps mission cap (was 25), navmesh spawn snap, ETC2 texture lock
- Crowd alive: bad-pick null-skip, live-guid pool (3 women + mercenary),
  stays enabled during missions, crowd quality 0.7
- High envs: verified on-device via content swap under the Low cache UID +
  catalogue size patch (see `GUIDE.md`); no fps cost on Santacroce sunny
- 2 real gameplay fixes: mission-guid → live NPC alias, lone-NPC
  formation null-check

## Requirements

- Your own AC Identity APK + its `AcierData` cache (not included, never will be)
- `apktool`, `apksigner`, `keytool`, `dotnet` 8 SDK (apktool/keytool need Java)
- Android device for install + `ACID_Revival/` folder on shared storage

## Build

```bash
# 1. decode your APK
apktool d ACIdentity.apk -o apk_dec

# 2. patch (default = hub boot; pass freeroam for direct-to-mission)
dotnet run --project patch -- "$PWD/apk_dec/assets/bin/Data/Managed" \
  "$PWD/work/Assembly-CSharp.dll" hub
# freeroam variant:
# dotnet run --project patch -- "$PWD/apk_dec/assets/bin/Data/Managed" \
#   "$PWD/work/Assembly-CSharp.dll" freeroam

# 3. rebuild + sign (first time: create YOUR keystore, keep it private)
mkdir -p work work/Missions
cp work/Assembly-CSharp.dll work/SharedBaseLib.dll apk_dec/assets/bin/Data/Managed/
apktool b apk_dec -o work/ACID-revival-unsigned.apk
cp work/ACID-revival-unsigned.apk work/ACID-revival.apk
keytool -genkeypair -keystore work/revival.keystore -storepass CHANGE_ME \
  -alias acid -keypass CHANGE_ME -keyalg RSA -keysize 2048 \
  -validity 10000 -dname "CN=ACID Revival"
apksigner sign --ks work/revival.keystore --ks-pass pass:CHANGE_ME \
  --ks-key-alias acid --key-pass pass:CHANGE_ME work/ACID-revival.apk
apksigner verify work/ACID-revival.apk

# 4. mission (edit spawn/guids in mission/Program.cs first)
dotnet run --project mission \
  -p:ManagedDir="$PWD/apk_dec/assets/bin/Data/Managed" \
  -- work/Missions/tutorial_0001.bin
```

Install the APK, push the mission to
`/storage/emulated/0/ACID_Revival/AcierData/Missions/tutorial_0001.bin`,
launch. Crash/error log (only on real errors) lands at
`/storage/emulated/0/ACID_Revival/boot.log`.

## Known issues (good first contributions)

- `ParcourHelper.CheckCivilianCover` NRE spam (player-side, pre-existing).
- Patrol routes unverified (`MissionNpcFormation` + `PatrolData` wiring).
- Male crowd variety: only 1 male body (`special_npc_mercenary_01`) ships in
  the offline GameDB; male civilian defs exist but their bodies were never
  dumped. Spawning male civilians as mission NPCs is an open experiment.
- Remaining 15 High envs untested (only Santacroce sunny verified;
  UIDs in `tools/high_env_uids.txt`).

## Legal

Bring your own APK and data. This repo contains only our patcher and mission
authoring code — no Ubisoft binaries, assets, or caches.
