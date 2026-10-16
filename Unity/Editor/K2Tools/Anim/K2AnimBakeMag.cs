// 재장전·탄창 확인 클립: 탄창 칸(mod_magazine·mod_magazine_new)은 끼운 자리 근처에서만 +LIFT(K2를 올린 만큼) 올라간다(build_anim.py).
// 왼손이 탄창을 들고 있으면 그 탄창과 같이 올라가야 한다 — 손바닥 목표 = 원래 + 무기 +z LIFT × w(탄창–끼운 자리 거리) × h(손–탄창 거리).
// 0.9.2 인게임: 탄창 칸을 늘 +27mm 해서 손에서 탄창이 떠 있었다("탄창을 안 잡음").
// 결과 형식은 K2AnimBakeCharge와 같다(side L).
// 실행: -executeMethod K2Tools.K2AnimBakeMag.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clips <클립:프레임 수 JSON> -k2Out <폴더>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeMag
    {
        public const float Lift = 0.027f, Near = 0.02f, Far = 0.08f, HandNear = 0.10f, HandFar = 0.18f;
        public static readonly Vector3 Seat = new Vector3(-0.0001f, -0.4402f, -0.0241f);   // AK 끼운 자리(무기 좌표)
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        public static float MagWeight(float d) => 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Near, Far, d));

        static void Solve(Transform upper, Transform elbow, Transform palm, Vector3 target)
        {
            var oldPalm = palm.rotation; var a = upper.position; var b = elbow.position; var c = palm.position;
            float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, c), d = Vector3.Distance(a, target);
            d = Mathf.Clamp(d, Mathf.Abs(l1 - l2) + .0005f, l1 + l2 - .0005f);
            var axis = (target - a).normalized; var pole = b - a; pole -= Vector3.Dot(pole, axis) * axis; pole.Normalize();
            float x = (l1 * l1 - l2 * l2 + d * d) / (2 * d); float h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - x * x));
            var newElbow = a + axis * x + pole * h;
            upper.rotation = Quaternion.FromToRotation(b - a, newElbow - a) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(palm.position - elbow.position, target - elbow.position) * elbow.rotation;
            palm.rotation = oldPalm;
        }

        public static void Run()
        {
            var rep = new K2AnimBakeCharge.Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                var mod = A("-k2Mod"); var game = A("-k2Game"); var loaded = new Dictionary<string, AssetBundle>();
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(mod, "bundles.json")), "\"(assets/[^\"]+|shaders|cubemaps)\""))
                {
                    var k = m.Groups[1].Value; if (loaded.ContainsKey(k)) continue;
                    var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k);
                    var ab = AssetBundle.LoadFromFile(p); if (ab) loaded[k] = ab;
                }
                var want = Regex.Matches(File.ReadAllText(A("-k2Clips")), "\"([^\"]+)\"\\s*:\\s*(\\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon"); var mags = new[] { Find(model.transform, "mod_magazine"), Find(model.transform, "mod_magazine_new") };
                var upper = Find(model.transform, "Base HumanLUpperarm"); var elbow = Find(model.transform, "Base HumanLForearm1"); var palm = Find(model.transform, "Base HumanLPalm");
                var clips = loaded["assets/content/weapons/ak100/client_assets.bundle"].LoadAllAssets<AnimationClip>().Where(c => want.ContainsKey(c.name)).ToArray();
                if (clips.Length != want.Count) throw new Exception($"클립 {clips.Length}/{want.Count}");
                foreach (var clip in clips)
                {
                    var data = new K2AnimBakeCharge.ClipData { name = clip.name, side = "L", frameCount = want[clip.name] };
                    float maxErr = 0, maxW = 0;
                    for (int i = 0; i < data.frameCount; i++)
                    {
                        float time = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, time);
                        var pw = weapon.InverseTransformPoint(palm.position);
                        // 손에 가장 가까운 탄창 노드
                        var mag = mags.OrderBy(m => Vector3.Distance(pw, weapon.InverseTransformPoint(m.position))).First();
                        var mp = weapon.InverseTransformPoint(mag.position);
                        float w = MagWeight(Vector3.Distance(mp, Seat)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(HandNear, HandFar, Vector3.Distance(pw, mp))));
                        var target = pw + new Vector3(0, 0, Lift) * w; maxW = Mathf.Max(maxW, w);
                        if (w > 0) for (int it = 0; it < 6; it++) { Solve(upper, elbow, palm, weapon.TransformPoint(target)); if (Vector3.Distance(palm.position, weapon.TransformPoint(target)) < .00001f) break; }
                        float err = Vector3.Distance(weapon.InverseTransformPoint(palm.position), target); maxErr = Mathf.Max(maxErr, err);
                        data.frames.Add(new K2AnimBakeCharge.Sample { time = time, weight = w, errorMetres = err, upper = upper.localEulerAngles, forearm = elbow.localEulerAngles, palm = palm.localEulerAngles });
                    }
                    data.minDistance = maxW;   // 이 도구에서는 최대 비중
                    if (maxErr > .001f) throw new Exception($"IK 오차 {clip.name} {maxErr * 1000:0.0}mm");
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
