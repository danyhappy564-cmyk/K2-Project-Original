// AK 클립 전부를 30fps로 재생하며 손바닥(L·R)과 장전손잡이(mod_charge)·개머리판(mod_stock)·조정간(weapon_selector) 거리를 잰다.
// 어떤 클립에서 어느 손이 무엇을 만지는지(동작 수정 대상) 찾는 용도. 바닐라 클립을 쓴다(우리 override 번들 클립 제외).
// 실행: -executeMethod K2Tools.K2AnimScan.Run -k2Mod <번들 폴더> -k2Game <StreamingAssets/Windows> -k2Out <폴더>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace K2Tools
{
    public static class K2AnimScan
    {
        [Serializable] public class Row { public string clip; public float length; public int frames; public float rCharge, lCharge, rStock, lStock, rSelector, lSelector; public string rChargeAt, lChargeAt, lStockAt, rStockAt, rSelectorAt; }
        [Serializable] public class Report { public List<Row> rows = new List<Row>(); public string error; }
        static string A(string k) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i < 0 ? null : a[i + 1]; }
        static Transform Find(Transform r, string n) => r.GetComponentsInChildren<Transform>(true).First(t => t.name == n);

        public static void Run()
        {
            var rep = new Report(); var outDir = A("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                var mod = A("-k2Mod"); var game = A("-k2Game"); var loaded = new Dictionary<string, AssetBundle>();
                var json = File.ReadAllText(Path.Combine(mod, "bundles.json"));
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(json, "\"(assets/[^\"]+|shaders|cubemaps)\""))
                {
                    var k = m.Groups[1].Value; if (loaded.ContainsKey(k)) continue;
                    var p = Path.Combine(mod, k); if (!File.Exists(p)) p = Path.Combine(game, k);
                    var ab = AssetBundle.LoadFromFile(p); if (ab) loaded[k] = ab;
                }
                var wp = loaded["assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle"].LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();
                var model = UnityEngine.Object.Instantiate(wp._weaponObject); model.GetComponent<Animator>().enabled = false;
                var w = Find(model.transform, "weapon");
                var rp = Find(model.transform, "Base HumanRPalm"); var lp = Find(model.transform, "Base HumanLPalm");
                var ch = Find(model.transform, "mod_charge"); var st = Find(model.transform, "mod_stock"); var se = Find(model.transform, "weapon_selector");
                var clips = loaded.Where(p => !p.Key.Contains("daewoo_k2")).SelectMany(p => p.Value.LoadAllAssets<AnimationClip>()).GroupBy(c => c.name).Select(g => g.First()).OrderBy(c => c.name).ToArray();
                foreach (var c in clips)
                {
                    var row = new Row { clip = c.name, length = c.length, rCharge = 9, lCharge = 9, rStock = 9, lStock = 9, rSelector = 9, lSelector = 9 };
                    int n = Mathf.Max(1, Mathf.CeilToInt(c.length * 30));
                    var rc = new List<string>(); var lc = new List<string>(); var ls = new List<string>(); var rs = new List<string>(); var rsel = new List<string>();
                    for (int i = 0; i <= n; i++)
                    {
                        float t = Mathf.Min(i / 30f, c.length); c.SampleAnimation(model, t); row.frames++;
                        Vector3 W(Transform x) => w.InverseTransformPoint(x.position);
                        float d1 = Vector3.Distance(W(rp), W(ch)), d2 = Vector3.Distance(W(lp), W(ch)), d3 = Vector3.Distance(W(rp), W(st)), d4 = Vector3.Distance(W(lp), W(st)), d5 = Vector3.Distance(W(rp), W(se)), d6 = Vector3.Distance(W(lp), W(se));
                        row.rCharge = Mathf.Min(row.rCharge, d1); row.lCharge = Mathf.Min(row.lCharge, d2); row.rStock = Mathf.Min(row.rStock, d3); row.lStock = Mathf.Min(row.lStock, d4); row.rSelector = Mathf.Min(row.rSelector, d5); row.lSelector = Mathf.Min(row.lSelector, d6);
                        rc.Add($"{t:0.00}:{d1 * 1000:0}"); lc.Add($"{t:0.00}:{d2 * 1000:0}"); rs.Add($"{t:0.00}:{d3 * 1000:0}"); ls.Add($"{t:0.00}:{d4 * 1000:0}"); rsel.Add($"{t:0.00}:{d5 * 1000:0}");
                    }
                    row.rChargeAt = string.Join(" ", rc); row.lChargeAt = string.Join(" ", lc); row.rStockAt = string.Join(" ", rs); row.lStockAt = string.Join(" ", ls); row.rSelectorAt = string.Join(" ", rsel);
                    rep.rows.Add(row);
                }
            }
            catch (Exception e) { rep.error = e.ToString(); }
            File.WriteAllText(Path.Combine(outDir, "anim-scan.json"), JsonUtility.ToJson(rep, true));
            EditorApplication.Exit(0);
        }
    }
}
