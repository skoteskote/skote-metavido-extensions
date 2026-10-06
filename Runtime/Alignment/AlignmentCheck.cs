using System.Collections.Generic;
using System.Text;
using Metavido.Decoder;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skote.MetavidoExtensions.Alignment
{
    // Numeric version of the "no doubled walls" check, on the current frame of each clip. Rebuilds
    // each clip's point cloud from its demuxed depth with the same ray params and InverseView the
    // VFX gets, then compares every pair of clouds (comparable to tools/align_ws.py, but one frame).
    public static class AlignmentCheck
    {
        const int Step = 4;            // depth pixel subsample
        const float MaxDepth = 4.5f;   // as in align_ws.py
        const float Cell = 0.10f;      // neighbour search radius

        public static List<Vector3> Cloud(AlignedMetavidoClip clip, bool flipV = false)
        {
            var points = new List<Vector3>();
            var meta = clip.Decoder.Metadata;
            var depthRT = clip.Demuxer.DepthTexture;
            if (!meta.IsValid || depthRT == null) return points;

            var request = AsyncGPUReadback.Request(depthRT, 0, TextureFormat.RFloat);
            request.WaitForCompletion();
            if (request.hasError) return points;
            var data = request.GetData<float>();

            var (w, h) = (depthRT.width, depthRT.height);
            var ray = RenderUtils.RayParams(meta);
            var iview = clip.InverseView(meta);
            var near = meta.DepthRange.x;
            for (var y = 0; y < h; y += Step)
            {
                for (var x = 0; x < w; x += Step)
                {
                    var d = data[y * w + x];
                    if (!(d > near + 0.05f && d < MaxDepth)) continue;
                    var u = (x + 0.5f) / w;
                    var v = (y + 0.5f) / h;
                    if (flipV) v = 1 - v;
                    var r = new Vector3(((u - 0.5f) * 2 + ray.x) * ray.z, ((v - 0.5f) * 2 + ray.y) * ray.w, 1);
                    points.Add(iview.MultiplyPoint3x4(r * d));
                }
            }
            return points;
        }

        public static string Report(AlignedMetavidoSession session)
        {
            var sb = new StringBuilder();
            var clouds = new List<(AlignedMetavidoClip clip, List<Vector3> pts)>();
            foreach (var clip in session.Clips)
            {
                if (clip == null || !clip.ShouldDraw) continue;
                var pts = Cloud(clip);
                clouds.Add((clip, pts));
                sb.AppendLine($"{clip.name}: t {clip.TargetTime:0.00} s, {pts.Count} points");
            }

            for (var i = 0; i < clouds.Count; i++)
                for (var j = 0; j < clouds.Count; j++)
                    if (i != j) sb.AppendLine(CompareReport(clouds[i], clouds[j]));
            return sb.ToString();
        }

        static string CompareReport((AlignedMetavidoClip clip, List<Vector3> pts) a,
                                    (AlignedMetavidoClip clip, List<Vector3> pts) b)
        {
            var grid = new Dictionary<Vector3Int, List<Vector3>>();
            foreach (var p in b.pts)
            {
                var k = Key(p);
                if (!grid.TryGetValue(k, out var list)) grid[k] = list = new List<Vector3>();
                list.Add(p);
            }

            var dists = new List<float>();
            var offsetSum = Vector3.zero;
            foreach (var p in a.pts)
            {
                var k = Key(p);
                var best = float.MaxValue;
                var bestQ = Vector3.zero;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                for (var dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue(k + new Vector3Int(dx, dy, dz), out var list)) continue;
                    foreach (var q in list)
                    {
                        var d = (q - p).sqrMagnitude;
                        if (d < best) { best = d; bestQ = q; }
                    }
                }
                best = Mathf.Sqrt(best);
                if (best < Cell) { dists.Add(best); offsetSum += bestQ - p; }
            }

            if (a.pts.Count == 0 || dists.Count == 0)
                return $"[{a.clip.name} -> {b.clip.name}] no overlap within {Cell * 100:0} cm";
            var mean = offsetSum / dists.Count;
            return $"[{a.clip.name} -> {b.clip.name}] {100f * dists.Count / a.pts.Count:0}% of points within {Cell * 100:0} cm; " +
                   $"median {Median(dists) * 1000:0.0} mm; mean offset to neighbour {mean * 1000} mm";
        }

        static Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / Cell);

        static float Median(List<float> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }
    }
}
