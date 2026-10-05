using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Skote.MetavidoExtensions
{
    [System.Serializable]
    [CreateAssetMenu(fileName = "VideoClipSignal", menuName = "Timeline/Video Clip Signal")]
    public class VideoClipSignal : SignalAsset
    {
        [Tooltip("Name of the video clip to play")]
        public string clipName;
    }
}
