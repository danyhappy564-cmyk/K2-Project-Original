using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace K2Original.Client;

/// <summary>
/// K2 소총 외형 — 소총 아이템은 바닐라 M4A1 프리팹을 그대로 써서 애니메이션·사운드·손 위치가 원본이고,
/// 이 플러그인이 본체(하부·노리쇠·노리쇠멈치·탄창멈치·조정간·방아쇠) 메시만 K2로 바꾸고 해머·왼쪽 탄창멈치를 덧붙인다.
/// K2 메시는 M4A1 노드 기준으로 미리 변환(bake)돼 있어 노드에 그대로 끼우면 애니메이션대로 움직인다.
/// 상부·총열·핸드가드 등 부품은 서버 모드의 K2 부품 번들이 그린다(여기서 건드리지 않는다).
/// </summary>
[BepInPlugin("com.rf.k2projectoriginal.client", "K2-Project-Original Client", "0.2.0")]
public sealed class K2Visual : BaseUnityPlugin
{
    internal const string WeaponId = "d22000000000000000000001";
    const string PrefabPath = "assets/k2/clientvisual/k2_body_visual.prefab";
    internal static K2Visual Instance = null!;
    internal readonly Dictionary<string, (Mesh mesh, Material[] mats)> Roles = new();
    Harmony? harmony;
    AssetBundle? bundle;
    bool shadersFixed;

    void Awake()
    {
        Instance = this;
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(Info.Location)!, "k2_body_visual.bundle");
            bundle = AssetBundle.LoadFromFile(path);
            if (!bundle) throw new InvalidDataException("외형 번들을 열지 못함: " + path);
            var prefab = bundle.LoadAsset<GameObject>(PrefabPath) ?? throw new InvalidDataException("외형 프리팹 없음: " + PrefabPath);
            foreach (Transform t in prefab.transform)
                Roles[t.name] = (t.GetComponent<MeshFilter>().sharedMesh, t.GetComponent<MeshRenderer>().sharedMaterials);
            harmony = new Harmony("com.rf.k2projectoriginal.client");
            harmony.PatchAll(typeof(K2Visual).Assembly);
            Logger.LogInfo($"[K2] 본체 외형 준비 — 역할 {Roles.Count}개");
        }
        catch (Exception e) { Logger.LogError("[K2] 초기화 실패 — K2는 M4A1 본체로 보입니다: " + e); }
    }

    // 번들 재질은 SDK 셰이더 복사본을 갖고 있어 게임에서 흰색으로 보인다 → 처음 적용할 때 같은 이름의 게임 셰이더로 바꿔 끼운다
    void FixShaders()
    {
        if (shadersFixed) return;
        int fixedCount = 0;
        foreach (var m in Roles.Values.SelectMany(r => r.mats).Where(m => m).Distinct())
        {
            var s = Shader.Find(m.shader.name);
            if (s && s != m.shader) { m.shader = s; fixedCount++; }
        }
        shadersFixed = fixedCount > 0;
        Logger.LogInfo($"[K2] 게임 셰이더로 교체: {fixedCount}개");
    }

    internal void Apply(Item item, GameObject root)
    {
        // 풀에서 재사용된 프리팹이 K2 모습을 남기지 않게, 어떤 아이템이든 먼저 원래대로 되돌린다
        var previous = root.GetComponent<K2BodyState>();
        if (previous) previous.Restore();
        if (item.TemplateId != WeaponId || Roles.Count == 0) return;
        FixShaders();
        var anchors = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "weapon" && t.parent && t.parent.name == "Weapon_root_anim").ToArray();
        if (anchors.Length == 0) { Logger.LogWarning("[K2] weapon 노드를 못 찾음: " + root.name); return; }
        var state = previous ? previous : root.AddComponent<K2BodyState>();
        int swapped = 0;
        foreach (var anchor in anchors)
        {
            foreach (var mr in anchor.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (InsideMod(mr.transform, anchor)) continue;   // 부품(상부·탄창 등)은 그대로
                var mf = mr.GetComponent<MeshFilter>();
                var role = RoleOf(mr.transform, anchor);
                if (role == null || !mf || !Roles.TryGetValue(role, out var r)) { state.Hide(mr); continue; }
                state.Swap(mf, mr, r.mesh, r.mats); swapped++;
            }
            state.Add(anchor, "hammer", Roles);
            var release = anchor.Find("weapon_release");
            if (release) state.Add(release, "release_l", Roles);
        }
        Logger.LogInfo($"[K2] 본체 외형 적용: {root.name} — 교체 {swapped}, weapon 노드 {anchors.Length}");
    }

    static bool InsideMod(Transform t, Transform anchor)
    {
        for (var p = t; p && p != anchor; p = p.parent)
            if (p.name.StartsWith("mod_", StringComparison.Ordinal)) return true;
        return false;
    }

    // M4A1 본체 메시 노드 → 역할 (K2BodyVisual.cs의 VisualRoles와 같은 짝)
    static string? RoleOf(Transform node, Transform anchor)
    {
        var parent = node.parent ? node.parent.name : "";
        if (node.parent == anchor && node.name.StartsWith("weapon_colt_m4a1_556x45_LOD", StringComparison.Ordinal))
            return node.name.Contains("LOD1") ? "lower_lod1" : "lower";
        return parent switch
        {
            "weapon_bolt" => "bolt",
            "weapon_bolt_catch" => "bolt_catch",
            "weapon_release" => "release",
            "weapon_selector" => "selector",
            "weapon_trigger" => "trigger",
            _ => null,
        };
    }

    internal static async Task<GameObject> Finish(Task<GameObject> pending, Item item)
    {
        var root = await pending;   // Unity 동기화 문맥 유지
        if (root)
        {
            try { Instance.Apply(item, root); }
            catch (Exception e) { Instance.Logger.LogError("[K2] 외형 적용 실패 — M4A1 본체로 보입니다: " + e); }
        }
        return root;
    }

    void OnDestroy()
    {
        harmony?.UnpatchSelf();
        foreach (var s in FindObjectsOfType<K2BodyState>(true)) s.Restore();
        if (bundle) bundle!.Unload(false);
    }

    [HarmonyPatch]
    static class CreateItemPatch
    {
        // EFT.ObjectsFactory.CreateItemAsync(Item, ECameraType, IPlayer, bool, YieldDelegate, CancellationToken) — SPT 4.1.6에서 확인
        static MethodBase TargetMethod() => typeof(Item).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Single(m => m.Name == "CreateItemAsync" && m.ReturnType == typeof(Task<GameObject>)
                && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(Item));
        static void Postfix(Item __0, ref Task<GameObject> __result) => __result = Finish(__result, __0);
    }
}

