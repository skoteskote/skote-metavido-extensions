# Changelog

## [0.2.0] - 2026-10-06

- Alignment (`Skote.MetavidoExtensions.Alignment`): plays several Metavido clips from different phones together in one shared marker space, in sync.
  - `AlignedMetavidoClip`, `AlignedMetavidoSession`, `VFXMetavidoMarkerSpaceBinder`, `MetavidoSidecar`, `AlignmentCheck`.
  - Editor: GameObject > Metavido > Aligned Metavido Session / Clip, and the `AlignedMetavidoSetup` helper for scripted scene building.
- The assembly now references the VFX Graph runtime and, optionally, the Input System (for the session's keyboard shortcuts).

## [0.1.0] - 2026-10-05

- Initial extraction from the Volumetrics project.
