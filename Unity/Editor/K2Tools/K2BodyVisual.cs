// K2 본체 외형 번들(클라 플러그인용) — 0.2.0 방식.
// 소총 아이템은 바닐라 M4A1 프리팹을 그대로 쓰고(애니메이션·사운드 원본), 클라 플러그인이 본체 메시만 K2로 바꾼다.
// 이 번들에는 "역할 이름" 자식마다 K2 메시(M4A1 노드 기준으로 bake된 것)와 재질만 담는다.
// 실행: Unity -batchmode -quit -projectPath <SDK> -executeMethod K2Tools.K2Builder.BuildBodyVisual -k2Out <새 폴더>
// (먼저 BuildAll로 K2 본체 모델 프리팹·bake 메시가 만들어져 있어야 한다)
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static partial class K2Builder
    {
        public const string BODY_VISUAL_BUNDLE = "k2_body_visual.bundle";
        const string VISUAL_DIR = "Assets/K2/ClientVisual/";

        // 역할 이름 ← K2 본체 모델 프리팹(weapon 기준) 안의 메시 노드 경로. 클라 플러그인 K2Visual.cs의 표와 같아야 한다.
        static readonly (string role, string path)[] VisualRoles =
        {
            ("lower", "weapon_colt_m4a1_556x45_LOD0"),
            ("lower_lod1", "weapon_colt_m4a1_556x45_LOD1"),
            ("bolt", "weapon_bolt/weapon_colt_m4a1_556x45_bolt_LOD0"),
            ("bolt_catch", "weapon_bolt_catch/bolt_catch_LOD0"),
            ("release", "weapon_release/release_LOD0"),
            ("release_l", "weapon_release/mag_release_L_LOD0"),
            ("selector", "weapon_selector/selector_LOD0"),
            ("trigger", "weapon_trigger/trigger_LOD0"),
            ("hammer", "hammer_LOD0"),
        };

        public static void BuildBodyVisual()
        {
            var outDir = Arg("-k2Out");
            try
            {
                if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
                    throw new Exception("출력 폴더가 비어 있지 않음 — 덮어쓰지 않습니다: " + outDir);
                Directory.CreateDirectory(outDir);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(OUT + NAME + "_model.generated.prefab");
                if (!model) throw new Exception("K2 본체 모델 프리팹 없음 — BuildAll 먼저");
                var weapon = model.transform.Find("Weapon_root/Weapon_root_anim/weapon");
                Directory.CreateDirectory(VISUAL_DIR);

                // 재질 복사본(공용 텍스처 번들과 분리) — 셰이더는 클라에서 게임 셰이더로 바꿔 끼운다
                Material CopyMat(Material m)
                {
                    var path = VISUAL_DIR + m.name + "_client.mat";
                    if (AssetDatabase.LoadAssetAtPath<Material>(path)) AssetDatabase.DeleteAsset(path);
                    var c = new Material(m) { name = m.name };
                    AssetDatabase.CreateAsset(c, path);
                    return c;
                }
                var matCache = new System.Collections.Generic.Dictionary<Material, Material>();

                var root = new GameObject("k2_body_visual");
                foreach (var (role, path) in VisualRoles)
                {
                    var src = weapon.Find(path);
                    if (!src) throw new Exception("노드 없음: " + path);
                    var mf = src.GetComponent<MeshFilter>(); var mr = src.GetComponent<MeshRenderer>();
                    if (!mf || !mf.sharedMesh || !mr) throw new Exception("메시 없음: " + path);
                    var go = new GameObject(role);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials
                        .Select(m => m ? (matCache.TryGetValue(m, out var c) ? c : matCache[m] = CopyMat(m)) : null).ToArray();
                    L($"외형 역할 {role}: {mf.sharedMesh.name} ({mf.sharedMesh.vertexCount} 정점)");
                }
                var prefabPath = VISUAL_DIR + "k2_body_visual.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)) AssetDatabase.DeleteAsset(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();

                var build = new AssetBundleBuild { assetBundleName = BODY_VISUAL_BUNDLE, assetNames = new[] { prefabPath } };
                if (BuildPipeline.BuildAssetBundles(outDir, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64) == null)
                    throw new Exception("번들 빌드 실패");
                // 다시 읽기
                AssetBundle.UnloadAllAssetBundles(true);
                var b = AssetBundle.LoadFromFile(Path.Combine(outDir, BODY_VISUAL_BUNDLE));
                var p = b.LoadAsset<GameObject>(prefabPath.ToLowerInvariant());
                if (!p) throw new Exception("다시 읽은 번들에 프리팹 없음: " + string.Join(",", b.GetAllAssetNames()));
                var roles = p.transform.Cast<Transform>().Select(t => t.name).ToArray();
                L($"재읽기: {b.GetAllAssetNames()[0]} / 역할 {roles.Length}개: {string.Join(" ", roles)} / {new FileInfo(Path.Combine(outDir, BODY_VISUAL_BUNDLE)).Length / 1048576.0:0.0} MB");
                b.Unload(true);
                L("완료");
            }
            catch (Exception e) { L("FAIL " + e); Save(outDir); EditorApplication.Exit(1); return; }
            Save(outDir);
        }
    }
}
