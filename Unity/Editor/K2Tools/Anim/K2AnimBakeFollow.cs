// 접기 클립 등: 한 손이 총의 한 자리(첫 프레임 자세 — 예: 왼손이 핸드가드)를 계속 잡게 한다.
// 0.9.1은 왼팔 곡선을 첫 값으로 고정했는데, 접기 클립은 총이 크게 움직여(굴림 −40°, 이동 7cm) 손이 허공에 떴다(인게임 영상).
// 매 프레임: 원래 클립 재생 → 그 손의 나머지 뼈(쇄골·전완2·3·손가락·마커)는 첫 프레임 로컬 값으로 되돌림(빌드에서 고정하는 것과 같게)
// → 손바닥 목표 = 총(weapon) 기준 첫 프레임 위치·회전 → 두 뼈 IK(상완·전완1) + 손바닥 회전.
// 결과 형식은 K2AnimBakeCharge와 같다(tools/build_anim.py가 읽음).
// 실행: -executeMethod K2Tools.K2AnimBakeFollow.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clips <클립:프레임 수 JSON> -k2Side L -k2Out <폴더>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeFollow
    {
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        static void Solve(Transform upper, Transform elbow, Transform palm, Vector3 target)
        {
            var a = upper.position; var b = elbow.position; var c = palm.position;
            float l1 = Vector3.Distance(a, b), l2 = Vector3.Distance(b, c), d = Vector3.Distance(a, target);
            d = Mathf.Clamp(d, Mathf.Abs(l1 - l2) + .0005f, l1 + l2 - .0005f);   // 닿지 않으면 최대한 뻗음(오차는 기록)
            var axis = (target - a).normalized; var pole = b - a; pole -= Vector3.Dot(pole, axis) * axis; pole.Normalize();
            float x = (l1 * l1 - l2 * l2 + d * d) / (2 * d); float h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - x * x));
            var newElbow = a + axis * x + pole * h;
            upper.rotation = Quaternion.FromToRotation(b - a, newElbow - a) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(palm.position - elbow.position, target - elbow.position) * elbow.rotation;
        }

        public static void Run()
        {
            var rep = new K2AnimBakeCharge.Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                var mod = A("-k2Mod"); var game = A("-k2Game"); var side = A("-k2Side") ?? "L"; var loaded = new Dictionary<string, AssetBundle>();
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(mod, "bundles.json")), "\"(assets/[^\"]+|shaders|cubemaps)\""))
                {
                    var k = m.Groups[1].Value; if (loaded.ContainsKey(k)) continue;
                    var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k);
                    var ab = AssetBundle.LoadFromFile(p); if (ab) loaded[k] = ab;
                }
                var want = Regex.Matches(File.ReadAllText(A("-k2Clips")), "\"([^\"]+)\"\\s*:\\s*(\\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon");
                var upper = Find(model.transform, $"Base Human{side}Upperarm"); var elbow = Find(model.transform, $"Base Human{side}Forearm1"); var palm = Find(model.transform, $"Base Human{side}Palm");
                var solved = new HashSet<Transform> { upper, elbow, palm };
                // 첫 프레임 값으로 되돌릴 뼈: 그쪽 팔·손가락 전부(쇄골 포함) — 빌드의 L_ARM 고정과 같은 범위
                var keep = model.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith($"Base Human{side}") && !solved.Contains(t)).ToArray();
                var clips = loaded["assets/content/weapons/ak100/client_assets.bundle"].LoadAllAssets<AnimationClip>().Where(c => want.ContainsKey(c.name)).ToArray();
                if (clips.Length != want.Count) throw new Exception($"클립 {clips.Length}/{want.Count}");
                foreach (var clip in clips)
                {
                    var data = new K2AnimBakeCharge.ClipData { name = clip.name, side = side, frameCount = want[clip.name] };
                    clip.SampleAnimation(model, 0);
                    var p0 = weapon.InverseTransformPoint(palm.position); var r0 = Quaternion.Inverse(weapon.rotation) * palm.rotation;
                    var k0 = keep.ToDictionary(t => t, t => (t.localPosition, t.localRotation));
                    float maxErr = 0;
                    for (int i = 0; i < data.frameCount; i++)
                    {
                        float time = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, time);
                        foreach (var kv in k0) { kv.Key.localPosition = kv.Value.localPosition; kv.Key.localRotation = kv.Value.localRotation; }
                        var target = weapon.TransformPoint(p0); var trot = weapon.rotation * r0;
                        for (int it = 0; it < 8; it++) { Solve(upper, elbow, palm, target); palm.rotation = trot; if (Vector3.Distance(palm.position, target) < .00001f) break; }
                        float err = Vector3.Distance(palm.position, target); maxErr = Mathf.Max(maxErr, err);
                        data.frames.Add(new K2AnimBakeCharge.Sample { time = time, weight = 1, errorMetres = err, upper = upper.localEulerAngles, forearm = elbow.localEulerAngles, palm = palm.localEulerAngles });
                    }
                    data.minDistance = maxErr;   // 이 도구에서는 최대 오차를 담는다
                    if (maxErr > .005f) throw new Exception($"손이 닿지 않음 {clip.name} 최대 {maxErr * 1000:0.0}mm");
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
