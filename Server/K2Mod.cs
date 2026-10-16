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

namespace K2Original;

/// <summary>
/// K2 소총과 전용 부품 6종을 등록한다(외형은 이 모드의 bundles.json 번들).
/// 순서: 아이템 복제 → 프리팹 경로를 K2 번들로 → 슬롯 정리·필터 → 기본 프리셋 → 피스키퍼 판매.
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
    const string PresetId = "d22000000000000000000002";
    const string HandguardScopeSlotId = "d22000000000000000000101";
    const string Peacekeeper = "5935c25fb3acc3127c3d8cd9";
    const string DefaultInventory = "55d7217a4bdc2d86028b456d";

    // 아이템 → 번들 키(Unity 빌더 K2Parts.cs의 번들 이름과 같아야 한다)
    static readonly Dictionary<string, string> Prefabs = new()
    {
        [Weapon] = "assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle",
        [Upper] = "assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle",
        [Barrel] = "assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle",
        [Handguard] = "assets/content/items/mods/handguards/handguard_daewoo_k2_mits_long.bundle",
        [Muzzle] = "assets/content/items/mods/muzzle/muzzle_daewoo_k2_std_556x45.bundle",
        [Stock] = "assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle",
        [Magazine] = "assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle",
    };

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var data = Path.Combine(folder, "data");
        T Read<T>(string file) where T : class =>
            json.DeserializeFromFile<T>(Path.Combine(data, file)) ?? throw new InvalidDataException($"[K2] 데이터 로드 실패: {file}");
        TemplateItem Item(string id) => templates.Items[new MongoId(id)];

        // 1. 아이템 복제 (data/items/*.json — 파일 이름 순서)
        foreach (var file in Directory.GetFiles(Path.Combine(data, "items"), "*.json").OrderBy(f => f))
        {
            var def = Read<NewItemFromCloneDetails>(Path.Combine("items", Path.GetFileName(file)));
            if (!templates.Items.ContainsKey(def.ItemTplToClone))
                throw new InvalidDataException($"[K2] 복제 원본 없음: {def.ItemTplToClone} ({Path.GetFileName(file)})");
            if (templates.Items.ContainsKey(def.NewId))
                throw new InvalidDataException($"[K2] 아이템 ID 충돌(중복 설치?): {def.NewId}");
            var result = customItems.CreateItemFromClone(def, Assembly.GetExecutingAssembly());
            if (!result.Success)
                throw new InvalidOperationException($"[K2] 등록 실패 {def.NewId}: {string.Join("; ", result.Errors)}");
        }

        // 2. 외형 = K2 번들
        foreach (var (id, path) in Prefabs) Item(id).Properties!.Prefab!.Path = path;

        // 3. 슬롯 — 프리팹에 실제로 있는 노드와 맞춘다(K2Parts.cs 참고)
        // 핸드가드 조준경 슬롯은 상부(M4 원본)의 mod_scope 정의를 복사해 만든다 — 상부에서 지우기 전에 복사
        var upperScope = Item(Upper).Properties!.Slots!.Single(s => s.Name == "mod_scope");
        var handguardScope = json.Deserialize<Slot>(json.Serialize(upperScope)!)!;
        handguardScope.Id = new MongoId(HandguardScopeSlotId);
        handguardScope.Parent = new MongoId(Handguard);

        KeepSlots(Weapon, "mod_magazine", "mod_reciever", "mod_stock");      // 권총손잡이 일체형, 장전손잡이는 노리쇠에 붙음
        KeepSlots(Upper, "mod_barrel", "mod_handguard");                     // 가늠자 일체형, 레일은 핸드가드 쪽
        KeepSlots(Barrel, "mod_muzzle");                                     // 가스블록·가늠쇠 일체형, 유탄발사기는 아직 없음
        KeepSlots(Handguard);                                                // M4 하부 핸드가드 칸 제거
        KeepSlots(Stock);
        Item(Handguard).Properties!.Slots = [handguardScope];

        AllowOnly(Weapon, "mod_reciever", Upper);
        AllowOnly(Weapon, "mod_stock", Stock);
        AllowAlso(Weapon, "mod_magazine", Magazine);                        // STANAG 호환 — 기존 M4 탄창도 그대로 허용
        AllowOnly(Upper, "mod_barrel", Barrel);
        AllowOnly(Upper, "mod_handguard", Handguard);
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

        // 6. 주무기 칸에 넣을 수 있게
        foreach (var slot in Item(DefaultInventory).Properties!.Slots!.Where(s => s.Name is "FirstPrimaryWeapon" or "SecondPrimaryWeapon"))
            foreach (var filter in slot.Properties!.Filters!) filter.Filter!.Add(new MongoId(Weapon));

        logger.Info("[K2] K2 소총·전용 부품 6종 등록 완료 — 피스키퍼 1레벨, 기본 프리셋 'K2 표준'");
        return Task.CompletedTask;

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
