using UnityEngine;
using UnityEngine.VFX;
using Metavido.Common;
using Metavido.Decoder;

namespace Skote.MetavidoExtensions
{
    [AddComponentMenu("Metavido/Camera Offset Manager")]
    public class CameraOffsetManager : MonoBehaviour
    {
        [SerializeField] private MetadataDecoder _decoder;
        [SerializeField] private VisualEffect _vfxGraph;
        [SerializeField] private string _inverseViewPropertyName = "OffsetInverseView";
        [SerializeField] private bool _applyOffset = true;
        [SerializeField] private bool _debugLogging = true;
        [SerializeField] private Camera _debugCamera; // Optional: to monitor what the camera is actually doing
        [SerializeField] private OffsetCameraController _cameraController; // Optional: to reset interpolation

        [Header("Origin Reset Detection")]
        [SerializeField] private bool _detectOriginResets = false;
        [Tooltip("Position jump threshold in meters to detect an ARKit origin reset")]
        [SerializeField] private float _originResetThreshold = 5f;

        // Current offset state
        private Vector3 _positionOffset = Vector3.zero;
        private Quaternion _rotationOffset = Quaternion.identity;
        private bool _hasValidOffset = false;

        // Reference frame for offset calculation
        private Vector3 _referencePosition;
        private Quaternion _referenceRotation;
        private bool _hasReference = false;

        // Last known camera state for calculating next offset
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private bool _lastStateValid = false;
        private bool _calculatingOffset = false;

        // Origin reset accumulation
        private Vector3 _accumulatedOriginOffset = Vector3.zero;
        private Vector3 _lastRawPosition;
        private bool _hasLastRawPosition = false;
        private int _originResetCount = 0;

        public Metadata RawMetadata => _decoder?.Metadata ?? default;
        public Vector3 TransformedCameraPosition { get; private set; }
        public Quaternion TransformedCameraRotation { get; private set; }
    // In CameraOffsetManager.cs
    public bool IsCalculatingOffset => _calculatingOffset;
        private void Update()
        {
            if (_decoder == null || !_decoder.Metadata.IsValid)
            {
                TransformedCameraPosition = Vector3.zero;
                TransformedCameraRotation = Quaternion.identity;
                return;
            }

            var rawMetadata = _decoder.Metadata;

            // Detect ARKit origin resets within a single clip
            if (_detectOriginResets)
            {
                DetectOriginReset(rawMetadata.CameraPosition);
            }

            // While calculating offset, hold at the last known good position
            if (_calculatingOffset && _lastStateValid)
            {
                TransformedCameraPosition = _lastCameraPosition;
                TransformedCameraRotation = _lastCameraRotation;
                UpdateVFXGraph();
                return;
            }

            // Apply accumulated origin offset to raw position
            Vector3 correctedPosition = rawMetadata.CameraPosition + _accumulatedOriginOffset;

            if (_applyOffset && _hasValidOffset)
            {
                // Apply rotation first, then position in the rotated coordinate system
                Vector3 rotatedPosition = _rotationOffset * correctedPosition;
                TransformedCameraPosition = rotatedPosition + _positionOffset;
                TransformedCameraRotation = _rotationOffset * rawMetadata.CameraRotation;
            }
            else
            {
                // No offset applied - pass through corrected data
                TransformedCameraPosition = correctedPosition;
                TransformedCameraRotation = rawMetadata.CameraRotation;

                if (_debugLogging && !_hasValidOffset && Time.frameCount % 30 == 0)
                {
                    Debug.Log($"[CameraOffsetManager] No offset applied - using raw data: pos {rawMetadata.CameraPosition}, rot {rawMetadata.CameraRotation.eulerAngles}");
                }
            }

            // Update VFX graph with offset inverse view matrix
            UpdateVFXGraph();

            // Update last known state for future offset calculations (but not during transition)
            if (!_calculatingOffset)
            {
                _lastCameraPosition = TransformedCameraPosition;
                _lastCameraRotation = TransformedCameraRotation;
                _lastStateValid = true;
            }
        }

