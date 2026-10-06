using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Skote.MetavidoExtensions.Alignment
{
    // Reader for the Meadow recorder's marker sidecar (JSON Lines: header, sightings, footer).
    // Poses are in the clip's world frame (the frame of the barcode camera pose).
    public sealed class MetavidoSidecar
    {
        [Serializable]
        public class Pose
        {
            public float[] p;
            public float[] q;

            public bool IsValid => p != null && p.Length == 3 && q != null && q.Length == 4;
            public Vector3 Position => new Vector3(p[0], p[1], p[2]);
            public Quaternion Rotation => Quaternion.Normalize(new Quaternion(q[0], q[1], q[2], q[3]));
        }

        [Serializable]
        public class MarkerAtStart
        {
            public int samples;
            public double positionRmsMeters;
            public float maxAngleDegrees;
            public Pose pose;
        }

        [Serializable]
        public class Device
        {
            public string model;
            public string name;
        }

        [Serializable]
        public class Header
        {
            public string type;
            public string video;
            public double t;
            public long wallMs;
            public Device device;
            public MarkerAtStart markerAtStart;
        }

        [Serializable]
        public class Sighting
        {
            public string type;
            public float hash;
            public double t;
            public double tVideo;
            public bool stable;
            public Pose marker;
            public Pose camera;
        }

        [Serializable]
        public class Footer
        {
            public string type;
            public double firstFrameT;
            public int sightings;
        }

        [Serializable]
        class LineType
        {
            public string type;
        }

        public Header header;
        public readonly List<Sighting> sightings = new List<Sighting>();
        public Footer footer;
        public int skippedLines;

        public bool HasMarkerAtStart =>
            header?.markerAtStart != null && header.markerAtStart.samples > 0 &&
            header.markerAtStart.pose != null && header.markerAtStart.pose.IsValid;

        public static MetavidoSidecar Load(string path)
        {
            var sidecar = new MetavidoSidecar();
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    // Crashed takes can end in a truncated line; anything unparsable is skipped.
                    switch (JsonUtility.FromJson<LineType>(line).type)
                    {
                        case "header": sidecar.header = JsonUtility.FromJson<Header>(line); break;
                        case "sighting": sidecar.sightings.Add(JsonUtility.FromJson<Sighting>(line)); break;
                        case "footer": sidecar.footer = JsonUtility.FromJson<Footer>(line); break;
                    }
                }
                catch (ArgumentException)
                {
                    sidecar.skippedLines++;
                }
            }
            return sidecar;
        }

        // The marker pose in the clip's world frame. Uses markerAtStart; takes without it fall back
        // to the median of the stable sightings.
        public bool TryGetMarkerPose(out Vector3 position, out Quaternion rotation, out string source)
        {
            if (HasMarkerAtStart)
            {
                var pose = header.markerAtStart.pose;
                position = pose.Position;
                rotation = pose.Rotation;
                source = $"markerAtStart ({header.markerAtStart.samples} samples, " +
                         $"{header.markerAtStart.positionRmsMeters * 1000:0.0} mm RMS)";
                return true;
            }

            var stable = sightings.Where(s => s.stable && s.marker != null && s.marker.IsValid).ToList();
            if (stable.Count > 0)
            {
                position = PoseMath.MedianPosition(stable.Select(s => s.marker.Position));
                rotation = PoseMath.AverageRotation(stable.Select(s => s.marker.Rotation));
                source = $"median of {stable.Count} stable sightings (no markerAtStart)";
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            source = "none";
            return false;
        }
    }

    public static class PoseMath
    {
        public static Vector3 MedianPosition(IEnumerable<Vector3> positions)
        {
            var list = positions.ToList();
            return new Vector3(Median(list.Select(v => v.x)),
                               Median(list.Select(v => v.y)),
                               Median(list.Select(v => v.z)));
        }

        // Sign-aligned mean; fine for the small spreads of a tracked marker or a static phone.
        public static Quaternion AverageRotation(IEnumerable<Quaternion> rotations)
        {
            var sum = Vector4.zero;
            Vector4? first = null;
            foreach (var q in rotations)
            {
                var v = new Vector4(q.x, q.y, q.z, q.w);
                first ??= v;
                sum += Vector4.Dot(v, first.Value) < 0 ? -v : v;
            }
            if (sum == Vector4.zero) return Quaternion.identity;
            sum.Normalize();
            return new Quaternion(sum.x, sum.y, sum.z, sum.w);
        }

        static float Median(IEnumerable<float> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return 0;
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5f;
        }
    }
}
