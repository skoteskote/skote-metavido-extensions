using UnityEngine;
using Metavido.Common;
using Metavido.Decoder;
using System.Collections;

namespace Skote.MetavidoExtensions
{
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Custom/Offset Camera Controller")]
    public sealed class OffsetCameraController : MonoBehaviour
    {
        #region Scene object references

        [SerializeField] MetadataDecoder _decoder = null;
        [SerializeField] CameraOffsetManager _offsetManager = null;
        [SerializeField] bool _interpolation = false;
        [SerializeField] bool _debugLogging = false;
        [SerializeField] int _transitionFrames = 8; // How many frames to blend between clips
        [SerializeField] AnimationCurve _transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        #endregion

        #region Private members

        Camera _camera;

        // Keyframes for interpolation - use TRANSFORMED coordinates, not raw
        (Vector3 p, Quaternion r, float t, float hash) _key1, _key2;

        // Transition state
        private bool _inTransition = false;
        private int _transitionFrameCount = 0;
        private Vector3 _clipAVelocity = Vector3.zero;
        private Vector3 _clipAAngularVelocity = Vector3.zero;
        private Vector3 _transitionStartPos = Vector3.zero;
        private Quaternion _transitionStartRot = Quaternion.identity;
        private Vector3 _lastTransformedPos = Vector3.zero;
        private Quaternion _lastTransformedRot = Quaternion.identity;

        // Transform update without interpolation
        void UpdateTransform(Vector3 position, Quaternion rotation)
        {
            if (_debugLogging)
            {
                var oldPos = transform.position;
                var oldRot = transform.rotation;

                transform.position = position;
                transform.rotation = rotation;

                float posDiff = Vector3.Distance(oldPos, position);
                float rotDiff = Quaternion.Angle(oldRot, rotation);

                if (posDiff > 0.01f || rotDiff > 1f)
                {
                    Debug.Log($"[OffsetCameraController] Direct transform update - Old: {oldPos}, {oldRot.eulerAngles} -> New: {position}, {rotation.eulerAngles}");
                }
            }
            else
            {
                transform.position = position;
                transform.rotation = rotation;
            }
        }

        // Transform update with interpolation and transition blending
        void UpdateTransformLerped(in Metadata meta, Vector3 transformedPosition, Quaternion transformedRotation)
        {
            // Handle transition blending
            if (_inTransition)
            {
                HandleTransitionBlending(transformedPosition, transformedRotation);
                return;
            }

            // Current time, extrapolated time of next frame
            var (t, nt) = (Time.time, Time.time + Time.deltaTime);

            // Keyframe update - use TRANSFORMED coordinates and actual metadata hash
            if (_key2.hash != meta.Hash)
            {
                if (_debugLogging)
                {
                    Debug.Log($"[OffsetCameraController] Hash changed from {_key2.hash} to {meta.Hash}");
                    Debug.Log($"[OffsetCameraController] Moving Key2 to Key1: {_key2.p}, {_key2.r.eulerAngles}");
                }

                _key1 = _key2;
                _key2 = (transformedPosition, transformedRotation, t, meta.Hash);

                if (_debugLogging)
                {
                    Debug.Log($"[OffsetCameraController] New Key2 set to: {_key2.p}, {_key2.r.eulerAngles}");

                    // Check for large rotation jumps
                    float rotationJump = Quaternion.Angle(_key1.r, _key2.r);
                    if (rotationJump > 90f)
                    {
                        Debug.LogWarning($"[OffsetCameraController] Large rotation jump detected: {rotationJump:F1}° - may cause interpolation artifacts");
                    }
                }
            }

            // Interpolation parameter with edge case handling
            float timeDelta = _key2.t - _key1.t;
            var ip = timeDelta > Time.fixedDeltaTime * 0.5f ? Mathf.Clamp01((nt - _key2.t) / timeDelta) : 0f;

            if (_debugLogging && (ip > 1.1f || ip < -0.1f || timeDelta < 0.001f))
            {
                Debug.LogWarning($"[OffsetCameraController] Interpolation edge case - IP: {ip:F6}, TimeDelta: {timeDelta:F6}, Key1.t: {_key1.t:F6}, Key2.t: {_key2.t:F6}");
            }

            // Transform update using TRANSFORMED coordinates
            var lerpedPos = Vector3.Lerp(_key1.p, _key2.p, ip);
            var lerpedRot = Quaternion.Slerp(_key1.r, _key2.r, ip);

            if (_debugLogging)
            {
                Debug.Log($"[OffsetCameraController] Interpolation - IP: {ip:F3}, Result: {lerpedPos}, {lerpedRot.eulerAngles}");
            }

            UpdateTransform(lerpedPos, lerpedRot);

            // Track last position for velocity calculation
            _lastTransformedPos = lerpedPos;
            _lastTransformedRot = lerpedRot;
        }

