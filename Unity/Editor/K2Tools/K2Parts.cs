// K2 전체 번들 빌더(본체 + 부품 6종 + 공용 텍스처) — 3단계.
// 실행: Unity -batchmode -quit -projectPath <SDK> -executeMethod K2Tools.K2Builder.BuildAll -k2Out <새 출력 폴더>
// (-nographics 없이 실행하면 조립 미리보기 PNG도 만든다)
//
// 좌표 규칙: "본체 루트 공간" = 본체 모델 프리팹 루트 기준(앞 +Z, 위 +Y). K2 부품 p는 본체 공간에서 (FBX 기준점 + 정렬 이동량)에 회전 없이 놓인다.
// 부품 프리팹 루트는 게임에서 부모 슬롯 위치에 붙으므로, 부품 안 노드 위치 = (부모 슬롯 행렬)^-1 × (본체 공간 목표).
// 템플릿은 M4 부품 추출본(K2Ripped/M4Parts). 슬롯 위치는 K2 부품 위치로 옮기되, 왼손 그립 자세는 M4A1 조립 때 위치를 유지한다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static partial class K2Builder
    {
        const string M4P = "Assets/K2Ripped/M4Parts/Content/Items/Mods/";
        const string MODS = "Assets/Content/Items/Mods/";
        const string SHARED_BUNDLE = "assets/content/weapons/daewoo_k2/daewoo_k2_textures.bundle";
        const string PLACEHOLDER_SHADERS = "shaders";
        const string SHADER_ASSET = "Assets/Shaders/Bumped Specular SMap.shader";

        class Part
        {
            public string Id, Template, Folder, Name, Mesh, Parent, Slot;
            public string PrefabPath => MODS + Folder + "/" + Name + ".prefab";
            public string Bundle => ("assets/content/items/mods/" + Folder + "/" + Name + ".bundle").ToLowerInvariant();
            public Matrix4x4 S;   // 부모 슬롯(= 이 부품 루트)의 본체 공간 행렬
        }

        static readonly Part[] PartDefs =
        {
            new Part { Id = "upper", Template = "recievers/reciever_ar15_colt_m4a1_std", Folder = "recievers", Name = "reciever_daewoo_k2_std", Mesh = "upper", Parent = "body", Slot = "mod_reciever" },
            new Part { Id = "barrel", Template = "barrels/barrel_ar15_370mm", Folder = "barrels", Name = "barrel_daewoo_k2_465mm", Mesh = "barrel_long", Parent = "upper", Slot = "mod_barrel" },
            new Part { Id = "handguard", Template = "handguards/handguard_ar15_colt_m4_length_std", Folder = "handguards", Name = "handguard_daewoo_k2_mits_long", Mesh = "handguard_long", Parent = "upper", Slot = "mod_handguard" },
            new Part { Id = "muzzle", Template = "muzzle/muzzle_ar15_colt_usgi_a2_556x45", Folder = "muzzle", Name = "muzzle_daewoo_k2_std_556x45", Mesh = "muzzle", Parent = "barrel", Slot = "mod_muzzle" },
            new Part { Id = "stock", Template = "stocks/stock_ar15_colt_stock_tube_std", Folder = "stocks", Name = "stock_daewoo_k2_std", Mesh = "stock", Parent = "body", Slot = "mod_stock" },
            new Part { Id = "magazine", Template = "magazines/mag_stanag_colt_ar15_std_556x45_30", Folder = "magazines", Name = "mag_stanag_daewoo_k2_std_556x45_30", Mesh = "magazine", Parent = "body", Slot = "mod_magazine" },
        };

        // 게임이 부품을 슬롯에 붙일 때 더하는 회전(바닐라 M4 조립 실험으로 확인)
        public static readonly Matrix4x4 AttachRot = Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0));

        static Matrix4x4 Rel(Transform root, Transform t) => root.worldToLocalMatrix * t.localToWorldMatrix;
        static Transform FindDeep(Transform r, string name) => r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        public static void BuildAll()
        {
            var outDir = Arg("-k2Out");
            try
            {
                if (string.IsNullOrEmpty(outDir)) throw new Exception("-k2Out 필요");
                if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
                    throw new Exception("출력 폴더가 비어 있지 않음 — 덮어쓰지 않습니다: " + outDir);
                Directory.CreateDirectory(outDir);

                ConfigureImports();
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(SRC + "k2_parts.fbx");
                var parts = fbx.GetComponentsInChildren<MeshFilter>(true).ToDictionary(m => m.name.Replace("daewoo_K2_", ""), m => m.transform);
                var mats = MakeMaterials(parts);
                Directory.CreateDirectory(OUT);
                string Copy(string from, string to)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    AssetDatabase.Refresh();
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(to)) AssetDatabase.DeleteAsset(to);   // SDK 안 중간 산출물만 다시 만든다
                    if (!AssetDatabase.CopyAsset(from, to)) throw new Exception("복사 실패 " + from + " → " + to);
                    return to;
                }

                // 1. 본체 (슬롯 mod_reciever·mod_stock을 K2 위치로)
                var modelPath = Copy(DONOR + DONOR_NAME + "_model.generated.prefab", OUT + NAME + "_model.generated.prefab");
                var simplePath = Copy(DONOR + DONOR_NAME + "_model_simple.generated.prefab", OUT + NAME + "_model_simple.generated.prefab");
                var containerPath = Copy(DONOR + DONOR_NAME + "_container.prefab", OUT + NAME + "_container.prefab");
                Vector3 off = Vector3.zero;
                EditModel(modelPath, NAME + "_model.generated", parts, mats, ref off, true);
                EditModel(simplePath, NAME + "_model_simple.generated", parts, mats, ref off, false);
                EditContainer(containerPath, modelPath, simplePath);
                AssetDatabase.SaveAssets();

                var body = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).transform;
                var bodyWeapon = body.Find("Weapon_root/Weapon_root_anim/weapon");
                var slotMatrix = new Dictionary<string, Matrix4x4>();   // "부품Id/슬롯" → 본체 공간 행렬
                foreach (var s in new[] { "mod_reciever", "mod_stock", "mod_magazine" }) slotMatrix["body/" + s] = Rel(body, bodyWeapon.Find(s));

                // M4A1 조립 상태의 본체 공간 행렬(왼손 그립·조준경 높이 기준용)
                var m4Body = AssetDatabase.LoadAssetAtPath<GameObject>(M4 + "weapon_colt_m4a1_556x45_model.generated.prefab").transform;
                var m4Rec = Rel(m4Body, m4Body.Find("Weapon_root/Weapon_root_anim/weapon/mod_reciever"));
                var m4Upper = AssetDatabase.LoadAssetAtPath<GameObject>(M4P + "recievers/reciever_ar15_colt_m4a1_std.prefab").transform;
                var m4HandguardSlot = m4Rec * Rel(m4Upper, FindDeep(m4Upper, "mod_handguard"));
                var m4ScopeSlot = m4Rec * Rel(m4Upper, FindDeep(m4Upper, "mod_scope"));
                float m4RailTop = m4Upper.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.Contains("LOD0"))
                    .SelectMany(m => Corners(m.sharedMesh.bounds).Select(c => (m4Rec * Rel(m4Upper, m.transform)).MultiplyPoint3x4(c).y)).Max();
                float scopeAboveRail = m4ScopeSlot.GetColumn(3).y - m4RailTop;
                L($"(참고) M4A1 레일 계산값: 조준경 슬롯 - 레일 윗면 = {scopeAboveRail * 1000:0.0} mm — 사용 안 함");

                Vector3 BodyPos(string mesh) => parts[mesh].localPosition + off;
                Bounds BodyBounds(string mesh) { var b = parts[mesh].GetComponent<MeshFilter>().sharedMesh.bounds; b.center += BodyPos(mesh); return b; }
                float boreX = BodyPos("muzzle").x, boreY = BodyPos("muzzle").y;

                // 2. 부품
                foreach (var d in PartDefs)
                {
                    // 게임 부착 규칙: 부품 루트 = 슬롯 × X축 +90° (바닐라 M4를 세 규칙으로 조립해 확인 — K2Check.AttachRuleTest, all-v003)
                    d.S = slotMatrix[d.Parent + "/" + d.Slot] * AttachRot;
                    var path = Copy(M4P + d.Template + ".prefab", d.PrefabPath);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        root.name = d.Name;
                        var r = root.transform; var Sinv = d.S.inverse;
                        // 노드 위치만 본체 공간 목표로 옮기고 회전은 템플릿 그대로(게임 규칙이 템플릿 회전을 전제로 한다)
                        void Put(Transform t, Vector3 bodyPos)
                        {
                            var parentM = d.S * (r.worldToLocalMatrix * t.parent.localToWorldMatrix);
                            t.localPosition = parentM.inverse.MultiplyPoint3x4(bodyPos);
                        }
                        ReplaceMeshes(r, d, parts, mats, BodyPos(d.Mesh));

                        switch (d.Id)
                        {
                            case "upper":
                                // mod_barrel·mod_handguard는 템플릿 위치 그대로 — 총열·핸드가드 메시를 그 슬롯 기준으로 변환한다
                                // K2 상부엔 가늠자가 붙어 있고 레일은 핸드가드 쪽 — 상부의 조준경·가늠자 슬롯은 뺀다
                                foreach (var n in new[] { "mod_scope", "mod_sight_rear" }) { var t = FindDeep(r, n); if (t) UnityEngine.Object.DestroyImmediate(t.gameObject); }
                                break;
                            case "barrel":
                            {
                                var fire = FindDeep(r, "fireport"); var mf = FindDeep(r, "muzzleflash_000");
                                float mfAhead = mf && fire ? (mf.localPosition - fire.localPosition).magnitude : 0.016f;
                                var muzzleBase = BodyPos("muzzle");
                                Put(FindDeep(r, "mod_muzzle"), muzzleBase);
                                Put(fire, muzzleBase);
                                if (mf) Put(mf, muzzleBase + Vector3.forward * mfAhead);
                                var gas = FindDeep(r, "mod_gas_block"); if (gas) UnityEngine.Object.DestroyImmediate(gas.gameObject);   // K2 총열에 가스블록·가늠쇠 일체
                                break;
                            }
                            case "handguard":
                            {
                                // 왼손 그립 자세(Base HumanLPalm)는 템플릿 그대로 — 핸드가드 루트가 바닐라 슬롯에 붙으므로 M4A1과 같은 손 위치
                                var sub = r.Cast<Transform>().FirstOrDefault(t => t.name == "mod_handguard"); if (sub) UnityEngine.Object.DestroyImmediate(sub.gameObject);
                                // 레일 윗면에 조준경 슬롯 — 앞뒤 위치는 M4A1 조준경 슬롯(상부 위)과 같게, 레일 범위 안으로
                                var b = BodyBounds("handguard_long");
                                var scope = new GameObject("mod_scope").transform;
                                scope.SetParent(r, false); scope.localRotation = Quaternion.Euler(270, 0, 0);   // 바닐라 슬롯과 같은 방향(상부 LOD0 X 270 아래 회전 0)
                                float z = Mathf.Clamp(m4ScopeSlot.GetColumn(3).z, b.min.z + 0.03f, b.max.z - 0.03f);
                                // 높이는 레일 윗면 그대로(all-v001에서 M4A1 레일 대비 계산은 -64mm로 총 안쪽에 들어가 폐기 — 참고 로그만 남김)
                                Put(scope, new Vector3(boreX, b.max.y, z));
                                L($"  handguard: mod_scope 본체 공간 ({boreX:0.000},{b.max.y:0.000},{z:0.000}) = 레일 윗면, 총열 축 y {boreY:0.000} → 레일 높이 {(b.max.y - boreY) * 1000:0} mm");
                                break;
                            }
                            case "muzzle":
                            {
                                var b = BodyBounds("muzzle"); var front = new Vector3(boreX, boreY, b.max.z);
                                var mf0 = FindDeep(r, "muzzleflash_000");
                                var want = Sinv.MultiplyPoint3x4(front);
                                var delta = mf0 ? want - mf0.localPosition : Vector3.zero;
                                foreach (var t in r.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("muzzleflash_")))
                                    if (t.parent == r) t.localPosition += delta;
                                var sub = r.Cast<Transform>().FirstOrDefault(t => t.name == "mod_muzzle"); if (sub) Put(sub, front);
                                break;
                            }
                            case "stock":
                                foreach (var t in r.Cast<Transform>().Where(t => t.name.StartsWith("mod_stock")).ToArray()) UnityEngine.Object.DestroyImmediate(t.gameObject);
                                break;
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }

                    // 자식 부품이 붙을 슬롯 행렬 기록
                    var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform;
                    foreach (var t in saved.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mod_")))
                        slotMatrix[d.Id + "/" + t.name] = d.S * Rel(saved, t);
                    L($"부품 {d.Id}: {d.Name} (부모 {d.Parent}/{d.Slot})");
                }
                AssetDatabase.SaveAssets();

                // 3. 번들: 공용 텍스처·재질 + 본체 + 부품
                // 실제로 쓰는 재질·텍스처만 공용 번들에(쓰지 않는 GL·개머리판 변형 텍스처는 빼서 용량 절약)
                var shared = AssetDatabase.GetDependencies(new[] { containerPath }.Concat(PartDefs.Select(p => p.PrefabPath)).ToArray(), true)
                    .Where(p => (p.StartsWith(OUT + "Materials/") && p.EndsWith(".mat")) || (p.StartsWith(SRC + "Textures/") && p.EndsWith(".png"))).ToArray();
                L("공용 번들 에셋: " + string.Join(", ", shared.Select(Path.GetFileName)));
                var builds = new List<AssetBundleBuild>
                {
                    new AssetBundleBuild { assetBundleName = SHARED_BUNDLE, assetNames = shared },
                    new AssetBundleBuild { assetBundleName = BUNDLE, assetNames = new[] { containerPath } },
                };
                builds.AddRange(PartDefs.Select(d => new AssetBundleBuild { assetBundleName = d.Bundle, assetNames = new[] { d.PrefabPath } }));
                // 셰이더는 번들에 넣지 않고 외부("shaders") 참조로 만든다 — 빌드 뒤 tools/fix_shader_refs.py가 게임 shaders 번들의
                // 진짜 셰이더 ID로 바꾼다. 이 "shaders" 번들 자체는 배포하지 않는다(1차 인게임에서 내장 SDK 셰이더는 흰색·회색으로 보임, all-v003).
                builds.Add(new AssetBundleBuild { assetBundleName = PLACEHOLDER_SHADERS, assetNames = new[] { SHADER_ASSET } });
                var manifest = BuildPipeline.BuildAssetBundles(outDir, builds.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
                if (manifest == null) throw new Exception("번들 빌드 실패");
                VerifyAll(outDir, manifest, builds);
                PreviewAssembled(Path.Combine(outDir, "preview-assembled.png"), modelPath);
                L("완료");
            }
            catch (Exception e) { L("FAIL " + e); Save(outDir); EditorApplication.Exit(1); return; }
            Save(outDir);
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        // 템플릿의 메시 노드를 K2 메시 하나로 바꾼다: LOD0·LOD1 첫 노드만 남기고 나머지 메시 노드는 지운다.
        // 노드 위치·회전은 템플릿 그대로 두고, K2 메시를 그 노드 기준으로 변환(bake)한다(all-v003).
        static void ReplaceMeshes(Transform r, Part d, Dictionary<string, Transform> parts, Dictionary<string, Material> mats, Vector3 bodyPos)
        {
            var meshNodes = r.GetComponentsInChildren<Transform>(true).Where(t => t.GetComponent<MeshFilter>() || (t.GetComponent<BoxCollider>() && t.name.Contains("_LOD"))).ToList();
            var lod0 = meshNodes.FirstOrDefault(t => !t.name.Contains("LOD1"));
            var lod1 = meshNodes.FirstOrDefault(t => t.name.Contains("LOD1"));
            foreach (var n in meshNodes.Where(t => t != lod0 && t != lod1).ToArray())
            {
                // 지울 메시 노드 아래 슬롯 등이 있으면 먼저 루트로 옮겨 살린다(위치·회전 유지)
                foreach (var c in n.Cast<Transform>().ToArray()) c.SetParent(r, true);
                UnityEngine.Object.DestroyImmediate(n.gameObject);
            }
            var src = parts[d.Mesh]; var srcMesh = src.GetComponent<MeshFilter>().sharedMesh;
            foreach (var (n, suffix) in new[] { (lod0, "_LOD0"), (lod1, "_LOD1") })
            {
                if (!n) continue;
                n.name = d.Name + suffix;
                var nodeM = d.S * (r.worldToLocalMatrix * n.localToWorldMatrix);
                var mesh = Bake(srcMesh, nodeM.inverse * Matrix4x4.Translate(bodyPos), d.Name + suffix);
                var mf = n.GetComponent<MeshFilter>(); if (mf) mf.sharedMesh = mesh;
                var mr = n.GetComponent<MeshRenderer>(); if (mr) mr.sharedMaterials = MatsFor(src, mats);
                var bc = n.GetComponent<BoxCollider>(); if (bc) { bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
            }
            var lod = r.GetComponent<LODGroup>();
            if (lod)
            {
                var lods = lod.GetLODs();
                if (lods.Length > 0) lods[0].renderers = lod0 && lod0.GetComponent<Renderer>() ? new[] { lod0.GetComponent<Renderer>() } : new Renderer[0];
                if (lods.Length > 1) lods[1].renderers = lod1 && lod1.GetComponent<Renderer>() ? new[] { lod1.GetComponent<Renderer>() } : new Renderer[0];
                lod.SetLODs(lods); lod.RecalculateBounds();
            }
        }

        static void VerifyAll(string outDir, AssetBundleManifest manifest, List<AssetBundleBuild> builds)
        {
            AssetBundle.UnloadAllAssetBundles(true);
            var sb = new StringBuilder("{\"bundles\":[\n");
            var sharedB = AssetBundle.LoadFromFile(Path.Combine(outDir, SHARED_BUNDLE));
            if (!sharedB) throw new Exception("공용 번들 다시 읽기 실패");
            try
            {
                bool first = true;
                foreach (var b in builds.Where(x => x.assetBundleName != PLACEHOLDER_SHADERS))
                {
                    var file = Path.Combine(outDir, b.assetBundleName);
                    var size = new FileInfo(file).Length / 1048576.0;
                    var deps = manifest.GetAllDependencies(b.assetBundleName).Where(x => x != PLACEHOLDER_SHADERS).ToArray();
                    string detail = "";
                    if (b.assetBundleName != SHARED_BUNDLE)
                    {
                        var ab = AssetBundle.LoadFromFile(file);
                        if (!ab) throw new Exception("다시 읽기 실패 " + b.assetBundleName);
                        try
                        {
                            var go = ab.LoadAllAssets<GameObject>().FirstOrDefault();
                            if (!go) throw new Exception("프리팹 없음 " + b.assetBundleName);
                            var inst = (GameObject)UnityEngine.Object.Instantiate(go);
                            var rs = inst.GetComponentsInChildren<Renderer>(true);
                            var missingTex = rs.SelectMany(x => x.sharedMaterials).Count(m => !m || !m.GetTexture("_MainTex"));
                            var slots = inst.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("mod_") || t.name == "fireport" || t.name.StartsWith("Base HumanLPalm")).Select(t => t.name).Distinct();
                            detail = $",\"asset\":\"{ab.GetAllAssetNames().FirstOrDefault()}\",\"renderers\":{rs.Length},\"materialsWithoutTexture\":{missingTex},\"nodes\":[" + string.Join(",", slots.Select(x => "\"" + x + "\"")) + "]";
                            L($"재읽기 {b.assetBundleName}: 렌더러 {rs.Length}, 텍스처 없는 재질 {missingTex}, 노드 {string.Join(" ", slots)}");
                            UnityEngine.Object.DestroyImmediate(inst);
                        }
                        finally { ab.Unload(true); }
                    }
                    if (!first) sb.Append(",\n"); first = false;
                    sb.Append($"{{\"key\":\"{b.assetBundleName}\",\"mb\":{size:0.00},\"dependencyKeys\":[" + string.Join(",", deps.Select(x => "\"" + x + "\"")) + "]" + detail + "}");
                }
            }
            finally { sharedB.Unload(true); }
            sb.Append("\n]}");
            File.WriteAllText(Path.Combine(outDir, "validation.json"), sb.ToString(), new UTF8Encoding(false));
        }

        // 프로젝트 프리팹으로 조립(본체 → 슬롯마다 부품)해 측면 렌더. 그래픽 장치가 없으면(-nographics) 건너뛴다.
        static void PreviewAssembled(string png, string modelPath)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { L("미리보기 건너뜀(-nographics)"); return; }
            var body = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            var all = new List<GameObject> { body };
            var placed = new Dictionary<string, Transform> { ["body"] = body.transform };
            foreach (var d in PartDefs)
            {
                var parent = placed[d.Parent];
                var slot = FindDeep(parent, d.Slot);
                var go = (GameObject)UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(d.PrefabPath), slot, false);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.Euler(90, 0, 0);   // 게임 부착 규칙
                placed[d.Id] = go.transform; all.Add(go);
            }
            var std = Shader.Find("Standard");
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("LOD1")) { r.enabled = false; continue; }
                r.sharedMaterials = r.sharedMaterials.Select(m => { var n = new Material(std); n.mainTexture = m ? m.GetTexture("_MainTex") : null; return n; }).ToArray();
            }
            var bnd = body.GetComponentsInChildren<Renderer>().Where(r => r.enabled).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = Mathf.Max(bnd.size.y, bnd.size.z / 1.8f) * 0.58f;
            cam.transform.position = bnd.center + Vector3.right * 2f; cam.transform.LookAt(bnd.center);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.27f);
            var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(30, -60, 0);
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);
            var rt = new RenderTexture(2000, 900, 24); cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(2000, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 2000, 900), 0, 0); tex.Apply();
            File.WriteAllBytes(png, tex.EncodeToPNG());
            L($"조립 미리보기: {png}, 전체 범위 {bnd.size}");

            // 조준 시점: aim_camera 위치에서 앞(+Z)을 좁은 화각으로 — 가늠자·가늠쇠가 화면 가운데 한 줄로 서는지 본다
            var aim = FindDeep(body.transform, "aim_camera");
            if (aim)
            {
                cam.orthographic = false; cam.fieldOfView = 12; cam.nearClipPlane = 0.01f;
                cam.transform.position = aim.position; cam.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                var rt2 = new RenderTexture(1200, 1200, 24); cam.targetTexture = rt2; cam.Render();
                RenderTexture.active = rt2; var t2 = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
                t2.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0); t2.Apply();
                File.WriteAllBytes(png.Replace("preview-assembled", "preview-ads"), t2.EncodeToPNG());
                L($"조준 시점: aim_camera 본체 공간 {body.transform.InverseTransformPoint(aim.position)}");
            }
        }
    }
}
