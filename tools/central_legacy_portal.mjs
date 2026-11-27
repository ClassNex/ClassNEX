// Drive only the browser and desktop processes spawned for the disposable loopback fixture.
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { createInterface } from 'node:readline';
import { setTimeout as sleep } from 'node:timers/promises';

const [host, issuer, mode, app, browser, root] = process.argv.slice(2);
const requireTest = (value, reason) => { if (!value) throw new Error(reason); };
requireTest([host, issuer].every(u => /^http:\/\/127\.0\.0\.1:\d+$/.test(u)) && ['old-settings','old-timetable','old-settings-boundary'].includes(mode), 'Loopback fixture only');
const isSettings = mode.startsWith('old-settings'), origins = new Set([host, issuer]);
const until = async (fn, reason, timeout=20000) => {
  const end=Date.now()+timeout; do { const result=await fn(); if (result) return result; await sleep(80); } while(Date.now()<end); throw new Error(reason);
};
const deadline = (work, reason, ms) => { let timer; return Promise.race([work,new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error(reason)),ms)})]).finally(()=>clearTimeout(timer)); };
const children = new Set();
function child(exe, args, options) {
  const proc=spawn(exe,args,{windowsHide:true,...options}); children.add(proc);
  proc.finished=once(proc,'exit').then(([code])=>{children.delete(proc);return code}); return proc;
}
function desktop(reopen=false) {
  const p=child(app,[host,issuer,mode+(reopen?'-reopen':'')],{cwd:dirname(app),stdio:['ignore','pipe','pipe']});
  let error='';p.stderr.on('data',part=>{error=(error+part).slice(-1400)});
  const source=createInterface({input:p.stdout,crlfDelay:Infinity})[Symbol.asyncIterator]();
  return {proc:p,async stage(expected){const {done,value}=await deadline(source.next(),'Desktop stage timed out: '+expected,65000);
    requireTest(!done,'Desktop exited before '+expected+': '+error);const item=JSON.parse(value);requireTest(item.stage===expected,'Unexpected desktop stage');return item;}};
}
class Page {
  constructor(socket){ this.ws=socket; this.seq=0; this.pending=new Map(); this.writes=new Map(); this.saved=[];
    socket.onmessage=({data})=>{ const msg=JSON.parse(data);
      if(msg.id){const task=this.pending.get(msg.id);if(task){this.pending.delete(msg.id);msg.error?task.reject(new Error(msg.error.message)):task.resolve(msg.result)}return;}
      const e=msg.params;
      if(msg.method==='Fetch.requestPaused'){ const url=new URL(e.request.url);const allowed=origins.has(url.origin);this.call(allowed?'Fetch.continueRequest':'Fetch.failRequest',allowed?{requestId:e.requestId}:{requestId:e.requestId,errorReason:'BlockedByClient'}).catch(()=>{}); }
      if(msg.method==='Network.requestWillBeSent'&&['PUT','POST'].includes(e.request.method)) {
        const path=new URL(e.request.url).pathname;const name=path.endsWith('/pairing/claim')?'pair':path.match(/\/classes\/[^/]+\/(timetable|settings)$/)?.[1]; if(name)this.writes.set(e.requestId,name);
      }
      if(msg.method==='Network.responseReceived'&&e.response.status===200&&this.writes.has(e.requestId))this.saved.push(this.writes.get(e.requestId));
    };
  }
  call(method,params={}){const id=++this.seq;return deadline(new Promise((resolve,reject)=>{this.pending.set(id,{resolve,reject});this.ws.send(JSON.stringify({id,method,params}))}),method+' timed out',12000);}
  async dom(expression){const r=await this.call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw new Error('Portal DOM action failed');return r.result.value;}
  async when(expression,reason){return until(async()=>{try{return await this.dom(expression)}catch{return false}},reason);}
}

