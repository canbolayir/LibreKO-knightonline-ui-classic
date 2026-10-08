"""Generate the Classic Magic Anvil placement snapshot from the LibreKO server seed tables.

The plugin embeds two files from assets/anvil:
  anvil_origins.json.gz       one row per upgradable origin: [id, class, type, grade, kind, scroll...]
  anvil_upgrade_settings.json a verbatim copy of ItemUpgradeSettings.json

The server stays authoritative; regenerate after recipe, item or upgrade-setting changes and rebuild
the plugin. Usage:
  python tools/generate_anvil_rules.py [--seed <LibreKO>/Server/LibreKO.Game/Seed/Data]
"""
import argparse
import gzip
import json
from pathlib import Path

plugin = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
parser.add_argument("--seed", type=Path, default=plugin.parent / "LibreKO/Server/LibreKO.Game/Seed/Data",
                    help="LibreKO server seed data folder")
parser.add_argument("--out", type=Path, default=plugin / "assets/anvil", help="output folder")
args = parser.parse_args()

recipes = {}
for row in json.loads((args.seed / "ItemUpgradeRecipes.json").read_text(encoding="utf-8-sig")):
    if row["NewNumber"]:
        recipes.setdefault(row["OriginNumber"], set()).add(row["RequiredItem"])
origins = {}
for source in sorted(args.seed.glob("Items.slot*.json")):
    for row in json.loads(source.read_text(encoding="utf-8-sig")):
        item = row["Num"]
        if item in recipes:
            origins[item] = [item, row.get("ItemClass", 0), row.get("ItemType", 0),
                             row.get("Grade", 0) or item % 10, row["Kind"], *sorted(recipes[item])]
args.out.mkdir(parents=True, exist_ok=True)
payload = json.dumps([origins[k] for k in sorted(origins)], separators=(",", ":")).encode()
(args.out / "anvil_origins.json.gz").write_bytes(gzip.compress(payload, mtime=0))
(args.out / "anvil_upgrade_settings.json").write_bytes((args.seed / "ItemUpgradeSettings.json").read_bytes())
print(f"Generated {len(origins)} origins from {args.seed}")
