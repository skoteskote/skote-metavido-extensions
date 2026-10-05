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
