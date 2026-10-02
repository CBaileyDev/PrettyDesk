# PrettyDesk — Art Direction & Prompt Guide

> **Audience:** the implementing agent (writes the prompts) and the owner (runs them through ChatGPT image generation and reviews the results).
> **Goal:** every wallpaper should look like something people would screenshot from a "my clean setup" TikTok. It needs to be calm, premium, and cohesive, and it must work behind desktop icons on any screen shape.

---

## 1. Quality bar

A PrettyDesk wallpaper is **done** only when it meets all of these:
1. It reads instantly at thumbnail size: one clear subject or one clear mood.
2. It's calm where the UI lives. The left edge (desktop icons) and the bottom edge (taskbar) are low-detail.
3. It survives every crop: 16:9, 16:10, 3:2, 21:9, 32:9 and, where portrait art exists, 9:16.
4. It's technically clean at 100% zoom on a 4K monitor. That means no banding, no blotchy darks, no AI garble, no text-like squiggles, and no seams.
5. It's newly composed artwork. Generic packs evoke a game; approved fan-art packs may use recognizable named subjects (§5).
6. It belongs to its set. Every wallpaper in a pack shares a palette and rendering style.

Fewer, better wallpapers beat many mediocre ones. If a generation is 90% good, regenerate it.

---

## 2. What the "clean setup" audience loves (research summary)

Desk-setup content on TikTok and Pinterest in 2025–26 keeps converging on the same few looks:
- **Warm minimalism:** clean surfaces, matte aluminum, light wood, neutral palettes and lots of negative space ("looks like it belongs in an architecture magazine").
- **Tactile naturalism:** linen, stone, plaster and live plants, often with sunlight and leaf shadows.
- **Moody accents:** steel blue, deep charcoal and matte black, plus dark-mode wallpapers built on deep blacks and subtle gradients.
- **Soft gradients and painted textures:** aura/mesh gradients, and painterly or impasto landscapes that read as art rather than photos.
- **Nature at 4K:** misty mountains, still lakes and quiet forests.
- **Color-matched setups:** people build all-black, all-white, wood-and-plants, pastel, or RGB/neon setups, and pick wallpapers that *match the desk*. Ultrawide users especially like minimal compositions that let the extra width breathe.

What this means for us:
- Default collections are organized around **setup color schemes** (§3).
- Onboarding asks "What does your setup look like?"
- Every game also gets a **minimal** wallpaper (§4.2) so clean-setup users can keep the vibe while gaming.

