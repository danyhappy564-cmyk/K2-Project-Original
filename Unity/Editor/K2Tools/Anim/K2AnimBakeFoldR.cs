// 접기·펴기 클립의 오른손 IK 마커 궤적: 손잡이 → 개머리판 윗면(경첩 뒤 10cm) → 개머리판과 같이 돌며 접음 → 손잡이로 복귀.
// 사용자 요청(0.9.3 이후): "왼손은 파지하고 오른손으로 접어야지". 1인칭 손은 weapon_R_IK_marker를 따른다(EFT Player.IkProcess).
// 입력 번들의 접기 클립은 총 굴림이 이미 좌우 거울(오른쪽 면이 보임)이어야 한다 — tools/build_anim.py 1차 결과.
// 출력: 클립별 밀집 프레임마다 마커 로컬 위치·오일러(Unity 표시값) → build_anim.py가 스트림 곡선으로 다시 인코딩.
// 실행: -executeMethod K2Tools.K2AnimBakeFoldR.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clips <클립:프레임 수 JSON> -k2Out <폴더>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeFoldR
    {
        [Serializable] public class Frame { public float time, weight, angle; public Vector3 pos, rot; }
        [Serializable] public class ClipData { public string name; public float tStart, tEnd; public Vector3 contact; public List<Frame> frames = new List<Frame>(); }
        [Serializable] public class Report { public bool passed; public string error; public List<ClipData> clips = new List<ClipData>(); }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        const string Anim = "assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle";
        const string Stock = "assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle";

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
                var want = Regex.Matches(File.ReadAllText(A("-k2Clips")), "\"([^\"]+)\"\\s*:\\s*(\\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon"); var marker = Find(model.transform, "weapon_R_IK_marker"); var slot = Find(model.transform, "mod_stock");
                var sp = loaded[Stock].LoadAllAssets<GameObject>().Single(); var stock = UnityEngine.Object.Instantiate(sp);
                stock.transform.SetParent(slot, false); stock.transform.localPosition = Vector3.zero; stock.transform.localRotation = Quaternion.Euler(90, 0, 0) * sp.transform.localRotation;

                // 개머리판 윗면 접촉점(mod_stock 로컬): 앞끝에서 뒤로 10cm 단면의 가운데 x, 윗면 z + 손바닥 두께 3cm
                var mf = stock.GetComponentsInChildren<MeshFilter>(true).First(m => m.name.Contains("LOD0"));
                var vs = mf.sharedMesh.vertices.Select(v => slot.InverseTransformPoint(mf.transform.TransformPoint(v))).ToArray();
                float y0 = vs.Min(v => v.y); var band = vs.Where(v => v.y > y0 + 0.08f && v.y < y0 + 0.12f).ToArray();
                var contact = new Vector3(band.Average(v => v.x), y0 + 0.10f, band.Max(v => v.z) + 0.03f);

                var clips = loaded[Anim].LoadAllAssets<AnimationClip>().Where(c => want.ContainsKey(c.name)).ToArray();
                if (clips.Length != want.Count) throw new Exception($"클립 {clips.Length}/{want.Count}");
                Vector3 WP(Transform t) => weapon.InverseTransformPoint(t.position);
                Quaternion WR(Transform t) => Quaternion.Inverse(weapon.rotation) * t.rotation;
                foreach (var clip in clips)
                {
                    int n = want[clip.name]; var data = new ClipData { name = clip.name, contact = contact };
                    var gp = new Vector3[n]; var gr = new Quaternion[n]; var sPos = new Vector3[n]; var sRot = new Quaternion[n]; var ang = new float[n]; var times = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        times[i] = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, times[i]);
                        gp[i] = marker.localPosition; gr[i] = marker.localRotation;   // 마커 부모 = weapon
                        sPos[i] = WP(slot); sRot[i] = WR(slot); ang[i] = Quaternion.Angle(sRot[0], sRot[i]);
                    }
                    float amax = ang.Max();
                    if (amax < 30) data.tStart = data.tEnd = -1;   // 접힌 상태 유지 클립: 손은 그대로
                    else { data.tStart = times[Array.FindIndex(ang, a => a > amax * 0.03f)]; data.tEnd = times[Array.FindIndex(ang, a => a > amax * 0.97f)]; }
                    int iS = Array.IndexOf(times, data.tStart);
                    var rRel = iS >= 0 ? Quaternion.Inverse(sRot[iS]) * gr[iS] : Quaternion.identity;
                    for (int i = 0; i < n; i++)
                    {
                        float w = data.tStart < 0 ? 0 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(data.tStart - 0.3f, data.tStart, times[i])) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(data.tEnd, data.tEnd + 0.3f, times[i])));
                        var cp = sPos[i] + sRot[i] * contact; var cr = sRot[i] * rRel;
                        marker.localPosition = Vector3.Lerp(gp[i], cp, w); marker.localRotation = Quaternion.Slerp(gr[i], cr, w);
                        data.frames.Add(new Frame { time = times[i], weight = w, angle = ang[i], pos = marker.localPosition, rot = marker.localEulerAngles });
                    }
                    rep.clips.Add(data);
                }
                rep.passed = true;
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(Path.Combine(outDir, "fold-right.json"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(rep.passed ? 0 : 1);
        }
    }
}
