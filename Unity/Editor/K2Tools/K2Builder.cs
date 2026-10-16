// K2 본체(무기) 번들 빌더 — M4A1 무기 프리팹(추출본)을 복제해 메시·재질만 K2로 바꾸고 번들을 만든 뒤 다시 읽어 검사한다.
// 실행: Unity -batchmode -quit -projectPath <SDK> -executeMethod K2Tools.K2Builder.BuildBody -k2Out <새 출력 폴더>
//
// 왜 M4A1 노드 위치를 그대로 두나: M4A1 애니메이션이 weapon 아래 노드(탄창 슬롯·손 마커·노리쇠·방아쇠·조준 카메라 등)의
// 위치를 직접 움직인다. 노드를 옮겨도 애니메이션이 원래 값으로 덮어쓰므로, K2 모델 쪽을 M4A1 좌표계에 맞춰 넣는다.
// 맞추는 방법: K2 기준점(Blender 리그 본 머리) 5개와 M4A1 노드 5개의 평균 차이만큼 K2 전체를 평행 이동(회전·배율 없음).
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
        const string M4 = "Assets/K2Ripped/M4A1/Content/Weapons/m4a1/";
        // 도너(애니메이션·사운드·손 위치의 원본 무기) — 0.3.0부터 SCAR-L(Mk16): 노리쇠에 붙은 측면 장전손잡이·AR식 탄창/멈치가 K2와 가깝다
        // 0.4.0: 도너 AK-101 — 오른쪽 장전손잡이가 노리쇠에 붙은 구조가 K2와 같다. 바닐라 컨테이너 번들을 직접 개조하므로
        // 여기서는 K2 메시를 AK 노드 기준으로 bake하고 부품 번들을 AK 슬롯 기준으로 만든다(K2Parts.BuildAll -k2Mode reskin).
        // 도너: ak101(0.4~0.6, 기본) / ak5c(0.7.0, WTT-Armory — 수정 금지 확인 후 보류) / scar(0.8.0, 바닐라 Mk16).
        // SCAR: 오른쪽 접기·STANAG 곧은 삽입·조정간/방아쇠/해머/멈치가 본체 모델 노드(K2 부품 그대로 끼움). 장전손잡이는 왼쪽(mod_charge 부품) — K2는 노리쇠 메시의 오른쪽 손잡이.
        // 모드·바닐라 번들의 도너 계층은 ImportDonor(K2Donor.cs)가 SDK 프리팹으로 옮겨 둔다.
        static string Donor => Arg("-k2Donor") ?? "ak101";
        static bool DonorAK5C => Donor == "ak5c";
        static bool DonorSCAR => Donor == "scar";
        static string DonorModelPath => DonorAK5C ? "Assets/K2Ripped/AK5C/weapon_ak5c_prefab.prefab"
            : DonorSCAR ? "Assets/K2Ripped/SCARV/weapon_fn_mk16_556x45_model.generated.prefab"
            : "Assets/K2Ripped/AK101/Content/Weapons/ak100/weapon_izhmash_ak101_556x45.generated.prefab";
        const string HIDE = "";   // Swaps에서 이 값이면 빈 메시로 숨긴다(K2에 없는 도너 부품)
        // 조준선 보정(m) — all-v007 조준 시점 렌더로 확정(가늠쇠 끝이 화면 중앙). -k2SightDy로 바꿔 시험 가능
        static float SightDy => float.TryParse(Arg("-k2SightDy"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;   // 0.4.0(AK): 조준 시점 렌더로 다시 정한다
        const string SRC = "Assets/K2/Source/";
        // 번들 안 에셋 경로를 바닐라 규칙(번들 키와 같은 assets/content/weapons/<무기>/...)에 맞춘다 (body-v003)
        const string OUT = "Assets/Content/Weapons/daewoo_k2/";
        const string NAME = "weapon_daewoo_k2_556x45";
        public const string BUNDLE = "assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle";
        const string SHADER = "p0/Reflective/Bumped Specular SMap";

        // K2 부품(FBX 오브젝트 이름 뒤쪽) ↔ M4A1 weapon 아래 노드 — 정렬 기준점(하부 조작부 4개, 위·앞뒤 평균).
        // 탄창은 뺀다: K2 탄창 본 머리가 탄창 아래쪽이라 M4A1 mod_magazine(탄창 위쪽)과 116mm 어긋났다(body-v001).
        static (string k2, string m4)[] Anchors => DonorSCAR ? new[] { ("trigger", "mod_trigger_group/mod_trigger") }
            : DonorAK5C ? new[] { ("trigger", "weapon_trigger") } : new[] { ("trigger", "weapon_trigger"), ("hammer", "weapon_hammer") };
        // 참고로만 오차를 기록하는 쌍
        static (string k2, string m4)[] InfoPairs => DonorSCAR
            ? new[] { ("magazine", "mod_magazine"), ("bolt_carrier", "weapon_bolt"), ("selector", "mod_trigger_group/mod_selector"), ("hammer", "mod_trigger_group/mod_hammer"), ("bolt_release", "mod_catch") }
            : DonorAK5C
            ? new[] { ("magazine", "mod_magazine"), ("bolt_carrier", "weapon_bolt"), ("selector", "weapon_selector"), ("bolt_release", "weapon_bolt_catch") }
            : new[] { ("magazine", "mod_magazine"), ("bolt_carrier", "weapon_bolt") };
        // 도너 메시 노드(weapon 기준 경로) → K2 부품. "a+b"는 한 메시로 합친다, HIDE는 빈 메시.
        // AK-101: AK 조정간은 오른쪽 큰 레버로 돌아가는 동작이라 K2 조정간(왼쪽 작은 것)은 총몸에 고정으로 합친다.
        // AK5C: 조정간·노리쇠멈치가 왼쪽에서 M4 동작으로 돈다 — K2 조정간·노리쇠멈치를 그 노드에. 해머 노드가 없어 총몸에 합친다.
        //       총몸(ak5c_frame)은 위·아래 리시버 일체라 K2 하부만(상부는 mod_reciever 부품), 덮개·가늠자·가늠쇠는 K2 부품에 있어 숨긴다.
        // SCAR: 본체 메시 = 하부 리시버. 방아쇠 뭉치 하우징·노리쇠 스프링은 K2 하부에 포함/안 보이므로 숨긴다.
        static (string node, string part)[] Swaps => DonorSCAR
            ? new[]
            {
                ("weapon_fn_mk16_556x45_LOD0", "lower"), ("weapon_fn_mk16_556x45_LOD1", "lower"),
                ("mod_trigger_group/trigger_group_mk16_fn_std_LOD0", HIDE), ("weapon_bolt/weapon_spring", HIDE),
                ("weapon_bolt", "bolt_carrier"), ("mod_trigger_group/mod_trigger", "trigger"), ("mod_trigger_group/mod_selector", "selector"),
                ("mod_trigger_group/mod_hammer", "hammer"), ("mod_catch", "bolt_release"), ("mod_release", "mag_release_R+mag_release_L"),
            }
            : DonorAK5C
            ? new[]
            {
                ("ak5c_frame", "lower+hammer"), ("ak5c_dustcover", HIDE), ("ak5c_fs", HIDE), ("ak5c_rs", HIDE),
                ("weapon_bolt", "bolt_carrier"), ("weapon_selector", "selector"), ("weapon_trigger", "trigger"), ("weapon_bolt_catch", "bolt_release"),
                ("weapon_release/ak5c_release", "mag_release_R+mag_release_L"), ("weapon_release/ak5c_release2", HIDE),
            }
            : new[]
            {
                ("weapon_izhmash_ak101_556x45_LOD0", "lower+bolt_release"), ("weapon_izhmash_ak74m_545x39_LOD1", "lower+bolt_release"),
                ("weapon_izhmash_ak74m_dovetail_LOD0", HIDE), ("weapon_izhmash_ak74m_kolodka_LOD0", HIDE), ("mod_stock_release", HIDE),
                ("weapon_bolt", "bolt_carrier"), ("weapon_hammer", "hammer"), ("weapon_release", "mag_release_R+mag_release_L"),
                ("weapon_selector", "selector"), ("weapon_trigger", "trigger"),   // 0.9.5: 조정간 분리 — 노드를 K2 조정간 축으로 옮겨 그 기준으로 bake(아래 Place)
            };
        static readonly (string parent, string part)[] Extras = { };
        // 개조 매핑: 도너 메시 노드 GameObject 이름 → (bake 메시 이름, 재질 이름들) — tools/reskin_vanilla.py가 쓴다
        public static readonly Dictionary<string, (string mesh, string[] mats)> ReskinMap = new();

        static readonly StringBuilder Log = new StringBuilder();
        static void L(string s) { Log.AppendLine(s); Debug.Log("K2BUILD " + s); }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        public static void BuildBody()
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
                if (!fbx) throw new Exception("FBX 없음");
                var parts = fbx.GetComponentsInChildren<MeshFilter>(true)
                    .ToDictionary(m => m.name.Replace("daewoo_K2_", ""), m => m.transform);
                Vector3 P(string p) => parts[p].localPosition;   // FBX 루트 기준 = Unity 축(앞 +Z, 위 +Y)

                // 축 확인: 총구가 개머리판보다 앞(+Z), 노리쇠가 탄창보다 위(+Y)
                L($"축 확인: muzzle {P("muzzle")} stock {P("stock")} bolt {P("bolt_carrier")} mag {P("magazine")}");
                if (!(P("muzzle").z > P("stock").z && P("bolt_carrier").y > P("magazine").y))
                    throw new Exception("FBX 축이 예상과 다름 — 앞 +Z / 위 +Y가 아님. 내보내기 설정 확인 필요");

                var mats = MakeMaterials(parts);
                Directory.CreateDirectory(OUT);
                string Copy(string from, string to)
                {
                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(to)) AssetDatabase.DeleteAsset(to);   // SDK 안 중간 산출물만 다시 만든다
                    if (!AssetDatabase.CopyAsset(from, to)) throw new Exception("복사 실패 " + from);
                    return to;
                }
                var modelPath = Copy(M4 + "weapon_colt_m4a1_556x45_model.generated.prefab", OUT + NAME + "_model.generated.prefab");
                var simplePath = Copy(M4 + "weapon_colt_m4a1_556x45_model_simple.generated.prefab", OUT + NAME + "_model_simple.generated.prefab");
                var containerPath = Copy(M4 + "weapon_colt_m4a1_556x45_container.prefab", OUT + NAME + "_container.prefab");

                Vector3 offset = Vector3.zero;
                EditModel(modelPath, NAME + "_model.generated", parts, mats, ref offset, true);
                EditModel(simplePath, NAME + "_model_simple.generated", parts, mats, ref offset, false);
                EditContainer(containerPath, modelPath, simplePath);
                AssetDatabase.SaveAssets();

                var build = new AssetBundleBuild { assetBundleName = BUNDLE, assetNames = new[] { containerPath } };
                var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
                if (manifest == null) throw new Exception("번들 빌드 실패");
                var built = Path.Combine(outDir, BUNDLE);
                L($"번들: {built} ({new FileInfo(built).Length / 1048576.0:0.0} MB)");
                Verify(built, outDir);
                L("완료");
            }
            catch (Exception e) { L("FAIL " + e); Save(outDir); EditorApplication.Exit(1); return; }
            Save(outDir);
        }

        static void Save(string outDir)
        {
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "build-log.txt"), Log.ToString(), new UTF8Encoding(false));
        }

        static void ConfigureImports()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(SRC + "k2_parts.fbx");
            mi.globalScale = 1; mi.useFileScale = true; mi.bakeAxisConversion = false;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;   // 재질 이름(슬롯 순서)을 읽기 위해서만
            mi.importTangents = ModelImporterTangents.CalculateMikk; mi.isReadable = true;   // Bake가 정점을 읽는다
            mi.SaveAndReimport();
            foreach (var png in Directory.GetFiles(SRC + "Textures", "*.png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(png.Replace('\\', '/'));
                ti.maxTextureSize = 2048; ti.mipmapEnabled = true;
                ti.textureType = png.EndsWith("_normal.png") ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
                ti.SaveAndReimport();
            }
        }

        // FBX 재질 이름 → EFT 셰이더 재질 (값은 M4A1 재질에서 가져옴)
        static Dictionary<string, Material> MakeMaterials(Dictionary<string, Transform> parts)
        {
            var shader = Shader.Find(SHADER);
            if (!shader) throw new Exception("셰이더 없음: " + SHADER);
            Directory.CreateDirectory(OUT + "Materials");
            var names = parts.Values.SelectMany(t => t.GetComponent<MeshRenderer>().sharedMaterials).Where(m => m).Select(m => m.name).Distinct();
            var result = new Dictionary<string, Material>();
            foreach (var n in names)
            {
                var tex = SRC + "Textures/" + n;
                var m = new Material(shader) { name = NAME + "_" + n.Replace("daewoo_K2_", "") };
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_main.png"));
                m.SetTexture("_SpecMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_spec.png"));
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_normal.png"));
                m.SetFloat("_Glossness", 1); m.SetFloat("_Specularness", 1);
                m.SetColor("_Color", Color.white); m.SetColor("_SpecColor", Color.white);
                m.SetColor("_ReflectColor", new Color(0.2924528f, 0.29107332f, 0.29107332f, 0.5019608f));
                m.SetVector("_DefVals", new Vector4(1, 0.5f, 0, 0)); m.SetVector("_SpecVals", new Vector4(1, 0.5f, 0, 0));
                var path = OUT + "Materials/" + m.name + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path)) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                result[n] = m;
                if (!m.GetTexture("_MainTex")) L("경고: 텍스처 없음 " + n);
            }
            return result;
        }

        static Material[] MatsFor(Transform part, Dictionary<string, Material> mats) =>
            part.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m ? mats[m.name] : null).ToArray();

        static void EditModel(string path, string rootName, Dictionary<string, Transform> parts, Dictionary<string, Material> mats, ref Vector3 offset, bool computeOffset)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = rootName;
                var weapon = root.transform.Find("Weapon_root/Weapon_root_anim/weapon");
                if (!weapon) throw new Exception("weapon 노드 없음: " + path);
                Vector3 RootPos(Transform t) => root.transform.InverseTransformPoint(t.position);

                if (computeOffset)
                {
                    var diffs = Anchors.Select(a => RootPos(weapon.Find(a.m4)) - parts[a.k2].localPosition).ToArray();
                    offset = diffs.Aggregate(Vector3.zero, (s, d) => s + d) / diffs.Length;
                    // 좌우는 총 중심선끼리: K2 노리쇠 중심(x) = M4A1 weapon_bolt 중심(x)
                    offset.x = RootPos(weapon.Find("weapon_bolt")).x - parts["bolt_carrier"].localPosition.x;
                    // 조준선 보정: 조준 카메라(애니메이션 노드라 못 옮김) 높이에 K2 가늠자 구멍 중심이 오도록 K2 전체를 올린다
                    // (all-v006 조준 시점 렌더에서 구멍 중심이 약 8mm 아래 — 인게임 "조준선 이상" 증상, 8.3mm 시험 후 7.5mm로)
                    offset.y += SightDy;
                    if (float.TryParse(Arg("-k2Forward"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var forward)) offset.z += forward;
                    L($"조준선 보정 {SightDy * 1000:+0.0;-0.0} mm");
                    // K2 장전손잡이(노리쇠 메시에서 옆으로 튀어나온 부분) ↔ AK mod_charge(손이 잡는 곳)
                    var boltMesh = parts["bolt_carrier"].GetComponent<MeshFilter>().sharedMesh; var bv = boltMesh.vertices;
                    float cx = 0f;
                    var side = bv.Where(v => v.x - cx > 0.018f).Distinct().ToArray();
                    var charge = DonorAK5C ? null : weapon.Find("weapon_bolt/mod_charge");
                    if (side.Length > 0 && charge)
                    {
                        var h = side.Aggregate(Vector3.zero, (acc, v) => acc + v) / side.Length + parts["bolt_carrier"].localPosition + offset;
                        var r = RootPos(charge) - h;
                        L($"  참고   장전손잡이(K2 {(h.x > RootPos(weapon.Find("weapon_bolt")).x ? "오른쪽" : "왼쪽")}) ↔ AK mod_charge 차이 {r.magnitude * 1000:0.0} mm (x{r.x * 1000:0}, y{r.y * 1000:0}, z{r.z * 1000:0})");
                    }
                    // 조준선: K2 가늠자 구멍 중심(FBX 좌표 0, 0.131557, -0.005787 — K2Diagnostics와 같은 값)이 조준 카메라 높이에 오려면
                    var aimY = RootPos(weapon.Find("aim_camera")).y; var rearY = 0.131557f + offset.y;
                    L($"  참고   조준 카메라 높이 - K2 가늠자 구멍 = {(aimY - rearY) * 1000:+0.0;-0.0} mm (0이면 조준선 일치, 지금 SightDy {SightDy * 1000:+0.0;-0.0} mm 포함)");
                    L($"정렬 이동량(K2→M4, m): {offset.x:0.0000} {offset.y:0.0000} {offset.z:0.0000}");
                    foreach (var (k2, m4) in Anchors.Concat(InfoPairs))
                    {
                        var r = RootPos(weapon.Find(m4)) - parts[k2].localPosition - offset;
                        L($"  {(Anchors.Contains((k2, m4)) ? "기준점" : "참고  ")} {k2,-14} ↔ {m4,-18} 남은 차이 {r.magnitude * 1000:0.0} mm  (x{r.x * 1000:0}, y{r.y * 1000:0}, z{r.z * 1000:0})");
                    }
                }
                var off = offset;
                // 노드(위치·회전)는 템플릿 그대로 두고, K2 메시를 그 노드 기준으로 미리 변환해 넣는다(bake).
                // 1차 인게임(all-v002)에서 노드 회전을 바꿨더니 애니메이션이 원래 회전을 다시 써서 노리쇠가 90° 꺾였다.
                void Place(Transform t, string part)
                {
                    if (part == HIDE)
                    {
                        var mfh = t.GetComponent<MeshFilter>(); if (mfh) mfh.sharedMesh = EmptyMesh();
                        if (computeOffset && mfh) ReskinMap[t.name] = (EmptyMesh().name, new string[0]);
                        var bch = t.GetComponent<BoxCollider>(); if (bch) bch.size = Vector3.zero;
                        return;
                    }
                    var names = part.Split('+');
                    var src = parts[names[0]];
                    if (part == "selector" && t.parent && t.parent.name == "weapon_selector" && !DonorSCAR && !DonorAK5C)
                    {   // 0.9.5: K2 조정간 축 = 원본 리그 selector 뼈 머리(작업 모델 기준 레버 메시 경계의 앞뒤 0.725·높이 0.503, 총구 쪽에서) — 노드를 축으로 옮긴다.
                        // 클립이 weapon_selector 위치를 매 프레임 쓰므로 tools/build_anim.py가 모든 클립·컨테이너의 위치를 이 값으로 바꾼다(selector-pivot.json).
                        var b = src.GetComponent<MeshFilter>().sharedMesh.bounds;
                        var pivotLocal = new Vector3(b.center.x, b.min.y + .503f * b.size.y, b.max.z - .725f * b.size.z);
                        var pivotBody = src.localPosition + off + src.localRotation * pivotLocal;
                        var node = t.parent; node.position = root.transform.TransformPoint(pivotBody);
                        var w = node.parent; var lp = node.localPosition;
                        L($"  조정간 축(weapon_selector 로컬, 부모 {w.name}): {lp.x:R} {lp.y:R} {lp.z:R}");
                        File.WriteAllText(Path.Combine(Arg("-k2Out"), "selector-pivot.json"), $"{{\"x\":{lp.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\"y\":{lp.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},\"z\":{lp.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}}");
                    }
                    var nodeM = root.transform.worldToLocalMatrix * t.localToWorldMatrix;   // 본체 공간에서 노드 행렬
                    var mesh = names.Length == 1
                        ? Bake(src.GetComponent<MeshFilter>().sharedMesh, nodeM.inverse * Matrix4x4.Translate(src.localPosition + off), $"{rootName}_{t.name}")
                        : BakeCombined(names.Select(n => (parts[n].GetComponent<MeshFilter>().sharedMesh, nodeM.inverse * Matrix4x4.Translate(parts[n].localPosition + off))).ToArray(), $"{rootName}_{t.name}");
                    if (computeOffset) ReskinMap[t.name] = (mesh.name, MatsFor(src, mats).Select(m => m.name).ToArray());
                    var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                    if (mf) mf.sharedMesh = mesh;
                    if (mr) mr.sharedMaterials = MatsFor(src, mats);
                    var bc = t.GetComponent<BoxCollider>();
                    if (bc) { bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
                }

                var lod = root.GetComponent<LODGroup>();
                var lod0Extra = new List<Renderer>();
                foreach (var (node, part) in Swaps)
                {
                    var n = weapon.Find(node);
                    if (!n) { L($"  {rootName}: 노드 없음 {node} — 건너뜀"); continue; }
                    // 메시가 노드 자신에 있거나(본체 LOD) 자식에 있다(움직이는 부품).
                    var targets = n.GetComponent<MeshFilter>() || n.GetComponent<BoxCollider>() ? new[] { n }
                        : n.Cast<Transform>().Where(c => c.GetComponent<MeshFilter>() || c.GetComponent<BoxCollider>()).ToArray();
                    foreach (var t in targets) Place(t, part);
                    L($"  {rootName}: {node} → {(part == HIDE ? "숨김" : part)} ({targets.Length}개)");
                }
                foreach (var (parent, part) in Extras)
                {
                    var p = parent == "" ? weapon : weapon.Find(parent);
                    var go = new GameObject(part + "_LOD0");
                    go.transform.SetParent(p, false);   // 위치·회전 0 — 메시를 이 기준으로 변환
                    go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>();
                    Place(go.transform, part);
                    lod0Extra.Add(go.GetComponent<MeshRenderer>());
                    L($"  {rootName}: 추가 {part} (부모 {(parent == "" ? "weapon" : parent)})");
                }
                // 본체 슬롯(mod_reciever·mod_stock·mod_magazine)은 템플릿 위치 그대로 — 부품 메시를 슬롯 기준으로 변환한다(all-v003)
                if (lod)
                {
                    var lods = lod.GetLODs();
                    lods[0].renderers = lods[0].renderers.Where(r => r).Concat(lod0Extra).ToArray();
                    lod.SetLODs(lods); lod.RecalculateBounds();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // 여러 부품을 같은 노드 기준으로 변환해 한 메시로(재질이 같아 서브메시도 합친다)
        static Mesh BakeCombined((Mesh mesh, Matrix4x4 m)[] items, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(items.Select(i => new CombineInstance { mesh = i.mesh, transform = i.m }).ToArray(), true, true);
            mesh.RecalculateBounds();
            var dir = OUT + "Meshes/"; Directory.CreateDirectory(dir);
            var path = dir + name.Replace(' ', '_') + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Mesh EmptyMesh()
        {
            var path = OUT + "Meshes/k2_empty.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m) return m;
            Directory.CreateDirectory(OUT + "Meshes/");
            m = new Mesh { name = "k2_empty" };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // 메시 복사본을 행렬 m(강체 변환)으로 옮겨 에셋으로 저장. 원본 FBX 메시는 그대로.
        public static Mesh Bake(Mesh src, Matrix4x4 m, string name)
        {
            var mesh = UnityEngine.Object.Instantiate(src); mesh.name = name;
            var v = mesh.vertices; var n = mesh.normals; var t = mesh.tangents;
            for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = m.MultiplyVector(n[i]).normalized;
            for (int i = 0; i < t.Length; i++) { var d = m.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized; t[i] = new Vector4(d.x, d.y, d.z, t[i].w); }
            mesh.vertices = v; mesh.normals = n; mesh.tangents = t; mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            var dir = OUT + "Meshes/"; Directory.CreateDirectory(dir);
            var path = dir + name.Replace(' ', '_') + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void EditContainer(string path, string modelPath, string simplePath)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = NAME + "_container";
                var wp = root.GetComponents<MonoBehaviour>().FirstOrDefault(c => c && c.GetType().Name == "WeaponPrefab");
                if (!wp) throw new Exception("WeaponPrefab 없음");
                var so = new SerializedObject(wp);
                // WTT-Armory HK417과 같은 형태(0.3.0): 모델 + 재구성 컨트롤러만. fast 데이터·simple·spirit은 비운다
                // (재구성 컨트롤러에 원본 fast 데이터를 붙이면 상태 번호가 어긋나 애니메이션이 동작하지 않았다 — 0.1.1)
                so.FindProperty("_weaponObject").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                foreach (var f in new[] { "_weaponObjectSimple", "_animatorSimple", "_animatorSpirit", "_fastAnimatorControllerBinaryData",
                                          "DefaultMuzzlePrefab", "DefaultSmokeport", "DefaultHeatHazeEffect" })
                {
                    var prop = so.FindProperty(f);
                    if (prop != null) prop.objectReferenceValue = null;   // 총구 효과 3종은 빌드 뒤 바닐라 외부 참조로 채운다(fix_vanilla_refs --only)
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                var ctrl = so.FindProperty("_originalAnimatorController").objectReferenceValue;
                L($"container: _weaponObject → K2 모델, 컨트롤러 {(ctrl ? ctrl.name : "없음!")}, fast·simple·spirit·총구효과 비움");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // 만든 번들을 다시 열어 내용 확인 → validation.json
        static void Verify(string bundlePath, string outDir)
        {
            AssetBundle.UnloadAllAssetBundles(true);
            var b = AssetBundle.LoadFromFile(bundlePath);
            if (!b) throw new Exception("번들 다시 읽기 실패");
            try
            {
                var names = b.GetAllAssetNames();
                var all = b.LoadAllAssets();
                var sb = new StringBuilder("{\"bundle\":\"" + BUNDLE + "\",\"assets\":[" + string.Join(",", names.Select(n => "\"" + n + "\"")) + "],");
                var types = all.GroupBy(o => o.GetType().Name).Select(g => $"\"{g.Key}\":{g.Count()}");
                sb.Append("\"types\":{" + string.Join(",", types) + "},");
                var container = all.OfType<GameObject>().FirstOrDefault(g => g.name == NAME + "_container");
                if (!container) throw new Exception("다시 읽은 번들에 container 없음");
                var wp = container.GetComponents<MonoBehaviour>().FirstOrDefault(c => c && c.GetType().Name == "WeaponPrefab");
                string Ref(string field)
                {
                    var f = wp.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    var v = f?.GetValue(wp) as UnityEngine.Object;
                    return v ? v.name : "null";
                }
                var refs = new[] { "_weaponObject", "_weaponObjectSimple", "_originalAnimatorController", "_animatorSimple", "_animatorSpirit" }
                    .Select(f => $"\"{f}\":\"{Ref(f)}\"");
                sb.Append("\"weaponPrefab\":{" + string.Join(",", refs) + "},");
                // 모델 프리팹은 명시 에셋이 아니라 의존 에셋이라 이름으로 못 꺼낸다 → WeaponPrefab 참조로 찾는다
                var modelRef = wp.GetType().GetField("_weaponObject", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(wp) as GameObject;
                if (!modelRef) throw new Exception("_weaponObject 참조가 비어 있음");
                var model = (GameObject)UnityEngine.Object.Instantiate(modelRef);
                var rs = model.GetComponentsInChildren<MeshRenderer>(true);
                var meshes = rs.Select(r => r.GetComponent<MeshFilter>().sharedMesh).Where(m => m).ToArray();
                var bounds = rs.Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                sb.Append($"\"modelRenderers\":{rs.Length},\"meshNames\":[" + string.Join(",", meshes.Select(m => "\"" + m.name + "\"")) + "],");
                sb.Append($"\"vertices\":{meshes.Sum(m => m.vertexCount)},\"modelBoundsSize\":[{bounds.size.x:0.0000},{bounds.size.y:0.0000},{bounds.size.z:0.0000}],");
                sb.Append("\"shaders\":[" + string.Join(",", rs.SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => "\"" + m.shader.name + "\"").Distinct()) + "]}");
                File.WriteAllText(Path.Combine(outDir, "validation.json"), sb.ToString(), new UTF8Encoding(false));
                L("재읽기: " + string.Join(", ", refs) + $" / 렌더러 {rs.Length} / 범위 {bounds.size}");
                UnityEngine.Object.DestroyImmediate(model);
            }
            finally { b.Unload(true); }
        }
    }
}

