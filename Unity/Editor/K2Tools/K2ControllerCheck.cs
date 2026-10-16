// 무기 컨테이너의 애니메이터 컨트롤러가 실제로 연결되는지 — 게임처럼 bundles.json 의존을 먼저 불러 놓고 컨테이너를 읽는다.
// 0.8.0 인게임 "LayersDefaultStates.Length 7 != _animator.layerCount 0"(컨트롤러 비어 팔 펴짐) 원인 확인용.
// 실행: Unity -batchmode -quit -projectPath <SDK> -executeMethod K2Tools.K2Builder.CheckController
//        -k2Mod <모드 폴더(bundles.json 위)> -k2Game <StreamingAssets/Windows> -k2Key <컨테이너 번들 키> -k2Out <로그 폴더> [-k2Override <모드폴더;...>]
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace K2Tools
{
    public static partial class K2Builder
    {
        public static void CheckController()
        {
            var outDir = Arg("-k2Out"); Directory.CreateDirectory(outDir);
            try
            {
                AssetBundle.UnloadAllAssetBundles(true);
                var mod = Arg("-k2Mod"); var game = Arg("-k2Game"); var key = Arg("-k2Key");
                var manifest = File.ReadAllText(Path.Combine(mod, "bundles.json"));
                // 아주 단순한 파서: 해당 키 항목의 dependencyKeys 배열
                var m = Regex.Match(manifest, "\"key\":\\s*\"" + Regex.Escape(key) + "\",\\s*\"dependencyKeys\":\\s*\\[(.*?)\\]", RegexOptions.Singleline);
                if (!m.Success) throw new Exception("키 없음 " + key);
                var deps = Regex.Matches(m.Groups[1].Value, "\"(.*?)\"").Cast<Match>().Select(x => x.Groups[1].Value).ToArray();
                foreach (var d in deps)
                {
                    // -k2Override: 바닐라 키를 덮어쓰는 다른 모드 폴더(예: WTT-ContentBackport) — 게임과 같은 조건으로
                    var ov = (Arg("-k2Override") ?? "").Split(';').Where(x => x != "").Select(x => Path.Combine(x, "bundles", d)).FirstOrDefault(File.Exists);
                    var p = File.Exists(Path.Combine(mod, "bundles", d)) ? Path.Combine(mod, "bundles", d) : ov ?? Path.Combine(game, d);
                    if (ov != null) L($"덮어쓴 번들 사용: {d}");
                    var ab = AssetBundle.LoadFromFile(p);
                    L($"의존 {(ab ? "OK " : "실패")} {d}");
                }
                var main = AssetBundle.LoadFromFile(Path.Combine(mod, "bundles", key));
                if (!main) throw new Exception("컨테이너 읽기 실패");
                foreach (var name in main.GetAllAssetNames())
                {
                    var go = main.LoadAsset<GameObject>(name);
                    L($"에셋 {name} → {(go ? go.name : "null")}");
                    if (!go) continue;
                    foreach (var wp in go.GetComponentsInChildren<WeaponPrefab>(true))
                    {
                        var c = wp._originalAnimatorController;
                        L($"  WeaponPrefab: 컨트롤러 {(c ? c.name + " (" + c.GetType().Name + ", 클립 " + c.animationClips.Length + ")" : "null")}, simple {(wp._animatorSimple ? "OK" : "null")}, spirit {(wp._animatorSpirit ? "OK" : "null")}, fast {(wp._fastAnimatorControllerBinaryData ? "OK" : "null")}");
                        var model = wp._weaponObject;
                        L($"  _weaponObject {(model ? model.name : "null")}");
                        if (model && c)
                        {
                            var inst = (GameObject)UnityEngine.Object.Instantiate(model);
                            var an = inst.GetComponent<Animator>();
                            an.runtimeAnimatorController = c; an.enabled = true; an.Update(0f);
                            L($"  인스턴스 Animator layerCount {an.layerCount}, avatar {(an.avatar ? an.avatar.name + (an.avatar.isValid ? " 유효" : " 무효") : "null")}, isInitialized {an.isInitialized}");
                            UnityEngine.Object.DestroyImmediate(inst);
                        }
                    }
                }
            }
            catch (Exception e) { L("FAIL " + e); }
            File.WriteAllText(Path.Combine(outDir, "controller-check.txt"), Log.ToString());
        }
    }
}
