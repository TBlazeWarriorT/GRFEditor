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

The command writes four UTF-8 CSVs with BOM, quoted fields and a header:

| File | Contents |
| --- | --- |
| `sprites.csv` | Every scanned ACT, its first-idle signature, status and capabilities. Missing companions and errors remain visible as rows. |
| `npcs.csv` | ACTs in an `npc` folder, with walking classification, mapped IDs and walking candidates in the same row. |
| `idle-groups.csv` | Groups of two or more files with the same first idle frame, including equipment and effects. |
| `walk-upgrades.csv` | NPCs without full animated walking, paired with NPC/monster candidates having the same first idle frame and animated walking in all eight directions. |

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

A walking candidate is a first-frame match, not a validated automatic replacement.
The catalog does not compare every idle direction or the remaining action groups.
It does not change the client's sprite mapping.

Decoded sprites and per-image hashes are released after each asset. Only row
metadata and signatures remain for grouping/export. Equipment/effects use the
existing first-image SPR decoder when their first frame references image 0 only;
otherwise their SPR is decoded normally. NPCs/monsters inspect the idle and walk
groups; later action groups are not parsed.

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

On the development machine, a full kRO `data.grf` scan completed in 40.73 seconds:
82,272 ACTs, 53,167 ACT/SPR pairs, 29,105 missing SPR companions, 6,587 empty
idles, 6,959 duplicate-idle groups and no parsing errors in the inspected data.
Among 1,375 NPC assets, 186 had independently animated walking in all eight
directions. There were 77 walking-candidate pairs covering 49 source NPCs.
Emily (`671`, `4_geffen_03`) matched `geffen_mage_3_1`, annotated with IDs
`2551;2914;20858` from the supplied client table.

Synthetic fixtures verified animated/static/partial/empty walking, copied idle
cycles with a changed starting phase, missing/broken files, invalid image references,
image and palette reindexing, different pixels, ID aliases, folder filtering and
refusal to overwrite an existing catalog.

Build: `GrfCL/bin/Release/`. Copy the executable together with that build's DLLs
when updating another installation.