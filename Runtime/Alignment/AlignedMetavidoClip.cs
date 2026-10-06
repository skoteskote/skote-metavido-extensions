using System.Collections;
using System.Collections.Generic;
using System.IO;
using Metavido.Common;
using Metavido.Decoder;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.VFX;

namespace Skote.MetavidoExtensions.Alignment
{
    // One Metavido clip placed in shared marker space and slaved to an AlignedMetavidoSession clock.
    // Plays from a file URL (never a transcoded VideoClip: a rescaled frame decodes to a wrong pose).
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class AlignedMetavidoClip : MonoBehaviour
    {
        [Header("Source (paths relative to the session's data folder)")]
        [SerializeField] string _videoFile = "";
        [SerializeField] string _sidecarFile = "";

        [Header("Time")]
        [Tooltip("Seconds this clip's t=0 came after the master clip's t=0. At master time T the clip plays at T - offset.")]
        [SerializeField] double _timeOffset = 0;
        [Tooltip("The first frames carry the pose from before the take's origin reset.")]
        [SerializeField] float _skipStart = 0.35f;
        [SerializeField] bool _playAudio = false;

        [Header("Pose")]
        [Tooltip("For a static phone: use the median camera pose over the window below (captured at startup) instead of the per-frame pose.")]
        [SerializeField] bool _freezePose = false;
        [SerializeField] float _freezeWindowStart = 0.5f;
        [SerializeField] float _freezeWindowLength = 3;

        [Header("Manual nudge (marker space, applied after the marker transform)")]
        [SerializeField] Vector3 _nudgePosition = Vector3.zero;
        [SerializeField] Vector3 _nudgeRotation = Vector3.zero;

        [Header("Scene references")]
        [SerializeField] MetadataDecoder _decoder = null;
        [SerializeField] TextureDemuxer _demuxer = null;
        [SerializeField] VisualEffect _visualEffect = null;
        [SerializeField] bool _visible = true;

        public MetadataDecoder Decoder => _decoder;
        public TextureDemuxer Demuxer => _demuxer;
        public double TimeOffset { get => _timeOffset; set => _timeOffset = value; }
        public bool FreezePose { get => _freezePose; set => _freezePose = value; }
        public bool HasFrozenPose => _frozenPose.HasValue;
        public bool Visible { get => _visible; set => _visible = value; }
        public Vector3 NudgePosition { get => _nudgePosition; set => _nudgePosition = value; }
        public Vector3 NudgeRotation { get => _nudgeRotation; set => _nudgeRotation = value; }
        public VideoPlayer Player => _player;
        public bool IsReady { get; private set; }
        public bool Failed { get; private set; }
        public bool IsSeeking => _seeking;
        public double Length { get; private set; }
        public double TargetTime { get; private set; }
        public double SyncError { get; private set; }
        public bool InRange { get; private set; }
        public string MarkerSource { get; private set; } = "";
        public string Status { get; private set; } = "";

        // Set by the session: marker space to display space.
        public Matrix4x4 DisplayMatrix { get; set; } = Matrix4x4.identity;

        VideoPlayer _player;
        Matrix4x4 _worldToMarker = Matrix4x4.identity;
        Matrix4x4? _frozenPose;
        bool _seeking;
        double _lastSeekTarget = double.NaN;
        float _seekStartedAt;
        float _settleUntil;
        bool _needSeek;
        float _nextSpeedUpdateAt;

        #region Pose

        // Replacement for RenderUtils.InverseView(meta): camera to display space via marker space.
        public Matrix4x4 InverseView(in Metadata meta)
        {
            var cameraToWorld = _freezePose && _frozenPose.HasValue
                ? _frozenPose.Value
                : RenderUtils.InverseView(meta);
            var nudge = Matrix4x4.TRS(_nudgePosition, Quaternion.Euler(_nudgeRotation), Vector3.one);
            return DisplayMatrix * nudge * _worldToMarker * cameraToWorld;
        }

        // Frames before the origin reset and frames outside the clip are not drawn.
        public bool ShouldDraw => _visible && IsReady && InRange && !_seeking && TargetTime >= _skipStart;

        #endregion

        #region Setup

        public IEnumerator Prepare(string dataFolder)
        {
            IsReady = false;
            Failed = false;
            _player = GetComponent<VideoPlayer>();

            var sidecarPath = Path.Combine(dataFolder, _sidecarFile);
            if (!File.Exists(sidecarPath))
            {
                Status = $"Sidecar not found: {sidecarPath}";
                Debug.LogError($"[{name}] {Status}", this);
                Failed = true;
                yield break;
            }
            var sidecar = MetavidoSidecar.Load(sidecarPath);
            if (!sidecar.TryGetMarkerPose(out var markerPos, out var markerRot, out var source))
            {
                Status = "Sidecar has no marker pose";
                Debug.LogError($"[{name}] {Status}", this);
                Failed = true;
                yield break;
            }
            _worldToMarker = Matrix4x4.TRS(markerPos, markerRot, Vector3.one).inverse;
            MarkerSource = source;
            Debug.Log($"[{name}] Marker from {source}; {sidecar.sightings.Count} sightings" +
                      (sidecar.footer == null ? ", no footer (crashed take)" : "") +
                      (sidecar.skippedLines > 0 ? $", {sidecar.skippedLines} unparsable lines skipped" : ""), this);

            var videoPath = Path.Combine(dataFolder, _videoFile);
            if (!File.Exists(videoPath))
            {
                Status = $"Video not found: {videoPath}";
                Debug.LogError($"[{name}] {Status}", this);
                Failed = true;
                yield break;
            }

            _player.source = VideoSource.Url;
            _player.url = videoPath;
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.waitForFirstFrame = true;
            _player.renderMode = VideoRenderMode.APIOnly;
            _player.audioOutputMode = _playAudio ? VideoAudioOutputMode.Direct : VideoAudioOutputMode.None;
            _player.seekCompleted -= OnSeekCompleted;
            _player.seekCompleted += OnSeekCompleted;

            Status = "Preparing";
            _player.Prepare();
            while (!_player.isPrepared) yield return null;
            Length = _player.length;

            // Captured for every clip so freeze can be toggled live.
            yield return CaptureFrozenPose();

            IsReady = true;
            Status = "Ready";
        }

        // Plays the freeze window muted and keeps the median of the decoded camera poses.
        IEnumerator CaptureFrozenPose()
        {
            Status = "Capturing freeze pose";
            var start = Mathf.Max(_freezeWindowStart, _skipStart);
            var end = start + _freezeWindowLength;
            if (_playAudio) _player.SetDirectAudioMute(0, true);

            yield return SeekAndWait(start);
            _player.Play();

            var positions = new List<Vector3>();
            var rotations = new List<Quaternion>();
            var lastCount = _decoder.DecodeCount;
            while (_player.time < end && _player.isPlaying)
            {
                yield return null;
                if (_decoder.DecodeCount == lastCount) continue;
                lastCount = _decoder.DecodeCount;
                var meta = _decoder.Metadata;
                // Async decoding lags a few frames; skip anything that could predate the seek.
                if (!meta.IsValid || _player.time < start + 0.25) continue;
                positions.Add(meta.CameraPosition);
                rotations.Add(meta.CameraRotation);
            }
            _player.Pause();
            if (_playAudio) _player.SetDirectAudioMute(0, false);

            if (positions.Count == 0)
            {
                Debug.LogWarning($"[{name}] Freeze pose: no valid frames in {start:0.00}-{end:0.00} s; using per-frame poses", this);
                yield break;
            }
            var p = PoseMath.MedianPosition(positions);
            var r = PoseMath.AverageRotation(rotations);
            _frozenPose = Matrix4x4.TRS(p, r, Vector3.one);

            var dev = positions.ConvertAll(x => Vector3.Distance(x, p));
            dev.Sort();
            Debug.Log($"[{name}] Freeze pose from {positions.Count} frames ({start:0.00}-{end:0.00} s); " +
                      $"distance from median: p50 {dev[dev.Count / 2] * 1000:0} mm, p90 {dev[dev.Count * 9 / 10] * 1000:0} mm, " +
                      $"max {dev[dev.Count - 1] * 1000:0} mm", this);
        }

        IEnumerator SeekAndWait(double time)
        {
            Seek(time);
            var timeout = Time.realtimeSinceStartup + 10;
            while (_seeking && Time.realtimeSinceStartup < timeout) yield return null;
            if (_seeking) EndSeek();
        }

        void OnSeekCompleted(VideoPlayer _) => EndSeek();

        void Seek(double time)
        {
            _seeking = true;
            _seekStartedAt = Time.realtimeSinceStartup;
            _lastSeekTarget = time;
            _player.time = time;
        }

        // VideoPlayer.time still reads the old position for a moment after seekCompleted. Judging
        // the error from it then triggers another seek, and seeking every frame wedges the player.
        void EndSeek()
        {
            _seeking = false;
            _settleUntil = Time.realtimeSinceStartup + SettleSeconds;
        }

        bool Settling => Time.realtimeSinceStartup < _settleUntil;

        #endregion

        #region Sync (called by the session every frame)

        const double SeekThreshold = 0.5;   // seconds of error before a hard seek
        const double SpeedGain = 0.5;       // proportional speed correction per second of error
        const double MaxSpeedCorrection = 0.1;
        const float SpeedUpdateInterval = 0.25f;
        const float SeekTimeout = 5;
        const float SettleSeconds = 1;

        public void Sync(double masterTime, bool playing, bool isMaster, bool forceSeek)
        {
            if (!IsReady) return;

            TargetTime = masterTime - _timeOffset;
            InRange = TargetTime >= 0 && TargetTime < Length;
            if (forceSeek) _needSeek = true;
            if (_seeking && Time.realtimeSinceStartup - _seekStartedAt > SeekTimeout) EndSeek();

            if (!InRange)
            {
                if (_player.isPlaying) _player.Pause();
                SyncError = 0;
            }
            else if (_needSeek || !playing)
            {
                // Scrubbing or paused: follow the target, one seek at a time.
                if (_player.isPlaying && !playing) _player.Pause();
                if (!_seeking && (_needSeek || !(System.Math.Abs(_lastSeekTarget - TargetTime) <= 1e-4)))
                {
                    Seek(TargetTime);
                    _needSeek = false;
                }
                SyncError = 0;
            }
            else if (_seeking)
            {
                SyncError = 0;
            }
            else if (!_player.isPlaying)
            {
                if (!Settling && System.Math.Abs(_player.time - TargetTime) > SeekThreshold) Seek(TargetTime);
                _player.Play();
                SyncError = 0;
            }
            else if (Settling)
            {
                SyncError = 0;
            }
            else
            {
                // The master's clock normally is master time, so its error only grows if it jumped.
                SyncError = _player.time - TargetTime;
                if (System.Math.Abs(SyncError) > SeekThreshold)
                {
                    Seek(TargetTime);
                    _player.playbackSpeed = 1;
                }
                else if (!isMaster && Time.realtimeSinceStartup >= _nextSpeedUpdateAt)
                {
                    // Rate changes are expensive for the platform player; a few per second is plenty.
                    _nextSpeedUpdateAt = Time.realtimeSinceStartup + SpeedUpdateInterval;
                    var correction = System.Math.Clamp(-SyncError * SpeedGain, -MaxSpeedCorrection, MaxSpeedCorrection);
                    var speed = (float)(1 + correction);
                    if (Mathf.Abs(speed - _player.playbackSpeed) > 0.002f) _player.playbackSpeed = speed;
                }
            }

            if (_visualEffect != null && _visualEffect.enabled != ShouldDraw)
                _visualEffect.enabled = ShouldDraw;
        }

        // The master clip's own clock, when it is a trustworthy source of master time: not around
        // a seek, and consistent with master time so a player that jumped cannot drag the session.
        public bool TryGetMasterTime(out double masterTime)
        {
            masterTime = _player != null ? _player.time + _timeOffset : 0;
            return IsReady && InRange && _player.isPlaying && !_seeking && !Settling &&
                   System.Math.Abs(_player.time - TargetTime) < SeekThreshold;
        }

        #endregion

        void OnDestroy()
        {
            if (_player != null) _player.seekCompleted -= OnSeekCompleted;
        }
    }
}