        private void UpdateVFXGraph()
        {
            if (_vfxGraph != null && !string.IsNullOrEmpty(_inverseViewPropertyName))
            {
                var offsetInverseView = GetOffsetInverseView();
                _vfxGraph.SetMatrix4x4(_inverseViewPropertyName, offsetInverseView);

                if (_debugLogging && _hasValidOffset)
                {
                    // Log occasionally to avoid spam
                    if (Time.frameCount % 60 == 0) // Every 60 frames
                    {
                        Debug.Log($"[CameraOffsetManager] VFX updated with offset matrix. Transform pos: {TransformedCameraPosition}, rot: {TransformedCameraRotation.eulerAngles}");

                        // Debug actual camera position if assigned
                        if (_debugCamera != null)
                        {
                            Debug.Log($"[CameraOffsetManager] Actual camera transform - pos: {_debugCamera.transform.position}, rot: {_debugCamera.transform.rotation.eulerAngles}");
                        }

                        // Check if VFX property exists
                        if (_vfxGraph.HasMatrix4x4(_inverseViewPropertyName))
                        {
                            var retrievedMatrix = _vfxGraph.GetMatrix4x4(_inverseViewPropertyName);
                            Debug.Log($"[CameraOffsetManager] VFX property '{_inverseViewPropertyName}' confirmed set. Matrix: {retrievedMatrix}");
                        }
                        else
                        {
                            Debug.LogError($"[CameraOffsetManager] VFX Graph does not have property '{_inverseViewPropertyName}'!");
                        }
                    }
                }
            }
            else if (_debugLogging && _vfxGraph == null)
            {
                if (Time.frameCount % 300 == 0) // Less frequent warning
                {
                    Debug.LogWarning("[CameraOffsetManager] VFX Graph not assigned!");
                }
            }
        }

        /// <summary>
        /// Call this when switching to a new clip to calculate the offset needed
        /// for seamless transition.
        /// </summary>
        public void OnClipChanged()
        {
            // Capture BEFORE any clip changes happen
            if (_decoder?.Metadata.IsValid == true)
            {
                _lastCameraPosition = TransformedCameraPosition;
                _lastCameraRotation = TransformedCameraRotation;
                _lastStateValid = true;

                if (_debugLogging)
                {
                    Debug.Log($"[CameraOffsetManager] Captured PRE-TRANSITION state - Position: {_lastCameraPosition}, Rotation: {_lastCameraRotation.eulerAngles}");
                }
            }

            _calculatingOffset = true;

            // Reset origin tracking - each clip has its own ARKit origin
            _accumulatedOriginOffset = Vector3.zero;
            _hasLastRawPosition = false;

            // Reset camera controller interpolation
            if (_cameraController != null)
            {
                _cameraController.OnClipChanged();
            }

            // Wait for the actual new clip data
            StartCoroutine(WaitForActualNewClip());
        }

        private System.Collections.IEnumerator WaitForActualNewClip()
        {
            Vector3 oldPos = _decoder.Metadata.CameraPosition;
            Quaternion oldRot = _decoder.Metadata.CameraRotation;

            // Wait until we get genuinely different camera data
            int maxWait = 30;
            int frames = 0;

            while (frames < maxWait)
            {
                yield return null;
                frames++;

                if (_decoder.Metadata.IsValid)
                {
                    float posDiff = Vector3.Distance(_decoder.Metadata.CameraPosition, oldPos);
                    float rotDiff = Quaternion.Angle(_decoder.Metadata.CameraRotation, oldRot);

                    if (posDiff > 0.05f || rotDiff > 5f) // Significant difference indicates new clip
                    {
                        if (_debugLogging)
                            Debug.Log($"[CameraOffsetManager] Detected new clip data after {frames} frames - posDiff: {posDiff}, rotDiff: {rotDiff}");

                        CalculateOffsetImmediate(_decoder.Metadata);
                        yield break;
                    }
                }
            }

            if (_debugLogging)
                Debug.LogWarning($"[CameraOffsetManager] No significant camera change detected after {maxWait} frames");
        }

        // private void CalculateOffsetImmediate(Metadata currentFrame)
        // {
        //     if (_debugLogging)
        //     {
        //         Debug.Log($"[CameraOffsetManager] CalculateOffsetImmediate - Target: {_lastCameraPosition}, {_lastCameraRotation.eulerAngles}");
        //         Debug.Log($"[CameraOffsetManager] CalculateOffsetImmediate - Current: {currentFrame.CameraPosition}, {currentFrame.CameraRotation.eulerAngles}");
        //     }

        //     _positionOffset = _lastCameraPosition - currentFrame.CameraPosition;

        //     // Use the shorter rotation path by negating the target if needed
        //     var targetRot = _lastCameraRotation;
        //     var currentRot = currentFrame.CameraRotation;

