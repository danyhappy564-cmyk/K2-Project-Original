using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;
using File = System.IO.File;

namespace K2Original;

/// <summary>
/// K2 소총과 전용 부품 9종(교체 손잡이 1·개머리판 2 포함)을 등록한다(외형은 이 모드의 bundles.json 번들).
/// GL 번들이 bundles.json에 있으면 K201 유탄발사기·GL 핸드가드도(data/items_gl, assort_gl.json).
/// 순서: 아이템 복제(외형·무게 포함, data/items) → 외형 경로 검사 → 슬롯 정리·필터 → 기본 프리셋 → 피스키퍼 판매.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.Preload + 2)]
public sealed class K2Mod(CustomItemService customItems, TemplateTable templates, GlobalTable globals,
    TradersTable traders, ModHelper modHelper, JsonUtil json, ISptLogger<K2Mod> logger) : IOnLoad
{
    public const string Weapon = "d22000000000000000000001";
    public const string Upper = "d22000000000000000000011";
    public const string Barrel = "d22000000000000000000012";
    public const string Handguard = "d22000000000000000000013";
    public const string Muzzle = "d22000000000000000000014";
    public const string Stock = "d22000000000000000000015";
    public const string Magazine = "d22000000000000000000016";
    public const string HandguardClassic = "d22000000000000000000017";   // 0.5.0 교체 부품(원본 팩 UPDATE2·stocks)
    public const string StockAr = "d22000000000000000000018";
    public const string StockLight = "d22000000000000000000019";
    const string PresetId = "d22000000000000000000002";
    const string HandguardScopeSlotId = "d22000000000000000000101";
    // 0.6.0 MITS 핸드가드 손잡이·M-LOK 레일 칸 — 바닐라 Geissele SMR Mk16 13.5" 슬롯 정의(필터)를 복사, 노드는 K2Parts.cs가 만든다
    const string MlokDonor = "5ea16ada09aa976f2e7a51be";
    static readonly (string name, string id)[] HandguardRailSlots =
    [
        ("mod_foregrip", "d22000000000000000000102"), ("mod_mount_000", "d22000000000000000000103"), ("mod_mount_001", "d22000000000000000000104"),
        ("mod_mount_002", "d22000000000000000000105"), ("mod_tactical_000", "d22000000000000000000106"),
    ];
    // GL(K201) — 핸드가드(GL 장착 칸 포함)·런처(바닐라 M203 개조 번들). 데이터는 data/items_gl·assort_gl.json,
    // bundles.json에 GL 번들이 있을 때만 등록한다(번들을 합치기 전에 패키징해도 서버가 멈추지 않게)
    public const string HandguardGl = "d22000000000000000000020";
    public const string LauncherK201 = "d22000000000000000000021";
    const string HandguardGlScopeSlotId = "d22000000000000000000107";
    const string HandguardGlLauncherSlotId = "d22000000000000000000108";
    const string LauncherSlotDonor = "55d3632e4bdc2d972f8b4569";   // 바닐라 AR 370mm 총열 — mod_launcher(M203 칸) 정의를 복사
    const string Peacekeeper = "5935c25fb3acc3127c3d8cd9";
    const string DefaultInventory = "55d7217a4bdc2d86028b456d";
    const string M4A1 = "5447a9cd4bdc2dbd208b4567";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var data = Path.Combine(folder, "data");
        T Read<T>(string file) where T : class =>
            json.DeserializeFromFile<T>(Path.Combine(data, file)) ?? throw new InvalidDataException($"[K2] 데이터 로드 실패: {file}");
        TemplateItem Item(string id) => templates.Items[new MongoId(id)];

        // 1. 아이템 복제 (data/items/*.json — 파일 이름 순서)
        CloneItems("items");

        // 2. 외형 확인 — Prefab 경로는 data/items/*.json의 overrideProperties에 적는다(WTT 방식). 여기서는 bundles.json에 있는지만 검사
        var bundleKeys = File.ReadAllText(Path.Combine(folder, "bundles.json"));
        CheckPrefabs(Weapon, Upper, Barrel, Handguard, Muzzle, Stock, Magazine, HandguardClassic, StockAr, StockLight);

        // 3. 슬롯 — 프리팹에 실제로 있는 노드와 맞춘다(K2Parts.cs 참고)
        // 핸드가드 조준경 슬롯은 상부(M4 원본)의 mod_scope 정의를 복사해 만든다 — 상부에서 지우기 전에 복사
        var upperScope = Item(Upper).Properties!.Slots!.Single(s => s.Name == "mod_scope");
        var handguardScope = json.Deserialize<Slot>(json.Serialize(upperScope)!)!;
        handguardScope.Id = new MongoId(HandguardScopeSlotId);
        handguardScope.Parent = new MongoId(Handguard);

        KeepSlots(Weapon, "mod_magazine", "mod_reciever", "mod_stock");      // 도너 AK-101 기준(0.4.0) — 가스블록·소염기·총열은 K2 상부 쪽, 권총손잡이·장전손잡이는 K2 메시에 포함
        KeepSlots(Upper, "mod_barrel", "mod_handguard");                     // 가늠자 일체형, 레일은 핸드가드 쪽
        KeepSlots(Barrel, "mod_muzzle");                                     // 가스블록·가늠쇠 일체형, 유탄발사기 칸은 GL 핸드가드 쪽(5b)
        KeepSlots(Handguard);                                                // M4 하부 핸드가드 칸 제거
        KeepSlots(HandguardClassic);                                         // 클래식 손잡이는 레일 없음 — 조준경 칸도 없음
        KeepSlots(Stock); KeepSlots(StockAr); KeepSlots(StockLight);        // 셋 다 접힘: 도너 AK-101 접기 동작을 오른쪽으로 거울 반전한 K2 전용 클립(0.9.0, anim_v002 번들)
        Item(Handguard).Properties!.Slots = [handguardScope, .. HandguardRailSlots.Select(r =>
        {
            var slot = json.Deserialize<Slot>(json.Serialize(FindSlot(MlokDonor, r.name))!)!;
            slot.Id = new MongoId(r.id); slot.Parent = new MongoId(Handguard);
            return slot;
        })];

        AllowOnly(Weapon, "mod_reciever", Upper);
        AllowOnly(Weapon, "mod_stock", Stock);
        AllowAlso(Weapon, "mod_stock", StockAr);
        AllowAlso(Weapon, "mod_stock", StockLight);
        // 탄창: AK 탄창이 아니라 K2 탄창 + STANAG(M4A1이 받는 탄창 목록) — K2는 STANAG 호환
        AllowOnly(Weapon, "mod_magazine", Magazine);
        foreach (var f in FindSlot(M4A1, "mod_magazine").Properties!.Filters!)
            foreach (var id in f.Filter!) AllowAlso(Weapon, "mod_magazine", id.ToString());
        AllowOnly(Upper, "mod_barrel", Barrel);
        AllowOnly(Upper, "mod_handguard", Handguard);
        AllowAlso(Upper, "mod_handguard", HandguardClassic);
        AllowAlso(Barrel, "mod_muzzle", Muzzle);                            // 5.56 AR 소염기·소음기 어댑터도 허용

        // 4. 기본 프리셋
        var preset = Read<Preset>("preset.json");
        if (globals.ItemPresets.ContainsKey(new MongoId(PresetId))) throw new InvalidDataException("[K2] 프리셋 ID 충돌");
        globals.ItemPresets[new MongoId(PresetId)] = preset;

        // 5. 피스키퍼 1레벨 판매(달러)
        var trader = traders.GetTrader(new MongoId(Peacekeeper)) ?? throw new InvalidDataException("[K2] 피스키퍼 없음");
        var assort = Read<TraderAssort>("assort.json");
        foreach (var it in assort.Items)
            if (trader.Assort.Items.Any(e => e.Id == it.Id)) throw new InvalidDataException($"[K2] 상품 ID 충돌: {it.Id}");
        foreach (var it in assort.Items) trader.Assort.Items.Add(it);
        foreach (var (id, scheme) in assort.BarterScheme) trader.Assort.BarterScheme[id] = scheme;
        foreach (var (id, level) in assort.LoyalLevelItems) trader.Assort.LoyalLevelItems[id] = level;

        // 5b. GL(K201) — GL 핸드가드의 mod_launcher 칸에 K201만. 바닐라 M203은 받지 않는다
        //     (M203 번들의 무기 기준 노드가 Hanson 총열 자리 기준이라 K2 자리에서는 런처 모드 때 총이 어긋난다 — K201 번들은 K2 자리로 보정)
        var glOn = new[] { HandguardGl, LauncherK201 }.All(id => GlPrefabListed(id));
        if (glOn)
        {
            CloneItems("items_gl");
            CheckPrefabs(HandguardGl, LauncherK201);
            var glScope = json.Deserialize<Slot>(json.Serialize(upperScope)!)!;
            glScope.Id = new MongoId(HandguardGlScopeSlotId); glScope.Parent = new MongoId(HandguardGl);
            var glLauncher = json.Deserialize<Slot>(json.Serialize(FindSlot(LauncherSlotDonor, "mod_launcher"))!)!;
            glLauncher.Id = new MongoId(HandguardGlLauncherSlotId); glLauncher.Parent = new MongoId(HandguardGl);
            Item(HandguardGl).Properties!.Slots = [glScope, glLauncher];
            AllowOnly(HandguardGl, "mod_launcher", LauncherK201);
            AllowAlso(Upper, "mod_handguard", HandguardGl);
            var glAssort = Read<TraderAssort>("assort_gl.json");
            foreach (var it in glAssort.Items)
                if (trader.Assort.Items.Any(e => e.Id == it.Id)) throw new InvalidDataException($"[K2] 상품 ID 충돌: {it.Id}");
            foreach (var it in glAssort.Items) trader.Assort.Items.Add(it);
            foreach (var (id, scheme) in glAssort.BarterScheme) trader.Assort.BarterScheme[id] = scheme;
            foreach (var (id, level) in glAssort.LoyalLevelItems) trader.Assort.LoyalLevelItems[id] = level;
        }
        else logger.Warning("[K2] GL 번들(handguard_daewoo_k2_gl·launcher_daewoo_k201)이 bundles.json에 없어 K201·GL 핸드가드는 건너뜀");

        // 6. 주무기 칸에 넣을 수 있게
        foreach (var slot in Item(DefaultInventory).Properties!.Slots!.Where(s => s.Name is "FirstPrimaryWeapon" or "SecondPrimaryWeapon"))
            foreach (var filter in slot.Properties!.Filters!) filter.Filter!.Add(new MongoId(Weapon));

        logger.Info($"[K2] K2 소총·전용 부품 {(glOn ? "11종(K201 유탄발사기·GL 핸드가드 포함)" : "9종")} 등록 완료 — 피스키퍼, 기본 프리셋 'K2 표준'");
        logger.Info("[K2] 높은 산 깊은 골 적막한 산하");
        return Task.CompletedTask;

        void CloneItems(string sub)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(data, sub), "*.json").OrderBy(f => f))
            {
                var def = Read<NewItemFromCloneDetails>(Path.Combine(sub, Path.GetFileName(file)));
                if (!templates.Items.ContainsKey(def.ItemTplToClone))
                    throw new InvalidDataException($"[K2] 복제 원본 없음: {def.ItemTplToClone} ({Path.GetFileName(file)})");
                if (templates.Items.ContainsKey(def.NewId))
                    throw new InvalidDataException($"[K2] 아이템 ID 충돌(중복 설치?): {def.NewId}");
                var result = customItems.CreateItemFromClone(def, Assembly.GetExecutingAssembly());
                if (!result.Success)
                    throw new InvalidOperationException($"[K2] 등록 실패 {def.NewId}: {string.Join("; ", result.Errors)}");
            }
        }
        void CheckPrefabs(params string[] ids)
        {
            foreach (var id in ids)
            {
                var path = Item(id).Properties?.Prefab?.Path;
                if (string.IsNullOrEmpty(path) || !bundleKeys.Contains($"\"{path}\""))
                    throw new InvalidDataException($"[K2] 외형 번들 경로가 bundles.json에 없음: {id} → {path}");
            }
        }
        // 복제 전이라 템플릿이 없으므로 data/items_gl의 Prefab 경로로 본다
        bool GlPrefabListed(string id)
        {
            var def = Directory.GetFiles(Path.Combine(data, "items_gl"), "*.json").Select(f => Read<NewItemFromCloneDetails>(Path.Combine("items_gl", Path.GetFileName(f))))
                .SingleOrDefault(d => d.NewId == new MongoId(id)) ?? throw new InvalidDataException($"[K2] data/items_gl에 {id} 없음");
            var path = def.OverrideProperties?.Prefab?.Path;
            return !string.IsNullOrEmpty(path) && bundleKeys.Contains($"\"{path}\"");
        }

        void KeepSlots(string id, params string[] names)
        {
            var p = Item(id).Properties!;
            p.Slots = p.Slots!.Where(s => names.Contains(s.Name)).ToList();
        }
        Slot FindSlot(string id, string name) =>
            Item(id).Properties!.Slots!.SingleOrDefault(s => s.Name == name) ?? throw new InvalidDataException($"[K2] 슬롯 없음: {id}/{name}");
        void AllowOnly(string id, string slotName, string part)
        {
            foreach (var f in FindSlot(id, slotName).Properties!.Filters!) { f.Filter!.Clear(); f.Filter.Add(new MongoId(part)); }
        }
        void AllowAlso(string id, string slotName, string part)
        {
            foreach (var f in FindSlot(id, slotName).Properties!.Filters!) f.Filter!.Add(new MongoId(part));
        }
    }
}
