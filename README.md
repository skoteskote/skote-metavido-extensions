# Skote Metavido Extensions

Extensions for [Keijiro's Metavido](https://github.com/keijiro/Metavido) volumetric video in Unity. This is not a Metavido implementation; it builds on top of `jp.keijiro.metavido` and `jp.keijiro.metavido.vfxgraph`.

## Contents

Namespace: `Skote.MetavidoExtensions`

| Component / asset | What it does |
|---|---|
| `MetavidoManager` | Plays Metavido clips on a `VideoPlayer` from a simple list, `VideoClipCollection`s or a single `RenderClipSettings`; Timeline marker/signal support and VFX rate ramping |
| `VideoClipCollection`, `PlayableVideoClip`, `RenderClipSettings` | ScriptableObjects describing clips and their playback settings |
| `VideoClipMarker`, `VideoClipSignal` | Timeline markers/signals that drive `MetavidoManager` |
| `CameraOffsetManager`, `OffsetCameraController` | Smooth camera offsets between clips, based on Metavido metadata |
| `VFX/MetavidoInverseProjectionCropped.vfxoperator` | Cropped variant of Metavido's *Inverse Projection* subgraph |
| `VFX/MetavidoSampleRandomCropped.vfxblock` | Samples random points inside a cropped region of a Metavido frame |

The cropped subgraphs are derived from Keijiro's Metavido subgraphs (Unlicense).

## Alignment: several phones in one shared space

Namespace: `Skote.MetavidoExtensions.Alignment`

Plays Metavido clips recorded on different phones together, in time and in one coordinate space. Each phone's poses are in its own ARKit world; a printed marker that every phone tracked before the take (stored in a sidecar file by the Meadow recorder) puts them all in marker space.

| Component | What it does |
|---|---|
| `AlignedMetavidoSession` | Master clock for a set of clips: the master clip's video clock drives time, the others follow by small speed corrections (or a seek when far off). Display rotation (marker space to scene; `(-90, 0, 0)` stands a wall marker's room upright), an IMGUI overlay with play/pause and a time slider, and a marker gizmo. Context menu **Measure Alignment** logs a numeric overlap check of the current frame. |
| `AlignedMetavidoClip` | One clip: plays from a file path (never a transcoded `VideoClip`; a rescaled frame decodes to a wrong pose), reads its sidecar, and exposes the per-clip controls: time offset, freeze pose (median camera pose over a window), manual nudge (position and rotation in marker space), visibility. Skips the first 0.35 s, which carries the pose from before the take's origin reset. |
| `VFXMetavidoMarkerSpaceBinder` | `VFXMetavidoBinder` with `InverseView` replaced by display × nudge × worldToMarker × cameraToWorld. Works whatever space the VFX simulates in. |
| `MetavidoSidecar` | Reader for the recorder's sidecar (JSON Lines: header with `markerAtStart`, sightings, footer). Skips truncated lines from crashed takes; falls back to the median of the sightings when there is no `markerAtStart`. |
| `AlignmentCheck` | Rebuilds each clip's point cloud from its demuxed depth with the same matrices the VFX gets, and compares the clouds pairwise (share of points within 10 cm, median distance, mean offset). |

### Setup

1. **GameObject > Metavido > Aligned Metavido Session**, then with the session selected, **GameObject > Metavido > Aligned Metavido Clip** once per clip. Each clip gets its `VideoPlayer`, decoder, demuxer, feeder and a `VFX` child with the binder, already wired.
2. On the session set the data folder (relative to the project folder, or absolute). On each clip set the video and sidecar paths (relative to that folder) and the time offset: seconds its t=0 came after the master clip's t=0. Use the clip with audio as the master.
3. Assign a VFX Graph to each clip's `VFX` child. It needs the exposed properties `ColorMap`, `DepthMap`, `RayParams`, `InverseView` and `DepthRange`, as for Keijiro's binder.

From editor scripts, `AlignedMetavidoSetup.CreateClip(...)` and `AddToSession(...)` build the same objects.

### Notes

- **Keep the Editor in front while playing.** In the background Unity throttles video decoding and playback nearly stops; the clips stay in sync and resume when it is in front again.
- Keyboard shortcuts (space, arrows, 1–9, O) need the Input System package; everything else works without it.
- Frame rates may vary and differ between phones; sync is on `VideoPlayer.time`, not frame index.

## Install

Add to `Packages/manifest.json` (pin a tag):

```json
"com.skote.metavido-extensions": "https://github.com/skoteskote/skote-metavido-extensions.git#v0.1.0"
```
Keijiro's packages come from his npm registry, so the project's `Packages/manifest.json` needs this scoped registry:

```json
"scopedRegistries": [
  { "name": "Keijiro", "url": "https://registry.npmjs.com", "scopes": [ "jp.keijiro" ] }
]
```

## Editing from any project

Clone the repo into the project's `Packages/` folder. Unity uses the embedded copy in place of the manifest entry, so you can edit, commit and push from there:

```
git clone git@github.com:skoteskote/skote-metavido-extensions.git Packages/com.skote.metavido-extensions
```

Add `Packages/com.skote.metavido-extensions` to the project's Plastic `ignore.conf` (or `.gitignore`). When you're done, tag a release and bump the `#tag` in the manifest.

## License

MIT. See [LICENSE.md](LICENSE.md).