        //     if (Quaternion.Dot(targetRot, currentRot) < 0)
        //     {
        //         if (_debugLogging)
        //             Debug.Log("[CameraOffsetManager] Quaternion polarity correction applied to target");
        //         targetRot = new Quaternion(-targetRot.x, -targetRot.y, -targetRot.z, -targetRot.w);
        //     }

        //     _rotationOffset = targetRot * Quaternion.Inverse(currentRot);
        //     _hasValidOffset = true;
        //     _calculatingOffset = false;

        //     // CRITICAL: Force camera controller to reset interpolation immediately
        //     if (_cameraController != null)
        //     {
        //         Vector3 newPos = currentFrame.CameraPosition + _positionOffset;
        //         Quaternion newRot = _rotationOffset * currentFrame.CameraRotation;
        //         _cameraController.ForceResetInterpolation(newPos, newRot);
        //     }

        //     if (_debugLogging)
        //     {
        //         Debug.Log($"[CameraOffsetManager] Calculated offset - Position: {_positionOffset}, Rotation: {_rotationOffset.eulerAngles}");

        //         // Verify the math immediately
        //         Vector3 testPos = currentFrame.CameraPosition + _positionOffset;
        //         Quaternion testRot = _rotationOffset * currentFrame.CameraRotation;
        //         Debug.Log($"[CameraOffsetManager] Verification - Result: {testPos}, {testRot.eulerAngles}");
        //         Debug.Log($"[CameraOffsetManager] Should match target: {_lastCameraPosition}, {_lastCameraRotation.eulerAngles}");

        //         float posDiff = Vector3.Distance(testPos, _lastCameraPosition);
        //         float rotDiff = Quaternion.Angle(testRot, _lastCameraRotation);
        //         Debug.Log($"[CameraOffsetManager] Verification differences - Pos: {posDiff:F6}, Rot: {rotDiff:F6}°");
        //     }
        // }

        // Replace the calculation in CalculateOffsetImmediate()
        private void CalculateOffsetImmediate(Metadata currentFrame)
        {
            if (_debugLogging)
            {
                Debug.Log($"[CameraOffsetManager] CalculateOffsetImmediate - Target: {_lastCameraPosition}, {_lastCameraRotation.eulerAngles}");
                Debug.Log($"[CameraOffsetManager] CalculateOffsetImmediate - Current: {currentFrame.CameraPosition}, {currentFrame.CameraRotation.eulerAngles}");
            }

            // Calculate rotation offset first
            var targetRot = _lastCameraRotation;
            var currentRot = currentFrame.CameraRotation;

            if (Quaternion.Dot(targetRot, currentRot) < 0)
            {
                if (_debugLogging)
                    Debug.Log("[CameraOffsetManager] Quaternion polarity correction applied to target");
                targetRot = new Quaternion(-targetRot.x, -targetRot.y, -targetRot.z, -targetRot.w);
            }

            _rotationOffset = targetRot * Quaternion.Inverse(currentRot);

            // Calculate position offset AFTER applying rotation
            // Use corrected position (raw + accumulatedOriginOffset) to match what Update() applies
            Vector3 correctedPosition = currentFrame.CameraPosition + _accumulatedOriginOffset;
            Vector3 rotatedCurrentPosition = _rotationOffset * correctedPosition;
            _positionOffset = _lastCameraPosition - rotatedCurrentPosition;

            _hasValidOffset = true;
            _calculatingOffset = false;

            // Force camera controller to reset interpolation immediately
            if (_cameraController != null)
            {
                Vector3 newPos = rotatedCurrentPosition + _positionOffset;
                Quaternion newRot = _rotationOffset * currentFrame.CameraRotation;
                _cameraController.ForceResetInterpolation(newPos, newRot);
            }

            if (_debugLogging)
            {
                Debug.Log($"[CameraOffsetManager] Calculated rotation offset: {_rotationOffset.eulerAngles}");
                Debug.Log($"[CameraOffsetManager] Rotated current position: {rotatedCurrentPosition}");
                Debug.Log($"[CameraOffsetManager] Calculated position offset: {_positionOffset}");

                // Verify the math
                Vector3 testPos = rotatedCurrentPosition + _positionOffset;
                Quaternion testRot = _rotationOffset * currentFrame.CameraRotation;
                Debug.Log($"[CameraOffsetManager] Verification - Result: {testPos}, {testRot.eulerAngles}");
                Debug.Log($"[CameraOffsetManager] Should match target: {_lastCameraPosition}, {_lastCameraRotation.eulerAngles}");

                float posDiff = Vector3.Distance(testPos, _lastCameraPosition);
                float rotDiff = Quaternion.Angle(testRot, _lastCameraRotation);
                Debug.Log($"[CameraOffsetManager] Verification differences - Pos: {posDiff:F6}, Rot: {rotDiff:F6}°");
            }
        }

