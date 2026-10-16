// K2: 프로젝트 안 프리팹을 검사 — 빠진 스크립트, 애니메이터 컨트롤러, 주요 컴포넌트를 로그로 남긴다.
// 실행: Unity -batchmode -projectPath <SDK> -executeMethod K2Tools.K2Check.Prefabs -k2Paths "Assets/a.prefab;Assets/b.prefab" -k2Out <txt>
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static class K2Check
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        // 미리보기: -k2Paths "주 프리팹;겹쳐 볼 프리팹(선택)" -k2Out <png>. 주 프리팹은 텍스처 회색, 겹쳐 볼 것은 반투명 빨강. 측면 직교.
        // -nographics 없이 실행해야 렌더링된다.
        public static void Preview()
        {
            var paths = Arg("-k2Paths").Split(';');
            var std = Shader.Find("Standard");
            var objs = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < paths.Length; i++)
            {
                var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]));
                foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.Contains("LOD1")) { r.enabled = false; continue; }
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        var n = new Material(std);
                        if (i == 0) { n.mainTexture = m && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null; n.color = Color.white; }
                        else
                        {
                            n.color = new Color(1, 0.1f, 0.1f, 0.35f);
                            n.SetFloat("_Mode", 3); n.SetInt("_SrcBlend", 5); n.SetInt("_DstBlend", 10); n.SetInt("_ZWrite", 0);
                            n.EnableKeyword("_ALPHABLEND_ON"); n.renderQueue = 3000;
                        }
                        return n;
                    }).ToArray();
                }
                objs.Add(go);
            }
            var b = objs.SelectMany(o => o.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).Select(r => r.bounds)
                .Aggregate((a, c) => { a.Encapsulate(c); return a; });
            var camGo = new GameObject("cam"); var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(b.size.y, b.size.z / 1.8f) * 0.6f;
            cam.transform.position = b.center + Vector3.right * 2f; cam.transform.LookAt(b.center);   // 오른쪽에서 왼쪽을 봄(앞 +Z가 화면 왼쪽)
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.27f);
            var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(30, -60, 0);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
            var rt = new RenderTexture(1800, 1000, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(1800, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1800, 1000), 0, 0); tex.Apply();
            File.WriteAllBytes(Arg("-k2Out"), tex.EncodeToPNG());
            Debug.Log("K2PREVIEW OK " + b.size);
        }

        // 부품 부착 규칙 실험: 바닐라 M4(추출본)를 규칙 A/B/C로 조립해 한 장에 나란히 렌더 → 정상으로 보이는 규칙이 게임 규칙.
        // A: 슬롯에 그대로(로컬 회전 0) / B: 슬롯에서 X +90° / C: 슬롯에서 X -90°
        public static void AttachRuleTest()
        {
            const string M4 = "Assets/K2Ripped/M4A1/Content/Weapons/m4a1/weapon_colt_m4a1_556x45_model.generated.prefab";
            const string P = "Assets/K2Ripped/M4Parts/Content/Items/Mods/";
            var chain = new (string id, string path, string parent, string slot)[]
            {
                ("upper", P + "recievers/reciever_ar15_colt_m4a1_std.prefab", "body", "mod_reciever"),
                ("barrel", P + "barrels/barrel_ar15_370mm.prefab", "upper", "mod_barrel"),
                ("handguard", P + "handguards/handguard_ar15_colt_m4_length_std.prefab", "upper", "mod_handguard"),
                ("muzzle", P + "muzzle/muzzle_ar15_colt_usgi_a2_556x45.prefab", "barrel", "mod_muzzle"),
                ("stock", P + "stocks/stock_ar15_colt_stock_tube_std.prefab", "body", "mod_stock"),
                ("mag", P + "magazines/mag_stanag_colt_ar15_std_556x45_30.prefab", "body", "mod_magazine"),
            };
            var rules = new[] { Quaternion.identity, Quaternion.Euler(90, 0, 0), Quaternion.Euler(-90, 0, 0) };
            var std = Shader.Find("Standard");
            var roots = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < rules.Length; i++)
            {
                var body = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(M4));
                body.transform.position = new Vector3(0, 0, i * 1.2f);
                var placed = new System.Collections.Generic.Dictionary<string, Transform> { ["body"] = body.transform };
                foreach (var c in chain)
                {
                    var slot = placed[c.parent].GetComponentsInChildren<Transform>(true).First(t => t.name == c.slot);
                    var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(c.path), slot, false);
                    go.transform.localPosition = Vector3.zero; go.transform.localRotation = rules[i];
                    placed[c.id] = go.transform;
                }
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.Contains("LOD1")) { r.enabled = false; continue; }
                    var col = new[] { Color.white, new Color(1f, .6f, .6f), new Color(.6f, .8f, 1f) }[i];
                    r.sharedMaterials = r.sharedMaterials.Select(m => { var n = new Material(std); n.color = col; return n; }).ToArray();
                }
                roots.Add(body);
                // 규칙마다 부품 위치를 로그로
                foreach (var kv in placed)
                {
                    var rs = kv.Value.GetComponentsInChildren<Renderer>().Where(x => x.enabled && (kv.Key == "body" ? x.transform.parent && x.transform.parent.name == "weapon" : true)).ToArray();
                    if (rs.Length == 0) continue;
                    var b = rs.Select(x => x.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                    Debug.Log($"K2RULE {"ABC"[i]} {kv.Key,-9} center {b.center - body.transform.position} size {b.size}");
                }
            }
            var all = roots.SelectMany(o => o.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(all.size.y, all.size.z / 1.8f) * 0.55f;
            cam.transform.position = all.center + Vector3.right * 3f; cam.transform.LookAt(all.center);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.2f, 0.22f);
            var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(30, -60, 0);
            var rt = new RenderTexture(2400, 1100, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(2400, 1100, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 2400, 1100), 0, 0); tex.Apply();
            File.WriteAllBytes(Arg("-k2Out"), tex.EncodeToPNG());
            Debug.Log("K2RULE OK");
        }

        // 번들 재질 진단: -k2Paths "의존1;의존2;...;본번들"(마지막이 본 번들, 절대 경로) → 렌더러 재질의 셰이더 이름·텍스처 유무
        public static void BundleMaterials()
        {
            var paths = Arg("-k2Paths").Split(';');
            var loaded = paths.Select(p => AssetBundle.LoadFromFile(p)).ToList();
            var sb = new StringBuilder();
            try
            {
                var main = loaded.Last();
                foreach (var go in main.LoadAllAssets<GameObject>())
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                            sb.AppendLine($"{r.name}\t{(m ? m.name : "NULL")}\tshader={(m && m.shader ? m.shader.name + (m.shader.isSupported ? "" : "(unsupported)") : "NULL")}\tMainTex={(m && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") ? m.GetTexture("_MainTex").name : "-")}");
            }
            finally { foreach (var b in loaded) if (b) b.Unload(true); }
            File.WriteAllText(Arg("-k2Out"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("K2MAT OK");
        }

        // 셰이더 외부 참조 시험: SDK 셰이더를 "shaders" 번들로, 재질 하나를 다른 번들로 만들어 참조 ID를 확인한다.
        public static void ShaderRefTest()
        {
            var outDir = Arg("-k2Out"); Directory.CreateDirectory(outDir);
            var builds = new[]
            {
                new AssetBundleBuild { assetBundleName = "shaders", assetNames = new[] { "Assets/Shaders/Bumped Specular SMap.shader" } },
                new AssetBundleBuild { assetBundleName = "k2_shader_test.bundle", assetNames = new[] { "Assets/Content/Weapons/daewoo_k2/Materials/weapon_daewoo_k2_556x45_upper.mat" } },
            };
            BuildPipeline.BuildAssetBundles(outDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            Debug.Log("K2SHADERTEST OK");
        }

        public static void Prefabs()
        {
            var sb = new StringBuilder();
            foreach (var p in Arg("-k2Paths").Split(';'))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (!go) { sb.AppendLine("열기 실패 " + p); continue; }
                int missing = go.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                sb.AppendLine($"## {p}  missingScripts={missing}");
                foreach (var c in go.GetComponents<Component>()) sb.AppendLine("  root: " + (c ? c.GetType().Name : "NULL"));
                foreach (var an in go.GetComponentsInChildren<Animator>(true))
                    sb.AppendLine($"  Animator@{an.name}: controller={(an.runtimeAnimatorController ? an.runtimeAnimatorController.name + " clips=" + an.runtimeAnimatorController.animationClips.Length : "없음")} avatar={(an.avatar ? an.avatar.name : "없음")}");
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine($"  Renderer@{r.name}: " + string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + (m.shader ? m.shader.name : "?") : "null")));
            }
            File.WriteAllText(Arg("-k2Out"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("K2CHECK OK");
        }
    }
}
