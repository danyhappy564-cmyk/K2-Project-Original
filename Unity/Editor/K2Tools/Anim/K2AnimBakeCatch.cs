// 빈 탄창 장전 한 가지(AK reload_charge_2 — 왼손이 위로 넘어가 오른쪽 장전손잡이를 당김)를 M4식 "왼손으로 노리쇠 멈치 때리기"로 바꾼다.
// 사용자(0.9.5 인게임): K2는 마지막 탄 후 노리쇠가 열린 채 멈추고 멈치를 눌러 닫는다 — 장전손잡이를 당기거나 멈치를 때리거나 둘 중 하나.
// 도너 = 바닐라 M4A1 m4a1_reload_catch_0. 시간 대응: 노리쇠가 앞으로 가기 시작하는 순간(AK: 최대 후퇴 프레임, M4: 처음 2mm 움직인 프레임)을 맞춘 구간 선형
// (상태의 소리 이벤트가 AK 시각 기준이라 맞춤). M4는 0.32초에 누르고 AK는 0.97초에 풀려 — 접근은 1.4배까지만 늘리고 그 앞은 손잡이에서 대기.
//  - 총(weapon): M4 대기 자세 대비 움직임을 K2 대기 자세에 적용, 끝 0.15초는 K2 원래 자세로.
//  - 왼손 IK 마커(무기 기준): M4 마커 + (K2 멈치 − M4 멈치). M4 손이 자기 대기 자세에서 3~8cm 벗어난 정도(c)로 K2 왼손 기준점(손잡이)과 섞음.
//  - 왼손가락: M4 값 그대로(손잡이 근처에서는 게임이 GripPose로 덮음). 노리쇠: 앞으로 가는 순간까지 최대 후퇴 위치.
// 출력: catch.json — tools/build_anim.py BAKE_CATCH.
// 실행: -executeMethod K2Tools.K2AnimBakeCatch.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Clip <AK 클립>:<프레임 수> -k2Release <x,y,z 무기 좌표>
//        -k2DonorBody <키> -k2DonorAnim <키> -k2DonorClip <이름> -k2DonorDeps <키;…> -k2Out <폴더>
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
        [Serializable] public class Report { public bool passed; public string error, clip, donor; public float akRelease, donorRelease; public Vector3 donorCatch, k2Release; public List<Frame> frames = new List<Frame>(); }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        const string Anim = "assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle";
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));

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
                var dweapon = Find(dmodel.transform, "weapon"); var dbolt = Find(dmodel.transform, "weapon_bolt"); var dcatch = Find(dmodel.transform, "weapon_bolt_catch"); var dl = Find(dmodel.transform, "weapon_L_IK_marker");
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
                dclip.SampleAnimation(dmodel, dclip.length); var lmEnd = dl.localPosition;
                var shift = rep.k2Release - rep.donorCatch;
                for (int i = 0; i < n; i++)
                {
                    float t = times[i]; float app = Mathf.Min(rep.akRelease, 1.4f * dr), t0 = rep.akRelease - app;   // 접근은 M4의 1.4배까지만 늘리고 그 앞은 대기(손잡이)
                    float tm = t <= t0 ? 0 : t <= rep.akRelease ? Mathf.Lerp(0, dr, (t - t0) / Mathf.Max(1e-4f, app)) : Mathf.Lerp(dr, dclip.length, (t - rep.akRelease) / Mathf.Max(1e-4f, clip.length - rep.akRelease));
                    dclip.SampleAnimation(dmodel, tm);
                    var dRot = dweapon.rotation * Quaternion.Inverse(dR0); var bRot = dRot * kR0; var bPos = dweapon.position + dRot * (kP0 - dP0);
                    float c = Smooth(.03f, .08f, Mathf.Min(Vector3.Distance(dl.localPosition, lm0), Vector3.Distance(dl.localPosition, lmEnd)));
                    var lPos = dl.localPosition + shift; var lRot = dl.localRotation;
                    var fr = new Frame { time = t, donorTime = tm, weight = c };
                    foreach (var f in dfingers) fr.fingers.Add(new Bone { name = f.name, rot = f.localEulerAngles });
                    clip.SampleAnimation(model, t);
                    float e = Smooth(clip.length - .15f, clip.length, t);
                    weapon.SetPositionAndRotation(Vector3.Lerp(bPos, weapon.position, e), Quaternion.Slerp(bRot, weapon.rotation, e));
                    fr.weaponPos = weapon.localPosition; fr.weaponRot = weapon.localEulerAngles;
                    fr.lPos = Vector3.Lerp(lref.localPosition, lPos, c); fr.lRot = Quaternion.Slerp(lref.localRotation, lRot, c).eulerAngles;
                    fr.boltPos = i <= kr ? boltRear : boltK[i];
                    rep.frames.Add(fr);
                }
                rep.passed = true;
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(Path.Combine(outDir, "catch.json"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(rep.passed ? 0 : 1);
        }
    }
}
