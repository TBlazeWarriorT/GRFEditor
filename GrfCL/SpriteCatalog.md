# Sprite Catalog

Build a reusable CSV catalog from one opened GRF, in one batch. The GRF is never
modified. No game directory, asset tunnel, or client repository path is hardcoded.

```text
GrfCL.exe -open "C:\RO\data.grf" -spriteCatalog "C:\RO\catalog"
```

Optional arguments: a GRF folder to scan, and the existing client
`src/DB/Monsters/MonsterTable.js` to annotate sprite IDs. An empty folder scans
all of the GRF; the folder filter applies to both sources and candidates.

```text
GrfCL.exe -open "C:\RO\data.grf" -spriteCatalog "C:\RO\catalog" "" "C:\client\src\DB\Monsters\MonsterTable.js"
GrfCL.exe -open "C:\RO\data.grf" -spriteCatalog "C:\RO\npc-catalog" "data\sprite\npc"
```

Use the full GRF scan to find NPC upgrades that live in the monster folder.
The mapping table is read as literal numeric ID/string entries, never executed.
Multiple IDs for the same sprite are preserved, separated by semicolons.
Without the table, the catalogs work normally with empty ID fields.

## Output

The command writes five UTF-8 CSVs with BOM, quoted fields and a header:

| File | Contents |
| --- | --- |
| `sprites.csv` | Every scanned ACT, its first-idle signature, status and capabilities. Missing companions and errors remain visible as rows. |
| `npcs.csv` | ACTs in an `npc` folder, with walking classification, mapped IDs and walking candidates in the same row. |
| `idle-groups.csv` | Groups of two or more files with the same first idle frame, including equipment and effects. |
| `walk-upgrades.csv` | NPCs without full animated walking, paired with NPC/monster candidates having the same first idle frame and animated walking in all eight directions. |
| `verified-upgrades.csv` | Full verification of those candidate pairs, classifications and detailed differences. |

Existing catalog files are not overwritten. Choose a new output folder for each
scan. Progress is printed approximately every five seconds, followed by counts
and elapsed scan/export time. Keep the CSVs to query later without reopening the
GRF; regenerate after changing assets or the mapping table.

## Walking classification

For NPC and monster assets, inspect group 1 (ACT entries 8-15), matching the
client's NPC `WALK = 1` convention. Evaluate decoded visible frames per direction,
including layer placement, transforms and colors. Count only directions in which
all frames are visible. Empty or malformed animations do not establish capability.

| `walk` | Meaning |
| --- | --- |
| `animated` | All eight directions contain at least two different visible poses, and their sequences differ from the corresponding idle. |
| `partial` | Some directions meet that criterion, but not all eight. Inspect the direction counts. |
| `idle-copy` | No independently animated walking direction; at least one animated sequence repeats its corresponding idle. |
| `static` | Visible walk frames exist but never change pose. |
| `empty` | Walk frames exist but no direction has a completely visible sequence. |
| `missing` | No walk direction contains frames. |
| `unknown` | Missing SPR or a parsing/image error prevents classification. |
| `not-applicable` | Equipment/effect/other folders participate in idle grouping only. |

Idle-copy detection ignores consecutive duplicate poses, loop repetitions and
starting phase. Frame delays, sounds and anchors are not used. Direction counts
are included separately: visible, independently animated, and idle copies.
`action_entries` counts directional ACT entries, not semantic action groups.

This is a catalog of animation data, not a semantic recognition of footsteps.
NPC classification follows the asset folder; monsters that can be used as NPCs
also appear in `sprites.csv` and can supply upgrade candidates. The supplied
mapping table annotates existing files; it does not change their folder category.

## Matching and candidates

Like `-findSameIdle`, grouping compares action 0 / frame 0: visible layer layout,
transforms, color masks, image dimensions and decoded pixels. Image/palette indices
and transparent RGB do not change the match. Different layer constructions with
the same flattened appearance are outside this exact-match search.

The lookup candidate is a first-frame match; the verified catalog classifies its full animation compatibility.
The lookup CSV remains separate from the full verification results.
Neither command changes the client's sprite mapping.

Decoded sprites and per-image hashes are released after each asset. Only row
metadata and signatures remain for grouping/export. Equipment/effects use the
existing first-image SPR decoder when their first frame references image 0 only;
otherwise their SPR is decoded normally. NPCs/monsters inspect the idle and walk
groups; later action groups are not parsed.

## Full upgrade verification

The batch automatically verifies its candidate pairs after writing the four lookup
CSVs. Only the candidate assets are fully read. You can verify an existing catalog
without scanning the GRF again:

```text
GrfCL.exe -open "C:\RO\data.grf" -verifySpriteUpgrades "C:\RO\catalog\walk-upgrades.csv" "C:\RO\verified-upgrades.csv"
```

