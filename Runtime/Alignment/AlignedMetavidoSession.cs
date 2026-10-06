using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SKOTE_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Skote.MetavidoExtensions.Alignment
{
    // Plays several AlignedMetavidoClips on one master clock. Master time is the master clip's own
    // video time (offset 0 normally); the other clips follow it by speed correction, or by seeking
    // when they fall too far behind. Variable frame rates are handled by syncing on VideoPlayer.time.
    public sealed class AlignedMetavidoSession : MonoBehaviour
    {
        [Tooltip("Folder holding the clips and sidecars; relative paths are relative to the Unity project folder.")]
        [SerializeField] string _dataFolder = "";
        [SerializeField] List<AlignedMetavidoClip> _clips = new List<AlignedMetavidoClip>();
        [Tooltip("Its video clock drives master time while it is in range. Use the clip with audio.")]
        [SerializeField] AlignedMetavidoClip _masterClip = null;
        [SerializeField] double _startTime = 0;
        [SerializeField] bool _playOnStart = true;

        [Header("Display")]
        [Tooltip("Marker space to display space. Marker +Z is up on a wall marker; (-90, 0, 0) stands the room upright.")]
        [SerializeField] Vector3 _displayRotation = new Vector3(-90, 0, 0);
        [SerializeField] Vector3 _displayOffset = Vector3.zero;
        [SerializeField] bool _showOverlay = true;

        public double MasterTime { get; private set; }
        public bool Playing { get; private set; }
        public bool Ready { get; private set; }
        public IReadOnlyList<AlignedMetavidoClip> Clips => _clips;

        bool _forceSeek;

        public string DataFolderPath =>
            Path.GetFullPath(Path.IsPathRooted(_dataFolder)
                ? _dataFolder
                : Path.Combine(Application.dataPath, "..", _dataFolder));

        public double Duration
        {
            get
            {
                double d = 0;
                foreach (var c in _clips)
                    if (c != null && c.IsReady) d = System.Math.Max(d, c.TimeOffset + c.Length);
                return d;
            }
        }

        public void Play() => Playing = true;
        public void Pause() => Playing = false;

        public void SeekTo(double time)
        {
            MasterTime = System.Math.Clamp(time, 0, System.Math.Max(Duration, 0));
            _forceSeek = true;
        }

        IEnumerator Start()
        {
            var folder = DataFolderPath;
            Debug.Log($"[MetavidoSession] Data folder: {folder}");
            foreach (var clip in _clips)
                if (clip != null) StartCoroutine(clip.Prepare(folder));
            yield return null;
            foreach (var clip in _clips)
                while (clip != null && !clip.IsReady && !clip.Failed)
                    yield return null;

            Ready = true;
            SeekTo(_startTime);
            Playing = _playOnStart;
        }

        void Update()
        {
            HandleKeys();
            if (!Ready) return;

            if (Playing && !_forceSeek)
            {
                if (_masterClip != null && _masterClip.TryGetMasterTime(out var t))
                    MasterTime = t;
                else
                    MasterTime += Time.deltaTime;
                if (MasterTime >= Duration) { MasterTime = Duration; Playing = false; }
            }

            var display = Matrix4x4.TRS(_displayOffset, Quaternion.Euler(_displayRotation), Vector3.one);
            foreach (var clip in _clips)
            {
                if (clip == null) continue;
                clip.DisplayMatrix = display;
                clip.Sync(MasterTime, Playing, clip == _masterClip, _forceSeek);
            }
            _forceSeek = false;
        }

        void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM && SKOTE_HAS_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null || !Ready) return;
            if (kb.spaceKey.wasPressedThisFrame) Playing = !Playing;
            var step = kb.shiftKey.isPressed ? 60 : 5;
            if (kb.rightArrowKey.wasPressedThisFrame) SeekTo(MasterTime + step);
            if (kb.leftArrowKey.wasPressedThisFrame) SeekTo(MasterTime - step);
            if (kb.oKey.wasPressedThisFrame) _showOverlay = !_showOverlay;
            for (var i = 0; i < _clips.Count && i < 9; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame && _clips[i] != null)
                    _clips[i].Visible = !_clips[i].Visible;
#endif
        }

        [ContextMenu("Measure Alignment (current frame)")]
        void MeasureAlignment() => Debug.Log($"[MetavidoSession] Alignment at T {MasterTime:0.00} s\n{AlignmentCheck.Report(this)}");

        // The printed marker (380 mm A3 image) and its axes, in display space. Wall points should
        // lie in the rectangle's plane.
        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(_displayOffset, Quaternion.Euler(_displayRotation), Vector3.one);
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.38f, 0, 0.2687f));
            Gizmos.color = Color.red; Gizmos.DrawLine(Vector3.zero, Vector3.right * 0.2f);
            Gizmos.color = Color.green; Gizmos.DrawLine(Vector3.zero, Vector3.up * 0.2f);
            Gizmos.color = Color.blue; Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.2f);
        }

        #region Overlay

        void OnGUI()
        {
            if (!_showOverlay) return;
            GUILayout.BeginArea(new Rect(10, 10, 560, 60 + 70 * _clips.Count), GUI.skin.box);
            if (!Ready)
            {
                GUILayout.Label("Preparing clips...");
                foreach (var c in _clips) if (c != null) GUILayout.Label($"{c.name}: {c.Status}");
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Playing ? "Pause" : "Play", GUILayout.Width(60))) Playing = !Playing;
            GUILayout.Label($"T = {FormatTime(MasterTime)} / {FormatTime(Duration)}", GUILayout.Width(170));
            GUILayout.Label("space play, ←/→ ±5 s (shift ±60), 1-9 toggle clip, O overlay");
            GUILayout.EndHorizontal();
            var scrub = GUILayout.HorizontalSlider((float)MasterTime, 0, (float)Duration);
            if (Mathf.Abs(scrub - (float)MasterTime) > 0.01f) SeekTo(scrub);

            for (var i = 0; i < _clips.Count; i++)
            {
                var c = _clips[i];
                if (c == null) continue;
                GUILayout.BeginHorizontal();
                c.Visible = GUILayout.Toggle(c.Visible, $"{i + 1} {c.name}", GUILayout.Width(220));
                c.FreezePose = GUILayout.Toggle(c.FreezePose, c.HasFrozenPose ? "freeze" : "freeze (n/a)", GUILayout.Width(100));
                GUILayout.Label(c == _masterClip ? "master" : $"err {c.SyncError * 1000:+0;-0} ms");
                GUILayout.EndHorizontal();
                var state = !c.InRange ? "out of range" : c.IsSeeking ? "seeking" : c.ShouldDraw ? "drawing" : "hidden";
                GUILayout.Label($"   t {FormatTime(c.TargetTime)}  offset {c.TimeOffset:0.000} s  {state}  " +
                                $"fps {c.Player.frameRate:0}  marker: {c.MarkerSource}");
            }
            GUILayout.EndArea();
        }

        static string FormatTime(double t)
        {
            var sign = t < 0 ? "-" : "";
            t = System.Math.Abs(t);
            return $"{sign}{(int)(t / 60)}:{t % 60:00.00}";
        }

        #endregion
    }
}
