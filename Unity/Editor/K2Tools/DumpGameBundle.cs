// K2: 게임 번들(읽기 전용)을 에디터에서 열어 프리팹 계층·트랜스폼·컴포넌트·애니메이터를 JSON으로 덤프한다.
// 실행: Unity -batchmode -projectPath <SDK> -executeMethod K2Tools.DumpGameBundle.Run -k2Game "<StreamingAssets/Windows>" -k2Bundle "<키>" -k2Out "<json>"
// 게임 파일은 읽기만 하고, 프로젝트에 에셋을 만들지 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static class DumpGameBundle
    {
        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        static string V3(Vector3 v) => $"[{v.x:0.#####},{v.y:0.#####},{v.z:0.#####}]";

        // Windows.json에서 키의 의존성을 깊이 우선으로 모은다(의존 대상이 먼저).
        static List<string> Order(Dictionary<string, List<string>> deps, string key)
        {
            var order = new List<string>(); var seen = new HashSet<string>();
            void Visit(string k, int depth)
            {
                if (depth > 64 || !seen.Add(k)) return;   // 순환·폭주 방지
                if (deps.TryGetValue(k, out var ds)) foreach (var d in ds) Visit(d, depth + 1);
                order.Add(k);
            }
            Visit(key, 0);
            return order;
        }

        static Dictionary<string, List<string>> ReadManifest(string path)
        {
            // 최소 파서: "키": {..."Dependencies":[...]} 만 뽑는다.
            var text = File.ReadAllText(path);
            var result = new Dictionary<string, List<string>>();
            var rx = new System.Text.RegularExpressions.Regex("\"([^\"]+)\"\\s*:\\s*\\{[^{}]*?\"Dependencies\"\\s*:\\s*\\[([^\\]]*)\\]");
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
            {
                var list = System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"")
                    .Cast<System.Text.RegularExpressions.Match>().Select(x => x.Groups[1].Value).ToList();
                result[m.Groups[1].Value] = list;
            }
            return result;
        }

        static void Node(StringBuilder sb, Transform t, Transform root, int depth)
        {
            if (depth > 40) return;
            var comps = t.GetComponents<Component>().Where(c => !(c is Transform))
                .Select(c => c == null ? "MISSING_SCRIPT" : c.GetType().Name).ToArray();
            var extra = "";
            var an = t.GetComponent<Animator>();
            if (an && an.runtimeAnimatorController) extra += $",\"controller\":\"{Esc(an.runtimeAnimatorController.name)}\",\"clips\":{an.runtimeAnimatorController.animationClips.Length}";
            var mf = t.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh) extra += $",\"mesh\":\"{Esc(mf.sharedMesh.name)}\",\"tris\":{mf.sharedMesh.triangles.Length / 3},\"bounds\":{V3(mf.sharedMesh.bounds.size)}";
            var mr = t.GetComponent<Renderer>();
            if (mr) extra += ",\"shaders\":[" + string.Join(",", mr.sharedMaterials.Where(x => x).Select(x => $"\"{Esc(x.shader ? x.shader.name : "?")}\"")) + "]";
            sb.Append(new string(' ', depth)).Append($"{{\"n\":\"{Esc(t.name)}\",\"p\":{V3(t.localPosition)},\"r\":{V3(t.localEulerAngles)},\"s\":{V3(t.localScale)},")
              .Append($"\"rootPos\":{V3(root.InverseTransformPoint(t.position))},\"c\":[{string.Join(",", comps.Select(c => $"\"{c}\""))}]{extra},\"ch\":[\n");
            for (int i = 0; i < t.childCount; i++) { Node(sb, t.GetChild(i), root, depth + 1); if (i < t.childCount - 1) sb.Append(",\n"); }
            sb.Append("]}");
        }

        // 여러 번들: -k2Bundle "키1;키2" -k2Out <폴더> → 폴더/<번들 파일 이름>.json
        public static void RunMany()
        {
            var game = Arg("-k2Game"); var outDir = Arg("-k2Out");
            foreach (var key in Arg("-k2Bundle").Split(';'))
                Dump(game, key, Path.Combine(outDir, Path.GetFileNameWithoutExtension(key) + ".json"));
        }

        public static void Run() => Dump(Arg("-k2Game"), Arg("-k2Bundle"), Arg("-k2Out"));

        static void Dump(string game, string key, string outPath)
        {
            var deps = ReadManifest(Path.Combine(game, "Windows.json"));
            var order = Order(deps, key);
            var loaded = new List<AssetBundle>(); var log = new StringBuilder();
            try
            {
                foreach (var k in order)
                {
                    var p = Path.Combine(game, k);
                    if (!File.Exists(p)) { log.AppendLine("없음: " + k); continue; }
                    var b = AssetBundle.LoadFromFile(p);
                    if (b == null) { log.AppendLine("열기 실패: " + k); continue; }
                    loaded.Add(b);
                }
                var main = loaded.LastOrDefault(b => b.name.Replace('\\', '/').EndsWith(Path.GetFileName(key), StringComparison.OrdinalIgnoreCase)) ?? loaded.Last();
                var sb = new StringBuilder("{\"bundle\":\"" + Esc(key) + "\",\"loadOrder\":[" + string.Join(",", order.Select(o => $"\"{Esc(o)}\"")) + "],\n\"assets\":[");
                var names = main.GetAllAssetNames();
                sb.Append(string.Join(",", names.Select(n => $"\"{Esc(n)}\""))).Append("],\n\"prefabs\":[\n");
                bool first = true;
                foreach (var n in names)
                {
                    var go = main.LoadAsset<GameObject>(n);
                    if (!go) continue;
                    if (!first) sb.Append(",\n"); first = false;
                    Node(sb, go.transform, go.transform, 0);
                }
                sb.Append("],\n\"log\":\"").Append(Esc(log.ToString())).Append("\"}");
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
                Debug.Log("K2DUMP OK " + outPath);
            }
            catch (Exception e) { Debug.LogError("K2DUMP FAIL " + e); EditorApplication.Exit(1); }
            finally { foreach (var b in loaded) b.Unload(true); }
        }

        // 다른 번들(애니메이션·client_assets) 안의 에셋 이름·종류 목록만.
        public static void ListAssets()
        {
            var game = Arg("-k2Game"); var keys = Arg("-k2Bundle").Split(';'); var outPath = Arg("-k2Out");
            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                var b = AssetBundle.LoadFromFile(Path.Combine(game, k));
                if (!b) { sb.AppendLine("열기 실패 " + k); continue; }
                sb.AppendLine("## " + k);
                foreach (var o in b.LoadAllAssets()) sb.AppendLine($"{o.GetType().Name}\t{o.name}" + (o is AnimationClip c ? $"\t{c.length:0.##}s" : "") + (o is RuntimeAnimatorController r ? $"\tclips={r.animationClips.Length}" : ""));
                b.Unload(true);
            }
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("K2LIST OK " + outPath);
        }
    }
}
