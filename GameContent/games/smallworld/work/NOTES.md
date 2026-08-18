# Small World — build state & plan (resume here)

## Done
- Sources in `GameContent/games/smallworld/src/` (Kariotip RU PnP, ~1.1GB — PDFs gitignored;
  consider gitignoring `src/` entirely).
- **Segmentation pipeline finished and run on all four boards** (`work/segment.py` + `work/analyze.py`
  + `work/symbols.py`). Recipe that works:
  - shrink to 2000px (2/3-player boards) or 2800px (4/5-player boards are drawn ~0.7× smaller, so
    they are upscaled to keep border/icon pixel sizes uniform).
  - white-border mask = `min(RGB) > 168 & max-min < 55`, then binary-CLOSE 3×3 ×4 — the close is
    essential: several borders on the boards are DASHED and erosion leaks through the gaps.
  - big blue bodies (`B-R > 50 & B > 100`, components ≥ 8k/15k px) are a SECOND barrier and become
    their own water regions — coastlines have no white border, so without this, land regions bleed
    into the sea (this was the biggest single bug).
  - erode 12 → seeds ≥ 900px → watershed on `-distance_transform(~border)`, masked to non-water,
    then water bodies appended as regions.
  - region centre = pole of inaccessibility on a ZERO-PADDED mask (padding matters: without it a
    region touching the image edge gets its click marker ON the edge).
  - adjacency = dilate each region 3px, intersect, require ≥ 20 shared px (symmetrised).
  - terrain by HSV fractions; water forced from the water mask.
  - symbols by masked multi-scale template match (`TM_SQDIFF_NORMED` with a circular mask) for
    mine/magic/cavern — real hits score ≤ 0.06, junk ≥ 0.11, so the threshold is clean. Lost-Tribe
    tiles are found by a global white-SQUARE scan (fill ≥ 0.78, 19-42px, aspect 0.6-1.5) instead —
    template matching alone gave hundreds of false positives on white road/river pixels.
- **Region counts extracted:** map 2 → 25 (22 land), map 3 → 36 (29 land), map 4 → 51 (45 land),
  map 5 → 73 (65 land). Symbols found: 4/5/7/9 mines, 4/5/7/9 magic, 4/5/7/9 caverns,
  9/9/14/18 lost tribes.
- **Assets shipped to `GameContent/games/smallworld/`:** `map_2..5.jpg` (2200px, ~1.6MB each),
  `regions_2..5.json`, `maps_index.json`, `races/<race>.png` + `races/<race>_declined.png` (14 each,
  cut from `PnP Маленький мир.pdf` with white made transparent), `powers/<power>.png` (20).
  Cover art at `GameContent/games/covers/smallworld.svg`.
- **`SmallWorldGameFlow.cs` written, registered and TESTED.** SMALL_WORLD added to `GameData.cs`,
  `BaseGameFlow.cs` (catalog + PrettyName + CreateGame) and `DataRepository.cs` (AttachGameFlow).
  - Board = ONE token item (the jpg); regions are server data; click markers sit at region centres
    (cx/cy are PERCENT of the image, so they track the board at any scale).
  - Full core loop: combo queue of 6 (1 coin onto each combo skipped, coins on the taken combo go
    to the purse), conquest = 2 + defenders (+1 mountain, +1 troll lair, Lost Tribe = 1 defender),
    reinforcement die (0,0,0,1,2,3) once per turn on the last conquest, decline (flip, keep 1 per
    region, the older declined race vanishes), redeploy phase (click own regions; click again to
    pick tokens back up), scoring 1/region + race/power bonuses, rounds 10/10/9/8 for 2/3/4/5p.
  - Defender retreat: loses 1 token (Elves lose none), the rest auto-spread over their other regions.
  - Races v1: Dwarves3, Elves6, Giants6, Humans5, Orcs5, Ratmen8, Skeletons6, Trolls5, Wizards5,
    Tritons6. Powers v1: Alchemist4, Commando4, Mounted5, Underworld5, Forest4, Hill4, Swamp4,
    Merchant2, Flying5, Pillaging5.
  - Because that pool is only 10/10, a long 5-player game can exhaust it; `RefillQueue` recycles
    RETIRED combos (not on the queue, not on the board) back into the decks, and a seat facing a
    genuinely empty queue can `PassTurn` — same as the real game running out of races.
  - AI: picks the affordable combo with the most tokens+coins, conquers cheapest-then-most-valuable,
    uses the die when the gap is ≤ 3, declines when fewer than 2 conquests are affordable, redeploys
    evenly with a border bias.
- **Verification done** (`work/TestHarness.cs`, `work/RulesTest.cs` — compile-check harness, not part
  of the server build): four all-AI games (2/3/4/5 seats) play to the last round with no exception,
  asserting after EVERY step that owned regions have tokens, no region is held active+declined by
  one seat, hands/purses never go negative, purse+queue coins never drop, and both the 3D scene and
  every seat panel rebuild. Plus 13 focused rule assertions on cost modifiers, decline and Elves.
  Final scores look sane (e.g. 4p → 70/73/71/78).

## Remaining steps
1. **Verify the extracted regions against the printed boards by eye** — this is the one step still
   open. Run `python3 work/mkviews.py N` and read `work/revN_blob.png` (labels+ids) and
   `work/revN_anno.png` (terrain/symbol tags); `work/crop.py N X Y [R] [--anno]` zooms. Fix with
   `fixes_N.json` (`{"seeds": [[x,y]], "merges": [[keepId, dropId, …]]}`, applied in segmentation)
   and `overrides_N.json` (`{"12": {"terrain": "swamp", "symbols": ["MINE"]}}`, applied in analyze),
   then re-run `segment.py N && analyze.py N && build_out.py`. `work/AGENT_README.md` is a complete
   briefing for doing this map-by-map (it was written for a subagent; it reads fine for a human).
   Known suspects: maps 4/5 look OVER-segmented (mountain ranges split by clouds/snow → merge), and
   a couple of coastal slivers should be merged into their neighbour. Expected real counts are
   roughly 23/30/37/44 regions, so map 4 (51) and map 5 (73) need the most work.
2. Play a real game through the UI and screenshot it (Carcassonne/Catan style) — the flow is
   verified headless but has never been rendered by the client.
3. Nice-to-haves once the maps are clean: race banners + power badges in a proper 3D combo-queue
   display (assets are already extracted); the Lost Tribe token could use its own art instead of the
   grey disc; races needing extra UI (Amazons, Sorcerers, Ghouls, Halflings) for v2.

## Files
- `work/segment.py`      board → watershed labels (`work/swN_labels.pkl`, `work/swN_ws.png`)
- `work/analyze.py`      labels → `work/regions_N.json` (terrain, symbols, adjacency, isBorder)
- `work/symbols.py`      icon detection (writes `work/symbols.json`, consumed by analyze)
- `work/build_out.py`    → `map_N.jpg` + `regions_N.json` + `maps_index.json` (the served assets)
- `work/mkviews.py`, `work/crop.py`   review renders
- `work/extract_assets.py`            race/power art out of the PnP PDF
- `work/TestHarness.cs`, `work/RulesTest.cs`  headless game + rule tests (see the header comments;
  they compile against a stub DataRepository, outside the server project)