let page, chrome;
try {
  const profile=join(root,'qa-browser');
  chrome=child(browser,['--headless=new','--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-component-update','--disable-extensions','--disable-sync','--disable-gpu','--no-proxy-server','--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1','--remote-debugging-address=127.0.0.1','--remote-debugging-port=0','--window-size=1200,860','--user-data-dir='+profile,'about:blank'],{stdio:'ignore'});
  const port=await until(async()=>{try{return Number((await readFile(join(profile,'DevToolsActivePort'),'utf8')).split(/\r?\n/)[0])}catch{return 0}},'Headless browser did not open a loopback test port');
  const tab=await until(async()=>{const tabs=await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();return tabs.find(p=>p.type==='page'&&p.url==='about:blank')},'No disposable browser page');
  const endpoint=new URL(tab.webSocketDebuggerUrl);requireTest(endpoint.hostname==='127.0.0.1'&&Number(endpoint.port)===port,'Unexpected debugging endpoint');
  const sock=new WebSocket(endpoint.href);await new Promise((ok,no)=>{sock.onopen=ok;sock.onerror=no;});page=new Page(sock);
  await page.call('Network.enable');await page.call('Fetch.enable',{patterns:[{urlPattern:'http*'}]});
  await page.call('Page.navigate',{url:host+'/Control/'});
  await page.when("!!document.querySelector('#loginScreen a[href=\"auth/login\"]')",'Packaged login page did not load');
  await page.dom("document.querySelector('#loginScreen a').click()");
  await page.when("!!document.querySelector('#appShell:not(.hidden)')",'Local mock login did not reach the old portal');
  const active=desktop();const ticket=await active.stage('pair');
  await page.dom("document.querySelector('[data-page=devices]').click()");
  requireTest(/^[2-9A-HJ-NP-Z]{4}-[2-9A-HJ-NP-Z]{4}$/.test(ticket.code),'Invalid anonymous pairing code');
  await page.dom(`document.querySelector('#pairCode').value=${JSON.stringify(ticket.code)};document.querySelector('#pairButton').click()`);
  await page.when("document.querySelector('#pairStatus')?.textContent.includes('已配对')",'The old portal did not confirm pairing');
  await active.stage('pending');
  const kind=isSettings?'timetable':'settings', section=isSettings?'archive-settings':'app-settings', status=isSettings?'archiveStatus':'settingsStatus';
  await page.dom(`document.querySelector('[data-page="${section}"]').click()`);
  await page.when(`document.getElementById('${status}')?.textContent.includes('正在编辑')`,'The selected classroom did not open');
  if(isSettings)await page.dom("document.querySelector('#newSubjectName').value='地理';document.querySelector('#addSubjectButton').click()");
  else await page.dom("const c=document.querySelector('[data-setting=ThemeMode]');c.value='dark';c.dispatchEvent(new Event('change',{bubbles:true}))");
  await page.when(`document.getElementById('${status}')?.textContent.includes('已同步')`,'The packaged editor did not acknowledge the change');
  const first=await active.stage('verified');requireTest(await active.proc.finished===0,'First desktop process failed');
  const remoteMatches=()=>page.dom(`(async()=>{const sel=document.getElementById('${isSettings?'archiveClassSelect':'settingsClassSelect'}');const r=await fetch(new URL('api/v1/classes/'+encodeURIComponent(sel.value),document.baseURI),{cache:'no-store'});if(!r.ok)return false;const c=await r.json();const prefs=Object.fromEntries(Object.entries(JSON.parse(c.settingsJson)).map(([k,v])=>[k.toLowerCase(),v]));return ${isSettings?"prefs.fontscale===1.25&&c.timetableYaml.includes('地理')":"prefs.thememode==='dark'&&c.timetableYaml.includes('数学')&&!c.timetableYaml.includes('生物')"}})()`);
  requireTest(await remoteMatches() && page.saved.includes('pair')&&page.saved.includes(kind),'The real portal did not persist the intended isolated edit');
  await writeFile(join(dirname(dirname(app)),'.legacy-binary-fixture'),mode,{flag:'wx'});
  const restored=desktop(true);const second=await restored.stage('verified');requireTest(await restored.proc.finished===0,'Reopened desktop failed');
  requireTest(await remoteMatches(),'Reopening the desktop altered the server resource');
  console.log(JSON.stringify({direction:mode,headlessPortal:'real packaged form',portalWrites:page.saved,serverPreservedPendingResource:true,first,reopened:second}));
} catch(error){console.error(error.message);process.exitCode=1;}
finally{
  if(page)try{void page.call('Browser.close').catch(()=>{});if(chrome)await Promise.race([chrome.finished,sleep(2000)]);page.ws.close()}catch{}
  for(const p of children)p.kill();await Promise.allSettled([...children].map(p=>p.finished));
}
