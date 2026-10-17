// K201 런처 조준 시점 확인: K2 + GL 핸드가드 + K201을 조립해 런처 mod_aim_camera(또는 -k2Cam로 준 위치) 시점에서 화면 중앙 십자선과 함께 렌더,
// 옆·위 정사영도 함께. 0.9.8 인게임 "유탄 탄착 왼쪽 아래" — Codex가 눈 위치를 조준기 밖으로 옮겨 보이는 조준기와 화면 중앙이 어긋났다.
// 실행: -executeMethod K2Tools.K2GLAimView.Run -k2Mod <번들 폴더> -k2Game <Windows> -k2Out <폴더> [-k2Cam "x,y,z;x,y,z…"(런처 모델 기준)]
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
    public static class K2GLAimView
    {
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

        public static void Run()
        {
            var log = new List<string>(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                var mod = A("-k2Mod"); var game = A("-k2Game"); var loaded = new Dictionary<string, AssetBundle>();
                AssetBundle Load(string k) { if (loaded.TryGetValue(k, out var b)) return b; var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k); b = AssetBundle.LoadFromFile(p); if (b) loaded[k] = b; return b; }
                foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(mod, "bundles.json")), "\"(assets/[^\"]+|shaders|cubemaps)\"")) Load(m.Groups[1].Value);
                GameObject Part(string key, Transform slot)
                {
                    var prefab = Load(key).LoadAllAssets<GameObject>().First(g => g.transform.parent == null);
                    var go = UnityEngine.Object.Instantiate(prefab); go.transform.SetParent(slot, false); go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.Euler(90, 0, 0) * prefab.transform.localRotation; return go;
                }
                var wp = Load("assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle").LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var upper = Part("assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle", Find(model.transform, "mod_reciever"));
                Part("assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle", Find(upper.transform, "mod_barrel"));
                var hg = Part("assets/content/items/mods/handguards/handguard_daewoo_k2_gl.bundle", Find(upper.transform, "mod_handguard"));
                var lslot = Find(hg.transform, "mod_launcher"); log.Add("mod_launcher " + (lslot ? "있음" : "없음"));
                var lprefab = Load("assets/content/weapons/daewoo_k2/launcher_daewoo_k201_40x46_container.bundle").LoadAllAssets<GameObject>().First(g => g.GetComponent<WeaponPrefab>() != null);
                var lwp = lprefab.GetComponent<WeaponPrefab>();
                var lmodel = UnityEngine.Object.Instantiate(lwp._weaponObject); if (lmodel.GetComponent<Animator>()) lmodel.GetComponent<Animator>().enabled = false;
                // 런처 배치: 런처 모델의 Weapon_root_original_position(WROP) = K2 Weapon_root 자리
                var wrop = Find(lmodel.transform, "Weapon_root_original_position"); var kroot = Find(model.transform, "Weapon_root");
                var M = kroot.localToWorldMatrix * (lmodel.transform.worldToLocalMatrix * wrop.localToWorldMatrix).inverse;   // 런처 모델 루트 월드 행렬
                lmodel.transform.SetPositionAndRotation(M.GetColumn(3), M.rotation);
                log.Add("WROP 확인 오차mm " + (Vector3.Distance(wrop.position, kroot.position) * 1000).ToString("F3"));
                var cam0 = Find(lmodel.transform, "mod_aim_camera"); var fire = Find(lmodel.transform, "fireport");
                log.Add($"mod_aim_camera 로컬 {cam0.localPosition.ToString("F4")} 회전 {cam0.localEulerAngles.ToString("F2")}");
                log.Add($"fireport 로컬 {fire.localPosition.ToString("F4")} 앞(+y?) {lmodel.transform.InverseTransformDirection(fire.up).ToString("F3")} / z {lmodel.transform.InverseTransformDirection(fire.forward).ToString("F3")}");
                foreach (var ax in new[] { Vector3.forward, Vector3.back, Vector3.up, Vector3.down })
                    log.Add($"카메라 축 {ax} → 런처 {lmodel.transform.InverseTransformDirection(cam0.rotation * ax).ToString("F4")}");
                // 재질: 텍스처만 Standard로
                foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(src => { var m = new Material(Shader.Find("Standard")); if (src && src.HasProperty("_MainTex")) m.mainTexture = src.GetTexture("_MainTex"); m.color = new Color(.75f, .75f, .75f); return m; }).ToArray();
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.6f, .6f, .6f);
                var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(40, -30, 0);
                var cams = new List<Vector3> { cam0.localPosition };
                foreach (var c in (A("-k2Cam") ?? "").Split(';').Where(x => x.Contains(","))) { var v = c.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); cams.Add(new Vector3(v[0], v[1], v[2])); }
                // 카메라가 보는 방향: 런처 앞(fireport 쪽)과 가장 가까운 카메라 축
                var fwdL = (fire.localPosition - cam0.localPosition).normalized;
                var axis = new[] { Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.left, Vector3.right }.OrderByDescending(a => Vector3.Dot(lmodel.transform.InverseTransformDirection(cam0.rotation * a), fwdL)).First();
                var upAxis = new[] { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.Where(a => Mathf.Abs(Vector3.Dot(a, axis)) < .5f)
                    .OrderByDescending(a => Vector3.Dot(lmodel.transform.InverseTransformDirection(cam0.rotation * a), Vector3.up)).First();
                log.Add($"시선 축 카메라{axis} 위 축 카메라{upAxis} → 런처 시선 {lmodel.transform.InverseTransformDirection(cam0.rotation * axis).ToString("F4")}");
                int k = 0;
                foreach (var cp in cams)
                {
                    var cam = new GameObject("C").AddComponent<Camera>(); cam.nearClipPlane = .005f; cam.fieldOfView = 35; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.2f, .23f, .26f);
                    cam.transform.position = lmodel.transform.TransformPoint(cp); cam.transform.rotation = Quaternion.LookRotation(cam0.rotation * axis, cam0.rotation * upAxis);
                    Shot(cam, Path.Combine(outDir, $"aim-{k}.png"), true); log.Add($"aim-{k}: 런처 로컬 {cp.ToString("F4")}"); UnityEngine.Object.DestroyImmediate(cam.gameObject); k++;
                }
                // 옆·위 정사영(런처 기준)
                var center = lmodel.transform.TransformPoint(new Vector3(0, .03f, -.15f));
                foreach (var (nm, dir, up) in new[] { ("left", -lmodel.transform.right, lmodel.transform.up), ("top", lmodel.transform.up, lmodel.transform.forward), ("back", -lmodel.transform.forward, lmodel.transform.up) })
                {
                    var cam = new GameObject("C").AddComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = .2f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.2f, .23f, .26f); cam.nearClipPlane = .01f; cam.farClipPlane = 3;
                    cam.transform.position = center + dir * 1f; cam.transform.rotation = Quaternion.LookRotation(-dir, up);
                    Shot(cam, Path.Combine(outDir, $"ortho-{nm}.png"), false); UnityEngine.Object.DestroyImmediate(cam.gameObject);
                }
            }
            catch (Exception e) { log.Add("ERROR " + e); }
            File.WriteAllLines(Path.Combine(outDir, "aim-view.txt"), log);
            EditorApplication.Exit(0);
        }

        static void Shot(Camera cam, string path, bool cross)
        {
            const int W = 1280, H = 800; var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            if (cross) for (int i = -40; i <= 40; i++) { tex.SetPixel(W / 2 + i, H / 2, Color.red); tex.SetPixel(W / 2, H / 2 + i, Color.red); }
            tex.Apply(); File.WriteAllBytes(path, tex.EncodeToPNG()); RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
