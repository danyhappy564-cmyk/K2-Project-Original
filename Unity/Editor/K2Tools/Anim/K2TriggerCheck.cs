// 기본 자세에서 오른손 검지가 K2 방아쇠에 닿는지 — 검지 끝·방아쇠 메시 위치(무기 좌표)와 확대 렌더.
// 실행: -executeMethod K2Tools.K2TriggerCheck.Run -k2Mod <번들 폴더(bundles.json 위)> -k2Game <StreamingAssets/Windows> -k2Clip <클립 이름> -k2Out <폴더> -k2Label <이름>
//        [-k2Look <노드, 기본 weapon_trigger>] [-k2Cam <무기 좌표 시점 방향 x,y,z>] [-k2Size <화면 반높이 m>] [-k2Time <0~1 클립 비율>]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2TriggerCheck
    {
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static readonly Dictionary<string, AssetBundle> loaded = new Dictionary<string, AssetBundle>();
        static string mod, game;
        static AssetBundle Load(string key)
        {
            if (loaded.TryGetValue(key, out var b)) return b;
            var p = Path.Combine(mod, key); if (!File.Exists(p)) p = Path.Combine(mod, "bundles", key); if (!File.Exists(p)) p = Path.Combine(game, key);
            var ab = AssetBundle.LoadFromFile(p); if (!ab) throw new Exception("LOAD " + key); return loaded[key] = ab;
        }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
        static readonly string[] partKeys = { "assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle", "assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle", "assets/content/items/mods/handguards/handguard_daewoo_k2_mits_long.bundle", "assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle", "assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle" };
        static readonly string[] slots = { "mod_reciever", "mod_barrel", "mod_handguard", "mod_stock", "mod_magazine" };

        public static void Run()
        {
            var log = new System.Text.StringBuilder(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                mod = A("-k2Mod"); game = A("-k2Game"); var label = A("-k2Label") ?? "k2";
                var json = File.ReadAllText(Path.Combine(mod, "bundles.json"));
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"(assets/[^\"]+|shaders|cubemaps)\""))
                    try { Load(m.Groups[1].Value); } catch (Exception e) { log.AppendLine("의존 실패 " + e.Message); }
                var key = "assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle";
                var wp = Load(key).LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var clips = loaded.Values.SelectMany(b => b.LoadAllAssets<AnimationClip>()).ToArray();
                var name = A("-k2Clip") ?? "ak100_idle_armed";
                // 우리 override 번들의 같은 이름 클립이 있으면 그것(수정본), 없으면 바닐라
                var ours = loaded.Where(p => p.Key.Contains("daewoo_k2/anim_")).SelectMany(p => p.Value.LoadAllAssets<AnimationClip>()).FirstOrDefault(c => c.name == name);
                var clip = ours ? ours : clips.First(c => c.name == name);
                log.AppendLine($"[{label}] 클립 {name} ({(ours ? "수정본" : "바닐라")}) 길이 {clip.length:F3}");
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                for (int i = 0; i < partKeys.Length; i++)
                {
                    var prefab = Load(partKeys[i]).LoadAllAssets<GameObject>().Single(); var go = UnityEngine.Object.Instantiate(prefab);
                    go.transform.SetParent(Find(model.transform, slots[i]), false); go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.Euler(90, 0, 0) * prefab.transform.localRotation;
                }
                float frac = float.TryParse(A("-k2Time"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fr) ? fr : 0f;
                clip.SampleAnimation(model, clip.length * frac);
                var weapon = Find(model.transform, "weapon");
                Vector3 W(Vector3 p) => weapon.InverseTransformPoint(p);
                foreach (var n in new[] { "Base HumanRDigit21", "Base HumanRDigit22", "Base HumanRDigit23", "Base HumanRPalm", "weapon_trigger", "aim_camera" })
                    log.AppendLine($"  {n,-20} 무기 좌표 {W(Find(model.transform, n).position).ToString("F4")}");
                var trig = Find(model.transform, "weapon_trigger").GetComponentsInChildren<MeshRenderer>(true).Where(r => r.enabled && !r.name.Contains("LOD1")).ToArray();
                foreach (var r in trig) { var b = r.bounds; log.AppendLine($"  방아쇠 메시 {r.name}: 무기 좌표 범위 {W(b.min).ToString("F4")} .. {W(b.max).ToString("F4")}"); }
                var tip = Find(model.transform, "Base HumanRDigit23"); var d3 = tip.childCount > 0 ? tip.GetChild(0) : tip;
                log.AppendLine($"  검지 끝(자식) {d3.name} {W(d3.position).ToString("F4")}");

                // 확대 렌더: 오른쪽에서 방아쇠 주변
                RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
                var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.transform.rotation = Quaternion.Euler(35, -50, 0);
                var mat = new Material(Shader.Find("Standard")) { color = new Color(.35f, .37f, .4f) }; var red = new Material(Shader.Find("Standard")) { color = new Color(1f, .45f, .1f) };
                foreach (var mr in model.GetComponentsInChildren<MeshRenderer>(true)) { if (mr.name.Contains("LOD1")) { mr.enabled = false; continue; } mr.sharedMaterials = mr.sharedMaterials.Select(_ => mr.transform.IsChildOf(Find(model.transform, "weapon_trigger")) ? red : mat).ToArray(); }
                foreach (var lod in model.GetComponentsInChildren<LODGroup>(true)) lod.ForceLOD(0);
                var hand = new Material(Shader.Find("Standard")) { color = new Color(.2f, .85f, 1f) };
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("Base HumanR") || t.parent == null || !t.parent.name.StartsWith("Base HumanR")) continue;
                    var a = t.parent.position; var bpos = t.position; var dd = bpos - a; if (dd.magnitude < 1e-5f) continue;
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.transform.position = (a + bpos) / 2; c.transform.rotation = Quaternion.FromToRotation(Vector3.up, dd.normalized);
                    float rad = t.name.Contains("Digit") ? .003f : .007f; c.transform.localScale = new Vector3(rad * 2, dd.magnitude / 2, rad * 2); c.GetComponent<Renderer>().sharedMaterial = hand;
                }
                var cam = new GameObject("C").AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = float.TryParse(A("-k2Size"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sz) ? sz : .09f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.06f, .07f, .09f); cam.nearClipPlane = .01f; cam.farClipPlane = 5;
                var look = Find(model.transform, A("-k2Look") ?? "weapon_trigger").position;
                var cv = (A("-k2Cam") ?? "0.6,0,0").Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var dir = new Vector3(cv[0], cv[1], cv[2]); var up = Mathf.Abs(dir.normalized.z) > .9f ? Vector3.down : Vector3.forward;
                cam.transform.position = look + weapon.TransformVector(dir); cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position, weapon.TransformDirection(up));
                var rt = new RenderTexture(900, 700, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                var tex = new Texture2D(900, 700, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 900, 700), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, $"{label}-{name}-{frac:0.##}.png"), tex.EncodeToPNG()); RenderTexture.active = null;
            }
            catch (Exception e) { log.AppendLine("FAIL " + e); }
            File.AppendAllText(Path.Combine(outDir, "trigger-check.txt"), log.ToString());
            EditorApplication.Exit(0);
        }
    }
}
