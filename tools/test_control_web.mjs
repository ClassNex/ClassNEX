// Exercises the shipped web UI in a fresh headless browser. The fixture never contacts a school or an identity provider.
import assert from 'node:assert/strict';
import http from 'node:http';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdtemp, readFile, writeFile, mkdir, rm, access} from 'node:fs/promises';
import {join, resolve} from 'node:path';
import {tmpdir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {setTimeout as delay} from 'node:timers/promises';

const assets=fileURLToPath(new URL('../src/ClassNex.Central/wwwroot/',import.meta.url));
const out=process.argv[2] && resolve(process.argv[2]);
if(out)await mkdir(out,{recursive:true});
const profile=await mkdtemp(join(tmpdir(),'classnex-web-check-'));
const executable=process.env.CLASSNEX_TEST_BROWSER||join(process.env['ProgramFiles(x86)']||'','Microsoft/Edge/Application/msedge.exe');
await access(executable);
const records=new Map(['a','b'].map((id,i)=>[id,{classId:id,name:`初二（${i+1}）班`,revision:1,settingsJson:'{"ThemeMode":"system"}',timetable:{version:1,subjects:[{name:'数学',color:'#123456'}],schedules:[]}}]));
const devices=[]; let rejectNext=false;
const server=http.createServer(async(req,res)=>{
  const path=new URL(req.url,'http://localhost').pathname.replace(/^\/Control/,'');
  const reply=(status,value)=>{res.writeHead(status,{'Content-Type':'application/json'});res.end(JSON.stringify(value));};
  if(path==='/'||path==='/control.css'){
    res.setHeader('Content-Type',path==='/'?'text/html; charset=utf-8':'text/css; charset=utf-8');return res.end(await readFile(join(assets,path==='/'?'index.html':'control.css')));
  }
  let text=''; for await(const part of req)text+=part;const body=text&&JSON.parse(text);
  if(path==='/api/v1/auth/me')return reply(200,{name:'班级管理员',subject:'local-review'});
  if(path==='/api/v1/devices')return reply(200,devices);
  if(path==='/api/v1/pairing/claim' && body.code==='ABCD2345'){
    const cls=records.get(body.classId);if(!cls)return reply(400,{message:'测试期望选中已有班级'});
    const device={clientId:'classroom',displayName:'教室电脑',classId:cls.classId,className:cls.name,isOnline:true};devices.push(device);return reply(200,device);
  }
  if(path==='/api/v1/classes')return reply(200,[...records.values()]);
  const match=path.match(/^\/api\/v1\/classes\/([ab])(?:\/(settings|timetable))?$/);
  if(!match)return reply(404,{});
  const [,id,kind]=match, record=records.get(id);
  if(!record)return reply(404,{});
  if(req.method==='DELETE'&&!kind){records.delete(id);return reply(200,{})}
  if(req.method==='PUT'){
    await delay(75); if(rejectNext){rejectNext=false;return reply(503,{message:'暂时离线'})}
    if(kind==='settings')record.settingsJson=body.settingsJson;else record.timetable=body;
    record.revision++;return reply(200,record);
  }
  await delay(id==='a'?30:8);return reply(200,kind==='timetable'?record.timetable:record);
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const base=`http://127.0.0.1:${server.address().port}/Control/`;
const edge=spawn(executable,['--headless=new','--disable-gpu','--disable-background-networking','--disable-sync','--no-first-run','--no-default-browser-check','--remote-debugging-port=0',`--user-data-dir=${profile}`,'about:blank'],{stdio:'ignore',windowsHide:true});
const exited=once(edge,'exit'); let socket;
async function until(test,description){const stop=Date.now()+18000;while(Date.now()<stop){if(await test())return;await delay(70)}throw new Error('Timed out: '+description)}
try{
  let port; await until(async()=>{try{port=Number((await readFile(join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);return port>0}catch{return false}},'headless browser');
  const tabs=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  socket=new WebSocket(tabs.find(t=>t.type==='page').webSocketDebuggerUrl);
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
  await rpc('Runtime.enable'); await rpc('Page.enable');
  const viewport=(width,height)=>rpc('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
  await rpc('Emulation.setEmulatedMedia',{features:[{name:'prefers-color-scheme',value:'light'}]});
  await viewport(1190,785);await rpc('Page.navigate',{url:base});await wait('document.querySelector("#page-overview.active")!=null');await shot('overview');
  await click('[data-go="devices"]');await shot('pair-device');await input('#pairCode','123');await click('#pairButton');await wait('$("pairCode").getAttribute("aria-invalid")==="true"');
  await input('#pairClass','a','change');await input('#pairCode','ABCD-2345');await click('#pairButton');await wait('$("pairStatus").textContent.includes("已配对")');assert.equal(devices.length,1);assert.equal(devices[0].classId,'a');
  await click('[data-page="app-settings"]');await wait('!!document.querySelector("[data-setting=ThemeMode]")');
  await input('[data-setting="ThemeMode"]','dark','change');await input('#settingsClassSelect','b','change');
  await wait('state.selectedClassId==="b"');assert.equal(JSON.parse(records.get('a').settingsJson).ThemeMode,'dark');assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'system');
  rejectNext=true;await input('[data-setting="ThemeMode"]','light','change');await wait('!$("saveSettingsButton").hidden');await click('#saveSettingsButton');await wait('!state.editorDirty');assert.equal(JSON.parse(records.get('b').settingsJson).ThemeMode,'light');
  await shot('settings');await click('[data-page="archive-settings"]');await click('[data-subject-index="0"]');await click('[data-cell-day="1"][data-cell-slot="0"]');await wait('!state.editorDirty');
  assert.equal(records.get('b').timetable.schedules[0].classes[0].subject,'数学');assert.equal(records.get('b').timetable.subjects[0].color,'#123456');
  const n=await js('state.timeSlots.length');await click('#addTimeSlotButton');await wait('!state.editorDirty');assert.equal(records.get('b').timetable.importedTimeLayout.times.length,n+1);
  await js('loadClass("b")');assert.equal(await js('state.timeSlots.length'),n+1);await shot('archive');
  await viewport(390,740);await click('[data-page="devices"]');await shot('devices-narrow');
  assert.ok(await js('document.querySelector(".brand-title").getBoundingClientRect().right<document.querySelector(".search").getBoundingClientRect().left'),'title must not overlap search');
  assert.ok(await js('document.documentElement.scrollWidth<=innerWidth'),JSON.stringify(await js('[innerWidth,document.documentElement.scrollWidth,...Array.from(document.querySelectorAll("#appShell *")).filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,8).map(e=>[e.tagName,e.className,Math.round(e.getBoundingClientRect().right)])]')));
  await click('#navToggle');await wait('document.querySelector(".nav-open .nav-text").getBoundingClientRect().width>0');await click('[data-page="devices"]');
  await rpc('Emulation.setEmulatedMedia',{features:[{name:'prefers-color-scheme',value:'dark'}]});await shot('devices-dark');
  await click('[data-page="app-settings"]');await js('window.confirm=()=>true');
  rejectNext=true;await input('[data-setting="ThemeMode"]','system','change');await click('#deleteSettingsClass');await wait('!$("saveSettingsButton").hidden');assert.ok(records.has('b'));
  await click('#deleteSettingsClass');await wait('state.selectedClassId==="a" && drafts.size===0');assert.ok(!records.has('b'));
  assert.deepEqual(errors,[]);console.log('PASS: pair into an existing class, switch class with pending edits, automatic sync, retry, edit cell, retain subject color, persist empty time slot, narrow layout, dark theme, deletion after a failed save; no browser errors.');
  await rpc('Browser.close').catch(()=>{});
}finally{
  if(socket?.readyState===1)socket.close();
  if(edge.exitCode===null){await Promise.race([exited,delay(2500)]);if(edge.exitCode===null)edge.kill()}
  await Promise.race([exited,delay(3000)]);server.closeAllConnections();server.close();
  await rm(profile,{recursive:true,force:true,maxRetries:10,retryDelay:250}).catch(e=>console.warn('Could not remove disposable profile:',e.code));
}