        private void CalculateOffset(Metadata newClipFirstFrame)
        {
            if (_debugLogging)
            {
                Debug.Log($"[CameraOffsetManager] New clip first frame - Position: {newClipFirstFrame.CameraPosition}, Rotation: {newClipFirstFrame.CameraRotation.eulerAngles}");
                Debug.Log($"[CameraOffsetManager] Last known - Position: {_lastCameraPosition}, Rotation: {_lastCameraRotation.eulerAngles}");
                Debug.Log($"[CameraOffsetManager] Quaternion values - Last: {_lastCameraRotation}, New: {newClipFirstFrame.CameraRotation}");
            }

            // Calculate what offset we need to apply to make the new clip
            // start where the previous clip ended
            Vector3 positionDiff = _lastCameraPosition - newClipFirstFrame.CameraPosition;
            Quaternion rotationDiff = _lastCameraRotation * Quaternion.Inverse(newClipFirstFrame.CameraRotation);

            // Check if the differences are significant enough to warrant an offset
            float positionMagnitude = positionDiff.magnitude;
            float rotationAngle = Quaternion.Angle(_lastCameraRotation, newClipFirstFrame.CameraRotation);

            if (_debugLogging)
            {
                Debug.Log($"[CameraOffsetManager] Position difference: {positionDiff} (magnitude: {positionMagnitude:F6})");
                Debug.Log($"[CameraOffsetManager] Rotation angle difference: {rotationAngle:F6} degrees");
                Debug.Log($"[CameraOffsetManager] Rotation difference quaternion: {rotationDiff} (euler: {rotationDiff.eulerAngles})");
            }

            // Apply offset if differences are significant (more than small precision errors)
            if (positionMagnitude > 0.001f || rotationAngle > 0.1f)
            {
                _positionOffset = positionDiff;
                _rotationOffset = rotationDiff;
                _hasValidOffset = true;

                if (_debugLogging)
                {
                    Debug.Log($"[CameraOffsetManager] Applied offset for hash {newClipFirstFrame.Hash} - Position: {_positionOffset}, Rotation: {_rotationOffset.eulerAngles}");

                    // Test the math
                    Vector3 testPos = newClipFirstFrame.CameraPosition + _positionOffset;
                    Quaternion testRot = _rotationOffset * newClipFirstFrame.CameraRotation;
                    Debug.Log($"[CameraOffsetManager] Verification - Should start at Position: {testPos}, Rotation: {testRot.eulerAngles}");
                    Debug.Log($"[CameraOffsetManager] Target was Position: {_lastCameraPosition}, Rotation: {_lastCameraRotation.eulerAngles}");
                }
            }
            else
            {
                // Differences are too small - just use identity offset to avoid precision issues
                _positionOffset = Vector3.zero;
                _rotationOffset = Quaternion.identity;
                _hasValidOffset = false; // Don't apply any offset for tiny differences

                if (_debugLogging)
                {
                    Debug.Log("[CameraOffsetManager] Differences too small, no offset applied - clips are already aligned");
                }
            }

            // Re-enable last state updates
            _calculatingOffset = false;
        }

        private void DetectOriginReset(Vector3 rawPosition)
        {
            if (!_hasLastRawPosition)
            {
                _lastRawPosition = rawPosition;
                _hasLastRawPosition = true;
                return;
            }

            float jump = Vector3.Distance(rawPosition, _lastRawPosition);

            if (jump > _originResetThreshold)
            {
                // ARKit reset detected — the new raw position jumped back near origin.
                // Accumulate the difference so the corrected position stays continuous.
                Vector3 resetOffset = _lastRawPosition - rawPosition;
                _accumulatedOriginOffset += resetOffset;
                _originResetCount++;

                if (_debugLogging)
                {
                    Debug.Log($"[CameraOffsetManager] Origin reset #{_originResetCount} detected! Jump: {jump:F2}m");
                    Debug.Log($"[CameraOffsetManager]   Raw: {_lastRawPosition} -> {rawPosition}");
                    Debug.Log($"[CameraOffsetManager]   Added offset: {resetOffset}, total accumulated: {_accumulatedOriginOffset}");
                }
            }

            _lastRawPosition = rawPosition;
        }

