using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace K2Tools {
 public static partial class K2Builder {
  static float Pitch => float.TryParse(Arg("-k2Pitch"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:0f;
  static GameObject PrepareSource() {
   var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SRC+"k2_parts.fbx"));
   var q=Quaternion.Euler(Pitch,0,0);
   foreach(var mf in go.GetComponentsInChildren<MeshFilter>(true)) {
    if(Quaternion.Angle(mf.transform.localRotation,Quaternion.identity)>0.01f || (mf.transform.localScale-Vector3.one).magnitude>0.001f) throw new Exception("Source transform is not identity: "+mf.name);
    var m=UnityEngine.Object.Instantiate(mf.sharedMesh); m.vertices=m.vertices.Select(v=>q*v).ToArray(); m.normals=m.normals.Select(v=>q*v).ToArray();
    m.tangents=m.tangents.Select(t=>{var v=q*new Vector3(t.x,t.y,t.z);return new Vector4(v.x,v.y,v.z,t.w);}).ToArray();m.RecalculateBounds();mf.sharedMesh=m;mf.transform.localPosition=q*mf.transform.localPosition;
   }
   return go;
  }
  public static void DiagnoseAK() {
   try {
    var fbx=PrepareSource();
    var parts=fbx.GetComponentsInChildren<MeshFilter>(true).ToDictionary(m=>m.name.Replace("daewoo_K2_",""),m=>m);
    var root=AssetDatabase.LoadAssetAtPath<GameObject>(DonorModelPath).transform;
    var weapon=root.Find("Weapon_root/Weapon_root_anim/weapon");
    Vector3 Pos(Transform t)=>root.InverseTransformPoint(t.position);
    var off=Anchors.Select(a=>Pos(weapon.Find(a.m4))-parts[a.k2].transform.localPosition).Aggregate(Vector3.zero,(a,b)=>a+b)/Anchors.Length;
    off.x=Pos(weapon.Find("weapon_bolt")).x-parts["bolt_carrier"].transform.localPosition.x;
    Vector3[] V(string key)=>parts[key].sharedMesh.vertices.Select(v=>fbx.transform.InverseTransformPoint(parts[key].transform.TransformPoint(v))+off).ToArray();
    var bolt=V("bolt_carrier"); var axis=Pos(weapon.Find("weapon_bolt")).x;
    var tip=bolt.Where(v=>v.x-axis>0.018f).Distinct().ToArray();
    var mean=tip.Aggregate(Vector3.zero,(a,b)=>a+b)/tip.Length;
    var charge=Pos(weapon.Find("weapon_bolt/mod_charge"));
    Debug.Log("AKDIAG tip="+mean.ToString("F6")+" charge="+charge.ToString("F6")+" delta="+(charge-mean).ToString("F6")+" count="+tip.Length);
    var aim=Pos(weapon.Find("aim_camera")); Debug.Log("AKDIAG aim="+aim.ToString("F6")+" offset="+off.ToString("F6"));
    var upper=V("upper"); var barrel=V("barrel_long");
    var rear=Quaternion.Euler(Pitch,0,0)*new Vector3(0,0.131557f,-0.005787f)+off; Debug.Log("AKDIAG suggestedSightDy="+(aim.y-rear.y).ToString("R")+" suggestedForward="+(charge.z-mean.z).ToString("R")); var folder=Arg("-k2Out"); Directory.CreateDirectory(folder);
    File.WriteAllLines(Path.Combine(folder,"sight-vertices.csv"),new[]{"part,x,y,z"}.Concat(new[]{("upper",upper),("barrel",barrel)}.SelectMany(pair=>pair.Item2.Distinct().Where(v=>v.y>aim.y-0.005f).Select(v=>FormattableString.Invariant($"{pair.Item1},{v.x:R},{v.y:R},{v.z:R}")))));
    File.WriteAllText(Path.Combine(folder,"charge-diagnosis.txt"),"tip="+mean.ToString("F6")+"\ncharge="+charge.ToString("F6")+"\ndelta="+(charge-mean).ToString("F6")+"\naim="+aim.ToString("F6")+"\noffset="+off.ToString("F6"));
   } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
  }
 }
}

