import copy
import json
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator

from assetpipe import publish
from assetpipe.packs import load_pack

from .conftest import pack_doc, wallpaper, write_pack

ROOT = Path(__file__).resolve().parents[3]
SCHEMA = json.loads((ROOT / "content" / "catalog.schema.json").read_text(encoding="utf-8"))
SOURCE = json.loads((ROOT / "content" / "catalog.src.json").read_text(encoding="utf-8"))


def errors(document):
    return [e.message for e in Draft202012Validator(SCHEMA).iter_errors(document)]


def test_the_shipped_seed_catalog_matches_the_schema():
    assert errors(SOURCE) == []


def test_a_published_wallpaper_entry_produced_by_assetpipe_matches_the_schema(tmp_path):
    pack = load_pack(write_pack(tmp_path, pack_doc(wallpapers=[wallpaper("mb-01", starter=True)])))
    ref = {"path": "mb-01_16x9.jpg", "width": 3840, "height": 2160, "bytes": 2_000_000, "sha256": "ab" * 32}
    built = {"thumb": {**ref, "path": "mb-01_thumb.jpg", "width": 640, "height": 360}, "variants": {"16x9": ref}}
    entry = publish.wallpaper_entry(pack.wallpapers[0], built, "packs/default.matte-black/v1")

    document = copy.deepcopy(SOURCE)
    document["packs"][0]["wallpapers"] = [entry]

    assert errors(document) == []
    assert entry["variants"]["16x9"]["path"] == "packs/default.matte-black/v1/mb-01_16x9.jpg"


@pytest.mark.parametrize(
    ("mutate", "fragment"),
    [
        (lambda d: d.update(schemaVersion=2), "1 was expected"),
        (lambda d: d.update(contentBaseUrl="http://insecure.example/v1/"), "does not match"),
        (lambda d: d["games"][0]["detection"].update(exeNames=[], steamAppIds=[]), "is not valid under any of the given schemas"),
        (lambda d: d["games"][0]["detection"]["exeNames"].append("..\\evil.exe"), "does not match"),
        (lambda d: d["collections"][0].update(tone="neon"), "is not one of"),
        (lambda d: d.update(unexpected=True), "Additional properties"),
    ],
)
def test_the_schema_rejects_broken_catalogs(mutate, fragment):
    document = copy.deepcopy(SOURCE)
    mutate(document)

    found = errors(document)

    assert found and any(fragment in message for message in found), found


def test_the_schema_rejects_unsafe_variant_paths_and_hashes():
    document = copy.deepcopy(SOURCE)
    bad = {"path": "../up.jpg", "w": 10, "h": 10, "bytes": 10, "sha256": "XYZ"}
    document["packs"][0]["wallpapers"] = [{"id": "x-01", "title": "x", "tone": "dark", "tags": [], "setupMatch": ["black"],
                                           "focal": {"x": 0.5, "y": 0.5}, "variants": {"16x9": bad, "5x4": bad}}]

    messages = " | ".join(errors(document))

    assert "does not match" in messages and "5x4" in messages
