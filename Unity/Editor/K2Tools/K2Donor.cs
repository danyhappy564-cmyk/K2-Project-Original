// 도너 무기 계층을 게임(모드) 번들에서 SDK 프리팹으로 옮긴다 — 빌더가 bake·슬롯 행렬·정렬에 쓰는 노드 위치·회전·메시 노드 이름만 필요하다.
// AssetRipper 없이: 번들을 에디터에서 읽어 인스턴스 → Transform·MeshFilter·MeshRenderer만 남기고 프리팹 저장(메시 참조는 비움).
// 실행: Unity -batchmode -quit -projectPath <SDK> -executeMethod K2Tools.K2Builder.ImportDonor
//        -k2DonorBundle <번들 파일> -k2DonorModel <모델 GameObject 이름> -k2DonorOut <Assets/... .prefab>
// 0.7.0: AK5C(WTT-Armory WeaponAK5C/weapon_ak5c_container.bundle, 모델 weapon_ak5c_prefab)
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static partial class K2Builder
    {
        public static void ImportDonor()
        {
            var outPath = Arg("-k2DonorOut");
            try
            {
                AssetBundle.UnloadAllAssetBundles(true);
                var ab = AssetBundle.LoadFromFile(Arg("-k2DonorBundle"));
                if (!ab) throw new Exception("번들 읽기 실패");
                var model = ab.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject)
                    .Concat(Resources.FindObjectsOfTypeAll<GameObject>()).FirstOrDefault(g => g.name == Arg("-k2DonorModel"));
                if (!model) throw new Exception("모델 없음: " + Arg("-k2DonorModel"));
                var go = (GameObject)UnityEngine.Object.Instantiate(model);
                go.name = model.name;
                int removed = 0;
                // 스크립트·애니메이터 등은 빼고(빌더가 안 씀), 메시 노드 표시용 MeshFilter·MeshRenderer는 남긴다(메시·재질 참조는 비움)
                foreach (var c in go.GetComponentsInChildren<Component>(true).Where(c => c && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)).ToArray())
                { UnityEngine.Object.DestroyImmediate(c); removed++; }
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) mf.sharedMesh = null;
                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.sharedMaterials = new Material[0];
                // 깨진 스크립트(누락 MonoBehaviour)도 지운다
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                PrefabUtility.SaveAsPrefabAsset(go, outPath);
                var nodes = go.GetComponentsInChildren<Transform>(true).Length;
                var meshes = string.Join(" ", go.GetComponentsInChildren<MeshFilter>(true).Select(m => m.name));
                UnityEngine.Object.DestroyImmediate(go);
                ab.Unload(true);
                L($"도너 가져옴: {outPath} — 노드 {nodes}, 지운 컴포넌트 {removed}, 메시 노드 {meshes}");
            }
            catch (Exception e) { L("FAIL " + e); Save(Path.GetDirectoryName(Arg("-k2Out") ?? "")); File.WriteAllText("import-donor.log", Log.ToString()); EditorApplication.Exit(1); return; }
            File.WriteAllText("import-donor.log", Log.ToString());
        }
    }
}
