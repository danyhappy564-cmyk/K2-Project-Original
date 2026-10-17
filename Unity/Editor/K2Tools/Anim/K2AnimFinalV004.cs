// 완성 번들을 다시 읽어 전체 카메라·양팔 오차와 렌더를 검사하는 에디터 도구입니다.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace K2AnimV002 {
 public static class FinalCheckB {
  [Serializable] public class ExpectedEntry {public string original,effective;public bool edited;}
  [Serializable] public class ExpectedController {public string name;public ExpectedEntry[] entries;}
  [Serializable] public class Expected {public ExpectedController[] controllers;}
  [Serializable] public class Measurement {public float[] delta;public Contact k2,ak;}
  [Serializable] public class Contact {public float[] contactCenter;}
  [Serializable] public class ClipCheck {public string name;public int samples;public float cameraErrorMax,handErrorMax;}
  [Serializable] public class Report {public bool passed;public string error;public int layerCount,overrideControllers,modifiedClips;public List<ClipCheck> clips=new List<ClipCheck>();public List<string> renders=new List<string>();public string renderLimitation="Hand bones are diagnostic cylinders; no skin mesh or collision guarantee. Bundle meshes already lifted 27mm (ak-v011).";public Vector3 beforeSize,afterSize;public List<string> controllers=new List<string>();}
  static Dictionary<string,AssetBundle> loaded=new Dictionary<string,AssetBundle>();
  static string Arg(string k){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,k);return i<0?null:a[i+1];}
  static Transform Find(Transform r,string n){return r.GetComponentsInChildren<Transform>(true).First(t=>t.name==n);}
  static string mod,source,game,outdir;
  static AssetBundle Load(string key){if(loaded.ContainsKey(key))return loaded[key];var p=Path.Combine(mod,"bundles",key);if(!File.Exists(p))p=Path.Combine(source,key);if(!File.Exists(p))p=Path.Combine(game,key);var ab=AssetBundle.LoadFromFile(p);if(!ab)throw new Exception("LOAD "+key);loaded[key]=ab;return ab;}
  static float Weight(float t){return Mathf.SmoothStep(0,1,Mathf.InverseLerp(.32f,.60f,t))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,1.30f,t)));}
  static Material Mat(Color c){var m=new Material(Shader.Find("Standard"));m.color=c;m.SetFloat("_Glossiness",.25f);return m;}
  static void Rod(Vector3 a,Vector3 b,float radius,Material mat,Transform parent){var d=b-a;if(d.magnitude<.00001f)return;var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(parent);go.transform.position=(a+b)*.5f;go.transform.rotation=Quaternion.FromToRotation(Vector3.up,d.normalized);go.transform.localScale=new Vector3(radius*2,d.magnitude*.5f,radius*2);go.GetComponent<Renderer>().sharedMaterial=mat;}
  static void Ball(Vector3 p,float radius,Material mat,Transform parent){var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.transform.SetParent(parent);go.transform.position=p;go.transform.localScale=Vector3.one*radius*2;go.GetComponent<Renderer>().sharedMaterial=mat;}
  static string[] partKeys={"assets/content/items/mods/recievers/reciever_daewoo_k2_std.bundle","assets/content/items/mods/barrels/barrel_daewoo_k2_465mm.bundle","assets/content/items/mods/handguards/handguard_daewoo_k2_mits_long.bundle","assets/content/items/mods/muzzle/muzzle_daewoo_k2_std_556x45.bundle","assets/content/items/mods/stocks/stock_daewoo_k2_std.bundle","assets/content/items/mods/magazines/mag_stanag_daewoo_k2_std_556x45_30.bundle"};
  static string[] slots={"mod_reciever","mod_barrel","mod_handguard","mod_muzzle","mod_stock","mod_magazine"};
  static void Assemble(GameObject model){for(int i=0;i<partKeys.Length;i++){var prefab=Load(partKeys[i]).LoadAllAssets<GameObject>().Single();var go=UnityEngine.Object.Instantiate(prefab);var slot=Find(model.transform,slots[i]);go.transform.SetParent(slot,false);go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.Euler(90,0,0)*prefab.transform.localRotation;}}
  static Vector3 Render(GameObject prefab,AnimationClip clip,float time,bool after,string label,string file,Measurement measure,bool fold=false){
   var model=UnityEngine.Object.Instantiate(prefab);model.GetComponent<Animator>().enabled=false;Assemble(model);clip.SampleAnimation(model,time);var weapon=Find(model.transform,"weapon");
   var gray=Mat(new Color(.31f,.33f,.36f));var orange=Mat(new Color(.95f,.4f,.08f));
   foreach(var mf in model.GetComponentsInChildren<MeshFilter>(true)){
    // (Claude v004) 번들 자체가 27mm 올라가 있어 수동 올림 없음
    var mr=mf.GetComponent<MeshRenderer>();if(!mr)continue;
    if(mf.name.Contains("LOD1")||mf.name.Contains("MuzzleJet")){mr.enabled=false;continue;}
    mr.sharedMaterials=mr.sharedMaterials.Select(x=>mf.name.Contains("bolt_LOD0")?orange:gray).ToArray();
   }
   foreach(var lod in model.GetComponentsInChildren<LODGroup>(true))lod.ForceLOD(0);
   var marks=new GameObject("Diagnostic bones");var handmat=Mat(after?new Color(.15f,.8f,.95f):new Color(1,.35f,.32f));
   foreach(var t in model.GetComponentsInChildren<Transform>(true)){
    if(!t.name.StartsWith("Base Human")||t.parent==null||!t.parent.name.StartsWith("Base Human"))continue;
    float radius=t.name.Contains("Digit")?.0028f:t.name.Contains("Palm")?.007f:.009f;
    Rod(t.parent.position,t.position,radius,handmat,marks.transform);
   }
   var center=measure.k2.contactCenter;var tip=weapon.TransformPoint(new Vector3(center[0],center[1],center[2]+.027f));
   // 기준점은 정지 볼트 좌표입니다. 렌더에서는 실제 샘플링한 볼트 이동을 반영합니다.
   var bolt=Find(model.transform,"weapon_bolt");var restBolt=new Vector3(.00006115913f,-.44515517f,.01763977f);
   tip+=weapon.TransformVector(bolt.localPosition-restBolt);Ball(tip,.004f,Mat(Color.yellow),marks.transform);
   var renderers=model.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled&&r.GetComponent<MeshFilter>()&&r.GetComponent<MeshFilter>().sharedMesh&&r.GetComponent<MeshFilter>().sharedMesh.vertexCount>0).ToArray();
   var bounds=new Bounds(renderers[0].bounds.center,Vector3.zero);foreach(var r in renderers)bounds.Encapsulate(r.bounds);
   if(fold){var stock=model.GetComponentsInChildren<Transform>().First(t=>t.name.StartsWith("stock_daewoo_k2_std"));var sr=stock.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).First();Debug.Log("FOLD_CENTER "+label+" "+weapon.InverseTransformPoint(sr.bounds.center).ToString("F6"));}
   var camera=new GameObject("CheckCamera").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=fold?.47f:.30f;camera.nearClipPlane=.01f;camera.farClipPlane=10;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.065f,.085f);
   var look=weapon.TransformPoint(fold?new Vector3(0,-.24f,.015f):new Vector3(0,-.35f,-.03f));
   camera.transform.position=look+weapon.TransformVector(fold?new Vector3(.55f,.45f,.9f):new Vector3(.9f,.04f,.30f));camera.transform.rotation=Quaternion.LookRotation(look-camera.transform.position,weapon.TransformDirection(Vector3.forward));
   var textgo=new GameObject("Label");var text=textgo.AddComponent<TextMesh>();text.text=label+"\nBONES ONLY / metres";text.fontSize=42;text.characterSize=.007f;text.color=Color.white;text.anchor=TextAnchor.UpperLeft;textgo.transform.position=camera.ViewportToWorldPoint(new Vector3(.03f,.96f,.8f));textgo.transform.rotation=camera.transform.rotation;
   var rt=new RenderTexture(1200,800,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1200,800,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1200,800),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(outdir,file),tex.EncodeToPNG());RenderTexture.active=null;
   UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(textgo);UnityEngine.Object.DestroyImmediate(marks);UnityEngine.Object.DestroyImmediate(model);return bounds.size;
  }
  // 0.9.7: 의도적으로 바꿔 끼운 칸(tools/build_anim.py SLOT_SWAP) — 숙련도 0단계 빈 탄창 장전을 멈치 때리기 클립으로
  static readonly Dictionary<string,string> Swap=new Dictionary<string,string>{{"ak100_reload_charge_0","ak100_reload_charge_2"}};
  public static void Run(){
   var report=new Report();outdir=Arg("-k2Out");Directory.CreateDirectory(outdir);
   try{
    mod=Arg("-k2Mod");source=Arg("-k2Source");game=Arg("-k2Game");string key="assets/content/weapons/daewoo_k2/weapon_daewoo_k2_556x45_container.bundle";
    var manifest=JsonUtility.FromJson<Probe.Manifest>(File.ReadAllText(Path.Combine(mod,"bundles.json")));foreach(var e in manifest.manifest)foreach(var dep in e.dependencyKeys)Load(dep);
    var srcmanifest=JsonUtility.FromJson<Probe.Manifest>(File.ReadAllText(Path.Combine(source,"bundles.json")));foreach(var e in srcmanifest.manifest.Where(e=>partKeys.Contains(e.key)))foreach(var dep in e.dependencyKeys)Load(dep);
    var wp=Load(key).LoadAllAssets<GameObject>().Single().GetComponent<WeaponPrefab>();var ours=Load("assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle").LoadAllAssets<AnimationClip>();report.modifiedClips=ours.Length;
    var measure=JsonUtility.FromJson<Measurement>(File.ReadAllText(Arg("-k2Measure")));var delta=new Vector3(measure.delta[0],measure.delta[1],measure.delta[2]);
    var vanilla=loaded.Where(p=>p.Key!="assets/content/weapons/daewoo_k2/anim_v002/k2_ak_overrides.bundle").SelectMany(p=>p.Value.LoadAllAssets<AnimationClip>()).GroupBy(c=>c.name).ToDictionary(g=>g.Key,g=>g.First());
    var before=UnityEngine.Object.Instantiate(wp._weaponObject);var after=UnityEngine.Object.Instantiate(wp._weaponObject);before.GetComponent<Animator>().enabled=false;after.GetComponent<Animator>().enabled=false;
    foreach(var ctrl in new[]{wp._originalAnimatorController,wp._animatorSimple,wp._animatorSpirit}){
     var animator=after.GetComponent<Animator>();animator.enabled=true;animator.runtimeAnimatorController=ctrl;animator.Update(0);if(animator.layerCount!=7)throw new Exception("layers");report.layerCount=animator.layerCount;animator.enabled=false;
     var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();((AnimatorOverrideController)ctrl).GetOverrides(pairs);int linked=pairs.Count(p=>p.Value&&ours.Contains(p.Value));report.controllers.Add(ctrl.name+" slots="+pairs.Count+" custom="+linked);var expectations=JsonUtility.FromJson<Expected>(File.ReadAllText(Arg("-k2Expected"))).controllers.Single(c=>c.name==ctrl.name); foreach(var pair in pairs){var exp=expectations.entries.First(x=>x.original==pair.Key.name);var effective=pair.Value?pair.Value:pair.Key;var want=exp.effective;if(Swap.TryGetValue(want,out var sw))want=sw;if(effective.name!=want || (exp.edited&&!ours.Contains(effective)))throw new Exception("override mismatch "+pair.Key.name+" expected="+exp.effective);}report.overrideControllers++;
    }
    foreach(var clip in ours){ Debug.Log("SAMPLE "+clip.name);
     var original=vanilla[clip.name];var result=new ClipCheck{name=clip.name};var bw=Find(before.transform,"weapon");var aw=Find(after.transform,"weapon");var bc=Find(before.transform,"aim_camera");var ac=Find(after.transform,"aim_camera");bool charge=clip.name.Contains("reload_charge_");
     string side=clip.name.EndsWith("_2")?"L":"R";var bp=Find(before.transform,"Base Human"+side+"Palm");var ap=Find(after.transform,"Base Human"+side+"Palm");
     int count=charge?Mathf.CeilToInt(clip.length*60):10;
     for(int i=0;i<=count;i++){
      float time=clip.length*i/count;original.SampleAnimation(before,time);clip.SampleAnimation(after,time);result.samples++;
      float cam=Vector3.Distance(aw.InverseTransformPoint(ac.position)-bw.InverseTransformPoint(bc.position),new Vector3(0,0,.027f));result.cameraErrorMax=Mathf.Max(result.cameraErrorMax,cam);
      if(charge){float err=Vector3.Distance(aw.InverseTransformPoint(ap.position),bw.InverseTransformPoint(bp.position)+delta*Weight(time));result.handErrorMax=Mathf.Max(result.handErrorMax,err);}
     }
     report.clips.Add(result);
    }
    UnityEngine.Object.DestroyImmediate(before);UnityEngine.Object.DestroyImmediate(after);
    RenderSettings.ambientLight=new Color(.65f,.65f,.65f);var light=new GameObject("CheckLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.4f;light.transform.rotation=Quaternion.Euler(35,-50,0);
    foreach(var name in new[]{"ak100_reload_charge_0","ak100_reload_charge_1","ak100_reload_charge_2","ak100_stock_folding_left","ak100_stock_folded_left"}){
     var edited=ours.Single(c=>c.name==name);bool fold=name.Contains("stock");float t=name.Contains("folding")?edited.length*.5f:fold?edited.length*.5f:.8f;
     foreach(bool isAfter in new[]{false,true}){var file=name+(isAfter?"-after.png":"-before.png");var size=Render(wp._weaponObject,isAfter?edited:vanilla[name],t,isAfter,name+" / "+(isAfter?"AFTER +27mm":"BEFORE"),file,measure,fold);report.renders.Add(file);if(name=="ak100_reload_charge_1"){if(isAfter)report.afterSize=size;else report.beforeSize=size;}}
    }
    report.passed=report.clips.All(c=>c.cameraErrorMax<.0001f&&c.handErrorMax<.001f);if(!report.passed)report.error="Camera 0.1mm / hand 1mm threshold exceeded; inspect per-clip maxima.";
   }catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Path.Combine(outdir,"final-check.json"),JsonUtility.ToJson(report,true));EditorApplication.Exit(report.passed?0:1);
  }
 }
}
