// Exercises the shipped web UI in a fresh headless browser. The fixture never contacts a school or an identity provider.
import assert from 'node:assert/strict';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtemp, readFile, writeFile, mkdir, rm, access, realpath} from 'node:fs/promises';
import {dirname, join, resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {setTimeout as delay} from 'node:timers/promises';

const assets=fileURLToPath(new URL('../src/ClassNex.Central/wwwroot/',import.meta.url));
const out=process.argv[2] && resolve(process.argv[2]);
if(out)await mkdir(out,{recursive:true});
const executable=process.env.CLASSNEX_TEST_BROWSER||join(process.env['ProgramFiles(x86)']||'','Microsoft/Edge/Application/msedge.exe');
await access(executable);
const tempRoot=await realpath(tmpdir());
const profile=await mkdtemp(join(tempRoot,'classnex-web-check-'));
const deviceOnlySettings={
  NexApiKey:'local-fixture-key',nExApIkEy:'duplicate-fixture-key',nexPermission:'readonly',NEXAPIBASEURL:'https://example.invalid/api',
  NexModel:'fixture-model',nexSystemPrompt:'local-fixture-prompt',AccountClientId:'fixture-account',accountEndpoint:'https://example.invalid',
  Email:'teacher@example.invalid',UserName:'测试账户',isOobeCompleted:true,timetablePath:'fixture.yaml',
  CentralManagement:{clientId:'fixture-device'},isDebugOptionsEnabled:true,UseLocalReminderSound:true
};
const localKeys=new Set(Object.keys(deviceOnlySettings).map(key=>key.toLowerCase()));
const settingsJson=JSON.stringify({...deviceOnlySettings,ThemeMode:'system',ThemeAccentColor:'#123456',NexEdgeBarSide:'right',Widgets:[
  {Type:'schedule',IsEnabled:true,FontScale:1},
  {Type:'clock',IsEnabled:true,FontScale:1.15},
  {Type:'schedule',IsEnabled:false,FontScale:.9}
]});
const records=new Map(['a','b'].map((id,i)=>[id,{classId:id,name:`初二（${i+1}）班`,revision:1,settingsRevision:1,timetableRevision:1,settingsJson,timetable:{version:1,subjects:[{name:'数学',color:'#123456'}],schedules:[]}}]));
const devices=[], reads=[], writes=[];
const primaryAccount={name:'班级管理员',subject:'local-review',organization:'初中校区',accountKey:'1'.repeat(64)};
const secondaryAccount={...primaryAccount,name:'另一个账号',subject:'other-review',accountKey:'2'.repeat(64)};
const differentSchool={...primaryAccount,organization:'高中校区',accountKey:'3'.repeat(64)};
const sessions=new Map([['initial',primaryAccount],['renewed',primaryAccount],['different-school',differentSchool],['other-account',secondaryAccount]]);
const codes=new Set(['ABCD2345','MATH2345','ROOM2345']);
const claims=[], rejectedAccounts=[], loginNavigations=[];
let rejectNext=false, holdPut, holdDetail, holdSessionReads, nextLogin='renewed';
const deferred=(id,kind)=>{
  let release;
  const ready=new Promise(resolve=>{release=resolve});
  return {id,kind,ready,release,seen:false};
};
const applyResource=(record,kind,value)=>{
  const field=kind==='settings'?'settingsJson':'timetable';
  if(JSON.stringify(record[field])!==JSON.stringify(value)){
    record[field]=structuredClone(value);
    record[`${kind}Revision`]=++record.revision;
  }
};
const remoteSubject=(id,name)=>{
  const record=records.get(id), profile=structuredClone(record.timetable);
  profile.subjects.push({name});
  applyResource(record,'timetable',profile);
};
const remoteSettings=(id,changes)=>{
  const record=records.get(id);
  applyResource(record,'settings',JSON.stringify({...JSON.parse(record.settingsJson),...changes}));
};
const server=http.createServer(async(req,res)=>{
  const path=new URL(req.url,'http://localhost').pathname.replace(/^\/Control/,'');
  const reply=(status,value)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(value));};
  if(path==='/'||path==='/control.css'){
    res.setHeader('Content-Type',path==='/'?'text/html; charset=utf-8':'text/css; charset=utf-8');return res.end(await readFile(join(assets,path==='/'?'index.html':'control.css')));
  }
  if(path==='/auth/login'){
    loginNavigations.push(req.url);
    res.writeHead(302,{Location:'/Control/','Set-Cookie':`fixture-session=${nextLogin}; Path=/; HttpOnly; SameSite=Lax`});
    return res.end();
  }
  const session=/\bfixture-session=([^;]+)/.exec(req.headers.cookie||'')?.[1];
  const user=sessions.get(session), expectedAccount=req.headers['x-classnex-account'];
  let text=''; for await(const part of req)text+=part;const body=text&&JSON.parse(text);
  if(path==='/api/v1/auth/me'){
    if(holdSessionReads)await holdSessionReads.ready;
    return reply(user?200:401,user||{});
  }
  if(!user || expectedAccount && expectedAccount!==user.accountKey){
    rejectedAccounts.push({path,method:req.method,expectedAccount,actual:user?.accountKey});
    return reply(401,{});
  }
  const ownClasses=user.accountKey===primaryAccount.accountKey;
  if(path==='/api/v1/devices')return reply(200,ownClasses?devices:[]);
  if(path==='/api/v1/pairing/claim'){
    const cls=ownClasses&&records.get(body.classId);
    if(!codes.has(body.code)||body.classId&&!cls)return reply(404,{message:'配对码无效、已过期或班级不可用'});
    claims.push({account:user.accountKey,code:body.code,classId:body.classId});
    codes.delete(body.code);
    const device={clientId:devices.length?'classroom-'+devices.length:'classroom',displayName:'教室电脑',classId:cls?.classId??'new',className:cls?.name??'新班级',isOnline:true};
    if(ownClasses)devices.push(device);
    return reply(200,device);
  }
  if(path==='/api/v1/classes')return reply(200,ownClasses?[...records.values()]:[]);
  const match=path.match(/^\/api\/v1\/classes\/([abc])(?:\/(settings|timetable))?$/);
  if(!match)return reply(404,{});
  const [,id,kind]=match, record=records.get(id);
  if(!ownClasses||!record)return reply(404,{});
  if(req.method==='DELETE'&&!kind){records.delete(id);return reply(200,{})}
  if(req.method==='PUT'){
    const header=req.headers['if-match'];
    const log={id,kind,header,base:Number(/^"(\d+)"$/.exec(header)?.[1]),body,expectedAccount};writes.push(log);
    const finish=(status,payload)=>{log.status=status;log.revision=record.revision;return reply(status,payload)};
    if(!Number.isSafeInteger(log.base))return finish(400,{message:'缺少有效的 If-Match'});
    if(holdPut?.id===id&&holdPut.kind===kind){const gate=holdPut;holdPut=null;gate.seen=true;await gate.ready}
    await delay(75); if(rejectNext){rejectNext=false;return finish(503,{message:'暂时离线'})}
    const value=kind==='settings'?body.settingsJson:body;
    const current=kind==='settings'?record.settingsJson:record.timetable;
    if(log.base>record.revision||log.base<record[`${kind}Revision`]&&JSON.stringify(value)!==JSON.stringify(current))
      return finish(409,{message:'服务器的同一项内容已更新'});
    applyResource(record,kind,value);return finish(200,record);
  }
  reads.push({id,kind:kind??'detail'});
  const snapshot=structuredClone(kind==='timetable'?record.timetable:record);
  if(!kind&&holdDetail?.id===id){const gate=holdDetail;holdDetail=null;gate.seen=true;await gate.ready}
  await delay(id==='a'?30:8);return reply(200,snapshot);
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const base=`http://127.0.0.1:${server.address().port}/Control/`;
const edge=spawn(executable,['--headless=new','--disable-gpu','--disable-background-networking','--disable-sync','--no-first-run','--no-default-browser-check','--remote-debugging-port=0',`--user-data-dir=${profile}`,'about:blank'],{stdio:'ignore',windowsHide:true});
const exited=once(edge,'exit'); let socket;
async function until(test,description){const stop=Date.now()+18000;while(Date.now()<stop){if(await test())return;await delay(70)}throw new Error('Timed out: '+description)}
try{
  let port; await until(async()=>{try{port=Number((await readFile(join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);return port>0}catch{return false}},'headless browser');
  const listTabs=async()=>(await(await fetch(`http://127.0.0.1:${port}/json/list`)).json()).filter(t=>t.type==='page');
  const originalTab=(await listTabs())[0];
  socket=new WebSocket(originalTab.webSocketDebuggerUrl);
  await new Promise((ok,fail)=>{socket.onopen=ok;socket.onerror=fail});
  let seq=0;const pending=new Map(),errors=[];
  socket.onmessage=({data})=>{const msg=JSON.parse(data);if(msg.method==='Runtime.exceptionThrown')errors.push(msg.params.exceptionDetails.exception?.description||msg.params.exceptionDetails.text);const cb=pending.get(msg.id);if(cb){pending.delete(msg.id);msg.error?cb.reject(new Error(msg.error.message)):cb.resolve(msg.result)}};
  const rpc=(method,params={})=>new Promise((resolve,reject)=>{const id=++seq;pending.set(id,{resolve,reject});socket.send(JSON.stringify({id,method,params}))});
  const js=async expression=>{const r=await rpc('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw new Error(r.exceptionDetails.exception?.description||expression);return r.result.value};
  const wait=exp=>until(()=>js(exp),exp);
  const click=selector=>js(`document.querySelector(${JSON.stringify(selector)}).click()`);
  const input=(selector,value,ev='input')=>js(`{let el=document.querySelector(${JSON.stringify(selector)});el.value=${JSON.stringify(value)};el.dispatchEvent(new Event(${JSON.stringify(ev)},{bubbles:true}))}`);
  const shot=async name=>{
    if(!out)return;
    await js('document.fonts.ready.then(() => Promise.allSettled(document.getAnimations().filter(a=>Number.isFinite(a.effect?.getComputedTiming().endTime)).map(a=>a.finished))).then(()=>new Promise(requestAnimationFrame))');
    await writeFile(join(out,name+'.png'),Buffer.from((await rpc('Page.captureScreenshot',{format:'png'})).data,'base64'));
  };
  const checkPairFit=async (width,wraps=false)=>{
    const fit=await js(`(() => {
      const code=$('pairCode'), style=getComputedStyle(code), probe=document.createElement('span');
      probe.style.cssText='position:absolute;visibility:hidden;white-space:pre';
      probe.style.font=style.font;
      probe.style.letterSpacing=style.letterSpacing;
      probe.style.fontVariantNumeric=style.fontVariantNumeric;
      document.body.append(probe);
      const textWidth=Math.max(...[code.placeholder,'WWWW-WWWW'].map(text=>{probe.textContent=text;return probe.getBoundingClientRect().width}));
      probe.remove();
      const fieldWidth=code.clientWidth-parseFloat(style.paddingLeft)-parseFloat(style.paddingRight);
      const form=$('pairForm').getBoundingClientRect();
      const controls=[code,$('pairClass'),$('pairButton')].map(el=>el.getBoundingClientRect());
      return {textWidth,fieldWidth,contained:controls.every(r=>r.left>=form.left-.5&&r.right<=form.right+.5),wrapped:controls[2].top>=controls[0].bottom};
    })()`);
    assert.ok(fit.fieldWidth>=fit.textWidth+2,`pair code clips at ${width}px: ${JSON.stringify(fit)}`);
    assert.ok(fit.contained,`pair controls overflow at ${width}px: ${JSON.stringify(fit)}`);
    if(wraps)assert.ok(fit.wrapped,`pair form did not wrap at ${width}px: ${JSON.stringify(fit)}`);
    console.log(`PASS: ${width}px pair form, ${fit.textWidth.toFixed(1)}px code in ${fit.fieldWidth.toFixed(1)}px usable width${wraps?' (wrapped)':''}.`);
  };
  await rpc('Runtime.enable'); await rpc('Page.enable'); await rpc('Accessibility.enable');await rpc('Network.enable');
  await rpc('Network.setCookie',{name:'fixture-session',value:'initial',url:base,path:'/',httpOnly:true});
  const viewport=(width,height)=>rpc('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
  await rpc('Emulation.setEmulatedMedia',{features:[{name:'prefers-color-scheme',value:'light'}]});
  await viewport(1190,785);await rpc('Page.navigate',{url:base});await wait('document.querySelector("#page-overview.active")!=null');await shot('overview');
  const {root}=await rpc('DOM.getDocument',{depth:0});
  let accessibleCount=0;
  const assertName=async (selector,name,role)=>{
    const {nodeId}=await rpc('DOM.querySelector',{nodeId:root.nodeId,selector});
    assert.ok(nodeId,`missing ${selector}`);
    const {nodes:[node]}=await rpc('Accessibility.getPartialAXTree',{nodeId,fetchRelatives:false});
    assert.ok(node&&!node.ignored,`${selector} missing from the accessibility tree`);
    assert.equal(node.name?.value,name,selector);
    if(role)assert.equal(node.role?.value,role,selector);
    accessibleCount++;
  };
  await click('[data-go="devices"]');await shot('pair-device');await checkPairFit(1190);await assertName('#pairCode','设备配对码','textbox');
  await input('#pairCode','123');await click('#pairButton');await wait('$("pairCode").getAttribute("aria-invalid")==="true"');
  await input('#pairClass','a','change');await input('#pairCode','ABCD-2345');await click('#pairButton');await wait('$("pairStatus").textContent.includes("已配对")');assert.equal(devices.length,1);assert.equal(devices[0].classId,'a');
  await click('[data-page="app-settings"]');await wait('!!document.querySelector("[data-setting=ThemeMode]")');
  const browserSettingKeys=await js(`[
    ...Object.keys(state.settings),
    ...Object.keys(JSON.parse(JSON.parse(observed.get(resourceKey(state.selectedClassId,"settings")).body).settingsJson)),
    ...Array.from(document.querySelectorAll('[data-setting]'), input=>input.dataset.setting)
  ]`);
  assert.deepEqual(browserSettingKeys.filter(key=>localKeys.has(key.toLowerCase())),[],'legacy device-only fields must not appear in the editor, its baseline, or any controls');
  await js('Object.defineProperty(document,"hidden",{configurable:true,value:true})');
  await input('[data-setting="NexEdgeBarSide"]','left','change');
  await input('[data-setting="ThemeMode"]','dark','change');await input('#settingsClassSelect','b','change');
  await wait('state.selectedClassId==="b"');
  await until(()=>JSON.parse(records.get('a').settingsJson).ThemeMode==='dark','switched class A saved independently');
  assert.equal(writes.find(event=>event.id==='a').header,'"1"');assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'system');
  assert.equal(JSON.parse(records.get('a').settingsJson).NexEdgeBarSide,'left','shared NEX settings are still editable');
  assert.equal(JSON.parse(records.get('a').settingsJson).ThemeAccentColor,'#123456','preserve shared settings without an editor control');
  rejectNext=true;await input('[data-setting="ThemeMode"]','light','change');await wait('!$("saveSettingsButton").hidden');await click('#saveSettingsButton');await wait('!drafts.size');assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'light');
  await shot('settings');
  await Promise.all(['课程表 1','时间','课程表 2'].flatMap((label,index)=>[
    assertName(`[data-widget-index="${index}"][data-widget-prop="IsEnabled"]`,'显示'+label,'switch'),
    assertName(`[data-widget-index="${index}"][data-widget-prop="FontScale"]`,label+' 字号','slider')
  ]));
  await js('document.querySelector(".widget-section-head").scrollIntoView({block:"start"})');await shot('widget-controls');
  await click('[data-page="archive-settings"]');
  const n=await js('state.timeSlots.length');assert.ok(n>0);
  await Promise.all([
    assertName('#timetableTable','班级课表','table'),
    assertName('#deleteArchiveClass','删除班级','button'),
    assertName('[data-remove-subject="0"]','删除科目：数学','button'),
    ...Array.from({length:n},(_,index)=>{
      const label=`第 ${index+1} 节`;
      return [
        assertName(`[data-slot-index="${index}"][data-slot-part="start"]`,label+'开始时间'),
        assertName(`[data-slot-index="${index}"][data-slot-part="end"]`,label+'结束时间'),
        assertName(`[data-delete-slot="${index}"]`,'删除'+label,'button'),
        ...['周一','周二','周三','周四','周五','周六','周日'].map((day,i)=>assertName(`[data-cell-day="${i+1}"][data-cell-slot="${index}"]`,`${day}${label}：点击排课`,'button'))
      ];
    }).flat()
  ]);
  await click('[data-subject-index="0"]');await click('[data-cell-day="1"][data-cell-slot="0"]');await wait('!drafts.size');
  assert.equal(records.get('b').timetable.schedules[0].classes[0].subject,'数学');assert.equal(records.get('b').timetable.subjects[0].color,'#123456');
  await assertName('[data-cell-day="1"][data-cell-slot="0"]','周一第 1 节：数学','button');
  await click('#addTimeSlotButton');await wait('!drafts.size');assert.equal(records.get('b').timetable.importedTimeLayout.times.length,n+1);
  await js('loadClass("b")');assert.equal(await js('state.timeSlots.length'),n+1);await shot('archive');

  const detailGate=holdDetail=deferred('b','detail'), readStart=reads.length;
  const readClass=js('loadClass("b")');
  try{
    await until(()=>detailGate.seen,'class detail request begun');await delay(150);
    assert.deepEqual(reads.slice(readStart),[{id:'b',kind:'detail'}],'do not request the timetable until the detail revision is read');
  }finally{detailGate.release()}
  await readClass;
  assert.deepEqual(reads.slice(readStart),[{id:'b',kind:'detail'},{id:'b',kind:'timetable'}]);
  const originalBase=records.get('b').revision;
  remoteSubject('b','物理');
  await click('[data-page="app-settings"]');await input('[data-setting="ThemeMode"]','dark','change');await wait('!drafts.size');
  assert.equal(writes.at(-1).base,originalBase);assert.equal(writes.at(-1).status,200,'timetable change must not conflict with settings');
  await click('[data-page="archive-settings"]');await click('#addTimeSlotButton');await wait('!$("archiveConflict").hidden');
  assert.equal(writes.at(-1).base,originalBase,'settings save must not advance the unread timetable baseline');assert.equal(writes.at(-1).status,409);
  assert.equal(records.get('b').timetable.importedTimeLayout.times.length,n+1);assert.equal(await js('state.timeSlots.length'),n+2);
  assert.ok(records.get('b').timetable.subjects.some(subject=>subject.name==='物理'));
  await shot('archive-conflict');

  await click('[data-page="app-settings"]');await input('[data-setting="ThemeMode"]','light','change');await wait('!draftFor("settings")');
  assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'light');assert.ok(await js('draftFor("timetable").conflict'));
  await input('#settingsClassSelect','a','change');await wait('state.selectedClassId==="a"');assert.ok(await js('$("archiveConflict").hidden'));
  await input('#settingsClassSelect','b','change');await wait('state.selectedClassId==="b"');
  await click('[data-page="archive-settings"]');assert.ok(await js('!$("archiveConflict").hidden && state.timeSlots.length==='+ (n+2)));
  assert.ok(await js('(() => {const event=new Event("beforeunload",{cancelable:true});return !window.dispatchEvent(event)})()'),'refresh must warn while an unsaved draft exists');
  const withoutChoice=writes.length;
  await js('window.dispatchEvent(new Event("online")); window.confirm=()=>false');
  await click('#archiveConflict [data-conflict-choice="local"]');await delay(450);
  assert.equal(writes.length,withoutChoice,'conflicts never retry on reconnect or canceled confirmation');
  const secondRace=holdPut=deferred('b','timetable'), replaceBase=records.get('b').revision;
  await js('window.confirm=()=>true');await click('#archiveConflict [data-conflict-choice="local"]');
  try{
    await until(()=>secondRace.seen,'confirmed timetable retry begun');
    assert.equal(writes.at(-1).base,replaceBase,'confirmed retry must read a new revision first');
    remoteSubject('b','音乐');
  }finally{secondRace.release()}
  await wait('draftFor("timetable")?.conflict && !draftFor("timetable")?.saving');assert.equal(writes.at(-1).status,409);
  assert.ok(records.get('b').timetable.subjects.some(subject=>subject.name==='音乐'));
  const retryBase=records.get('b').revision;
  await click('#archiveConflict [data-conflict-choice="local"]');await wait('!draftFor("timetable")');
  assert.equal(writes.at(-1).base,retryBase);assert.equal(writes.at(-1).status,200);
  assert.equal(records.get('b').timetable.importedTimeLayout.times.length,n+2);
  assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'light','confirmed timetable save cannot overwrite settings');

  remoteSettings('b',{ThemeMode:'system'});
  await click('[data-page="app-settings"]');await input('[data-setting="ThemeMode"]','dark','change');await wait('!$("settingsConflict").hidden');
  assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'system');
  assert.equal(await js('document.querySelector("[data-setting=ThemeMode]").value'),'dark');
  remoteSubject('b','体育');
  await js('delete document.hidden; refreshSelectedClassRevision()');
  await wait('state.timetable.subjects.some(subject=>subject.name==="体育")');
  assert.ok(await js('draftFor("settings").conflict && document.querySelector("[data-setting=ThemeMode]").value==="dark"'),'background refresh updates only the clean timetable');
  await js('Object.defineProperty(document,"hidden",{configurable:true,value:true})');
  await shot('settings-conflict');
  const beforeAdopt=writes.length;
  await click('#settingsConflict [data-conflict-choice="server"]');await wait('!draftFor("settings")');
  assert.equal(writes.length,beforeAdopt,'adopt reads without writing');
  assert.equal(await js('document.querySelector("[data-setting=ThemeMode]").value'),'system');

  const parallelStart=writes.length, settingGate=holdPut=deferred('b','settings');
  await input('[data-setting="ThemeMode"]','light','change');
  try{
    await until(()=>settingGate.seen,'first setting save in flight');
    await input('[data-setting="ThemeMode"]','dark','change');
    await click('[data-page="archive-settings"]');await click('#addTimeSlotButton');
    await until(()=>writes.slice(parallelStart).some(event=>event.kind==='timetable'&&event.status===200),'timetable saves while settings request is held');
    assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'system');
  }finally{settingGate.release()}
  await wait('!drafts.size');
  const [firstSetting,nextSetting]=writes.slice(parallelStart).filter(event=>event.kind==='settings');
  assert.equal(firstSetting.status,200);assert.equal(nextSetting.status,200);assert.equal(nextSetting.base,firstSetting.revision,'further local edits use the preceding successful write revision');
  assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'dark');
  assert.equal(records.get('b').timetable.importedTimeLayout.times.length,n+3);

  await rpc('Network.enable');
  const offline=value=>rpc('Network.emulateNetworkConditions',{offline:value,latency:0,downloadThroughput:-1,uploadThroughput:-1});
  await click('[data-page="app-settings"]');
  await offline(true);
  try{
    const beforeOffline=writes.length;
    await input('[data-setting="ThemeMode"]','light','change');await wait('!$("saveSettingsButton").hidden');
    assert.ok(await js('draftFor("settings") && document.querySelector("[data-setting=ThemeMode]").value==="light"'));
    assert.equal(writes.length,beforeOffline,'no offline write reached the server');
  }finally{await offline(false)}
  await js('window.dispatchEvent(new Event("online"))');await wait('!drafts.size');
  assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'light');
  console.log('PASS: sequential reads, conditional writes and independent baselines, retained drafts across class switches and offline recovery, both conflict choices, guarded reload, concurrent resources, and consecutive local edits.');

  await viewport(390,740);await click('[data-page="devices"]');await shot('devices-narrow');
  await checkPairFit(390,true);
  assert.ok(await js('document.querySelector(".brand-title").getBoundingClientRect().right<document.querySelector(".search").getBoundingClientRect().left'),'title must not overlap search');
  assert.ok(await js('document.documentElement.scrollWidth<=innerWidth'),JSON.stringify(await js('[innerWidth,document.documentElement.scrollWidth,...Array.from(document.querySelectorAll("#appShell *")).filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,8).map(e=>[e.tagName,e.className,Math.round(e.getBoundingClientRect().right)])]')));
  await viewport(360,740);await checkPairFit(360,true);await input('#pairCode','WWWW-WWWW');await shot('pair-code-narrow');
  await input('#pairCode','');await viewport(390,740);
  await click('#navToggle');await wait('document.querySelector(".nav-open .nav-text").getBoundingClientRect().width>0');await click('[data-page="devices"]');
  await rpc('Emulation.setEmulatedMedia',{features:[{name:'prefers-color-scheme',value:'dark'}]});await shot('devices-dark');
  await click('[data-page="app-settings"]');await js('window.confirm=()=>true');
  rejectNext=true;await input('[data-setting="ThemeMode"]','system','change');await click('#deleteSettingsClass');await wait('!$("saveSettingsButton").hidden');assert.ok(records.has('b'));
  await click('#deleteSettingsClass');await wait('state.selectedClassId==="a" && drafts.size===0');assert.ok(!records.has('b'));

  await rpc('Emulation.setEmulatedMedia',{features:[{name:'prefers-color-scheme',value:'light'}]});
  await viewport(1190,785);await click('[data-page="devices"]');
  records.set('c',{...structuredClone(records.get('a')),classId:'c',name:'临时班级'});
  await click('#deviceRefresh');await wait('[...$("pairClass").options].some(option=>option.value==="c")');
  await input('#pairClass','c','change');await input('#pairCode','MATH-2345');
  records.delete('c');
  await click('#deviceRefresh');await wait('$("pairClass").getAttribute("aria-invalid")==="true"');
  assert.ok(await js('$("pairButton").disabled && $("pairClass").selectedOptions[0].disabled && $("pairClass").value==="c" && $("pairCode").value==="MATH-2345"'));
  await shot('pair-class-unavailable');
  const beforeReselect=claims.length;
  await click('#pairButton');await delay(150);
  assert.equal(claims.length,beforeReselect,'a vanished class must not fall back to a new class');assert.ok(codes.has('MATH2345'));
  await input('#pairClass','a','change');await click('#pairButton');await wait('$("pairStatus").textContent.includes("已配对")');
  assert.equal(claims.at(-1).classId,'a');

  const hasCookie=async value=>(await rpc('Network.getCookies',{urls:[base]})).cookies.some(c=>c.name==='fixture-session'&&c.value===value);
  const closeOtherTabs=async()=>{for(const tab of await listTabs())if(tab.id!==originalTab.id)await rpc('Target.closeTarget',{targetId:tab.id})};
  await click('[data-page="app-settings"]');
  assert.equal((await listTabs()).length,1,'the renewal scenario starts with a single tab');
  const beforeRenew=writes.length;
  sessions.delete('initial');
  await input('[data-setting="ThemeMode"]','light','change');
  await wait('browserSession.blocked && !$("sessionActions").hidden && !!draftFor("settings")');
  assert.equal(writes.length,beforeRenew,'an expired session must not save a draft');
  assert.ok(await js('getComputedStyle($("appShell")).display==="none" && $("loginLink").hidden'));
  await shot('session-expired-draft');
  await rpc('Runtime.evaluate',{expression:'$("renewSessionLink").click()',userGesture:true});
  await until(async()=>await hasCookie('renewed')&&(await listTabs()).length===2,'new login tab established same-account cookie');
  assert.equal(loginNavigations.at(-1),'/Control/auth/login','the original page sends no draft or credentials in the sign-in URL');
  await rpc('Page.bringToFront');
  await wait('!browserSession.blocked && !drafts.size');
  assert.equal(writes.length,beforeRenew+1);assert.equal(JSON.parse(records.get('a').settingsJson).ThemeMode,'light');
  assert.equal(await js('location.href'),base,'renewal retains the original document and its draft');
  await closeOtherTabs();

  await click('[data-page="app-settings"]');
  rejectNext=true;await input('[data-setting="ThemeMode"]','system','change');await wait('!$("saveSettingsButton").hidden');
  await click('[data-page="devices"]');await input('#pairClass','','change');await input('#pairCode','ROOM-2345');
  const beforeAccountChange=writes.length, beforeNewPair=claims.length;
  const blockedRead=holdSessionReads=deferred('me','auth');
  try{
    nextLogin='different-school';
    await rpc('Target.createTarget',{url:base+'auth/login',background:true});
    await until(()=>hasCookie('different-school'),'a second tab changed organizations');
    await click('#pairButton');
    await wait('browserSession.blocked && !!draftFor("settings")');
    assert.ok(rejectedAccounts.some(event=>event.path==='/api/v1/pairing/claim'&&event.method==='POST'&&
      event.expectedAccount===primaryAccount.accountKey&&event.actual===differentSchool.accountKey),'server must reject a stale pair request before processing it');
    assert.equal(claims.length,beforeNewPair);assert.ok(codes.has('ROOM2345'));
  }finally{holdSessionReads=null;blockedRead.release()}
  await wait('$("loginStatus").textContent.includes("不是此页原来的账号")');
  await rpc('Page.bringToFront');
  await shot('session-changed-account');
  assert.ok(await js('getComputedStyle($("appShell")).display==="none" && $("toast").textContent==="" && drafts.size===1'));
  await js('window.dispatchEvent(new Event("online"))');
  await wait('!browserSession.check');
  assert.equal(writes.length,beforeAccountChange,'a different organization must not resume a draft from the previous account');
  await closeOtherTabs();
  nextLogin='other-account';
  await rpc('Target.createTarget',{url:base+'auth/login',background:true});
  await until(()=>hasCookie('other-account'),'a second tab signed in to a different account');
  await click('#checkSessionButton');await wait('!browserSession.check && browserSession.blocked');
  assert.equal(writes.length,beforeAccountChange);assert.ok(await js('drafts.size===1'));
  await js('window.confirm=()=>false');await click('#discardSessionButton');
  assert.ok(await js('browserSession.blocked && drafts.size===1'),'canceling discard preserves the draft');
  await js('window.confirm=()=>true');
  await click('#discardSessionButton').catch(error=>{if(!String(error).includes('Execution context was destroyed'))throw error});
  await until(async()=>{try{return await js('state.user?.subject==="other-review" && !browserSession.blocked && drafts.size===0 && state.classes.length===0')}catch{return false}},'explicit discard opens the current account without the previous draft');
  assert.equal(writes.length,beforeAccountChange);assert.equal(claims.length,beforeNewPair);
  await closeOtherTabs();
  console.log('PASS: unavailable pairing target requires explicit re-selection; single-tab expiry renews in a new tab; stale pairing and drafts are isolated across organizations and accounts; discard requires confirmation.');

  assert.ok(writes.every(event=>Number.isSafeInteger(event.base)), 'every resource PUT had a quoted revision');
  assert.ok(writes.every(event=>event.expectedAccount===primaryAccount.accountKey),'every settings or timetable PUT is bound to its original account');
  for(const event of writes.filter(event=>event.kind==='settings'))
    assert.deepEqual(Object.keys(JSON.parse(event.body.settingsJson)).filter(key=>localKeys.has(key.toLowerCase())),[], 'no device-only field may be sent in a settings PUT');
  console.log('PASS: legacy device-only values, including NEX credentials and permissions, are absent from the editor and all settings writes. Shared settings are preserved.');
  assert.deepEqual(errors,[]);console.log(`PASS: ${accessibleCount} names in the Edge accessibility tree, ${writes.length} conditional updates, pairing, retry, subject color, empty slots, narrow layout, dark theme, and safe class deletion; no browser errors.`);
  await rpc('Browser.close').catch(()=>{});
}finally{
  if(socket?.readyState===1)socket.close();
  if(edge.exitCode===null){await Promise.race([exited,delay(2500)]);if(edge.exitCode===null)edge.kill()}
  await Promise.race([exited,delay(3000)]);server.closeAllConnections();server.close();
  const checkedProfile=await realpath(profile).catch(()=>null);
  if(checkedProfile===profile&&dirname(checkedProfile)===tempRoot)
    await rm(checkedProfile,{recursive:true,force:true,maxRetries:10,retryDelay:250}).catch(e=>console.warn('Could not remove disposable profile:',e.code));
}
