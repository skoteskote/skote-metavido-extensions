using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Timeline;
using UnityEngine.Playables;
using UnityEngine.VFX;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Skote.MetavidoExtensions
{
    public enum PlaybackSource
    {
        SimpleList,
        ClipCollection,
        IndividualClip
    }

    [RequireComponent(typeof(VideoPlayer))]
    public class MetavidoManager : MonoBehaviour, INotificationReceiver
    {
        [Header("Playback Source")]
        [SerializeField] private PlaybackSource source = PlaybackSource.SimpleList;

        [Tooltip("Simple list of video clips (SimpleList mode)")]
        [SerializeField] private List<VideoClip> simpleClips = new List<VideoClip>();

        [Tooltip("Collections of clips with metadata (ClipCollection mode)")]
        [SerializeField] private List<VideoClipCollection> clipCollections = new List<VideoClipCollection>();

        [Tooltip("Single clip render settings (IndividualClip mode)")]
        [SerializeField] private RenderClipSettings renderClipSettings;

        [Header("Playback Settings")]
        [SerializeField] private bool autoStart = false;
        [SerializeField] private bool loopSequence = true;
        [SerializeField] private CameraOffsetManager _offsetManager;

        [Header("Advanced - Rate Ramping (IndividualClip mode only)")]
        [SerializeField] private VisualEffect vfxGraph;
        [SerializeField] private float startRampDuration = 1f;
        [SerializeField] private float offRampDuration = 1f;
        [SerializeField] private AnimationCurve rampCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private string rateMultiplierProperty = "RateMultiplier";
        [SerializeField] private float longestParticleLifetime = 5f;

        [Header("Advanced - Editor")]
        [SerializeField] private AudioClip exitSound;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private bool endPlayOnExit = false;

        // Runtime state
        private VideoPlayer videoPlayer;
        private int currentClipIndex = -1;
        private bool isPlaying = false;
        private int currentCollectionIndex = 0;
        private PlayableVideoClip currentPlayableClip;
        private Coroutine playbackCoroutine;

        // Public properties
        public int CurrentClipIndex => currentClipIndex;
        public int CurrentCollectionIndex => currentCollectionIndex;
        public int CollectionCount => clipCollections.Count;
        public bool IsPlaying => isPlaying;
        public PlaybackSource Source => source;

        private VideoClipCollection ActiveCollection =>
            clipCollections.Count > 0 && currentCollectionIndex >= 0 && currentCollectionIndex < clipCollections.Count
                ? clipCollections[currentCollectionIndex]
                : null;

        public string CurrentClipName
        {
            get
            {
                if (currentClipIndex < 0) return "None";
                return source switch
                {
                    PlaybackSource.SimpleList when currentClipIndex < simpleClips.Count
                        => simpleClips[currentClipIndex]?.name ?? "None",
                    PlaybackSource.ClipCollection => currentPlayableClip?.name ?? "None",
                    PlaybackSource.IndividualClip => renderClipSettings?.ClipName ?? "None",
                    _ => "None"
                };
            }
        }

        public int ClipCount
        {
            get
            {
                return source switch
                {
                    PlaybackSource.SimpleList => simpleClips.Count,
                    PlaybackSource.ClipCollection => ActiveCollection?.Count ?? 0,
                    PlaybackSource.IndividualClip => 1,
                    _ => 0
                };
            }
        }

        private void Awake()
        {
            videoPlayer = GetComponent<VideoPlayer>();
            videoPlayer.isLooping = false;
            videoPlayer.loopPointReached += OnVideoLoopPointReached;
        }

        private void Start()
        {
            if (autoStart)
            {
                if (source == PlaybackSource.IndividualClip)
                {
                    StartIndividualClipPlayback();
                }
                else
                {
                    PlayClipAtIndex(0);
                }
            }
        }

        private void OnDestroy()
        {
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= OnVideoLoopPointReached;
            }
        }

        #region Public Navigation API

        public void NextClip()
        {
            if (source == PlaybackSource.IndividualClip) return;

            int nextIndex = currentClipIndex + 1;
            if (nextIndex >= ClipCount)
            {
                nextIndex = loopSequence ? 0 : ClipCount - 1;
            }
            PlayClipAtIndex(nextIndex);
        }

        public void PreviousClip()
        {
            if (source == PlaybackSource.IndividualClip) return;

            int prevIndex = currentClipIndex - 1;
            if (prevIndex < 0)
            {
                prevIndex = loopSequence ? ClipCount - 1 : 0;
            }
            PlayClipAtIndex(prevIndex);
        }

        public void RandomClip()
        {
            if (source == PlaybackSource.IndividualClip) return;
            if (ClipCount <= 1) return;

            int randomIndex;
            do
            {
                randomIndex = Random.Range(0, ClipCount);
            } while (randomIndex == currentClipIndex && ClipCount > 1);

            PlayClipAtIndex(randomIndex);
        }

        public void NextCollection()
        {
            if (source != PlaybackSource.ClipCollection || clipCollections.Count <= 1) return;

            if (currentClipIndex >= 0 && _offsetManager != null)
                _offsetManager.OnClipChanged();

            currentCollectionIndex = (currentCollectionIndex + 1) % clipCollections.Count;
            currentClipIndex = -1;
            PlayClipAtIndex(0);
            Debug.Log($"[MetavidoManager] Switched to collection {currentCollectionIndex}: '{ActiveCollection?.name}'");
        }

        public void PreviousCollection()
        {
            if (source != PlaybackSource.ClipCollection || clipCollections.Count <= 1) return;

            if (currentClipIndex >= 0 && _offsetManager != null)
                _offsetManager.OnClipChanged();

            currentCollectionIndex = (currentCollectionIndex - 1 + clipCollections.Count) % clipCollections.Count;
            currentClipIndex = -1;
            PlayClipAtIndex(0);
            Debug.Log($"[MetavidoManager] Switched to collection {currentCollectionIndex}: '{ActiveCollection?.name}'");
        }

        public void SetCollection(int index)
        {
            if (source != PlaybackSource.ClipCollection) return;
            if (index < 0 || index >= clipCollections.Count) return;

            if (currentClipIndex >= 0 && _offsetManager != null)
                _offsetManager.OnClipChanged();

            currentCollectionIndex = index;
            currentClipIndex = -1;
            PlayClipAtIndex(0);
        }

        public bool SetCollectionByName(string collectionName)
        {
            if (source != PlaybackSource.ClipCollection) return false;

            for (int i = 0; i < clipCollections.Count; i++)
            {
                if (clipCollections[i] != null && clipCollections[i].name == collectionName)
                {
                    SetCollection(i);
                    return true;
                }
            }

            Debug.LogWarning($"[MetavidoManager] Collection '{collectionName}' not found");
            return false;
        }

        public void PlayClipAtIndex(int index)
        {
            if (source == PlaybackSource.IndividualClip)
            {
                StartIndividualClipPlayback();
                return;
            }

            if (index < 0 || index >= ClipCount) return;

            // Handle skip flags in ClipCollection mode
            if (source == PlaybackSource.ClipCollection)
            {
                index = FindNextValidClip(index);
                if (index == -1)
                {
                    Debug.Log("[MetavidoManager] No valid clips found");
                    Stop();
                    return;
                }
            }

            // Notify offset manager for smooth camera transition (but not on first clip)
            if (currentClipIndex >= 0 && _offsetManager != null)
            {
                _offsetManager.OnClipChanged();
            }

            currentClipIndex = index;
            isPlaying = true;

            if (source == PlaybackSource.SimpleList)
            {
                PlaySimpleClip(index);
            }
            else if (source == PlaybackSource.ClipCollection)
            {
                PlayCollectionClip(index);
            }
        }

        public void PlayClip(string clipName)
        {
            if (source != PlaybackSource.ClipCollection || ActiveCollection == null) return;

            int index = ActiveCollection.GetClipIndex(clipName);
            if (index >= 0)
            {
                PlayClipAtIndex(index);
            }
        }

        public void Play()
        {
            if (source == PlaybackSource.IndividualClip)
            {
                StartIndividualClipPlayback();
            }
            else if (currentClipIndex < 0)
            {
                PlayClipAtIndex(0);
            }
            else
            {
                PlayClipAtIndex(currentClipIndex);
            }
        }

        public void Stop()
        {
            isPlaying = false;
            videoPlayer.Stop();

            if (playbackCoroutine != null)
            {
                StopCoroutine(playbackCoroutine);
                playbackCoroutine = null;
            }
        }

        #endregion

        #region SimpleList Mode

        private void PlaySimpleClip(int index)
        {
            if (index < 0 || index >= simpleClips.Count) return;
            var clip = simpleClips[index];
            if (clip == null) return;

            videoPlayer.clip = clip;
            videoPlayer.isLooping = false;
            videoPlayer.Play();
            videoPlayer.playbackSpeed = 1f;
            videoPlayer.time = 0;

            Debug.Log($"[MetavidoManager] Playing simple clip {index}: '{clip.name}'");
        }

        #endregion

        #region ClipCollection Mode

        private void PlayCollectionClip(int index)
        {
            var collection = ActiveCollection;
            if (collection == null) return;

            var clipToPlay = collection.GetClip(index);
            if (clipToPlay == null || clipToPlay.videoClip == null) return;

            currentPlayableClip = clipToPlay;

            videoPlayer.clip = clipToPlay.videoClip;
            videoPlayer.isLooping = false;
            videoPlayer.Play();
            videoPlayer.playbackSpeed = clipToPlay.speed;
            videoPlayer.time = clipToPlay.startingPoint > 0f ? clipToPlay.startingPoint : 0;

            Debug.Log($"[MetavidoManager] Playing collection clip {index}: '{clipToPlay.name}' (speed={clipToPlay.speed}) from '{collection.name}'");
        }

        private int FindNextValidClip(int startIndex)
        {
            var collection = ActiveCollection;
            if (collection == null) return -1;

            int attempts = 0;
            int maxAttempts = collection.Count;

            while (attempts < maxAttempts)
            {
                if (startIndex >= collection.Count)
                {
                    if (loopSequence)
                        startIndex = 0;
                    else
                        return -1;
                }

                var clip = collection.GetClip(startIndex);
                if (clip != null && clip.videoClip != null && !clip.skip)
                {
                    return startIndex;
                }

                startIndex++;
                attempts++;
            }

            return -1;
        }

        #endregion

        #region IndividualClip Mode

        public void StartIndividualClipPlayback()
        {
            if (renderClipSettings == null || renderClipSettings.VideoClip == null) return;

            if (_offsetManager != null)
            {
                _offsetManager.SetOffsetEnabled(false);
            }

            if (playbackCoroutine != null)
            {
                StopCoroutine(playbackCoroutine);
            }

            isPlaying = true;
            playbackCoroutine = StartCoroutine(IndividualPlaybackCoroutine());
        }

        private IEnumerator IndividualPlaybackCoroutine()
        {
            currentClipIndex = 0;

            videoPlayer.clip = renderClipSettings.VideoClip;
            videoPlayer.isLooping = false;

            if (vfxGraph != null)
            {
                vfxGraph.SetFloat(rateMultiplierProperty, 0f);
                vfxGraph.SetFloat("DepthOffset", renderClipSettings.DepthOffset);
            }

            videoPlayer.Play();
            videoPlayer.playbackSpeed = renderClipSettings.Speed;
            videoPlayer.time = renderClipSettings.StartingPoint > 0f ? renderClipSettings.StartingPoint : 0;

            if (renderClipSettings.RateDelay > 0f)
                yield return new WaitForSeconds(renderClipSettings.RateDelay);

            yield return StartCoroutine(RampRate(0f, 1f, startRampDuration));

            if (renderClipSettings.RateDuration > 0f)
                yield return new WaitForSeconds(renderClipSettings.RateDuration);

            yield return StartCoroutine(RampRate(1f, 0f, offRampDuration));

            yield return new WaitForSeconds(longestParticleLifetime);

    #if UNITY_EDITOR
            if (endPlayOnExit)
            {
                PlayExitSound();
                yield return new WaitForSeconds(exitSound != null ? exitSound.length : 0.1f);
                EditorApplication.isPlaying = false;
            }
    #endif

            isPlaying = false;
            currentClipIndex = -1;
            playbackCoroutine = null;
        }

        private IEnumerator RampRate(float fromRate, float toRate, float duration)
        {
            if (vfxGraph == null || !vfxGraph.HasFloat(rateMultiplierProperty)) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float curveValue = rampCurve.Evaluate(t);
                vfxGraph.SetFloat(rateMultiplierProperty, Mathf.Lerp(fromRate, toRate, curveValue));
                elapsed += Time.deltaTime;
                yield return null;
            }
            vfxGraph.SetFloat(rateMultiplierProperty, toRate);
        }

        private void PlayExitSound()
        {
            if (exitSound == null) return;
            if (audioSource != null)
            {
                audioSource.PlayOneShot(exitSound);
            }
            else
            {
                var tempGO = new GameObject("TempExitAudio");
                var tempSource = tempGO.AddComponent<AudioSource>();
                tempSource.clip = exitSound;
                tempSource.Play();
                Destroy(tempGO, exitSound.length + 0.1f);
            }
        }

        #endregion

        #region Video Events

        private void OnVideoLoopPointReached(VideoPlayer vp)
        {
            if (!isPlaying || source == PlaybackSource.IndividualClip) return;

            int nextIndex = currentClipIndex + 1;
            if (nextIndex >= ClipCount)
            {
                if (loopSequence)
                {
                    nextIndex = 0;
                }
                else
                {
                    Stop();
                    return;
                }
            }

            StartCoroutine(PlayNextClipDelayed(nextIndex));
        }

        private IEnumerator PlayNextClipDelayed(int index)
        {
            yield return new WaitForEndOfFrame();
            PlayClipAtIndex(index);
        }

        #endregion

        #region Timeline Integration

        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (source == PlaybackSource.IndividualClip) return;

            if (notification is VideoClipSignal videoSignal)
            {
                PlayClip(videoSignal.clipName);
            }
            else if (notification is VideoClipMarker videoMarker)
            {
                PlayClip(videoMarker.ClipName);
            }
        }

        #endregion

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (_offsetManager == null)
                _offsetManager = FindObjectOfType<CameraOffsetManager>();
            if (vfxGraph == null)
                vfxGraph = FindObjectOfType<VisualEffect>();
        }
    #endif
    }
}
