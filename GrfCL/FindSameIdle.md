# Find Same Idle

Read-only lookup in one opened GRF. Compare action 0, frame 0 of an ACT/SPR
pair and list matching pairs, sorted by action count (highest first).

```text
GrfCL.exe -open "C:\RO\data.grf" -findSameIdle "data\sprite\npc\4_geffen_03.spr"
```

The target can be an `.act` or `.spr` inside the GRF, or a local file with its
same-name companion. An optional second argument limits the search to a GRF
folder and its descendants:

```text
GrfCL.exe -open "C:\RO\data.grf" -findSameIdle "C:\sprites\emily.act" "data\sprite"
```

Paths are supplied by the caller; no client directory or server URL is built in.
The command never saves or changes the GRF and does not extract assets.

Matching compares the visible first-frame layers: their placement, transforms,
color masks, image dimensions, and decoded pixels. Image and palette indices,
compression, later frames, frame delays, sounds, and anchors are ignored.
Transparent pixels are normalized. Identical appearances assembled with different
layer layouts are outside this exact-match lookup.

Empty first frames and missing SPR companions are counted separately. Malformed
candidates are skipped, with up to five warnings and a failure count in the summary.
The action count is the number of ACT entries, including directional entries;
more entries are a useful lead, not a guarantee of usable walking animations.

Validation: the kRO `data.grf` search for Emily found `geffen_mage_3_1.act` (40
entries) and `4_geffen_03.act` (8 entries), scanning 53,165 pairs in approximately
10 seconds on the development machine. Synthetic fixtures also verified image
index changes, palette reordering, different pixels, local targets, and folder
filtering.

The Release build is in `GrfCL/bin/Release/`. Keep the freshly built DLLs beside
`GrfCL.exe` when copying the build to another installation.
For a reusable batch catalog of all identical-first-frame groups, NPC walking
capabilities and walking candidates, see [Sprite Catalog](SpriteCatalog.md).