Sources:
- [Spring desk setup aesthetic trends 2026](https://rackorapro.com/blogs/tips/top-spring-desk-setup-aesthetic-trends-for-2026-tiktok-pinterest)
- [TikTok: Clean Setup](https://www.tiktok.com/discover/clean-setup)
- [TikTok: Aesthetic Desktop Wallpapers](https://www.tiktok.com/discover/aesthetic-desktop-wallpapers)
- [mjhdwallpapers: 2025–26 wallpaper trends](https://mjhdwallpapers.com/best-desktop-wallpapers-2025-2026-4k-5k-ultrawide-dual-monitor-backgrounds-hd-wallpaper/)
- [UltrawideWallpapers: minimalist](https://ultrawidewallpapers.net/minimalist-wallpapers)
- [Backdrova: minimal wallpapers for clean desktops](https://backdrova.com/blog/minimal-wallpapers-clean-desktop)

---

## 3. Default collections

The 13 base collections have **8 wallpapers each** (`soft-gradients` has 10), 106 wallpapers in total.
The user-requested `default.ios-glass` supplement adds an intentionally small three-colorway capsule (Tide, Bloom, Dusk),
bringing the default art plan to 14 collections and 109 wallpapers. Its original glass sculptures use separately authored landscape,
ultrawide and portrait compositions; all six rendered ratios are tagged `bundled` for offline use. See `IOS_DESIGN.md`.
Within each collection, vary:
- the subject
- the time of day
- the focal position: at least 2 centered and symmetrical (for people who hide their icons), and the rest right-of-center
- the tone, where the collection allows it

| # | Pack id | Title | Setup match | Tone | Palette & materials | Subject ideas (non-exhaustive) |
|---|---|---|---|---|---|---|
| 1 | `default.matte-black` | Matte Black | black | dark | #050506–#1A1B1E, one cool or warm rim light; OLED-friendly true blacks | black sand dunes, folded black silk, basalt stone, liquid metal droplet, eclipse rim, monolith in fog |
| 2 | `default.clean-white` | Clean White | white | light | #F4F2EE, #E8E6E1, soft grey shadows | leaf shadows on plaster, folded paper, white curved architecture, overcast snowfield, ceramic forms |
| 3 | `default.warm-minimal` | Warm Minimal | wood | light/mid | sand, oat, clay, travertine, light oak, linen | dunes at golden hour, stone arches, ceramic still life, linen curtain light, terracotta walls |
| 4 | `default.sage-botanical` | Sage & Botanical | wood, white | light/mid | sage #9CAF88, olive, moss, cream | monstera shadow, misty fern forest, moss macro, eucalyptus on linen, greenhouse glass |
| 5 | `default.soft-gradients` | Aura Gradients | all | both | aura/mesh gradients + fine grain; palettes: dusk peach, ocean teal, lavender haze, graphite, mint | flowing ribbons, frosted-glass orbs, soft blurred color fields (original shapes; never imitate OS default wallpapers) |
| 6 | `default.misty-nature` | Misty Nature | all | both | desaturated greens/blues, fog white | layered mountain ridges in fog, still alpine lake at blue hour, black-sand beach, pine forest mist |
| 7 | `default.painted` | Painted Landscapes | wood, pastel | both | oil/impasto, visible brushwork, harmonious muted palette | impasto clouds, palette-knife sea, impressionist wheat field, painted mountains at dusk |
| 8 | `default.steel-blue-night` | Steel Blue Night | black, rgb | dark | steel blue #4A6A8A, slate, cold whites | rain on glass with blurred city bokeh, night coastline, cold concrete architecture, foggy bridge |
| 9 | `default.cozy-lofi` | Cozy Lo-fi | wood, pastel | mid/dark | warm lamp amber + dusk blue | hand-painted animated-film-background style: a desk by a rainy window, a reading nook, a rooftop at dusk. No characters. |
| 10 | `default.deep-space` | Deep Space | black, rgb | dark | true black, subtle nebula hues | planet limb with thin atmosphere glow, eclipse corona, lunar horizon, sparse starfield, ringed planet edge |
| 11 | `default.pastel` | Pastel Dream | pastel | light | blush pink, lavender, mint, butter | pastel cloudscape, pastel desert, soft 3D clay shapes, cotton-candy sunset over calm sea |
| 12 | `default.neon-minimal` | Neon Minimal | rgb | dark | near-black + ONE neon accent per image (magenta, cyan, violet or orange) | single neon light line in a dark room, wet street reflection, minimal synth sunset, glowing ring in fog |
| 13 | `default.architectural` | Architecture & Light | white, black | both | concrete, plaster, glass; strong light and shadow | brutalist concrete, curved staircase, light shafts, minimal pool, arches in sun |

**Onboarding quiz → preselected collections**

| Answer | Collections |
|---|---|
| Matte Black | matte-black, deep-space, steel-blue-night, architectural (dark only) |
| Clean White | clean-white, soft-gradients (light only), architectural (light only), misty-nature (light only) |
| Warm Wood & Plants | warm-minimal, sage-botanical, painted, cozy-lofi |
| Pastel | pastel, soft-gradients, cozy-lofi |
| RGB | neon-minimal, deep-space, steel-blue-night |
| Surprise me | one hand-picked "best of" from every collection (`starter` tag) |

**Starter set (bundled in the installer):** tag exactly **one** wallpaper per collection `starter: true`. Pick the strongest and most universal one.

---

## 4. Universal composition rules

### 4.1 Safe zones
```
┌──────────────────────────────────────────────────────┐
│░░░│                                                  │
│░░░│        ┌──────────── central 80% width ──────┐   │
│░░░│        │                         ● focal     │   │  ← keep subject ~50–70% across,
│░░░│        │   (all important content in here)   │   │     35–55% down (or centered)
│░░░│        └─────────────────────────────────────┘   │
│░░░│                                                  │
│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│  ← bottom 8%: taskbar, keep calm
└──────────────────────────────────────────────────────┘
 ░ left 15%: desktop icon column, keep calm, low-detail, low-contrast
```
- **Crop math to respect:**
  - 16:9 → 3:2 removes about 16% of the width. 16:9 → 16:10 removes 10%.
  - A 3:1 ultrawide master → 32:9 removes about 16% of the height. → 21:9 removes about 21% of the width.

  So **all important content stays inside the central 80% of width and the middle 70% of height**.
- Horizons sit on the thirds (about 33% or 66%), never exactly at 50% unless the composition is deliberately symmetrical.
- No important detail touches the frame edges. Edges should be "extendable" (sky, fog, dunes, water, gradient) so the ultrawide extension in ChatGPT is seamless.

### 4.2 Roles for game packs
Every game pack has **3 required wallpapers + 1 optional**:

| Role | Purpose | Notes |
|---|---|---|
| `hero` | The iconic-feeling environment vista that instantly says "this game's world" | Most detailed, richest color, cinematic light |
| `minimal` | The clean-setup version: **one** motif and **≥ 70% negative space**, in the game's palette | Think single silhouette against a gradient, one glowing object in fog, or a lone landmark at dusk. This is the most important one for our audience. |
| `mood` | Atmosphere and texture: an environmental close-up, weather, or a material from the world | Darker and calmer; a great everyday background |
| `alt` (optional) | A different time of day or season, or a **light-tone** version | Lets "follow Windows light/dark" work for the game |

Each game pack MUST contain at least one `tone: dark` wallpaper and SHOULD contain one `tone: light`.

### 4.3 Technical rendering rules (applied in every prompt)
- Smooth, clean gradients in skies and empty areas. Ask for "banding-free" and "no noise". The pipeline adds controlled grain.
- No heavy vignette, no lens dirt, no chromatic aberration, no borders or frames.
- Darks are rich but not crushed. Detail should still be visible in the shadows (a black wallpaper still needs form).
- At most one tiny, distant, anonymous silhouette for scale. No faces and no characters as the subject.

---

## 5. Generic inspiration and approved recognizable fan art

The owner has approved recognizable Rocket League fan art, including the game
name, Octane, Fennec and Batmobile in generation prompts. This supersedes the old
blanket prohibition for that art direction. The [pilot document](updates/rocket-league-fan-art.md)
contains full named-subject drafts and the pending pipeline migration.

| Mode | Prompt direction | Current implementation |
|---|---|---|
| Generic inspiration (default) | Translate genre, setting, palette, motifs, weather and materials without named game subjects | Existing YAML and lint behavior |
| Approved recognizable fan art | Use explicitly approved game, location and car names; recognizable forms in newly authored compositions | Rocket League direction approved; per-pack pipeline mode still pending |

Both modes retain desktop safe zones, full written prompts, technical review,
provenance and deliberate shot variety. No readable text, HUD, watermark or copied
official key-art composition. Do not extract official assets or imply endorsement.
Recognizable approved car geometry is a review goal for the Rocket League pilot,
not an automatic rejection reason.

The existing `inspiration:` YAML block and linter still implement generic mode;
do not weaken validation globally or claim the new drafts already pass it. Keep
named drafts in update docs until parsing, scoped lint and generated review text
are migrated together. Public release applicability and attribution are tracked
separately in the [pilot release checklist](updates/rocket-league-fan-art.md#review-and-distribution).

---

## 6. Generation workflow (for the owner, using ChatGPT)

1. Open `art/PROMPTS.md`. It's generated from the YAML and has one section per pack, with copy-paste blocks.
2. **Start a new ChatGPT chat for each pack.** Paste the pack's **style bible** first (it tells ChatGPT the shared palette and rules for this series), then paste each wallpaper's prompts in order.
3. For each wallpaper:
   - **L, the landscape master.** Paste the `landscape` prompt and ask for **16:9 landscape at the highest resolution available**. Download it with ChatGPT's download button (full-resolution PNG), not a screenshot.
   - **U, the ultrawide master.** In the same chat, with that image selected or attached, paste the `ultrawide` prompt. It *extends* the existing image to a **3:1 panorama**. If ChatGPT can't do 3:1, accept 21:9 or wider; the pipeline adapts.
   - **P, the portrait master** (only when `portrait: true`). Paste the `portrait` prompt with the L image attached as a reference.
4. Save the files as `art/raw/{packId}/{wallpaperId}_L.png`, `_U.png` and `_P.png`.
5. Run `assetpipe status`, then `assetpipe build --pack {packId}`, then `assetpipe review --pack {packId}`, and check the contact sheet against the **review checklist**:
   - [ ] No text, letters, glyph-like squiggles, logos or watermarks anywhere (zoom to 100%)
   - [ ] No warped geometry, melted objects, broken perspective, or duplicate or extra limbs
   - [ ] The ultrawide extension has no visible seam, repeated objects or lighting mismatch
   - [ ] The left 15% and bottom 8% are calm; the focal point sits where the YAML says
   - [ ] Gradients and darks are smooth at 100% zoom (temporarily raise the screen brightness to check for banding or blotches)
   - [ ] It still reads well at thumbnail size
   - [ ] It follows its approved art mode: generic inspiration, or recognizable approved fan-art subjects in a new composition; no implied official endorsement
   - [ ] It feels like the rest of its pack
6. If a check fails, regenerate and add a short note to `reviewNotes`. If it passes, set `approved: true`, and adjust `focal` if the subject landed somewhere else.

---

## 7. Prompt file format (`art/prompts/{packId}.yaml`)

One file per pack. Prompts MUST be **fully written out**, with no template placeholders, so any prompt can be copy-pasted on its own. `assetpipe prompts` renders all YAML into `art/PROMPTS.md`.

```yaml
pack: game.cs2                 # or default.matte-black
kind: game                     # game | default
title: Counter-Strike 2        # display only — NEVER used inside prompt text
inspiration:                   # games only — the IP translation block (§5)
  genre: grounded modern tactical shooter
  setting: sun-bleached desert towns, Mediterranean/North-African sandstone architecture, industrial yards
  palette: ["#D9A441", "#C8B08A", "#3E5C76", "#1B1F24"]
  motifs: [arched doorways, long dawn shadows, dust in the air, wooden crates, tiled rooftops]
  avoid: [game title, maps by name, soldiers, weapons, logos, crosshairs, graffiti text, team insignia]
styleBible: |                  # pasted once at the start of the pack's ChatGPT chat
  We are creating a cohesive series of premium desktop wallpapers...
wallpapers:
  - id: cs2.hero-01
    title: Desert Courtyard at Dawn
    role: hero                 # hero | minimal | mood | alt  (defaults: use "default")
    tone: dark                 # dark | light | mid
    setupMatch: [wood, black]  # black | white | wood | pastel | rgb
    tags: [warm, architectural]
    starter: false             # defaults only: exactly one per collection = true
    accent: "#D9A441"
    focal: { x: 0.64, y: 0.46 }
    upscaler: x4plus           # x4plus (painterly/photo) | x4plus-anime (flat/illustrated)
    grain: 0.15
    portrait: true
    prompts:
      landscape: |
        ...
      ultrawide: |
        ...
      portrait: |
        ...
    reviewNotes: ""
    approved: false
    rawSha256: {}              # filled by assetpipe
```

### 7.1 Landscape prompt structure (always in this order)
1. **Format line:** "A 16:9 landscape desktop wallpaper."
2. **Scene:** 2–4 sentences. Concrete subject, setting, light direction, weather and materials. Describe what we *see*, not adjectives about quality.
3. **Style:** the medium and rendering ("fine-art photography, medium-format look", "matte digital painting with soft brushwork", "hand-painted animated-film background").
4. **Palette & mood:** 3–5 hex colors plus 2–3 mood words.
5. **Composition:** where the focal subject sits (in % across and down), plus the standard safe-zone sentence:
   > "The left 15% and the bottom 8% stay calm and low-detail (desktop icons and taskbar go there). Keep every important element inside the central 80% of the width and the middle 70% of the height so the image can be cropped to other screen shapes."
6. **Technical:**
   > "Highest available resolution; crisp detail on the focal subject; perfectly smooth, banding-free gradients; no noise, no vignette, no border or frame."
7. **Exclusions:**
   > "Strictly no text, letters, numbers, logos, symbols, emblems, watermarks, signatures, UI or HUD elements; no characters or people as the subject."

   Add pack-specific `avoid` items here.

Target **120–220 words**. Don't use "4K/8K/ultra HD" keyword spam, "trending on ArtStation", or "masterpiece"; they add nothing.

### 7.2 Ultrawide prompt (an edit of L)
> "Extend this exact image into an ultra-wide 3:1 panorama by continuing the scene naturally to the left and right. Do not change, move, re-light or re-style anything that already exists. New area on the left: {calm, low-detail continuation}. New area on the right: {continuation with gentle interest}. Same lighting direction, palette, materials and level of detail. No seams, no repeated or mirrored objects, no text or logos."

### 7.3 Portrait prompt (recompose using L as a reference)
> "Using the attached image as the style and content reference, recompose the same scene as a 9:16 portrait phone/monitor wallpaper. {where the subject sits vertically}. Same lighting, palette, materials and rendering style. Keep the bottom 10% calm. Strictly no text, letters, logos, watermarks or UI."

---

## 8. Baseline deliverables and prompt self-review

These A1/A2 counts describe the initial plan. The supplemental Liquid Glass
collection and current completion counts are tracked in `art/GENERATION_STATUS.json`.
The approved Rocket League direction follows §5 and its separate migration plan.

**A1: default collections (before engineering milestone M3)**
- `art/prompts/default.*.yaml`: 13 files and 106 wallpapers in total, following §3, §4 and §7.
- Exactly one `starter: true` per collection.
- A minimal `tools/assetpipe` with the `prompts` command, which renders `art/PROMPTS.md`, so the owner can start generating right away.

**A2: game packs**
- `art/prompts/game.*.yaml` for **every** game in `GAME_CATALOG_SEED.md`, with 3 required wallpapers each (`hero`, `minimal`, `mood`) plus `alt` where the game has a strong second look.
- Each file starts with an `inspiration` block researched from public descriptions of the game's world. Generic packs translate the mood; the approved Rocket League fan-art pilot uses recognizable subjects in newly composed scenes (§5).

**Self-review before committing prompts** (check every one):
- [ ] Generic packs avoid game proper nouns; approved fan-art packs use only the scoped named subjects authorized for that pack (§5)
- [ ] The structure follows §7.1 and is 120–220 words
- [ ] Focal position is stated and matches `focal`
- [ ] Safe-zone, technical and exclusion sentences are present
- [ ] Within a pack, the wallpapers differ in subject, composition and time of day, while the palette and style stay coherent
- [ ] The `ultrawide` prompt describes concrete content for the new left and right areas
- [ ] The YAML is valid (`assetpipe prompts` runs without errors)

---

## 9. Gold-standard examples

These set the quality bar. Match their specificity.

### 9.1 Default: `default.matte-black` / `mb-01` "Obsidian Dune"
```yaml
  - id: mb-01
    title: Obsidian Dune
    role: default
    tone: dark
    setupMatch: [black, rgb]
    tags: [minimal, oled, sculptural]
    starter: true
    accent: "#AEB4BC"
    focal: { x: 0.66, y: 0.50 }
    upscaler: x4plus
    grain: 0.25
    portrait: true
    prompts:
      landscape: |
        A 16:9 landscape desktop wallpaper. An ultra-minimal scene of a single smooth sand dune made of fine
        matte-black sand. Its sharp, curved crest sweeps from the lower right toward the center of the frame.
        A faint cool silver rim light grazes the crest from the upper right, revealing delicate wind ripples on
        the slope; everything else falls away into deep charcoal and true black. The sky is a seamless
        near-black gradient with no stars.
        Style: high-end fine-art photography with a medium-format look; sculptural, quiet and luxurious.
        Palette: #050506, #0E0F11, #1A1B1E with a subtle silver highlight #AEB4BC. Mood: calm, focused, premium.
        Composition: the dune crest occupies the right half, about 55–75% across and 40–60% down. The left 15%
        and the bottom 8% stay calm and low-detail (desktop icons and taskbar go there). Keep every important
        element inside the central 80% of the width and the middle 70% of the height so the image can be
        cropped to other screen shapes.
        Technical: highest available resolution; crisp detail on the ridge; perfectly smooth, banding-free dark
        gradients; no noise, no vignette, no border or frame.
        Strictly no text, letters, numbers, logos, symbols, watermarks, signatures, people, animals or UI elements.
      ultrawide: |
        Extend this exact image into an ultra-wide 3:1 panorama by continuing the scene naturally to the left
        and right. Do not change, move, re-light or re-style anything that already exists. New area on the left:
        calm, near-black empty space with only the faintest suggestion of a low dune. New area on the right: a
        second, smaller dune ridge fading into darkness, catching a whisper of the same silver rim light. Same
        lighting direction, palette, sand texture and level of detail. No seams, no repeated or mirrored shapes,
        no text or logos.
      portrait: |
        Using the attached image as the style and content reference, recompose the same scene as a 9:16
        portrait wallpaper. The dune crest rises diagonally from the lower right toward the upper center, with
        its brightest rim light at about 40% of the height. Same silver rim lighting, matte-black sand, palette
        and photographic style. Keep the bottom 10% calm and dark. Strictly no text, letters, logos, watermarks
        or UI.
```

### 9.2 Default: `default.clean-white` / `cw-01` "Afternoon Leaf Shadows"
```yaml
  - id: cw-01
    title: Afternoon Leaf Shadows
    role: default
    tone: light
    setupMatch: [white, wood]
    tags: [botanical, shadow, plaster]
    starter: true
    accent: "#B9B2A6"
    focal: { x: 0.62, y: 0.42 }
    upscaler: x4plus
    grain: 0.2
    portrait: true
    prompts:
      landscape: |
        A 16:9 landscape desktop wallpaper. A softly textured warm off-white plaster wall in late-afternoon
        sun. Crisp but gentle shadows of a leafy olive branch and a few long grass blades fall diagonally
        across the wall from the upper right, slightly blurred at their tips as if the plant sits a little way
        from the wall. A faint warm glow fades toward the lower left.
        Style: minimalist interior photography, natural light, true-to-life plaster texture, quiet and airy.
        Palette: #F4F2EE, #E8E4DC, #CFC8BC with soft grey-taupe shadows #B9B2A6. Mood: serene, clean, sunlit.
        Composition: the densest cluster of leaf shadows sits about 55–75% across and 30–55% down; the
        shadows thin out toward the edges. The left 15% and the bottom 8% stay calm and low-detail (desktop
        icons and taskbar go there). Keep every important element inside the central 80% of the width and the
        middle 70% of the height so the image can be cropped to other screen shapes.
        Technical: highest available resolution; crisp detail in the shadow edges; perfectly smooth,
        banding-free light falloff; no noise, no vignette, no border or frame.
        Strictly no text, letters, numbers, logos, symbols, watermarks, signatures, furniture, people or UI
        elements; the plant itself is not visible, only its shadow.
      ultrawide: |
        Extend this exact image into an ultra-wide 3:1 panorama by continuing the scene naturally to the left
        and right. Do not change, move, re-light or re-style anything that already exists. New area on the left:
        smooth, sunlit plaster with only a hint of warm glow. New area on the right: a few more sparse leaf and
        grass shadows trailing off the branch, gradually softer. Same light direction, plaster texture and
        palette. No seams, no repeated shadow shapes, no text or logos.
      portrait: |
        Using the attached image as the style and content reference, recompose the same scene as a 9:16
        portrait wallpaper. The olive-branch shadow enters from the upper right and arcs down to about 45% of
        the height, with grass-blade shadows below it. Same light, plaster texture and palette. Keep the bottom
        10% calm. Strictly no text, letters, logos, watermarks or UI.
```

### 9.3 Game: `game.cs2` / `cs2.minimal-01` "Lone Doorway"
```yaml
  - id: cs2.minimal-01
    title: Lone Doorway
    role: minimal
    tone: light
    setupMatch: [wood, white]
    tags: [warm, minimal, architectural]
    accent: "#D9A441"
    focal: { x: 0.64, y: 0.48 }
    upscaler: x4plus
    grain: 0.2
    portrait: true
    prompts:
      landscape: |
        A 16:9 landscape desktop wallpaper. A vast, sun-bleached sandstone wall fills the frame, almost
        featureless. Set into it, right of center, is a single tall arched wooden double door painted a faded
        dusty blue, slightly ajar, with a thin warm line of light inside. Early-morning sun rakes across the
        wall from the left, revealing fine stone texture, and a long soft shadow of the arch falls across the
        sand-colored ground. A few motes of dust float in the light.
        Style: refined architectural photography, minimal and warm, gentle contrast.
        Palette: #E9D9BC, #D9A441, #C8B08A with the door in #5E7C93 and shadows in #8A7356. Mood: quiet,
        anticipatory, sunlit.
        Composition: the doorway sits about 58–70% across and 30–70% down; more than 70% of the image is
        plain wall and ground. The left 15% and the bottom 8% stay calm and low-detail (desktop icons and
        taskbar go there). Keep every important element inside the central 80% of the width and the middle 70%
        of the height so the image can be cropped to other screen shapes.
        Technical: highest available resolution; crisp stone and wood-grain detail around the door; perfectly
        smooth, banding-free light gradients; no noise, no vignette, no border or frame.
        Strictly no text, letters, numbers, logos, symbols, emblems, graffiti, signage, watermarks, signatures,
        weapons, soldiers, people or UI/HUD elements.
      ultrawide: |
        Extend this exact image into an ultra-wide 3:1 panorama by continuing the scene naturally to the left
        and right. Do not change, move, re-light or re-style anything that already exists. New area on the left:
        more plain sunlit sandstone wall with soft texture, low detail. New area on the right: the wall continues
        and turns gently into shadow, with a small barred window high up. Same light direction, palette and
        stone texture. No seams, no duplicated doors, no text, graffiti or logos.
      portrait: |
        Using the attached image as the style and content reference, recompose the same scene as a 9:16
        portrait wallpaper. The arched blue doorway is centered horizontally and spans roughly 30–75% of the
        height, with its long shadow falling toward the lower left. Same morning light, sandstone texture and
        palette. Keep the bottom 10% calm. Strictly no text, letters, logos, graffiti, watermarks or UI.
```
