using UnityEngine;
using UnityEngine.Video;

namespace Skote.MetavidoExtensions
{
    [System.Serializable]
    public class PlayableVideoClip
    {
        [Header("Basic Settings")]
        public string name;
        public VideoClip videoClip;
        public float speed = 1f;
        [Tooltip("Starting point in seconds")]
        public float startingPoint = 0f;
        public bool skip = false;
    }
}
