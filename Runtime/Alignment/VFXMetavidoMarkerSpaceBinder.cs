using Metavido.Decoder;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;

namespace Skote.MetavidoExtensions.Alignment
{
    // VFXMetavidoBinder with one change: InverseView goes through the clip's marker-space transform
    // (display * nudge * worldToMarker * cameraToWorld) instead of the clip's own ARKit world.
    [AddComponentMenu("VFX/Property Binders/Metavido Marker Space Binder")]
    [VFXBinder("Metavido (Marker Space)")]
    public sealed class VFXMetavidoMarkerSpaceBinder : VFXBinderBase
    {
        [SerializeField] MetadataDecoder _decoder = null;
        [SerializeField] TextureDemuxer _demux = null;
        [SerializeField] AlignedMetavidoClip _alignment = null;

        [VFXPropertyBinding("UnityEngine.Texture2D"), SerializeField]
        ExposedProperty _colorMapProperty = "ColorMap";

        [VFXPropertyBinding("UnityEngine.Texture2D"), SerializeField]
        ExposedProperty _depthMapProperty = "DepthMap";

        [VFXPropertyBinding("UnityEngine.Vector4"), SerializeField]
        ExposedProperty _rayParamsProperty = "RayParams";

        [VFXPropertyBinding("UnityEngine.Matrix4x4"), SerializeField]
        ExposedProperty _inverseViewProperty = "InverseView";

        [VFXPropertyBinding("UnityEngine.Vector2"), SerializeField]
        ExposedProperty _depthRangeProperty = "DepthRange";

        public override bool IsValid(VisualEffect component)
          => _decoder != null && _demux != null && _alignment != null &&
             component.HasTexture(_colorMapProperty) &&
             component.HasTexture(_depthMapProperty) &&
             component.HasVector4(_rayParamsProperty) &&
             component.HasMatrix4x4(_inverseViewProperty) &&
             component.HasVector2(_depthRangeProperty);

        public override void UpdateBinding(VisualEffect component)
        {
            var meta = _decoder.Metadata;
            if (!meta.IsValid || _demux.ColorTexture == null) return;

            component.SetTexture(_colorMapProperty, _demux.ColorTexture);
            component.SetTexture(_depthMapProperty, _demux.DepthTexture);
            component.SetVector4(_rayParamsProperty, RenderUtils.RayParams(meta));
            component.SetMatrix4x4(_inverseViewProperty, _alignment.InverseView(meta));
            component.SetVector2(_depthRangeProperty, meta.DepthRange);
        }

        public override string ToString()
          => $"Metavido (Marker Space) : {_colorMapProperty}, {_depthMapProperty}";
    }
}
