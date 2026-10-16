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
