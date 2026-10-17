"""가려진 조준 시점을 보정합니다. 이전 번들과 검사 화면은 별도 보존합니다."""
import pathlib,json,shutil,UnityPy
ROOT=pathlib.Path(__file__).resolve().parents[1];OUT=ROOT/'Bundles/gl-v001';BACK=ROOT/'diagnostics/pre-aim-v001'
assert not BACK.exists();BACK.mkdir()
key='assets/content/weapons/daewoo_k2/launcher_daewoo_k201_40x46_container.bundle'
for p in [OUT/key,OUT/'unity-readback.json',*OUT.glob('readback-*.png')]:shutil.copy2(p,BACK/p.name)
env=UnityPy.load((OUT/key).read_bytes());sf=next(s for s in env.file.files.values() if hasattr(s,'objects'))
names={o.path_id:o.read_typetree()['m_Name'] for o in sf.objects.values() if o.type.name=='GameObject'}
position={'x':-0.0443810157,'y':0.0614623152,'z':-0.3369944096};changes=[]
audit=json.loads((OUT/'clip-binding-audit.json').read_text(encoding='utf8'));assert not audit['aimBindings']
for o in sf.objects.values():
 if o.type.name!='Transform':continue
 t=o.read_typetree()
 if names[t['m_GameObject']['m_PathID']]!='mod_aim_camera':continue
 changes.append({'before':t['m_LocalPosition'],'after':position});t['m_LocalPosition']=position;o.save_typetree(t)
assert len(changes)==2
tmp=OUT/(key+'.tmp');tmp.write_bytes(env.file.save(packer='lz4'));tmp.replace(OUT/key)
(OUT/'aim-adjustment.json').write_text(json.dumps({'reason':'기존 눈 위치가 총몸에 가려짐. GL_side_sight 외곽에서 왼쪽 8mm, 위 5mm 위치를 눈 높이로 사용. 뒤 거리 250mm 유지. 접힌 조준기이며 탄도 조준 정합은 미검증.','source_sight_bounds_blender_m':[[-.2029405236,-.1085084528],[-.0362810157,-.0195493307],[.1065904796,.1237830967]],'changes':changes},ensure_ascii=False,indent=2),encoding='utf8')
print('이전 번들·화면 보존 후 조준 시점 2개 보정',position)
