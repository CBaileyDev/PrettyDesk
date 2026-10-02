# 2026-10-02: Scoped Rocket League prompt mode

Status: implemented and locally validated. This change makes the approved named
fan-art allowance explicit per subject and keeps the generic prompt restrictions
active for every other pack. Public distribution rights, additional light-tone art
and dedicated ultrawide/portrait masters remain pending.

The Rocket League pack already had `artMode: named-fan-art` and a `namedSubjects`
list, but the linter used every name in that pack's static allowlist regardless of
which names were declared. It also generated a generic checklist saying that all
known characters were forbidden. The mode now derives its exceptions from the
declared, reviewed subject list. Known Rocket League names remain blocked in
generic packs, and the mode does not bypass safe zones, prompt length, banned
phrases, composition, crop, role or tone checks.

The generated prompt checklist now describes both supported modes. The Rocket
League section marks the approved fan-art direction and still reminds reviewers
to check car geometry, original composition, safe crops and distribution rights.

The current catalog contains four reviewed entries: Floodlight Haze, Octane
Aerial, Quiet Fennec and Neo Tokyo Rain. They have 16:9 files and thumbnails in
the local bundle. The pack has no light-tone wallpaper yet, so lint keeps its
single non-blocking advisory. The current entries have no dedicated ultrawide or
portrait masters; catalog variants use the available landscape masters.

Validation: assetpipe tests pass (75 cases); generic and Rocket League lint pass with zero
errors; prompt Markdown is regenerated and `prompts --check` passes. Rocket
League lint reports only the expected light-tone advisory. Catalog schema,
bundled file integrity and the locked Release app checks are recorded in the
[navigation and default-art update](2026-10-02-top-navigation-and-default-art.md).