Read every ACT action and frame in each source/candidate pair. Compare the eight
idle directions, then all original actions at the same indices. Compare decoded
image content, layer order/count, transforms, colors, exact positions (including
fractional ACT 2.6 offsets), frame counts/order, action delays, anchors (including
auxiliary fields), and sound events. Resolve sound indices to their filenames, so
reordering a sound table does not produce a difference. Image/palette reindexing
also remains equivalent. SPR images unused by any frame, ACT reserved bytes and
invisible-layer data do not affect the comparison.

Walking is rechecked from the current candidate assets rather than trusting the
CSV's flags. All eight directions must be independently animated, with positive
delays. The source must lack that full walking capability.

| `classification` | Meaning |
| --- | --- |
| `direct-upgrade` | All eight complete idle directions and every original action are preserved, and the candidate adds full animated walking. |
| `identical-idle` | Complete idle is identical, but another original action differs or walking is not gained. |
| `first-frame-only` | First idle frame matches, but complete idle differs. |
| `no-match` | First idle frame no longer matches, or is empty. |
| `error` | A missing/malformed asset or incomplete data prevents exact verification. Equality fields remain blank. |

All original action indices must be preserved. Replacing an existing static or
copied walk group with animated walking therefore remains a manual-review case,
even when the idle is identical. Originals containing only the idle group can
become direct upgrades by preserving that group and adding walking.

The output retains the input columns and adds:

- `idle_equal`: every idle frame, delay, anchor and sound event agrees.
- `idle_visual_equal`: exact idle frame count/order, drawings and positions agree;
  timing, anchors and sound events may differ.
- `idle_cycle_equal`: the visual idle cycle agrees after collapsing repeated poses,
  repeated loops and starting phase. It ignores timing, anchors and sounds, and
  never relaxes automatic certification.
- `original_actions_equal`, current source/candidate walking and `walk_gained`.
- First difference's action/frame, reason, and source/candidate values.
- All difference categories for the idle and for the original actions, plus
  `difference_details` containing a line for every discrepancy in paired frames.
  Unequal frame counts are reported separately; extra unpaired frames are covered
  by that count difference. Action and frame indices are zero-based.
- `verification_error` for assets that could not be verified.

Drawing/transform differences are diagnosed separately from position-only changes.
The frame comparisons remain exact: no fuzzy image threshold or timing tolerance.
The CSV reader supports quoted fields, escaped quotes, multiline fields and BOM.
Existing verification output is never overwritten.

Each distinct asset is decoded once per verification run; only fingerprints and
metadata remain cached. On the development GRF, 77 pairs involved 126 assets:
verification took approximately 2-3.5 seconds, yielding 50 direct-upgrade pairs
for 36 NPCs and 27 manual-review pairs. Emily passed the complete comparison.
A combined scan and verification run took 45.76 seconds; timings vary with I/O.

Synthetic fixtures verified full preservation, later-frame/direction changes,
delays, anchors, sound changes and sound-table reindexing, image reindexing,
fractional positions, changed non-idle actions, stale first-frame matches,
malformed/missing assets and truncated delay tables, quoted multiline CSV fields
and refusal to overwrite existing reports.

## Quick consultation in PowerShell

Look up a numeric sprite ID without scanning the GRF again:

```powershell
Import-Csv "C:\RO\catalog\npcs.csv" |
    Where-Object { ($_.ids -split ';') -contains '671' } |
    Select-Object ids, act, walk, walk_candidates
```

List the NPCs with fully animated walking:

```powershell
Import-Csv "C:\RO\catalog\npcs.csv" |
    Where-Object walk -eq 'animated' |
    Select-Object ids, act
```

Query `sprites.csv` instead to include monsters usable as NPCs. Use
`idle_signature` to look up other members of any identical-first-frame group.

## Validation

On the development machine, a full kRO `data.grf` scan completed in 39.37 seconds:
82,272 ACTs, 53,167 ACT/SPR pairs, 29,105 missing SPR companions, 6,587 empty
idles, 6,959 duplicate-idle groups and no parsing errors in the inspected data.
Among 1,375 NPC assets, 186 had independently animated walking in all eight
directions. There were 77 walking-candidate pairs covering 49 source NPCs.
Emily (`671`, `4_geffen_03`) matched `geffen_mage_3_1`, annotated with IDs
`2551;2914;20858` from the supplied client table.

Synthetic fixtures verified animated/static/partial/empty walking, copied idle
cycles with a changed starting phase, missing/broken files, invalid image references,
image and palette reindexing, different pixels, ID aliases and hex/Unicode escapes,
folder filtering and
refusal to overwrite an existing catalog.

Build: `GrfCL/bin/Release/`. Copy the executable together with that build's DLLs
when updating another installation.
