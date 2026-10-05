using UnityEngine;
using UnityEngine.Video;

namespace Skote.MetavidoExtensions
{
    [CreateAssetMenu(fileName = "RenderClipSettings", menuName = "Video/Render Clip Settings")]
    public class RenderClipSettings : ScriptableObject
    {
        [Header("Basic Settings")]
        [SerializeField] private string clipName;
        [SerializeField] private VideoClip videoClip;
        [Range(0.1f, 3f)]
        [SerializeField] private float speed = 1f;
        [Tooltip("Starting point in seconds")]
        [SerializeField] private float startingPoint = 0f;

        [Header("Individual Clip Rate Control")]
        [Tooltip("Delay in seconds before rate begins to ramp up")]
        [SerializeField] private float rateDelay = 0f;
        [Tooltip("Duration in seconds at full rate (between ramp up and ramp down)")]
        [SerializeField] private float rateDuration = 10f;
        [SerializeField] private float depthOffset = 0f; // Offset for depth in rendering

        // Public getters
        public string ClipName => clipName;
        public VideoClip VideoClip => videoClip;
        public float Speed => speed;
        public float StartingPoint => startingPoint;
        public float RateDelay => rateDelay;
        public float RateDuration => rateDuration;
        public float DepthOffset => depthOffset;

        /// <summary>
        /// Calculate total clip duration for individual playback mode
        /// </summary>
        /// <param name="startRampDuration">Duration of start ramp (from controller)</param>
        /// <param name="offRampDuration">Duration of off ramp (from controller)</param>
        /// <returns>Total duration including particle death time</returns>
        public float GetTotalIndividualDuration(float startRampDuration, float offRampDuration)
        {
            return rateDelay + startRampDuration + rateDuration + offRampDuration + 5f; // +5s for particle death
        }

        /// <summary>
        /// Get the duration at which rate ramp down begins
        /// </summary>
        /// <param name="startRampDuration">Duration of start ramp (from controller)</param>
        /// <returns>Time when ramp down should start</returns>
        public float GetRampDownStartTime(float startRampDuration)
        {
            return rateDelay + startRampDuration + rateDuration;
        }

        /// <summary>
        /// Validate the timing settings
        /// </summary>
        /// <returns>True if settings are valid</returns>
        public bool ValidateTimings()
        {
            if (rateDelay < 0f)
            {
                Debug.LogWarning($"RenderClipSettings '{name}': rateDelay cannot be negative");
                return false;
            }

            if (rateDuration <= 0f)
            {
                Debug.LogWarning($"RenderClipSettings '{name}': rateDuration must be greater than 0");
                return false;
            }

            if (videoClip == null)
            {
                Debug.LogWarning($"RenderClipSettings '{name}': no video clip assigned");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Copy settings from another RenderClipSettings (useful for creating variants)
        /// </summary>
        public void CopyFrom(RenderClipSettings other)
        {
            if (other == null) return;

            clipName = other.clipName;
            videoClip = other.videoClip;
            speed = other.speed;
            startingPoint = other.startingPoint;
            rateDelay = other.rateDelay;
            rateDuration = other.rateDuration;

            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Get formatted timing info for editor display
        /// </summary>
        public string GetTimingInfo(float startRampDuration, float offRampDuration)
        {
            float totalDuration = GetTotalIndividualDuration(startRampDuration, offRampDuration);
            return $"Delay: {rateDelay}s, Duration: {rateDuration}s, Total: {totalDuration:F1}s";
        }

        private void OnValidate()
        {
            // Ensure positive values
            if (rateDelay < 0f) rateDelay = 0f;
            if (rateDuration < 0f) rateDuration = 0f;
            if (speed < 0.1f) speed = 0.1f;
            if (startingPoint < 0f) startingPoint = 0f;

            // Auto-generate clip name from video clip if empty
            if (string.IsNullOrEmpty(clipName) && videoClip != null)
            {
                clipName = videoClip.name;
            }
        }
        #endif
    }
}
