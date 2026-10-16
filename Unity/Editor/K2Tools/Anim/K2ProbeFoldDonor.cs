// 접기 도너(바닐라 G36 등) 조사: 접기 클립을 재생하며 개머리판 회전 노드와 오른손 IK 마커의 상대 자세를 기록한다.
// 결과(JSON): 프레임별 시간·접힘 각도·마커 위치/회전(회전 노드 기준)·마커 위치(무기 기준)·왼손 마커–기준점 거리.
// 실행: -executeMethod K2Tools.K2ProbeFoldDonor.Run -k2Game <Windows> -k2Body <컨테이너 키> -k2Anim <클립 번들 키> -k2Clip <이름> -k2Deps <키;키…> -k2Out <json>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2ProbeFoldDonor
    {
        [Serializable] public class Frame { public float time, angle; public Vector3 rLocalPos, rLocalRot, rWeaponPos, lMarkerToRef; }
        [Serializable] public class Report { public string error, rotatingNode; public Vector3 pivotWeapon, axisWeapon; public List<Frame> frames = new List<Frame>(); public List<string> nodes = new List<string>(); }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }

        public static void Run()
        {
            var rep = new Report();
            try
            {
                var game = A("-k2Game");
                foreach (var k in (A("-k2Deps") ?? "").Split(';').Where(x => x != "")) AssetBundle.LoadFromFile(Path.Combine(game, k));
                var body = AssetBundle.LoadFromFile(Path.Combine(game, A("-k2Body")));
                var anim = A("-k2Anim") == A("-k2Body") ? body : AssetBundle.LoadFromFile(Path.Combine(game, A("-k2Anim")));
                var wp = body.LoadAllAssets<GameObject>().Select(g => g.GetComponent<WeaponPrefab>()).First(w => w != null);
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var all = model.GetComponentsInChildren<Transform>(true);
                Transform F(string n) => all.First(t => t.name == n);
                var weapon = F("weapon"); var rm = F("weapon_R_IK_marker"); var lm = F("weapon_L_IK_marker"); var lr = F("weapon_L_hand_marker");
                var clip = anim.LoadAllAssets<AnimationClip>().First(c => c.name == A("-k2Clip"));
                var slot = F("mod_stock");
                // 회전 노드: mod_stock과 그 아래(접기 축 노드) 중 클립 동안 회전이 가장 큰 것
                var cands = slot.GetComponentsInChildren<Transform>(true).ToArray();
                clip.SampleAnimation(model, 0); var r0 = cands.ToDictionary(t => t, t => Quaternion.Inverse(weapon.rotation) * t.rotation);
                var maxAng = cands.ToDictionary(t => t, t => 0f);
                for (int i = 0; i <= 30; i++) { clip.SampleAnimation(model, clip.length * i / 30f); foreach (var t in cands) maxAng[t] = Mathf.Max(maxAng[t], Quaternion.Angle(r0[t], Quaternion.Inverse(weapon.rotation) * t.rotation)); }
                foreach (var t in cands) rep.nodes.Add($"{t.name} 최대각 {maxAng[t]:F1}");
                var rot = cands.OrderByDescending(t => maxAng[t]).First(); rep.rotatingNode = rot.name;
                int n = Mathf.CeilToInt(clip.length * 30);
                for (int i = 0; i <= n; i++)
                {
                    float tm = Mathf.Min(i / 30f, clip.length); clip.SampleAnimation(model, tm);
                    rep.frames.Add(new Frame { time = tm, angle = Quaternion.Angle(r0[rot], Quaternion.Inverse(weapon.rotation) * rot.rotation),
                        rLocalPos = rot.InverseTransformPoint(rm.position), rLocalRot = (Quaternion.Inverse(rot.rotation) * rm.rotation).eulerAngles,
                        rWeaponPos = weapon.InverseTransformPoint(rm.position), lMarkerToRef = new Vector3(Vector3.Distance(lm.position, lr.position), 0, 0) });
                }
                rep.pivotWeapon = weapon.InverseTransformPoint(rot.position);
                clip.SampleAnimation(model, clip.length * .5f); var q = Quaternion.Inverse(r0[rot]) * (Quaternion.Inverse(weapon.rotation) * rot.rotation); q.ToAngleAxis(out _, out var ax); rep.axisWeapon = r0[rot] * ax;
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(A("-k2Out"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(0);
        }
    }
}
