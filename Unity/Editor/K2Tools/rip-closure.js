// AssetRipper 추출본(ExportedProject/Assets)에서 M4A1 폴더가 참조하는 파일만 SDK로 복사하고 스크립트 GUID를 SDK 것으로 바꾼다.
// node rip-closure.js <추출본 Assets> <SDK Assets> <SDK 안 대상 폴더> <dry|copy> [시작 폴더]
// 추출본에서 M4A1 폴더 에셋이 참조하는 파일들(GUID 연쇄)을 모아 SDK로 복사하고, 스크립트 GUID를 SDK 스크립트로 바꾼다.
const fs=require("fs"),path=require("path");
const [,,RIP,SDK,DEST_REL,DRY,START_REL]=process.argv;  // START_REL: 추출본 안 시작 폴더(기본 Content/Weapons/m4a1)
const walk=(d,out=[])=>{for(const e of fs.readdirSync(d,{withFileTypes:true})){const p=path.join(d,e.name);e.isDirectory()?walk(p,out):out.push(p);}return out;};
const idx=(root)=>{const m={};for(const f of walk(root))if(f.endsWith(".meta")){const g=(fs.readFileSync(f,"utf8").match(/^guid: (\w+)/m)||[])[1];if(g)m[g]=f.slice(0,-5);}return m;};
const rip=idx(RIP), sdk=idx(SDK);
// 스크립트 매핑: 클래스 이름 → SDK guid
const sdkScript={};for(const [g,p] of Object.entries(sdk))if(p.endsWith(".cs"))sdkScript[path.basename(p,".cs")]=g;
const scriptMap={},missingScripts=[];
for(const [g,p] of Object.entries(rip))if(p.endsWith(".cs")){const n=path.basename(p,".cs");if(sdkScript[n])scriptMap[g]=sdkScript[n];else missingScripts.push(n);}
const textExt=/\.(prefab|controller|overrideController|anim|asset|mat|mask)$/;
const startPath=path.join(RIP,START_REL||"Content/Weapons/m4a1");
const start=(fs.statSync(startPath).isFile()?[startPath]:walk(startPath)).filter(f=>!f.endsWith(".meta"));   // 파일 하나 또는 폴더
const todo=[...start],need=new Set(),reuse=new Set();
while(todo.length){const f=todo.pop();if(need.has(f))continue;need.add(f);
 if(!textExt.test(f))continue;const t=fs.readFileSync(f,"utf8");
 for(const m of t.matchAll(/guid: (\w{32})/g)){const g=m[1];if(scriptMap[g]||!rip[g])continue;
   if(sdk[g]){reuse.add(g);continue;} if(!rip[g].endsWith(".cs"))todo.push(rip[g]);}}
let bytes=0;const files=[...need];for(const f of files)bytes+=fs.statSync(f).size;
console.log("files",files.length,"MB",(bytes/1e6).toFixed(1),"reuse-from-sdk",reuse.size,"scriptsMapped",Object.keys(scriptMap).length,"missingScripts",missingScripts.join(","));
const byTop={};for(const f of files){const k=path.relative(RIP,f).split(path.sep).slice(0,3).join("/");byTop[k]=(byTop[k]||0)+1;}console.log(byTop);
if(DRY==="dry")process.exit(0);
const dest=path.join(SDK,DEST_REL);let changed=0;
for(const f of files){const rel=path.relative(RIP,f);const to=path.join(dest,rel);fs.mkdirSync(path.dirname(to),{recursive:true});
 if(textExt.test(f)){let t=fs.readFileSync(f,"utf8");const before=t;t=t.replace(/guid: (\w{32})/g,(s,g)=>scriptMap[g]?"guid: "+scriptMap[g]:s);if(t!==before)changed++;fs.writeFileSync(to,t);}else fs.copyFileSync(f,to);
 fs.copyFileSync(f+".meta",to+".meta");}
console.log("copied to",dest,"script-remapped files",changed);
