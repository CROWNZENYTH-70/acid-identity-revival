#!/usr/bin/env python3
"""Recover the 7 named metadata files from a UID-keyed bundle cache.

The game (and dumps like IA's AC-Identity.zip) store downloads as
files/cache/<uid> with no names. The name<->uid mapping lives in
files/cache/catalogue.bin as (url, uid) pairs. This script reads that
table and copies out the files the offline patch needs:

  GameDB_AndroidDXT.bin / GameDB_AndroidETC2.bin / GameDB_AndroidGeneric.bin
  AssetBundleDict_AndroidDXT.xml / ..._AndroidETC2.xml / ..._AndroidGeneric.xml
  FirstContact_Android.xml

Usage:
  extract_metadata.py <cache_dir> [out_dir]
    cache_dir : directory containing catalogue.bin + uid files
                (e.g. extracted com.ubisoft.assassinscreed.identity/files/cache)
    out_dir   : default ./AcierData

If the catalogue lacks an entry (e.g. a live catalogue whose FirstContact
row was replaced at runtime), falls back to the known IA-revision UIDs.
Exits non-zero listing anything still missing. Stdlib only.
"""

import os
import re
import shutil
import sys

WANT = [
    "GameDB_AndroidDXT.bin",
    "GameDB_AndroidETC2.bin",
    "GameDB_AndroidGeneric.bin",
    "AssetBundleDict_AndroidDXT.xml",
    "AssetBundleDict_AndroidETC2.xml",
    "AssetBundleDict_AndroidGeneric.xml",
    "FirstContact_Android.xml",
]

# IA AC-Identity.zip revision fallback (verified against its index).
KNOWN_UIDS = {
    "GameDB_AndroidDXT.bin": "855bdb39-e751-4b3f-adf3-3eac0b7a5e62",
    "GameDB_AndroidETC2.bin": "6926e03d-98cd-41bb-87e0-64d4423989a5",
    "GameDB_AndroidGeneric.bin": "b9f84efa-07ca-4f26-9661-9803e1aa412b",
    "AssetBundleDict_AndroidDXT.xml": "3b8c2e93-db91-46df-a09c-0a1bcde6a598",
    "AssetBundleDict_AndroidETC2.xml": "2103c619-9de4-4575-aa14-d8a86fbd447b",
    "AssetBundleDict_AndroidGeneric.xml": "0e2dd360-4170-4e77-9029-6b774b8f40ee",
    "FirstContact_Android.xml": "6c5da090-1113-4710-8e3b-0bef0b4322ff",
}

PAIR = re.compile(
    rb"(https?://[^\x00]+)\x00[^$]*\$"
    rb"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\n"
)


def parse_catalogue(path):
    with open(path, "rb") as fh:
        data = fh.read()
    table = {}
    for url, uid in PAIR.findall(data):
        try:
            name = url.decode("utf-8", "strict").rsplit("/", 1)[-1]
        except UnicodeDecodeError:
            continue
        table[name] = uid.decode()
    return table


def main(argv):
    if len(argv) < 2 or len(argv) > 3:
        print(__doc__.strip().splitlines()[-5])
        print("usage: extract_metadata.py <cache_dir> [out_dir]")
        return 2
    cache, out = argv[1], (argv[2] if len(argv) > 2 else "AcierData")
    cat = os.path.join(cache, "catalogue.bin")
    if not os.path.isfile(cat):
        print(f"no catalogue.bin in {cache}")
        return 2
    table = parse_catalogue(cat)
    print(f"catalogue: {len(table)} url->uid pairs")
    os.makedirs(out, exist_ok=True)
    missing = []
    for name in WANT:
        uid = table.get(name) or KNOWN_UIDS[name]
        src = os.path.join(cache, uid)
        how = "catalogue" if name in table else "known-uid"
        if not os.path.isfile(src):
            print(f"MISS  {name} (uid {uid} not in cache dir)")
            missing.append(name)
            continue
        dst = os.path.join(out, name)
        shutil.copyfile(src, dst)
        print(f"OK    {name} <- {uid} [{how}] ({os.path.getsize(dst)} bytes)")
    if missing:
        print(f"missing {len(missing)}: {', '.join(missing)}")
        return 1
    print(f"wrote {len(WANT)} files to {out}/")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
