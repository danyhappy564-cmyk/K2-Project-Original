// 번들 애니메이션을 재읽어 원본/사본 연결과 장전 중 양손 위치를 측정합니다.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace K2AnimV002 {
 public static class Probe {
  [Serializable] public class Entry {public string key;public string[] dependencyKeys;}
  [Serializable] public class Manifest {public Entry[] manifest;}
  [Serializable] public class Frame {public float time;public Vector3 right,left,charge,bolt,marker;}
  [Serializable] public class ClipResult {public string name;public float length;public List<Frame> frames=new List<Frame>();}
  [Serializable] public class Result {public bool passed;public string error;public int layers;public List<string> overrides=new List<string>();public List<ClipResult> clips=new List<ClipResult>();}
  static string Arg(string key) {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i<0?null:a[i+1];}
  static Transform Find(Transform r,string name) {return r.GetComponentsInChildren<Transform>(true).First(t=>t.name==name);}
  public static void Run() {
   var report=new Result();var output=Arg("-k2Out");Directory.CreateDirectory(output);
   try {
    var mod=Arg("-k2Mod");var game=Arg("-k2Game");var key=Arg("-k2Key");
    var m=JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(mod,"bundles.json")));
    var entry=m.manifest.First(x=>x.key==key);var loaded=new Dictionary<string,AssetBundle>();
    foreach(var d in entry.dependencyKeys) {string p=Path.Combine(mod,"bundles",d);if(!File.Exists(p))p=Path.Combine(game,d);var ab=AssetBundle.LoadFromFile(p);if(!ab)throw new Exception("LOAD "+d);loaded[d]=ab;}
    var main=AssetBundle.LoadFromFile(Path.Combine(mod,"bundles",key));
    var prefab=main.LoadAllAssets<GameObject>().Single();var wp=prefab.GetComponent<WeaponPrefab>();if(!wp)throw new Exception("WeaponPrefab missing");
    var instance=UnityEngine.Object.Instantiate(wp._weaponObject);var anim=instance.GetComponent<Animator>();anim.runtimeAnimatorController=wp._originalAnimatorController;anim.Update(0);report.layers=anim.layerCount;anim.enabled=false;
    var ours=loaded.First(x=>x.Key.EndsWith("k2_ak_overrides.bundle")).Value.LoadAllAssets<AnimationClip>().Single();
    foreach(var c in new[]{wp._originalAnimatorController,wp._animatorSimple,wp._animatorSpirit}) {
     var aoc=c as AnimatorOverrideController;if(!aoc)throw new Exception("not override");
     var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();aoc.GetOverrides(pairs);
     var pair=pairs.Single(x=>x.Key.name==ours.name);bool linked=pair.Value==ours;
     report.overrides.Add(c.name+": custom clip linked="+linked+", slots="+pairs.Count);if(!linked)throw new Exception("custom override not linked");
    }
    var client=loaded["assets/content/weapons/ak100/client_assets.bundle"];
    var weapon=Find(instance.transform,"weapon");var right=Find(instance.transform,"Base HumanRPalm");var left=Find(instance.transform,"Base HumanLPalm");var charge=Find(instance.transform,"mod_charge");var bolt=Find(instance.transform,"weapon_bolt");var marker=Find(instance.transform,"weapon_R_IK_marker");
    foreach(var clip in client.LoadAllAssets<AnimationClip>().Where(c=>c.name.Contains("charge"))) {
     var result=new ClipResult{name=clip.name,length=clip.length};
     for(int i=0;i<=20;i++) {float time=clip.length*i/20;clip.SampleAnimation(instance,time);result.frames.Add(new Frame{time=time,right=weapon.InverseTransformPoint(right.position),left=weapon.InverseTransformPoint(left.position),charge=weapon.InverseTransformPoint(charge.position),bolt=weapon.InverseTransformPoint(bolt.position),marker=weapon.InverseTransformPoint(marker.position)});}
     report.clips.Add(result);
    }
    report.passed=report.layers==7;if(!report.passed)throw new Exception("layer count");
    UnityEngine.Object.DestroyImmediate(instance);
   }catch(Exception e){report.error=e.ToString();Debug.LogException(e);}
   File.WriteAllText(Path.Combine(output,"probe.json"),JsonUtility.ToJson(report,true));
   EditorApplication.Exit(report.passed?0:1);
  }
 }
}
