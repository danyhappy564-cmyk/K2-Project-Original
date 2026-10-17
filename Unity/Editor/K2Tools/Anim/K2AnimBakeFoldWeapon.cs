// K2 접기·펴기 클립의 총(weapon 노드) 궤적 = 도너(바닐라 G36)가 접는 동안 총을 내미는 궤적. K2 경첩이 G36 회전 노드 자리에 오도록 놓는다.
// 왜: 0.9.5는 AK 총 궤적 그대로 G36 손 자세(개머리판 기준)만 옮겨 개머리판이 얼굴 앞에 있었다 — 검사기 고정 카메라: 손–카메라 69mm(G36 224mm),
// 오른팔꿈치 9°(G36 72°), 인게임 "팔이 너무 꺾임". 손 IK 마커는 무기 직속이라 총만 옮기면 손(개머리판 기준 자세)도 같이 G36 자리로 간다.
// 시간 대응은 K2AnimBakeFoldDonor와 같다(접힘 각도 진행률). 총 움직임 = 도너의 대기 자세 대비 움직임을 K2 대기 자세에 적용,
// 거기에 경첩을 (도너 회전 노드 + 어깨 차이) 자리로 옮기는 보정을 접기 전·후 시간에 걸쳐 서서히(c).
// 입력 번들은 tools/build_anim.py 1차 결과(총 굴림 거울 적용). 출력: 클립별 프레임마다 weapon 로컬 위치·오일러(Unity) — build_anim.py BAKE_FOLD_W.
// 실행: -executeMethod K2Tools.K2AnimBakeFoldWeapon.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clips <클립:프레임 수 JSON>
//        -k2Donors <클립=도너 클립 이름=도너 조사 json;…> -k2DonorBody <키> -k2DonorAnim <키> -k2DonorDeps <키;…> -k2Out <폴더>
//        [-k2Amp <0~1, 움직임 크기>] [-k2EndWin <끝 복귀 초>] [-k2Smooth <시간 평활 초>]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeFoldWeapon
    {
        [Serializable] public class Frame { public float time, weight, angle; public Vector3 pos, rot, hingeWorld, donorPivotWorld, shoulder, donorShoulder; }
        [Serializable] public class Clip { public string name, donor; public List<Frame> frames = new List<Frame>(); }
        [Serializable] public class Report { public bool passed; public string error; public List<Clip> clips = new List<Clip>(); }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        const string Anim = "assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle";
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
        static (int, int) Motion(float[] ang) { float m = ang.Max(); return (Array.FindIndex(ang, a => a > 3f), Array.FindIndex(ang, a => a > m - 3f)); }

        public static void Run()
        {
            var rep = new Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                float amp = A("-k2Amp") == null ? 1 : float.Parse(A("-k2Amp"), System.Globalization.CultureInfo.InvariantCulture);
                float smoothSec = A("-k2Smooth") == null ? 0 : float.Parse(A("-k2Smooth"), System.Globalization.CultureInfo.InvariantCulture);
                float endWin = A("-k2EndWin") == null ? .15f : float.Parse(A("-k2EndWin"), System.Globalization.CultureInfo.InvariantCulture);
                var mod = A("-k2Mod"); var game = A("-k2Game"); var loaded = new Dictionary<string, AssetBundle>();
                AssetBundle Load(string k) { if (loaded.TryGetValue(k, out var b)) return b; var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k); b = AssetBundle.LoadFromFile(p); if (b) loaded[k] = b; return b; }
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(mod, "bundles.json")), "\"(assets/[^\"]+|shaders|cubemaps)\"")) Load(m.Groups[1].Value);
                foreach (var k in A("-k2DonorDeps").Split(';').Where(x => x != "")) Load(k);
                var want = Regex.Matches(File.ReadAllText(A("-k2Clips")), "\"([^\"]+)\"\\s*:\\s*(\\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
                var donors = A("-k2Donors").Split(';').Where(x => x.Contains("=")).Select(x => x.Split(new[] { '=' }, 3)).ToDictionary(x => x[0], x => (clip: x[1], probe: JsonUtility.FromJson<K2ProbeFoldDonor.Report>(File.ReadAllText(x[2]))));

                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon"); var slot = Find(model.transform, "mod_stock"); var shoulder = Find(model.transform, "Base HumanRUpperarm");
                var dwp = Load(A("-k2DonorBody")).LoadAllAssets<GameObject>().Select(g => g.GetComponent<WeaponPrefab>()).First(w => w != null);
                var dmodel = UnityEngine.Object.Instantiate(dwp._weaponObject); dmodel.GetComponent<Animator>().enabled = false;
                var dweapon = Find(dmodel.transform, "weapon"); var dshoulder = Find(dmodel.transform, "Base HumanRUpperarm");
                var dclips = Load(A("-k2DonorAnim")).LoadAllAssets<AnimationClip>();
                var clips = loaded[Anim].LoadAllAssets<AnimationClip>().Where(c => want.ContainsKey(c.name) && donors.ContainsKey(c.name)).ToArray();
                if (clips.Length != donors.Count) throw new Exception($"클립 {clips.Length}/{donors.Count}");
                foreach (var clip in clips)
                {
                    var (dname, d) = donors[clip.name]; var dclip = dclips.First(c => c.name == dname);
                    int n = want[clip.name]; var data = new Clip { name = clip.name, donor = dname };
                    var times = new float[n]; var ang = new float[n]; var sr0 = Quaternion.identity; Vector3 hingeLocal = Vector3.zero;
                    for (int i = 0; i < n; i++)
                    {
                        times[i] = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, times[i]);
                        var sr = Quaternion.Inverse(weapon.rotation) * slot.rotation; if (i == 0) { sr0 = sr; hingeLocal = weapon.InverseTransformPoint(slot.position); }
                        ang[i] = Quaternion.Angle(sr0, sr);
                    }
                    var dt = d.frames.Select(f => f.time).ToArray(); var da = d.frames.Select(f => f.angle).ToArray();
                    var (ks, ke) = Motion(ang); var (gs, ge) = Motion(da); float dmax = da.Max(), kmax = ang.Max();
                    // 도너 대기 자세(시작) — 이후 도너 총 움직임 D = 현재 · 대기⁻¹ 을 K2 대기 자세에 그대로 적용(시작·끝에서 저절로 K2 대기 자세)
                    dclip.SampleAnimation(dmodel, 0); var dP0 = dweapon.position; var dR0 = dweapon.rotation; var dPiv0 = dweapon.TransformPoint(d.pivotWeapon);
                    clip.SampleAnimation(model, 0); var kP0 = weapon.position; var kR0 = weapon.rotation;
                    var shoulderDiff = shoulder.position - dshoulder.position;   // 어깨 기준으로 맞춤(손–어깨 = G36 → 팔꿈치 각 G36)
                    float tks = Mathf.Max(times[ks], .2f), tke = times[ke], end = clip.length;
                    var wp_ = new List<Vector3>(); var wr_ = new List<Quaternion>();
                    for (int i = 0; i < n; i++)
                    {
                        float t = times[i], tg;
                        if (i <= ks) tg = Mathf.Lerp(0, dt[gs], ks == 0 ? 1 : (float)i / ks);
                        else if (i >= ke) tg = Mathf.Lerp(dt[ge], dt.Last(), (t - times[ke]) / Mathf.Max(1e-4f, clip.length - times[ke]));
                        else { float frac = ang[i] / kmax * dmax; int j = gs; while (j < ge && da[j + 1] < frac) j++; tg = Mathf.Lerp(dt[j], dt[Mathf.Min(j + 1, ge)], Mathf.InverseLerp(da[j], da[Mathf.Min(j + 1, ge)], frac)); }
                        dclip.SampleAnimation(dmodel, tg); var dPivot = dweapon.TransformPoint(d.pivotWeapon);
                        var dRot = dweapon.rotation * Quaternion.Inverse(dR0);                      // 도너 움직임(회전)
                        var bRot = dRot * kR0; var bPos = dweapon.position + dRot * (kP0 - dP0);    // K2 대기 자세에 같은 움직임
                        var bHinge = bPos + bRot * Vector3.Scale(hingeLocal, weapon.lossyScale);
                        var goal = dPivot + shoulderDiff;                                          // 경첩 목표
                        float c = Smooth(0, tks, t) * (1 - Smooth(tke, end, t));                    // 경첩 맞춤 비중: 접기 전 서서히, 접은 뒤 서서히 풀기
                        clip.SampleAnimation(model, t);
                        float e = Smooth(end - endWin, end, t);                                      // 끝: K2 원래 자세로
                        // 크기(amp): K2 클립 원래 자세 ↔ G36식 목표 사이. 0.9.6(amp 1)은 G36 2.27초 움직임을 1.7초에 넣어 총구 각속도 529°/s(G36 171) — 인게임 "휘두름"
                        var tPos = bPos + (goal - bHinge) * c; var tRot = bRot;
                        tPos = Vector3.Lerp(weapon.position, tPos, amp); tRot = Quaternion.Slerp(weapon.rotation, tRot, amp);
                        var pos = Vector3.Lerp(tPos, weapon.position, e); var rot = Quaternion.Slerp(tRot, weapon.rotation, e);
                        wp_.Add(pos); wr_.Add(rot);
                        data.frames.Add(new Frame { time = t, weight = c, angle = ang[i], donorPivotWorld = dPivot, shoulder = shoulder.position, donorShoulder = dshoulder.position });
                    }
                    // 시간 평활(-k2Smooth 초, 가우스): 압축된 도너 움직임의 각속도 봉우리를 낮춘다. 첫·끝 프레임은 그대로(K2 원래 자세).
                    int rad = Mathf.RoundToInt(smoothSec * 30);
                    for (int i = 0; i < n; i++)
                    {
                        var P = wp_[i]; var Q = wr_[i];
                        if (rad > 0 && i > 0 && i < n - 1)
                        {
                            Vector3 ps = Vector3.zero; Vector4 qs = Vector4.zero; float ws = 0;
                            for (int j = Mathf.Max(0, i - rad); j <= Mathf.Min(n - 1, i + rad); j++)
                            {
                                float w = Mathf.Exp(-.5f * Mathf.Pow((j - i) / (rad / 2f), 2)); var q = wr_[j]; if (Quaternion.Dot(q, wr_[i]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                                ps += wp_[j] * w; qs += new Vector4(q.x, q.y, q.z, q.w) * w; ws += w;
                            }
                            float edge = Mathf.Min(1, Mathf.Min(i, n - 1 - i) / (float)rad);   // 끝 근처는 평활을 줄여 끝 자세 유지
                            qs /= ws; var qa = new Quaternion(qs.x, qs.y, qs.z, qs.w).normalized;
                            P = Vector3.Lerp(P, ps / ws, edge); Q = Quaternion.Slerp(Q, qa, edge);
                        }
                        clip.SampleAnimation(model, times[i]); weapon.SetPositionAndRotation(P, Q);
                        var fr = data.frames[i]; fr.pos = weapon.localPosition; fr.rot = weapon.localEulerAngles; fr.hingeWorld = weapon.TransformPoint(hingeLocal);
                    }
                    rep.clips.Add(data);
                }
                rep.passed = true;
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(Path.Combine(outDir, "fold-weapon.json"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(rep.passed ? 0 : 1);
        }
    }
}
