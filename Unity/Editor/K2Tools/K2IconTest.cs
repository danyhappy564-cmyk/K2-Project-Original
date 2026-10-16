// 아이템 아이콘 자세 재현 — 게임 ItemIconCreator.PoseModelByPivot(회전 = PreviewPivot.Icon.rotation, 경계 상자 중심·직교 카메라)를 따라 그린다.
// 실행: Unity -batchmode -quit -projectPath <SDK> -executeMethod K2Tools.K2IconTest.Run -k2Out <폴더> -k2Bundles <번들1;번들2...>
// (게임 셰이더는 없으므로 회색 Standard 재질로 형태만 본다. 카메라는 +Z를 본다고 가정 — 두 번들을 같은 조건으로 비교하는 용도)
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static class K2IconTest
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public static void Run()
        {
            var outDir = Arg("-k2Out"); Directory.CreateDirectory(outDir);
            var log = new System.Text.StringBuilder();
            try
            {
                foreach (var path in Arg("-k2Bundles").Split(';'))
                {
                    AssetBundle.UnloadAllAssetBundles(true);
                    var ab = AssetBundle.LoadFromFile(path);
                    var prefab = ab.LoadAllAssets<GameObject>().First();
                    var go = (GameObject)UnityEngine.Object.Instantiate(prefab);
                    // PreviewPivot.Icon.rotation — 스크립트가 없으면 직렬화 값을 SerializedObject로 읽는다
                    Quaternion rot = Quaternion.identity; string how = "기본";
                    foreach (var mb in go.GetComponents<MonoBehaviour>())
                    {
                        if (!mb) continue;
                        var so = new SerializedObject(mb); var p = so.FindProperty("Icon.rotation");
                        if (p != null) { rot = p.quaternionValue; how = "PreviewPivot"; }
                    }
                    var std = Shader.Find("Standard");
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = !r.name.Contains("LOD1");
                        r.sharedMaterials = r.sharedMaterials.Select(_ => new Material(std) { color = new Color(0.6f, 0.6f, 0.6f) }).ToArray();
                    }
                    var cam = new GameObject("cam").AddComponent<Camera>();
                    cam.transform.position = Vector3.zero; cam.transform.rotation = Quaternion.identity;
                    cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.15f, 0.15f, 0.17f);
                    go.transform.SetParent(cam.transform); go.transform.localPosition = Vector3.zero; go.transform.rotation = rot;
                    var b = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                    go.transform.position += new Vector3(-b.center.x, -b.center.y, 1f - b.center.z);
                    float aspect = 0.5f;   // 1×2 칸
                    cam.orthographicSize = b.extents.x / b.extents.y > aspect ? b.extents.x / aspect : b.extents.y;
                    cam.nearClipPlane = 0.01f; cam.farClipPlane = 10f; cam.aspect = aspect;
                    var light = new GameObject("light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(30, 30, 0);
                    RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.5f);
                    var rt = new RenderTexture(256, 512, 24); cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; var tex = new Texture2D(256, 512, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 256, 512), 0, 0); tex.Apply();
                    var png = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path) + ".png");
                    File.WriteAllBytes(png, tex.EncodeToPNG());
                    log.AppendLine($"{Path.GetFileName(path)}: 회전 {rot.eulerAngles} ({how}), 경계 {b.size} → {png}");
                    UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(cam.gameObject); UnityEngine.Object.DestroyImmediate(light.gameObject);
                    ab.Unload(true);
                }
            }
            catch (Exception e) { log.AppendLine("FAIL " + e); File.WriteAllText(Path.Combine(outDir, "icon-test.txt"), log.ToString()); EditorApplication.Exit(1); return; }
            File.WriteAllText(Path.Combine(outDir, "icon-test.txt"), log.ToString());
        }
    }
}