/// <summary>바꾼 메시·재질과 덧붙인 오브젝트를 기억했다가 되돌린다(풀 재사용 대비).</summary>
public sealed class K2BodyState : MonoBehaviour
{
    readonly List<(MeshFilter mf, MeshRenderer mr, Mesh mesh, Material[] mats)> swapped = new();
    readonly List<(Renderer r, bool off)> hidden = new();
    readonly List<GameObject> added = new();

    public void Swap(MeshFilter mf, MeshRenderer mr, Mesh mesh, Material[] mats)
    {
        swapped.Add((mf, mr, mf.sharedMesh, mr.sharedMaterials));
        mf.sharedMesh = mesh; mr.sharedMaterials = mats;
    }

    public void Hide(Renderer r)
    {
        hidden.Add((r, r.forceRenderingOff));
        r.forceRenderingOff = true;
    }

    public void Add(Transform parent, string role, Dictionary<string, (Mesh mesh, Material[] mats)> roles)
    {
        if (!roles.TryGetValue(role, out var r)) return;
        var go = new GameObject("k2_" + role) { layer = parent.gameObject.layer };
        go.transform.SetParent(parent, false);   // 위치·회전 0 — 메시가 이 기준으로 bake돼 있다
        go.AddComponent<MeshFilter>().sharedMesh = r.mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = r.mats;
        added.Add(go);
    }

    public void Restore()
    {
        foreach (var (mf, mr, mesh, mats) in swapped)
        {
            if (mf) mf.sharedMesh = mesh;
            if (mr) mr.sharedMaterials = mats;
        }
        swapped.Clear();
        foreach (var (r, off) in hidden) if (r) r.forceRenderingOff = off;
        hidden.Clear();
        foreach (var go in added) if (go) Destroy(go);
        added.Clear();
    }

    void OnDestroy() => Restore();
}
