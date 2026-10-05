using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace Skote.MetavidoExtensions
{
    [CreateAssetMenu(fileName = "VideoClipCollection", menuName = "Video/Video Clip Collection")]
    public class VideoClipCollection : ScriptableObject
    {
        [SerializeField] private List<PlayableVideoClip> videoClips = new List<PlayableVideoClip>();

        public List<PlayableVideoClip> VideoClips => videoClips;
        public int Count => videoClips.Count;

        /// <summary>
        /// Get clip by name
        /// </summary>
        public PlayableVideoClip GetClip(string clipName)
        {
            return videoClips.Find(clip => clip.name == clipName);
        }

        /// <summary>
        /// Get clip by index
        /// </summary>
        public PlayableVideoClip GetClip(int index)
        {
            if (index < 0 || index >= videoClips.Count) return null;
            return videoClips[index];
        }

        /// <summary>
        /// Get index of clip by name
        /// </summary>
        public int GetClipIndex(string clipName)
        {
            return videoClips.FindIndex(clip => clip.name == clipName);
        }

        /// <summary>
        /// Get all clip names
        /// </summary>
        public List<string> GetClipNames()
        {
            List<string> names = new List<string>();
            foreach (var clip in videoClips)
            {
                if (!string.IsNullOrEmpty(clip.name))
                    names.Add(clip.name);
            }
            return names;
        }

        /// <summary>
        /// Check if collection contains a clip with the given name
        /// </summary>
        public bool HasClip(string clipName)
        {
            return videoClips.Exists(clip => clip.name == clipName);
        }

        /// <summary>
        /// Add a new clip (Editor use)
        /// </summary>
        public void AddClip(PlayableVideoClip clip)
        {
            if (clip != null)
            {
                videoClips.Add(clip);
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
                #endif
            }
        }

        /// <summary>
        /// Remove clip by name (Editor use)
        /// </summary>
        public bool RemoveClip(string clipName)
        {
            int index = GetClipIndex(clipName);
            if (index >= 0)
            {
                videoClips.RemoveAt(index);
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
                #endif
                return true;
            }
            return false;
        }

        #if UNITY_EDITOR
        private void OnValidate()
        {
            // Ensure no duplicate names
            HashSet<string> usedNames = new HashSet<string>();
            for (int i = 0; i < videoClips.Count; i++)
            {
                if (string.IsNullOrEmpty(videoClips[i].name))
                    continue;

                string originalName = videoClips[i].name;
                string uniqueName = originalName;
                int counter = 1;

                while (usedNames.Contains(uniqueName))
                {
                    uniqueName = $"{originalName}_{counter}";
                    counter++;
                }

                videoClips[i].name = uniqueName;
                usedNames.Add(uniqueName);
            }
        }
        #endif
    }
}
