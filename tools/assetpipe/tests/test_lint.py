from pathlib import Path

from assetpipe import lint
from assetpipe.packs import load_pack

from .conftest import LANDSCAPE, pack_doc, wallpaper, write_pack


def _pack(tmp_path: Path, doc: dict):
    return load_pack(write_pack(tmp_path, doc))


def errors(findings):
    return [f.message for f in findings if f.severity == "error"]


def _default_pack_doc(n: int = 8):
    wps = []
    for i in range(n):
        centered = i < 2
        wp = wallpaper(f"mb-{i + 1:02d}", starter=(i == 0), focal={"x": 0.5 if centered else 0.66, "y": 0.5})
        if centered:
            wp["prompts"]["landscape"] = LANDSCAPE.replace("55–75% across", "40–60% across")
        wps.append(wp)
    return pack_doc(wallpapers=wps)


def test_gold_standard_example_is_clean(tmp_path):
    findings = lint.lint_pack(_pack(tmp_path, _default_pack_doc()))
    assert errors(findings) == []


def test_overlong_landscape_prompt_fails(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["prompts"]["landscape"] = LANDSCAPE.replace("Style:", "Style: " + "very quiet " * 40)
    assert any("words" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_too_short_landscape_prompt_fails(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["prompts"]["landscape"] = "A 16:9 landscape desktop wallpaper. A dune."
    assert any("words" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_game_name_in_prompt_text_is_rejected(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["prompts"]["landscape"] = LANDSCAPE.replace("sand dune", "Elden Ring sand dune")
    assert any("proper noun" in m and "elden ring" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_title_of_a_game_pack_is_forbidden_in_its_own_prompts(tmp_path):
    wps = [
        wallpaper("zz.hero-01", role="hero"), wallpaper("zz.minimal-01", role="minimal"), wallpaper("zz.mood-01", role="mood"),
    ]
    wps[0]["prompts"]["landscape"] = LANDSCAPE.replace("matte-black sand", "Zorbulon-style sand")
    doc = pack_doc("game.zz", "game", wps, title="Zorbulon Quest",
                   inspiration={"genre": "g", "setting": "s", "palette": ["#111111"], "motifs": ["m"], "avoid": ["logo", "text", "character"]})
    msgs = errors(lint.lint_pack(_pack(tmp_path, doc)))
    assert any("proper noun 'zorbulon'" in m for m in msgs)
    wps[0]["prompts"]["landscape"] = LANDSCAPE
    assert not any("zorbulon" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_banned_phrases(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["prompts"]["landscape"] = LANDSCAPE.replace("Style:", "Style: trending on ArtStation, masterpiece, 8K;")
    msgs = errors(lint.lint_pack(_pack(tmp_path, doc)))
    assert any("trending on" in m for m in msgs) and any("masterpiece" in m for m in msgs)


def test_focal_must_match_the_stated_position(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][2]["focal"] = {"x": 0.9, "y": 0.5}
    assert any("outside the stated" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_missing_position_statement(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][2]["prompts"]["landscape"] = LANDSCAPE.replace("about 55–75% across and 40–60% down", "somewhere on the right")
    assert any("about A-B% across" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_default_pack_needs_exactly_one_starter_and_two_centred(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][1]["starter"] = True
    assert any("exactly one" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))
    doc = _default_pack_doc()
    for w in doc["wallpapers"]:
        w["focal"] = {"x": 0.66, "y": 0.5}
    assert any("centred focal point" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_default_pack_size(tmp_path):
    assert any("must have 8" in m for m in errors(lint.lint_pack(_pack(tmp_path, _default_pack_doc(7)))))


def test_missing_standard_sentences(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["prompts"]["landscape"] = LANDSCAPE.replace("Strictly no text, letters, numbers, logos, symbols,", "No words,")
    assert any("exclusions" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_ultrawide_needs_concrete_left_and_right(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["prompts"]["ultrawide"] = "Extend this exact image into an ultra-wide 3:1 panorama. New area on the left: dark. New area on the right: more. No seams."
    assert any("concrete content" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_portrait_flag_must_match_prompts(tmp_path):
    doc = _default_pack_doc()
    doc["wallpapers"][0]["portrait"] = False
    assert any("portrait prompt present" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))
    doc = _default_pack_doc()
    del doc["wallpapers"][0]["prompts"]["portrait"]
    assert any("missing prompts: portrait" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_game_pack_requires_inspiration_roles_and_a_dark_tone(tmp_path):
    wps = [wallpaper("zz.hero-01", role="hero", tone="light"), wallpaper("zz.minimal-01", role="minimal", tone="light")]
    doc = pack_doc("game.zz", "game", wps, title="Zz")
    msgs = errors(lint.lint_pack(_pack(tmp_path, doc)))
    assert any("inspiration" in m for m in msgs)
    assert any("missing required role 'mood'" in m for m in msgs)
    assert any("tone: dark" in m for m in msgs)


def test_game_wallpaper_ids_are_prefixed(tmp_path):
    wps = [wallpaper("hero-01", role="hero"), wallpaper("zz.minimal-01", role="minimal"), wallpaper("zz.mood-01", role="mood")]
    doc = pack_doc("game.zz", "game", wps, title="Zz",
                   inspiration={"genre": "g", "setting": "s", "palette": ["#111111"], "motifs": ["m"], "avoid": ["logo", "text", "character"]})
    assert any("id must start with 'zz.'" in m for m in errors(lint.lint_pack(_pack(tmp_path, doc))))


def test_file_name_must_match_pack_id(tmp_path):
    path = write_pack(tmp_path, _default_pack_doc())
    renamed = path.with_name("other.yaml")
    path.rename(renamed)
    assert any("must be 'default.matte-black.yaml'" in m for m in errors(lint.lint_pack(load_pack(renamed))))


def test_duplicate_wallpaper_ids_across_packs(tmp_path):
    a = load_pack(write_pack(tmp_path, _default_pack_doc()))
    other = _default_pack_doc()
    other["pack"] = "default.clean-white"
    b = load_pack(write_pack(tmp_path, other))
    assert any("also used in" in f.message for f in lint.lint_all([a, b]))


def test_ordinary_words_that_overlap_game_titles_are_allowed():
    # "steam rising", "rust", "rusted" and "horizon" are ordinary English and must not trip the proper-noun check
    terms = lint.forbidden_terms.__globals__["PROPER_NOUNS"]
    assert "steam" not in lint.STUDIO_NAMES and "rust" not in terms and "horizon" not in terms
