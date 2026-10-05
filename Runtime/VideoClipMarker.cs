using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.Playables;

namespace Skote.MetavidoExtensions
{
    [System.Serializable]
    [CustomStyle("VideoClipMarker")]
    public class VideoClipMarker : Marker, INotification
    {
        [SerializeField] private string clipName;

        public string ClipName 
        { 
            get => clipName; 
            set => clipName = value; 
        }

        public PropertyName id => new PropertyName("VideoClipMarker");

        // This will be displayed in the Timeline marker
        public override string ToString()
        {
            return string.IsNullOrEmpty(clipName) ? "Video Clip" : $"Play: {clipName}";
        }
    }
}