        private void HandleTransitionBlending(Vector3 newClipPosition, Quaternion newClipRotation)
        {
            _transitionFrameCount++;

            // Calculate blend factor using the curve
            float blendFactor = _transitionCurve.Evaluate((float)_transitionFrameCount / _transitionFrames);

            // Continue clip A's motion
            Vector3 continuedPosition = _transitionStartPos + _clipAVelocity * (_transitionFrameCount * Time.fixedDeltaTime);

            // For rotation, convert angular velocity to quaternion delta
            Vector3 eulerDelta = _clipAAngularVelocity * (_transitionFrameCount * Time.fixedDeltaTime);
            Quaternion continuedRotation = _transitionStartRot * Quaternion.Euler(eulerDelta);

            // Blend between continued motion and new clip data
            Vector3 blendedPosition = Vector3.Lerp(continuedPosition, newClipPosition, blendFactor);
            Quaternion blendedRotation = Quaternion.Slerp(continuedRotation, newClipRotation, blendFactor);

            if (_debugLogging)
            {
                Debug.Log($"[OffsetCameraController] Transition frame {_transitionFrameCount}/{_transitionFrames}, blend: {blendFactor:F3}");
                Debug.Log($"[OffsetCameraController] Continued: {continuedPosition}, New: {newClipPosition}, Result: {blendedPosition}");
            }

            UpdateTransform(blendedPosition, blendedRotation);

            // End transition
            if (_transitionFrameCount >= _transitionFrames)
            {
                _inTransition = false;
                _transitionFrameCount = 0;

                // Reset keyframes for normal interpolation
                var currentTime = Time.time;
                var currentHash = _offsetManager?.RawMetadata.Hash ?? 0f;
                _key1 = (blendedPosition, blendedRotation, currentTime - Time.fixedDeltaTime, currentHash);
                _key2 = (newClipPosition, newClipRotation, currentTime, currentHash);

                if (_debugLogging)
                {
                    Debug.Log("[OffsetCameraController] Transition completed, resuming normal interpolation");
                }
            }
        }

        // Camera matrix update
        void UpdateCameraMatrix(in Metadata meta)
          => _camera.projectionMatrix =
               meta.ReconstructProjectionMatrix(_camera.projectionMatrix);

        /// <summary>
        /// Call this when a clip changes to start smooth transition
        /// </summary>
        public void OnClipChanged()
        {
            // Calculate velocity from recent motion
            CalculateClipAVelocity();

            // Start transition
            _inTransition = true;
            _transitionFrameCount = 0;
            _transitionStartPos = _lastTransformedPos;
            _transitionStartRot = _lastTransformedRot;

            if (_debugLogging)
            {
                Debug.Log($"[OffsetCameraController] Starting transition from {_transitionStartPos}, velocity: {_clipAVelocity}");
                Debug.Log($"[OffsetCameraController] Angular velocity: {_clipAAngularVelocity}");
            }
        }

        private void CalculateClipAVelocity()
        {
            // Calculate velocity from keyframe data
            float timeDelta = _key2.t - _key1.t;

            if (timeDelta > 0.001f)
            {
                _clipAVelocity = (_key2.p - _key1.p) / timeDelta;

                // Calculate angular velocity (simplified - convert to euler for velocity calculation)
                Vector3 eulerDiff = (_key2.r * Quaternion.Inverse(_key1.r)).eulerAngles;

                // Handle angle wrapping (convert 350° to -10°, etc.)
                if (eulerDiff.x > 180f) eulerDiff.x -= 360f;
                if (eulerDiff.y > 180f) eulerDiff.y -= 360f;
                if (eulerDiff.z > 180f) eulerDiff.z -= 360f;

                _clipAAngularVelocity = eulerDiff / timeDelta;
            }
            else
            {
                // Fallback: no velocity if we don't have good timing data
                _clipAVelocity = Vector3.zero;
                _clipAAngularVelocity = Vector3.zero;
            }

            // Clamp velocities to reasonable values to prevent wild extrapolation
            _clipAVelocity = Vector3.ClampMagnitude(_clipAVelocity, 10f); // Max 10 units/second
            _clipAAngularVelocity = Vector3.ClampMagnitude(_clipAAngularVelocity, 180f); // Max 180°/second
        }

        /// <summary>
        /// Force reset interpolation keyframes to specific transformed position
        /// Called when offset calculation completes to prevent interpolation artifacts
        /// </summary>
        public void ForceResetInterpolation(Vector3 transformedPosition, Quaternion transformedRotation)
        {
            // No longer needed - transition system handles this
            if (_debugLogging)
            {
                Debug.Log($"[OffsetCameraController] ForceResetInterpolation called but using transition system instead");
            }
        }

        #endregion

        #region MonoBehaviour implementation

        void Start()
          => _camera = GetComponent<Camera>();

        void LateUpdate()
        {
            // Use offset manager if available, otherwise fall back to decoder
            if (_offsetManager != null)
            {
                if (!_offsetManager.RawMetadata.IsValid) return;

                var meta = _offsetManager.RawMetadata;
                var transformedPos = _offsetManager.TransformedCameraPosition;
                var transformedRot = _offsetManager.TransformedCameraRotation;

                if (_debugLogging && Time.frameCount % 60 == 0) // Log every 60 frames
                {
                    Debug.Log($"[OffsetCameraController] Frame {Time.frameCount} - Raw: {meta.CameraPosition}, {meta.CameraRotation.eulerAngles}");
                    Debug.Log($"[OffsetCameraController] Frame {Time.frameCount} - Transformed: {transformedPos}, {transformedRot.eulerAngles}");
                    Debug.Log($"[OffsetCameraController] Frame {Time.frameCount} - In transition: {_inTransition}");
                }

                if (_interpolation)
                    UpdateTransformLerped(meta, transformedPos, transformedRot);
                else
                    UpdateTransform(transformedPos, transformedRot);

                UpdateCameraMatrix(meta);
            }
            else
            {
                // Fallback to raw decoder
                var meta = _decoder.Metadata;
                if (!meta.IsValid) return;

                if (_interpolation)
                    UpdateTransformLerped(meta, meta.CameraPosition, meta.CameraRotation);
                else
                    UpdateTransform(meta.CameraPosition, meta.CameraRotation);

                UpdateCameraMatrix(meta);
            }
        }

        #endregion
    }
}
