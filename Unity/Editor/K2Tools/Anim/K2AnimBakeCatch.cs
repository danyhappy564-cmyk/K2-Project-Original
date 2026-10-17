// 빈 탄창 장전 한 가지(AK reload_charge_2 — 왼손이 위로 넘어가 오른쪽 장전손잡이를 당김)를 M4식 "왼손으로 노리쇠 멈치 때리기"로 바꾼다.
// 사용자(0.9.5 인게임): K2는 마지막 탄 후 노리쇠가 열린 채 멈추고 멈치를 눌러 닫는다 — 장전손잡이를 당기거나 멈치를 때리거나 둘 중 하나.
// 도너 = 바닐라 M4A1 m4a1_reload_catch_0. 시간 대응: 노리쇠가 앞으로 가기 시작하는 순간(AK: 최대 후퇴 프레임, M4: 처음 2mm 움직인 프레임)을 맞춘 구간 선형
// (상태의 소리 이벤트가 AK 시각 기준이라 맞춤). M4는 0.32초에 누르고 AK는 0.97초에 풀려 — 접근은 1.4배까지만 늘리고 그 앞은 손잡이에서 대기.
//  - 총(weapon): M4 대기 자세 대비 움직임을 K2 대기 자세에 적용, 끝 0.15초는 K2 원래 자세로.
//  - 왼손 IK 마커(무기 기준): M4 마커 + (K2 멈치 − M4 멈치). M4 손이 자기 대기 자세에서 3~8cm 벗어난 정도(c)로 K2 왼손 기준점(손잡이)과 섞음.
//  - 왼손가락: M4 값 그대로(손잡이 근처에서는 게임이 GripPose로 덮음). 노리쇠: 앞으로 가는 순간까지 최대 후퇴 위치.
// 출력: catch.json — tools/build_anim.py BAKE_CATCH.
// 실행: -executeMethod K2Tools.K2AnimBakeCatch.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clip <AK 클립>:<프레임 수> -k2Release <x,y,z 무기 좌표>
//        -k2DonorBody <키> -k2DonorAnim <키> -k2DonorClip <이름> -k2DonorDeps <키;…> -k2Out <폴더> [-k2Press <초>] [-k2Stretch <배>] [-k2Exit <정규화>] [-k2ByHand 1] [-k2SmoothR <초>]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimBakeCatch
    {
        [Serializable] public class Bone { public string name; public Vector3 rot; }
        [Serializable] public class Frame { public float time, donorTime, weight; public Vector3 weaponPos, weaponRot, lPos, lRot, boltPos; public List<Bone> fingers = new List<Bone>(); }
        [Serializable] public class Report { public bool passed; public string error, clip, donor; public float akRelease, donorRelease, donorStartToHandMm, donorEndToHandMm; public Vector3 donorCatch, k2Release; public List<Frame> frames = new List<Frame>(); }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        const string Anim = "assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle";
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));


        static float[] Gauss(int i, int n, int rad, out int lo) { lo = Mathf.Max(0, i - rad); int hi = Mathf.Min(n - 1, i + rad); var w = new float[hi - lo + 1]; for (int j = lo; j <= hi; j++) w[j - lo] = Mathf.Exp(-.5f * Mathf.Pow((j - i) / (rad / 2f), 2)); return w; }
        static void Smooth3(Vector3[] P, Quaternion[] Q, float sec)
        {
            int n = P.Length, rad = Mathf.RoundToInt(sec * 30); if (rad <= 0) return; var P0 = (Vector3[])P.Clone(); var Q0 = (Quaternion[])Q.Clone();
            for (int i = 1; i < n - 1; i++)
            {
                var w = Gauss(i, n, rad, out int lo); Vector3 ps = Vector3.zero; Vector4 qs = Vector4.zero; float ws = 0;
                for (int k = 0; k < w.Length; k++) { var q = Q0[lo + k]; if (Quaternion.Dot(q, Q0[i]) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); ps += P0[lo + k] * w[k]; qs += new Vector4(q.x, q.y, q.z, q.w) * w[k]; ws += w[k]; }
                float edge = Mathf.Min(1, Mathf.Min(i, n - 1 - i) / (float)rad); qs /= ws;
                P[i] = Vector3.Lerp(P0[i], ps / ws, edge); Q[i] = Quaternion.Slerp(Q0[i], new Quaternion(qs.x, qs.y, qs.z, qs.w).normalized, edge);
            }
        }
        static void SmoothF(float[] F, float sec)
        {
            int n = F.Length, rad = Mathf.RoundToInt(sec * 30); if (rad <= 0) return; var F0 = (float[])F.Clone();
            for (int i = 1; i < n - 1; i++) { var w = Gauss(i, n, rad, out int lo); float s = 0, ws = 0; for (int k = 0; k < w.Length; k++) { s += F0[lo + k] * w[k]; ws += w[k]; } F[i] = Mathf.Lerp(F0[i], s / ws, Mathf.Min(1, Mathf.Min(i, n - 1 - i) / (float)rad)); }
        }

        public static void Run()
        {
            var rep = new Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                var mod = A("-k2Mod"); var game = A("-k2Game"); var loaded = new Dictionary<string, AssetBundle>();
                AssetBundle Load(string k) { if (loaded.TryGetValue(k, out var b)) return b; var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k); b = AssetBundle.LoadFromFile(p); if (b) loaded[k] = b; return b; }
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(mod, "bundles.json")), "\"(assets/[^\"]+|shaders|cubemaps)\"")) Load(m.Groups[1].Value);
                foreach (var k in A("-k2DonorDeps").Split(';').Where(x => x != "")) Load(k);
                var cs = A("-k2Clip").Split(':'); int n = int.Parse(cs[1]); rep.clip = cs[0]; rep.donor = A("-k2DonorClip");
                var rv = A("-k2Release").Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); rep.k2Release = new Vector3(rv[0], rv[1], rv[2]);

                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var weapon = Find(model.transform, "weapon"); var bolt = Find(model.transform, "weapon_bolt"); var lref = Find(model.transform, "weapon_L_hand_marker");
                var dwp = Load(A("-k2DonorBody")).LoadAllAssets<GameObject>().Select(g => g.GetComponent<WeaponPrefab>()).First(w => w != null);
                var dmodel = UnityEngine.Object.Instantiate(dwp._weaponObject); dmodel.GetComponent<Animator>().enabled = false;
                var dweapon = Find(dmodel.transform, "weapon"); var dbolt = Find(dmodel.transform, "weapon_bolt"); var dcatch = Find(dmodel.transform, "weapon_bolt_catch"); var dl = Find(dmodel.transform, "weapon_L_IK_marker"); var dhand = Find(dmodel.transform, "weapon_L_hand_marker");
                var dpalm = Find(dmodel.transform, "Base HumanLPalm"); var dfingers = dpalm.GetComponentsInChildren<Transform>().Where(t => t != dpalm).ToArray();
                var clip = loaded[Anim].LoadAllAssets<AnimationClip>().Single(c => c.name == rep.clip);
                var dclip = Load(A("-k2DonorAnim")).LoadAllAssets<AnimationClip>().Single(c => c.name == rep.donor);

                // AK: 노리쇠 최대 후퇴 프레임(이후 앞으로)
                var times = Enumerable.Range(0, n).Select(i => Mathf.Min(i / 30f, clip.length)).ToArray();
                clip.SampleAnimation(model, 0); var b0 = bolt.localPosition; var kP0 = weapon.position; var kR0 = weapon.rotation;
                var boltK = times.Select(t => { clip.SampleAnimation(model, t); return bolt.localPosition; }).ToArray();
                int kr = Enumerable.Range(0, n).OrderByDescending(i => (boltK[i] - b0).magnitude).First(); rep.akRelease = times[kr]; var boltRear = boltK[kr];
                // M4: 노리쇠가 처음 2mm 움직인 시각(멈치에 걸려 있다가 앞으로)
                int dn = Mathf.CeilToInt(dclip.length * 60); dclip.SampleAnimation(dmodel, 0); var db0 = dbolt.localPosition; var dP0 = dweapon.position; var dR0 = dweapon.rotation;
                rep.donorCatch = dweapon.InverseTransformPoint(dcatch.position); var lm0 = dl.localPosition; float dr = dclip.length;
                for (int i = 0; i <= dn; i++) { float t = Mathf.Min(i / 60f, dclip.length); dclip.SampleAnimation(dmodel, t); if ((dbolt.localPosition - db0).magnitude > .002f) { dr = t; break; } }
                rep.donorRelease = dr;
                float press = A("-k2Press") == null ? rep.akRelease : float.Parse(A("-k2Press"), CultureInfo.InvariantCulture);
                float stretch = A("-k2Stretch") == null ? 1.4f : float.Parse(A("-k2Stretch"), CultureInfo.InvariantCulture);
                float exitT = A("-k2Exit") == null ? clip.length : float.Parse(A("-k2Exit"), CultureInfo.InvariantCulture) * clip.length;
                dclip.SampleAnimation(dmodel, dclip.length); var lmEnd = dl.localPosition;
                dclip.SampleAnimation(dmodel, 0); rep.donorStartToHandMm = Vector3.Distance(dl.localPosition, dhand.localPosition) * 1000;
                dclip.SampleAnimation(dmodel, dclip.length); rep.donorEndToHandMm = Vector3.Distance(dl.localPosition, dhand.localPosition) * 1000;
                bool byHand = A("-k2ByHand") != null;   // 0.9.10: 손잡이에 붙음 = M4 손이 자기 손 기준점에서 3~10cm(게임 GripPose 10cm와 같은 뜻) — 시작 자세 기준이면 M4가 탄창 넣은 자리에서 출발해도 "손잡이 잡고 시작"이 됐다
                var shift = rep.k2Release - rep.donorCatch;
                float smoothR = A("-k2SmoothR") == null ? 0 : float.Parse(A("-k2SmoothR"), CultureInfo.InvariantCulture);
                var WP = new Vector3[n]; var WR = new Quaternion[n]; var LP = new Vector3[n]; var LR = new Quaternion[n]; var C = new float[n];
                for (int i = 0; i < n; i++)
                {
                    // 0.9.9: 누르는 순간 = press(기본 AK 상태 이벤트 BoltOut 0.478 = 0.76초 — 금속 소리), 앞 대기 짧게(접근 M4의 stretch배),
                    // 상태가 빠져나가는 exitT(RELOAD CHARGE END → IDLE 0.92) 전에 M4 끝까지 — 0.9.8은 0.52초 손잡이 대기 후 1.4배 접근이라 "손잡이 잡고 → 멈치"
                    float t = times[i]; float app = Mathf.Min(press, stretch * dr), t0 = press - app;
                    float tm = t <= t0 ? 0 : t <= press ? Mathf.Lerp(0, dr, (t - t0) / Mathf.Max(1e-4f, app)) : Mathf.Lerp(dr, dclip.length, Mathf.Clamp01((t - press) / Mathf.Max(1e-4f, exitT - press)));
                    dclip.SampleAnimation(dmodel, tm);
                    var dRot = dweapon.rotation * Quaternion.Inverse(dR0); var bRot = dRot * kR0; var bPos = dweapon.position + dRot * (kP0 - dP0);
                    C[i] = byHand ? Smooth(.03f, .15f, Vector3.Distance(dl.localPosition, dhand.localPosition)) : Smooth(.03f, .08f, Mathf.Min(Vector3.Distance(dl.localPosition, lm0), Vector3.Distance(dl.localPosition, lmEnd)));
                    LP[i] = dl.localPosition + shift; LR[i] = dl.localRotation;
                    var fr = new Frame { time = t, donorTime = tm };
                    foreach (var f in dfingers) fr.fingers.Add(new Bone { name = f.name, rot = f.localEulerAngles });
                    float e = Smooth(exitT - .15f, exitT, t);
                    WP[i] = Vector3.Lerp(bPos, kP0, e); WR[i] = Quaternion.Slerp(bRot, kR0, e);   // 끝: 대기 자세(첫 프레임) — 클립 원래 자세가 아니라
                    if (t <= press) fr.boltPos = boltRear; else { clip.SampleAnimation(model, Mathf.Min(clip.length, rep.akRelease + (t - press))); fr.boltPos = bolt.localPosition; }   // 노리쇠 앞으로 = 누르는 순간부터 AK 앞 행정
                    rep.frames.Add(fr);
                }
                // 0.9.11 시간 평활(가우스, -k2SmoothR 초): 손잡이로 돌아갈 때 손 회전 959°/s 튐·1.2초에 툭 멈춤, 총 끝부분 속도 튐(인게임 "프레임 모자람")
                Smooth3(WP, WR, smoothR); Smooth3(LP, LR, smoothR); SmoothF(C, smoothR);
                for (int i = 0; i < n; i++)
                {
                    var fr = rep.frames[i]; clip.SampleAnimation(model, times[i]);
                    weapon.SetPositionAndRotation(WP[i], WR[i]); fr.weaponPos = weapon.localPosition; fr.weaponRot = weapon.localEulerAngles; fr.weight = C[i];
                    fr.lPos = Vector3.Lerp(lref.localPosition, LP[i], C[i]); fr.lRot = Quaternion.Slerp(lref.localRotation, LR[i], C[i]).eulerAngles;
                }
                rep.passed = true;
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(Path.Combine(outDir, "catch.json"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(rep.passed ? 0 : 1);
        }
    }
}