        /// <summary>
        /// Get the inverse view matrix with offset applied.
        /// Use this in your VFX graph instead of the raw decoder data.
        /// </summary>
        public Matrix4x4 GetOffsetInverseView()
        {
            if (!RawMetadata.IsValid)
                return Matrix4x4.identity;

            return Matrix4x4.TRS(TransformedCameraPosition, TransformedCameraRotation, Vector3.one);
        }

        /// <summary>
        /// Reset offset state. Call this if you want to return to original positions.
        /// </summary>
        public void ResetOffset()
        {
            _positionOffset = Vector3.zero;
            _rotationOffset = Quaternion.identity;
            _hasValidOffset = false;
            _lastStateValid = false;
            _accumulatedOriginOffset = Vector3.zero;
            _hasLastRawPosition = false;
            _originResetCount = 0;
        }

        /// <summary>
        /// Enable or disable offset application at runtime.
        /// </summary>
        public void SetOffsetEnabled(bool enabled)
        {
            _applyOffset = enabled;
            if (!enabled)
            {
                _hasValidOffset = false;
            }
        }

        /// <summary>
        /// Manual test - capture current camera state as "last known"
        /// </summary>
        [ContextMenu("Capture Current State")]
        public void CaptureCurrentState()
        {
            if (_decoder != null && _decoder.Metadata.IsValid)
            {
                _lastCameraPosition = _decoder.Metadata.CameraPosition;
                _lastCameraRotation = _decoder.Metadata.CameraRotation;
                _lastStateValid = true;

                if (_debugLogging)
                {
                    Debug.Log($"[CameraOffsetManager] Manually captured state - Position: {_lastCameraPosition}, Rotation: {_lastCameraRotation.eulerAngles}");
                }
            }
        }

        /// <summary>
        /// Manual test - force calculate offset with current metadata
        /// </summary>
        [ContextMenu("Force Calculate Offset")]
        public void ForceCalculateOffset()
        {
            if (_decoder != null && _decoder.Metadata.IsValid && _lastStateValid)
            {
                CalculateOffset(_decoder.Metadata);
            }
            else
            {
                Debug.LogWarning("[CameraOffsetManager] Cannot calculate offset - decoder invalid or no last state");
            }
        }

        [ContextMenu("Test Offset Math")]
        private void TestOffsetMath()
        {
            if (_hasValidOffset && _decoder.Metadata.IsValid)
            {
                var testPos = _decoder.Metadata.CameraPosition + _positionOffset;
                var testRot = _rotationOffset * _decoder.Metadata.CameraRotation;

                Debug.Log($"Raw: {_decoder.Metadata.CameraPosition}, {_decoder.Metadata.CameraRotation.eulerAngles}");
                Debug.Log($"Target: {_lastCameraPosition}, {_lastCameraRotation.eulerAngles}");
                Debug.Log($"Offset: {_positionOffset}, {_rotationOffset.eulerAngles}");
                Debug.Log($"Result: {testPos}, {testRot.eulerAngles}");

                float posDiff = Vector3.Distance(testPos, _lastCameraPosition);
                float rotDiff = Quaternion.Angle(testRot, _lastCameraRotation);
                Debug.Log($"Differences - Pos: {posDiff:F6}, Rot: {rotDiff:F6}°");
            }
        }

        // Debug visualization in Scene view
        private void OnDrawGizmosSelected()
        {
            if (!RawMetadata.IsValid) return;

            // Draw transformed camera position
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(TransformedCameraPosition, Vector3.one * 0.1f);

            // Draw camera forward direction
            Gizmos.color = Color.blue;
            Vector3 forward = TransformedCameraRotation * Vector3.forward;
            Gizmos.DrawRay(TransformedCameraPosition, forward * 0.5f);

            if (_hasValidOffset)
            {
                // Draw offset vector
                Gizmos.color = Color.red;
                Vector3 originalPos = TransformedCameraPosition - _positionOffset;
                Gizmos.DrawLine(originalPos, TransformedCameraPosition);
            }
        }
    }
}
