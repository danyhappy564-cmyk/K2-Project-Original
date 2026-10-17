// K2 접기·펴기 오른손 IK 마커 = 도너(바닐라 G36) 오른손의 "개머리판 회전 노드 기준 상대 자세"를 K2 개머리판(mod_stock)에 옮긴 것.
// 0.9.4(K2AnimBakeFoldR)는 접촉점을 어림잡아 손이 개머리판 속으로 2cm 들어갔다(검사기 K2Validation). G36은 윗면을 잡았다가 왼쪽 면을 밀어 오른쪽으로 접는다.
// 시간 대응: 회전 전 = 도너 회전 전 구간 비율, 회전 중 = 접힘 각도 진행률이 같은 도너 시각, 회전 후 = 도너 회전 후 구간 비율.
// 손잡이와의 섞임: 도너 손이 자기 손잡이 자세에서 3~8cm 벗어난 정도(c)로 K2 원래 마커(손잡이)와 섞는다.
// 입력 번들의 접기 클립은 총 굴림이 이미 거울(tools/build_anim.py 1차 결과)이어야 한다. 출력 형식은 K2AnimBakeFoldR과 같다(build_anim.py BAKE_FOLD_R).
// 실행: -executeMethod K2Tools.K2AnimBakeFoldDonor.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clips <클립:프레임 수 JSON> -k2Donors <클립=도너json;…> -k2Out <폴더>
//        [-k2Exit <클립=정규화 빠져나가는 시점;…>] [-k2Push <검사기 report.json;…>] — 그 검사에서 오른손–개머리판 틈이 −2mm보다 작은 프레임만큼 손을 개머리판 중심선에서 바깥으로 민다(누적 반복 가능)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeFoldDonor
    {
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        const string Anim = "assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle";
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
        static Vector3 InterpV((float t, Vector3 v)[] a, float t) { if (a.Length == 0) return Vector3.zero; int i = Array.FindLastIndex(a, x => x.t <= t); if (i < 0) return a[0].v; if (i >= a.Length - 1) return a[i].v; return Vector3.Lerp(a[i].v, a[i + 1].v, Mathf.InverseLerp(a[i].t, a[i + 1].t, t)); }
        static float Interp((float t, float g)[] a, float t) { if (a.Length == 0) return 0; int i = Array.FindLastIndex(a, x => x.t <= t); if (i < 0) return a[0].g; if (i >= a.Length - 1) return a[i].g; return Mathf.Lerp(a[i].g, a[i + 1].g, Mathf.InverseLerp(a[i].t, a[i + 1].t, t)); }
        static (int, int) Motion(float[] ang) { float m = ang.Max(); return (Array.FindIndex(ang, a => a > 3f), Array.FindIndex(ang, a => a > m - 3f)); }

        public static void Run()
        {
            var rep = new K2AnimBakeFoldR.Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
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
                var push = new Dictionary<string, List<(float t, Vector3 v)[]>>();
                foreach (var pj in (A("-k2Push") ?? "").Split(';').Where(x => x != ""))
                    foreach (var c in JsonUtility.FromJson<K2ValidationV003C.Validation.Report>(File.ReadAllText(pj)).clips)
                    { if (!push.ContainsKey(c.name)) push[c.name] = new List<(float, Vector3)[]>(); push[c.name].Add(c.frames.Select(f => (f.time, f.rPalmPushStockLocal)).ToArray()); }
                var exits = (A("-k2Exit") ?? "").Split(';').Where(x => x.Contains("=")).ToDictionary(x => x.Split('=')[0], x => float.Parse(x.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture));
                var donors = A("-k2Donors").Split(';').Where(x => x.Contains("=")).ToDictionary(x => x.Split('=')[0], x => JsonUtility.FromJson<K2ProbeFoldDonor.Report>(File.ReadAllText(x.Substring(x.IndexOf('=') + 1))));
                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon"); var marker = Find(model.transform, "weapon_R_IK_marker"); var slot = Find(model.transform, "mod_stock");
                var clips = loaded[Anim].LoadAllAssets<AnimationClip>().Where(c => want.ContainsKey(c.name)).ToArray();
                if (clips.Length != want.Count) throw new Exception($"클립 {clips.Length}/{want.Count}");
                foreach (var clip in clips)
                {
                    int n = want[clip.name]; var data = new K2AnimBakeFoldR.ClipData { name = clip.name };
                    var times = new float[n]; var gp = new Vector3[n]; var gr = new Quaternion[n]; var sp = new Vector3[n]; var sr = new Quaternion[n]; var ang = new float[n];
                    for (int i = 0; i < n; i++)
                    {
                        times[i] = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, times[i]);
                        gp[i] = marker.localPosition; gr[i] = marker.localRotation;
                        sp[i] = weapon.InverseTransformPoint(slot.position); sr[i] = Quaternion.Inverse(weapon.rotation) * slot.rotation;
                        ang[i] = Quaternion.Angle(sr[0], sr[i]);
                    }
                    donors.TryGetValue(clip.name, out var d);
                    if (d == null || ang.Max() < 30)
                    {   // 접힌 상태 유지 등: 손은 원래대로
                        for (int i = 0; i < n; i++) data.frames.Add(new K2AnimBakeFoldR.Frame { time = times[i], pos = gp[i], rot = gr[i].eulerAngles });
                        data.tStart = data.tEnd = -1; rep.clips.Add(data); continue;
                    }
                    var (ks, ke) = Motion(ang); data.tStart = times[ks]; data.tEnd = times[ke];
                    float endT = exits.TryGetValue(clip.name, out var ex) ? ex * clip.length : clip.length;
                    var dt = d.frames.Select(f => f.time).ToArray(); var da = d.frames.Select(f => f.angle).ToArray();
                    var (gs, ge) = Motion(da); float dmax = da.Max(), kmax = ang.Max();
                    Vector3 g0 = d.frames[0].rWeaponPos, gEnd = d.frames.Last().rWeaponPos;
                    for (int i = 0; i < n; i++)
                    {
                        float t = times[i], tg;
                        if (i <= ks) tg = Mathf.Lerp(0, dt[gs], ks == 0 ? 1 : (float)i / ks);
                        else if (i >= ke) tg = Mathf.Lerp(dt[ge], dt.Last(), Mathf.Clamp01((t - times[ke]) / Mathf.Max(1e-4f, endT - times[ke])));   // 0.9.9: 빠져나가는 시점까지
                        else
                        {   // 접힘 진행률이 같은 도너 시각
                            float frac = ang[i] / kmax * dmax; int j = gs; while (j < ge && da[j + 1] < frac) j++;
                            tg = Mathf.Lerp(dt[j], dt[Mathf.Min(j + 1, ge)], Mathf.InverseLerp(da[j], da[Mathf.Min(j + 1, ge)], frac));
                        }
                        int a0 = Mathf.Clamp(Array.FindLastIndex(dt, x => x <= tg), 0, dt.Length - 1), a1 = Mathf.Min(a0 + 1, dt.Length - 1); float u = a1 == a0 ? 0 : Mathf.InverseLerp(dt[a0], dt[a1], tg);
                        var f0 = d.frames[a0]; var f1 = d.frames[a1];
                        var relP = Vector3.Lerp(f0.rLocalPos, f1.rLocalPos, u); var relR = Quaternion.Slerp(Quaternion.Euler(f0.rLocalRot), Quaternion.Euler(f1.rLocalRot), u);
                        var wpos = Vector3.Lerp(f0.rWeaponPos, f1.rWeaponPos, u);
                        float c = Smooth(.03f, .08f, Mathf.Min(Vector3.Distance(wpos, g0), Vector3.Distance(wpos, gEnd)));
                        if (push.TryGetValue(clip.name, out var pl))
                            foreach (var arr in pl) relP += InterpV(arr, t) * c;   // 검사기 PalmPush(개머리판 기준) 누적
                        var mp = sp[i] + sr[i] * relP; var mr = sr[i] * relR;
                        c *= 1 - Smooth(endT - .1f, endT, t);
                        float h = Smooth(endT - .3f, endT, t);   // 끝: 원래 마커 대신 첫 프레임 마커(권총손잡이) — AK 원래 손은 빠져나갈 때 아직 개머리판 쪽
                        var bp = Vector3.Lerp(gp[i], gp[0], h); var br = Quaternion.Slerp(gr[i], gr[0], h);
                        marker.localPosition = Vector3.Lerp(bp, mp, c); marker.localRotation = Quaternion.Slerp(br, mr, c);
                        data.frames.Add(new K2AnimBakeFoldR.Frame { time = t, weight = c, angle = ang[i], pos = marker.localPosition, rot = marker.localEulerAngles });
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
