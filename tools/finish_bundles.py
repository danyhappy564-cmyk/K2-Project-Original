"""셰이더까지 고친 Unity 빌드 결과(<버전>-shader)를 설치용 최종 번들(<버전>)로 마무리한다.

  python tools/finish_bundles.py <셰이더 고친 폴더> <새 최종 폴더> [ak5c|scar]
  scar(0.8.0): 도너 컨테이너 바닐라 SCAR Mk16, 왼손 노드 단계 건너뜀
  ak5c(0.7.0): 도너 컨테이너·왼손 기준 손잡이를 WTT-Armory AK5C 번들로(사용자 개인용 — WTT 에셋 포함이라 배포 금지)

1) 부품: 바닐라 M4 부품의 컴포넌트 참조(fix_vanilla_refs) 2) 바닐라 AK-101 컨테이너 개조(reskin_vanilla)
3) 컨테이너 등록 목록 하나로(fix_container_table, 0.4.1) 4) 손잡이 왼손·탄창 총알 노드(fix_part_nodes, 0.4.2) 5) bundles.json
"""
import json,os,pathlib,shutil,subprocess,sys
base=pathlib.Path(__file__).resolve().parent.parent; tools=base/'tools'; src=pathlib.Path(sys.argv[1]); dst=pathlib.Path(sys.argv[2]); game=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
WTT=pathlib.Path('E:/SPT 4.1/SPT_Runtime/user/mods/WTT-Armory/bundles/WeaponAK5C'); DONOR=sys.argv[3] if len(sys.argv)>3 else 'ak101'; AK5C=DONOR=='ak5c'; SCAR=DONOR=='scar'
donor_container=WTT/'weapon_ak5c_container.bundle' if AK5C else game/'assets/content/weapons/scar/weapon_fn_mk16_556x45_container.bundle' if SCAR else game/'assets/content/weapons/ak100/weapon_izhmash_ak101_556x45_container.bundle'
assert not dst.exists(),'Refuse overwrite'
dst.mkdir(parents=True)
def run(script,*args):subprocess.run([sys.executable,str(tools/script),*map(str,args)],check=True)
pairs=[('recievers','reciever_daewoo_k2_std','reciever_ar15_colt_m4a1_std'),('barrels','barrel_daewoo_k2_465mm','barrel_ar15_370mm'),
       ('handguards','handguard_daewoo_k2_mits_long','handguard_ar15_colt_m4_length_std'),('handguards','handguard_daewoo_k2_classic','handguard_ar15_colt_m4_length_std'),
       ('muzzle','muzzle_daewoo_k2_std_556x45','muzzle_ar15_colt_usgi_a2_556x45'),
       ('stocks','stock_daewoo_k2_std','stock_ar15_colt_stock_tube_std'),('stocks','stock_daewoo_k2_ar','stock_ar15_colt_stock_tube_std'),('stocks','stock_daewoo_k2_light','stock_ar15_colt_stock_tube_std'),
       ('magazines','mag_stanag_daewoo_k2_std_556x45_30','mag_stanag_colt_ar15_std_556x45_30')]
for folder,ours,theirs in pairs:
 key=f'assets/content/items/mods/{folder}/{ours}.bundle'; donor=f'assets/content/items/mods/{folder}/{theirs}.bundle';run('fix_vanilla_refs.py',src/key,dst/key,game/donor,ours+'='+theirs)
for name in ['daewoo_k2_textures.bundle','daewoo_k2_body_meshes.bundle']:
 key='assets/content/weapons/daewoo_k2/'+name;(dst/key).parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src/key,dst/key)
for name in ['reskin-map.json','shader-fix.json','build-log.txt','preview-ads.png','preview-assembled.png']:
 if (src/name).exists():shutil.copy2(src/name,dst/name)
mapping=json.loads((src/'reskin-map.json').read_text());val=json.loads((src/'validation.json').read_text())
val['bundles'].append({'key':'assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle','dependencyKeys':[mapping['meshBundle'],mapping['texturesBundle']]})
(dst/'validation.json').write_text(json.dumps(val,indent=2),encoding='utf-8')
container=dst/'assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle'; raw=dst/'container-reskin-raw.bundle'
run('reskin_vanilla.py',donor_container,dst,dst/'reskin-map.json',raw)
run('fix_container_table.py',raw,container,'assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.prefab')
os.remove(raw)
run('fix_part_nodes.py',dst,dst,game,'--inplace',*(['--donor-handguard',WTT/'ak5c_handguard.bundle'] if AK5C else ['--no-palm'] if SCAR else []))
# 0.8.2 SCAR: 컨트롤러가 든 바닐라 FDE 번들을 우리 키·CAB으로 복사(다른 모드가 바닐라 키를 덮어써도 안 깨지게, vendor_bundle.py)
if SCAR:
    VKEY='assets/content/weapons/daewoo_k2/daewoo_k2_scar_fde_container.bundle'
    run('vendor_bundle.py',game/'assets/content/weapons/scar/weapon_fn_mk16_556x45_fde_container.bundle',dst/VKEY,VKEY,container)
    val=json.loads((dst/'validation.json').read_text(encoding='utf-8'))
    for b in val['bundles']:
        if b['key'].endswith('weapon_daewoo_k2_556x45_container.bundle'):b['dependencyKeys'].append(VKEY)
    val['bundles'].append({'key':VKEY,'dependencyKeys':[]})
    (dst/'validation.json').write_text(json.dumps(val,indent=2),encoding='utf-8')
run('make_bundles_json.py',dst,dst/'validation.json',game,dst/'bundles.json',*([DONOR] if DONOR!='ak101' else []))
