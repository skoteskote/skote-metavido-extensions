using Metavido.Decoder;
using Skote.MetavidoExtensions.Alignment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.VFX;
using UnityEngine.VFX.Utility;

namespace Skote.MetavidoExtensions.Editor.Alignment
{
    // Builds the GameObjects for marker-space alignment: a session, and clips with their video
    // player, decoder, demuxer, feeder and a VFX with the marker-space binder, all wired up.
    public static class AlignedMetavidoSetup
    {
        [MenuItem("GameObject/Metavido/Aligned Metavido Session", false, 10)]
        static void CreateSessionMenu(MenuCommand command)
        {
            var go = new GameObject("Aligned Metavido Session");
            go.AddComponent<AlignedMetavidoSession>();
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Aligned Metavido Session");
            Selection.activeGameObject = go;
        }

        // With a session selected, the clip goes under it and joins its clip list.
        [MenuItem("GameObject/Metavido/Aligned Metavido Clip", false, 11)]
        static void CreateClipMenu(MenuCommand command)
        {
            var parent = command.context as GameObject;
            var session = parent != null ? parent.GetComponent<AlignedMetavidoSession>() : null;
            var clip = CreateClip("Aligned Metavido Clip", "", "", 0, null, playAudio: false,
                                  parent != null ? parent.transform : null);
            if (session != null) AddToSession(session, clip, makeMaster: false);
            Undo.RegisterCreatedObjectUndo(clip.gameObject, "Create Aligned Metavido Clip");
            Selection.activeGameObject = clip.gameObject;
        }

        // video/sidecar are relative to the session's data folder; offset is seconds after the
        // master clip's t=0.
        public static AlignedMetavidoClip CreateClip(string name, string videoFile, string sidecarFile,
                                                     double timeOffset, VisualEffectAsset vfxAsset,
                                                     bool playAudio, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);

            var player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.APIOnly;

            var decoder = go.AddComponent<MetadataDecoder>();
            AssignDefaultReference(decoder, "_shader");
            var demuxer = go.AddComponent<TextureDemuxer>();
            AssignDefaultReference(demuxer, "_shader");

            var feeder = go.AddComponent<VideoFeeder>();
            SetReferences(feeder, ("_decoder", decoder), ("_demuxer", demuxer));

            var vfxGo = new GameObject("VFX");
            vfxGo.transform.SetParent(go.transform, false);
            var vfx = vfxGo.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = vfxAsset;
            var binder = vfxGo.AddComponent<VFXPropertyBinder>().AddPropertyBinder<VFXMetavidoMarkerSpaceBinder>();

            var clip = go.AddComponent<AlignedMetavidoClip>();
            var clipSo = new SerializedObject(clip);
            clipSo.FindProperty("_videoFile").stringValue = videoFile;
            clipSo.FindProperty("_sidecarFile").stringValue = sidecarFile;
            clipSo.FindProperty("_timeOffset").doubleValue = timeOffset;
            clipSo.FindProperty("_playAudio").boolValue = playAudio;
            clipSo.ApplyModifiedPropertiesWithoutUndo();
            SetReferences(clip, ("_decoder", decoder), ("_demuxer", demuxer), ("_visualEffect", vfx));
            SetReferences(binder, ("_decoder", decoder), ("_demux", demuxer), ("_alignment", clip));

            return clip;
        }

        public static void AddToSession(AlignedMetavidoSession session, AlignedMetavidoClip clip, bool makeMaster)
        {
            var so = new SerializedObject(session);
            var list = so.FindProperty("_clips");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = clip;
            var master = so.FindProperty("_masterClip");
            if (makeMaster || master.objectReferenceValue == null) master.objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetReferences(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs) so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Metavido's components get their shaders from script default references, which a
        // scripted AddComponent does not always apply.
        static void AssignDefaultReference(MonoBehaviour component, string field)
        {
            var script = MonoScript.FromMonoBehaviour(component);
            var importer = (MonoImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(script));
            SetReferences(component, (field, importer.GetDefaultReference(field)));
        }
    }
}
