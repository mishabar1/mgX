# Small World map verification — per-map agent instructions

You are verifying the automatic region extraction of ONE Small World board scan (map N = your
assigned player count). Everything runs in /root/sw. The working image is
work/swN_small.png (2000px wide for maps 2/3, 2800px for maps 4/5). ALL coordinates below are
in working-image pixels ("working coords"). The review views have a 200px coordinate grid drawn
on them (red numbers).

## Goal
regions_N.json must describe the real board 1:1:
1. Every real region = exactly one label (no merged pair of regions under one label, no region
   split into fragments, none missing). Real regions are areas enclosed by WHITE borders on the
   map, plus each sea/lake as its own region. Mountain ranges partly covered by clouds are still
   ONE region if they are one enclosed area.
2. terrain correct for every region: farm (bright orange fields), forest (dark green trees),
   hill (light green meadows), swamp (brown-orange murky), mountain (grey/white rock), water
   (sea/lake only).
3. symbols correct: mine (red disc w/ crossed pick+shovel), magic (blue disc w/ white crystals),
   cavern (dark disc w/ cave mounds, golden ring), tribe (small white SQUARE tile w/ circular
   emblem = Lost Tribe marker). A region lists one entry per symbol instance.
4. isBorder: true iff region touches the board edge OR is adjacent to a SEA (not the lake).
   (Computed automatically — just sanity-check a few.)

Expected region ballpark (not exact — trust the map image, count enclosed areas yourself):
map 2 ≈ 23 land+water total, map 3 ≈ 30, map 4 ≈ 37, map 5 ≈ 44-48. Each map has exactly 2 seas
(at board edges) plus 1+ lakes. If your count differs after careful review, the map wins.

## Tools (run from /root/sw)
- python3 segment.py N        → re-runs segmentation for map N (applies fixes_N.json), writes
                                 work/swN_labels.pkl + work/swN_ws.png
- python3 analyze.py N        → recomputes regions_N.json (terrain/symbols/adjacency; applies
                                 overrides_N.json) + work/sheet_N.png contact sheet
- python3 mkviews.py N        → writes review views:
      work/revN_blob.png  colored label extents + id numbers + grid  ← main structural view
      work/revN_anno.png  boundaries + "id:TERRAIN[symbols][+B]" tags + grid ← terrain/symbol view
      work/revN_raw.png   plain map + grid (ground truth)
- python3 crop.py N X Y [R] [--anno] → work/crop.png zoomed at (X,Y), for fine inspection
- Read the PNGs with your Read tool. For 2800-wide views read the file directly (it will be
  downscaled for display ~x2 — multiply what you measure by the stated factor to get working
  coords; the red grid numbers are already working coords, use them as anchors).

## Fix files (create/edit, then re-run the two scripts + mkviews and re-check)
fixes_N.json   — structural fixes applied during segmentation:
  {"seeds": [[x, y], ...],            ← add a seed point INSIDE a region that got no own label
   "merges": [[keepId, dropId, ...]]} ← merge fragment labels into one region (ids from the
                                        CURRENT run; ids are renumbered after merges, so re-run
                                        and re-view after every change)
overrides_N.json — per-region data fixes applied in analyze:
  {"12": {"terrain": "swamp"},
   "7":  {"symbols": ["mine", "tribe"]},     ← full replacement list
   "3":  {"isBorder": true}}
  NOTE: overrides key by region id — only add them ONCE STRUCTURE IS FINAL (ids stop moving).

## Procedure
1. python3 segment.py N && python3 analyze.py N && python3 mkviews.py N
2. Read work/revN_raw.png and count the real regions carefully (enclosed white-border areas +
   seas/lakes). Note their rough locations.
3. Read work/revN_blob.png. Match every real region to exactly one label id. Find:
   - fragments (one real region covered by 2+ labels, e.g. a mountain split by clouds) → merge
   - swallowed regions (label spans 2+ real regions across a white border) → add a seed inside
     the swallowed part (pick a point via crop.py to be sure it's inside, away from borders)
   - missing regions (area with no sensible label) → add a seed
   Tiny sliver labels along borders/coasts → merge into the region they belong to.
4. Iterate step 1-3 until structure is 1:1. THEN check work/revN_anno.png + work/sheet_N.png:
   verify terrain + symbols of every region; write overrides_N.json for wrong ones.
   Terrain tags: FARM/FRST/HILL/SWMP/MNT/WATR-SEA-LAKE; symbols: M=mine S=magic C=cavern T=tribe.
   Symbol sanity: each map has 4-10 of each disc type; tribes: map2≈7, map3≈6, maps 4/5 more.
   If a symbol is missing from a region or misassigned (icon sits on the border between two
   regions), fix via overrides on the correct region.
5. Re-run analyze + mkviews, confirm the annotated view is fully correct.
6. Final check of regions_N.json: every region's adj list spot-check 3-4 regions (neighbors
   across a white border are adjacent; across a river/lake/sea they are NOT unless they truly
   share a land border; point-corner contacts should not create adjacency).
   KNOWN QUIRK: if two land regions only meet across a thin river that belongs to a sea/lake
   label, adjacency is already correct (they won't be adjacent). If a bogus adjacency remains,
   add {"adj": [...]} full-list override for those regions.
7. Report (final message): map N: region count, terrain histogram, symbol totals, list of fixes
   applied, and any remaining doubts. Do NOT modify any file not listed above. Do NOT touch other
   maps' files.
