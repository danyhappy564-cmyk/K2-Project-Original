import json,pathlib,shutil,subprocess,sys
base=pathlib.Path(__file__).resolve().parent.parent; tools=base/'tools'; src=base/'Bundles/ak-v004-shader'; dst=base/'Bundles/ak-v004-final'; game=pathlib.Path('E:/SPT 4.1/EscapeFromTarkov_Data/StreamingAssets/Windows')
assert not dst.exists(),'Refuse overwrite'
dst.mkdir(parents=True)
def run(script,*args):subprocess.run([sys.executable,str(tools/script),*map(str,args)],check=True)
pairs=[('recievers','reciever_daewoo_k2_std','reciever_ar15_colt_m4a1_std'),('barrels','barrel_daewoo_k2_465mm','barrel_ar15_370mm'),('handguards','handguard_daewoo_k2_mits_long','handguard_ar15_colt_m4_length_std'),('muzzle','muzzle_daewoo_k2_std_556x45','muzzle_ar15_colt_usgi_a2_556x45'),('stocks','stock_daewoo_k2_std','stock_ar15_colt_stock_tube_std'),('magazines','mag_stanag_daewoo_k2_std_556x45_30','mag_stanag_colt_ar15_std_556x45_30')]
for folder,ours,theirs in pairs:
 key=f'assets/content/items/mods/{folder}/{ours}.bundle'; donor=f'assets/content/items/mods/{folder}/{theirs}.bundle';run('fix_vanilla_refs.py',src/key,dst/key,game/donor,ours+'='+theirs)
for name in ['daewoo_k2_textures.bundle','daewoo_k2_body_meshes.bundle']:
 key='assets/content/weapons/daewoo_k2/'+name;(dst/key).parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src/key,dst/key)
for name in ['reskin-map.json','shader-fix.json','build-log.txt','preview-ads.png','preview-assembled.png']:shutil.copy2(src/name,dst/name)
mapping=json.loads((src/'reskin-map.json').read_text());val=json.loads((src/'validation.json').read_text())
val['bundles'].append({'key':'assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle','dependencyKeys':[mapping['meshBundle'],mapping['texturesBundle']]})
(dst/'validation.json').write_text(json.dumps(val,indent=2),encoding='utf-8')
run('reskin_vanilla.py',game/'assets/content/weapons/ak100/weapon_izhmash_ak101_556x45_container.bundle',dst,dst/'reskin-map.json',dst/'assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle')
run('make_bundles_json.py',dst,dst/'validation.json',game,dst/'bundles.json')


