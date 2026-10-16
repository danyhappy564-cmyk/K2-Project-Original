using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace K2Tools {
 public static partial class K2Builder {
  [Serializable] class FinalReport { public bool passed; public int loadedBundles,customMeshes,animatorControllers,partPrefabs; public Vector3 assembledSizeMetres,fireportRootMetres; public string limitation="Editor structural/visual validation only; no in-game hands, animation or sound playback test."; }
  static GameObject VisualClone(Transform source) {
   var go=new GameObject(source.name);go.transform.localPosition=source.localPosition;go.transform.localRotation=source.localRotation;go.transform.localScale=source.localScale;
   var mf=source.GetComponent<MeshFilter>();var mr=source.GetComponent<MeshRenderer>();
   if(mf)go.AddComponent<MeshFilter>().sharedMesh=mf.sharedMesh;
   if(mr)go.AddComponent<MeshRenderer>().sharedMaterials=mr.sharedMaterials;
   foreach(Transform child in source){var c=VisualClone(child);c.transform.SetParent(go.transform,false);}
   return go;
  }
  public static void VerifyFinalAK() {
   var report=new FinalReport();var bundles=new Dictionary<string,AssetBundle>();
   try {
    AssetBundle.UnloadAllAssetBundles(true);
    foreach(var path in File.ReadAllLines(Arg("-k2LoadOrder"))) {
     var ab=AssetBundle.LoadFromFile(path);if(!ab)throw new Exception("Bundle load failed: "+path);bundles[path.Replace('\\','/')]=ab;report.loadedBundles++;
    }
    var folder=Arg("-k2Out").Replace('\\','/');var abody=bundles[folder+"/"+BUNDLE];GameObject main=null;
    foreach(var name in abody.GetAllAssetNames()) {
     var go=abody.LoadAsset<GameObject>(name);if(!go)throw new Exception("Missing prefab "+name);
     if(name.EndsWith(".generated.prefab")&&!name.Contains("simple"))main=go;
     foreach(var mf in go.GetComponentsInChildren<MeshFilter>(true)) {
      if(mf.name=="MuzzleJetCombinedMesh")continue;
      if(!mf.sharedMesh || !(mf.sharedMesh.name.StartsWith(NAME)||mf.sharedMesh.name=="k2_empty"))throw new Exception("Unreplaced mesh: "+mf.name);
      report.customMeshes++;
      var mr=mf.GetComponent<MeshRenderer>();if(!mr||mr.sharedMaterials.Any(m=>!m))throw new Exception("Missing material: "+mf.name);
     }
     foreach(var wp in go.GetComponentsInChildren<WeaponPrefab>(true)) {
      if(!wp._originalAnimatorController||!wp._animatorSimple||!wp._animatorSpirit)throw new Exception("Missing WeaponPrefab controllers");report.animatorControllers+=3;
     }
    }
    if(!main||report.customMeshes!=20||report.animatorControllers!=3)throw new Exception("Unexpected model count: "+report.customMeshes);
    var body=VisualClone(main.transform);body.transform.position=Vector3.zero;body.transform.rotation=Quaternion.identity;
    var placed=new Dictionary<string,Transform>{{"body",body.transform}};
    foreach(var d in PartDefs) {
     var ab=bundles[folder+"/"+d.Bundle];var prefab=ab.LoadAllAssets<GameObject>().Single();var go=VisualClone(prefab.transform);
     var slot=FindDeep(placed[d.Parent],d.Slot);if(!slot)throw new Exception("Missing attachment slot "+d.Slot);
     go.transform.SetParent(slot,false);go.transform.localPosition=Vector3.zero;go.transform.localRotation=Quaternion.Euler(90,0,0);placed[d.Id]=go.transform;report.partPrefabs++;
    }
    var points=body.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.sharedMesh&&m.sharedMesh.vertexCount>0&&m.name!="MuzzleJetCombinedMesh").SelectMany(m=>Corners(m.sharedMesh.bounds).Select(v=>m.transform.TransformPoint(v))).ToArray();
    var bounds=new Bounds(points[0],Vector3.zero);foreach(var v in points)bounds.Encapsulate(v);report.assembledSizeMetres=bounds.size;
    if(Mathf.Abs(bounds.size.z-0.98f)>0.005f||bounds.size.x>0.1f||bounds.size.y>0.4f)throw new Exception("Unexpected size "+bounds.size);
    var weapon=body.transform.Find("Weapon_root/Weapon_root_anim/weapon");report.fireportRootMetres=body.transform.InverseTransformPoint(weapon.Find("fireport").position);
    foreach(var r in body.GetComponentsInChildren<MeshRenderer>()) {
     if(r.name.Contains("LOD1")){r.enabled=false;continue;}
     r.sharedMaterials=r.sharedMaterials.Select(m=>{var material=new Material(Shader.Find("Standard"));material.color=new Color(.25f,.25f,.25f);return material;}).ToArray();
    }
    var camera=new GameObject("ReadbackCamera").AddComponent<Camera>();camera.fieldOfView=12;camera.nearClipPlane=.01f;camera.transform.position=FindDeep(body.transform,"aim_camera").position;camera.transform.rotation=Quaternion.LookRotation(Vector3.forward,Vector3.up);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.25f,.25f,.27f);
    var light=new GameObject("ReadbackLight").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(30,-60,0);RenderSettings.ambientLight=new Color(.55f,.55f,.55f);
    var rt=new RenderTexture(1200,1200,24);camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;var tex=new Texture2D(1200,1200,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1200,1200),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(folder,"readback-ads.png"),tex.EncodeToPNG());
    report.passed=true;File.WriteAllText(Path.Combine(folder,"unity-readback.json"),JsonUtility.ToJson(report,true));Debug.Log("FINAL_READBACK "+JsonUtility.ToJson(report));
   }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
  }
 }
}

