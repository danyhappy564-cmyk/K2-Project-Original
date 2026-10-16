// 오른손이 장전손잡이를 만지는 AK 클립 전부: 손바닥 목표를 K2 손잡이 쪽(delta)으로 옮겨 두 뼈 IK(상완·전완1, 손바닥 회전 유지)로 다시 계산.
// 보정 비중은 시간 고정(Codex v003)이 아니라 "손바닥–mod_charge 거리"로: 클립 최소 거리 +20mm 안 = 1, +90mm 밖 = 0(부드럽게).
// 결과 형식은 Codex bake-ak-v003/ik-curves.json과 같다(tools/build_anim.py가 읽음). IK 방법은 Codex K2AnimBakeV003(BakeMeasured) 그대로.
// 실행: -executeMethod K2Tools.K2AnimBakeCharge.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clips <클립:프레임 수 JSON> -k2Measure <ak-measure-v003.json> -k2Out <폴더>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeCharge
    {
        [Serializable] public class Measure { public float[] delta; }
        [Serializable] public class Sample { public float time, weight, errorMetres, distance; public Vector3 upper, forearm, palm, marker; }
        [Serializable] public class ClipData { public string name, side; public int frameCount; public float minDistance; public List<Sample> frames = new List<Sample>(); }
        [Serializable] public class Report { public bool passed; public string error; public List<ClipData> clips = new List<ClipData>(); }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        static void Solve(Transform upper, Transform elbow, Transform palm, Vector3 target)
        {
            var oldPalm = palm.rotation; var a = upper.position; var b = elbow.position; var c = palm.position;
            float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, c), d = Vector3.Distance(a, target);
            if (d >= l1 + l2 - .00001f || d <= Mathf.Abs(l1 - l2) + .00001f) throw new Exception("IK target unreachable");
            var axis = (target - a) / d; var pole = b - a; pole -= Vector3.Dot(pole, axis) * axis; pole.Normalize();
            float x = (l1 * l1 - l2 * l2 + d * d) / (2 * d); float h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - x * x));
            var newElbow = a + axis * x + pole * h;
            upper.rotation = Quaternion.FromToRotation(b - a, newElbow - a) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(palm.position - elbow.position, target - elbow.position) * elbow.rotation;
            palm.rotation = oldPalm;
        }

        public static void Run()
        {
            var rep = new Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                var mod = A("-k2Mod"); var game = A("-k2Game"); var loaded = new Dictionary<string, AssetBundle>();
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(mod, "bundles.json")), "\"(assets/[^\"]+|shaders|cubemaps)\""))
                {
                    var k = m.Groups[1].Value; if (loaded.ContainsKey(k)) continue;
                    var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k);
                    var ab = AssetBundle.LoadFromFile(p); if (ab) loaded[k] = ab;
                }
                var shift = JsonUtility.FromJson<Measure>(File.ReadAllText(A("-k2Measure"))).delta; var delta = new Vector3(shift[0], shift[1], shift[2]);
                var want = Regex.Matches(File.ReadAllText(A("-k2Clips")), "\"([^\"]+)\"\\s*:\\s*(\\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon"); var marker = Find(model.transform, "weapon_R_IK_marker"); var charge = Find(model.transform, "mod_charge");
                var upper = Find(model.transform, "Base HumanRUpperarm"); var elbow = Find(model.transform, "Base HumanRForearm1"); var palm = Find(model.transform, "Base HumanRPalm");
                var clips = loaded["assets/content/weapons/ak100/client_assets.bundle"].LoadAllAssets<AnimationClip>().Where(c => want.ContainsKey(c.name)).ToArray();
                if (clips.Length != want.Count) throw new Exception($"클립 {clips.Length}/{want.Count}");
                foreach (var clip in clips)
                {
                    var data = new ClipData { name = clip.name, side = "R", frameCount = want[clip.name] };
                    var dist = new float[data.frameCount];
                    for (int i = 0; i < data.frameCount; i++)
                    {
                        clip.SampleAnimation(model, Mathf.Min(i / 30f, clip.length));
                        dist[i] = Vector3.Distance(weapon.InverseTransformPoint(palm.position), weapon.InverseTransformPoint(charge.position));
                    }
                    data.minDistance = dist.Min();
                    for (int i = 0; i < data.frameCount; i++)
                    {
                        float time = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, time);
                        float w = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(data.minDistance + .02f, data.minDistance + .09f, dist[i]));
                        var target = weapon.InverseTransformPoint(palm.position) + delta * w;
                        if (w > 0) for (int it = 0; it < 6; it++) { Solve(upper, elbow, palm, weapon.TransformPoint(target)); if (Vector3.Distance(palm.position, weapon.TransformPoint(target)) < .00001f) break; }
                        marker.position += weapon.TransformVector(delta * w);
                        float err = Vector3.Distance(weapon.InverseTransformPoint(palm.position), target);
                        if (err > .001f) throw new Exception($"IK residual {err} clip={clip.name} time={time}");
                        data.frames.Add(new Sample { time = time, weight = w, errorMetres = err, distance = dist[i], upper = upper.localEulerAngles, forearm = elbow.localEulerAngles, palm = palm.localEulerAngles, marker = marker.localPosition });
                    }
                    rep.clips.Add(data);
                }
                rep.passed = true;
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(Path.Combine(outDir, "ik-curves.json"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(rep.passed ? 0 : 1);
        }
    }
}
