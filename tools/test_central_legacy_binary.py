"""Run the current native pairing service and old portal against the saved pre-conditional binary, entirely on loopback."""
import argparse
import hashlib
import http.client
import http.server
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import threading
import time

from test_central_oauth import Provider, request


ROOT = Path(__file__).resolve().parents[1]
PUBLISHED = '7dd4ddb'
PINNED = {
    'ClassNex.Central.dll': '0a9d9e406cef94716d0f6b25f56233b0d05dadb5a9287d74bbe927cc8ea27540',
    'ClassNex.Core.dll': '04ad7319caa4c5e4ec41c9297275f74eda31e05b08adbe295333f509283dab99',
    'YamlDotNet.dll': '86f873f682c619a0c24e263dcdd15fe027104666641c92f503516671d61edda6',
    'wwwroot/index.html': '38387cd941c77eb608de4112c55e31f68f938ad74742b362cec18a1df07b70be',
    'wwwroot/control.css': '0932dd42fe7c3b75cc1022ed8a55ff3d5443f7270b331e131716696e15d4da04',
}
FILES = [*PINNED, 'ClassNex.Central.deps.json', 'ClassNex.Central.runtimeconfig.json', 'ClassNex.Central.staticwebassets.endpoints.json']


class UnexpectedOldWrite(Exception):
    def __init__(self, mode, counts):
        super().__init__('A desktop report contained a pending payload in '+mode)
        self.proof = {'direction':mode,'verified':False,**dict(zip(('fullPublishes','conditionalPublishes','pendingFullReports','reportsContainingDraft'),map(int,counts)))}


class ControlMount(http.server.BaseHTTPRequestHandler):
    """The deployed reverse proxy strips /Control; the saved Kestrel binary serves its original root routes."""
    def log_message(self, *args): pass

    def forward(self):
        if not self.path.startswith('/Control/'):
            return self.send_error(404)
        upstream=http.client.HTTPConnection('127.0.0.1',self.server.internal_port,timeout=8)
        try:
            length=int(self.headers.get('Content-Length','0'))
            if length>5*1024*1024:return self.send_error(413)
            body=self.rfile.read(length) if length else None
            headers={k:self.headers[k] for k in ('Host','Content-Type','Cookie','Authorization','Origin','Accept','If-Match') if k in self.headers}
            upstream.request(self.command,self.path[len('/Control'):],body,headers)
            r=upstream.getresponse();payload=r.read();self.send_response(r.status)
            for k,v in r.getheaders():
                if k.lower() not in ('connection','transfer-encoding','content-length','server','date'):self.send_header(k,v)
            self.send_header('Content-Length',str(len(payload)));self.end_headers()
            try:self.wfile.write(payload)
            except (BrokenPipeError,ConnectionResetError):pass
        finally:upstream.close()

    do_GET=do_POST=do_PUT=do_DELETE=forward


def digest(data):
    return hashlib.sha256(data).hexdigest()


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def matches_git(source, checksum):
    result = subprocess.run(['git','cat-file','blob',PUBLISHED+':'+source],cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL)
    lf = result.stdout.replace(b'\r\n',b'\n')
    return not result.returncode and checksum in {digest(result.stdout),digest(lf),digest(lf.replace(b'\n',b'\r\n'))}


def provenance(directory, runner):
    binary = {name: digest((directory / name).read_bytes()) for name in PINNED}
    check(binary == PINNED, 'Wrong binary/portal: this test pins the archived classroom deployment artifact. Nothing was executed.')
    evidence = {'sha256': binary, 'gitSource': subprocess.check_output(['git', 'rev-parse', PUBLISHED], cwd=ROOT, text=True).strip(), 'pdb': {}}
    evidence['portalMatchesGit'] = {name:matches_git('src/ClassNex.Central/'+name, checksum) for name,checksum in binary.items() if name.startswith('wwwroot/')}
    check(all(evidence['portalMatchesGit'].values()), 'Archived portal no longer matches the recorded deployment source')
    for project in ('ClassNex.Central', 'ClassNex.Core'):
        meta = json.loads(subprocess.check_output([str(runner), '--legacy-pdb', str(directory / (project+'.pdb'))], cwd=ROOT))
        sources = meta['documents']; check(sources, 'No project source in the verified PDB')
        if project == 'ClassNex.Core':
            sources = {k: v for k, v in sources.items() if Path(k).name in ('CentralManagementProtocol.cs','CentralPairing.cs','CentralSettingsPayload.cs','CsesCodec.cs')}
            check(len(sources) == 4, 'Core protocol sources not found in the PDB')
        else:
            check({'Program.cs','PairingRoutes.cs','CentralStore.Pairing.cs','CentralStore.cs','CentralSessionStore.cs'} <= {Path(k).name for k in sources}, 'Deployment source documents missing')
        different = []
        for source, checksum in sources.items():
            check(source.startswith('src/'+project+'/') and '..' not in Path(source).parts, 'Invalid source name')
            if not matches_git(source, checksum):
                different.append(source)
        evidence['pdb'][project] = {'version': meta['productVersion'], 'documentsChecked': len(sources), 'different': different}
        check(not different, project+' symbols no longer match the recorded deployment source')
    return evidence


def exercise(mode, old, runner, browser):
    with tempfile.TemporaryDirectory(prefix='classnex-central-test-') as path:
        root=Path(path); old_app=root/'saved-service'; old_app.mkdir()
        for name in FILES:
            out=old_app/name;out.parent.mkdir(exist_ok=True);shutil.copyfile(old/name,out)
            check(digest(out.read_bytes()) == digest((old/name).read_bytes()), 'Saved deployment copy changed')
        current=root/'paired-desktop'/'app';shutil.copytree(runner.parent,current)
        check(not (old_app/'data').exists() and not (current.parent/'data').exists(), 'Expected a fresh anonymous fixture')
        provider=http.server.ThreadingHTTPServer(('127.0.0.1',0),Provider)
        provider.codes={};provider.identity='session';provider.userinfo_query_seen=False
        provider.avatar_started,provider.avatar_release=threading.Event(),threading.Event();provider.avatar_release.set()
        threading.Thread(target=provider.serve_forever,daemon=True).start()
        mount=http.server.ThreadingHTTPServer(('127.0.0.1',0),ControlMount)
        with socket.socket() as reserved:
            reserved.bind(('127.0.0.1',0));mount.internal_port=reserved.getsockname()[1]
        port=mount.server_port;host=f'http://127.0.0.1:{port}';issuer=f'http://127.0.0.1:{provider.server_port}'
        env={**os.environ,'ASPNETCORE_URLS':f'http://127.0.0.1:{mount.internal_port}','ASPNETCORE_CONTENTROOT':str(old_app),'CENTRAL_DATA_DIRECTORY':str(root/'server-state'),
             'CASDOOR_URL':issuer,'CASDOOR_INTERNAL_URL':issuer,'CASDOOR_CLIENT_ID':'fixture','CASDOOR_CLIENT_SECRET':'','CENTRAL_PUBLIC_URL':host+'/Control',
             'NO_PROXY':'127.0.0.1,localhost','no_proxy':'127.0.0.1,localhost'}
        for key in ('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NODE_OPTIONS'): env.pop(key,None)
        server=None
        threading.Thread(target=mount.serve_forever,daemon=True).start()
        try:
            server=subprocess.Popen(['dotnet',str(old_app/'ClassNex.Central.dll')],cwd=old_app,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            for _ in range(220):
                check(server.poll() is None,'The archived service exited during startup')
                try:
                    if request(mount.internal_port,'GET','/api/v1/auth/me')[0]==401: break
                except OSError: pass
                time.sleep(.1)
            else: raise RuntimeError('The archived service did not open its loopback listener')
            for url,name in [('/Control/','wwwroot/index.html'),('/Control/control.css','wwwroot/control.css')]:
                status,_,body=request(port,'GET',url);check(status==200 and digest(body)==PINNED[name],'Served portal did not match the archived files')
            result=subprocess.run(['node',str(ROOT/'tools/central_legacy_portal.mjs'),host,issuer,mode,str(current/runner.name),str(browser),str(root)],
                                  env=env,cwd=root,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=180)
            if result.returncode:
                detail=result.stderr.decode('utf8',errors='replace').strip()
                counts=re.search(r'Unexpected old-protocol write in '+re.escape(mode)+r': publishes=(\d+), changes=(\d+), fullReportsWhilePending=(\d+), reportsContainingDraft=(\d+)',detail)
                if counts: raise UnexpectedOldWrite(mode,counts.groups())
                raise RuntimeError(detail or 'Portal or desktop did not finish')
            return json.loads(result.stdout)
        finally:
            if server is not None and server.poll() is None:server.terminate();server.wait(timeout=10)
            mount.shutdown();mount.server_close()
            provider.shutdown();provider.server_close()


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--old-dir',type=Path,default=ROOT/'build/central-service-26w48b-classroom')
    p.add_argument('--runner-dir',type=Path,help='Pre-published current ClassNex.PairingReview directory; by default this test publishes its own copy.')
    p.add_argument('--browser',type=Path,help='Chrome or Edge executable; always starts a new headless test profile')
    p.add_argument('--report',type=Path,help='Write the verified, credential-free JSON proof to this new or existing report file.')
    p.add_argument('--boundary',action='store_true',help='Exercise the moment the newly paired desktop announces its first applied revision, immediately before the first acknowledgement.')
    args=p.parse_args();old=args.old_dir.resolve(strict=True)
    browser=args.browser or next((p for p in [Path(os.environ.get('ProgramFiles','C:/Program Files'))/'Google/Chrome/Application/chrome.exe',Path(os.environ.get('ProgramFiles(x86)','C:/Program Files (x86)'))/'Microsoft/Edge/Application/msedge.exe'] if p.is_file()),None)
    check(browser and browser.is_file(),'No headless Chromium browser found. Supply --browser.')
    out=ROOT/'build/central-legacy-qa-1127';published=args.runner_dir or out/'published'
    if not args.runner_dir:
        subprocess.run(['dotnet','publish',str(ROOT/'tools/ClassNex.PairingReview/ClassNex.PairingReview.csproj'),'-c','Release','--no-self-contained','--artifacts-path',str(out),'-o',str(published),'--nologo','--verbosity','quiet'],check=True)
    runner=(published/'ClassNex.PairingReview.exe').resolve(strict=True)
    evidence=provenance(old,runner)
    evidence['checkoutHeadAtVerification']=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    evidence['desktopAssemblySha256']=digest((runner.parent/'ClassNex.dll').read_bytes())
    evidence['scenarios']=[]
    failed=None
    try:
        for mode in (('old-settings-boundary',) if args.boundary else ('old-settings','old-timetable')):
            evidence['scenarios'].append(exercise(mode,old,runner,browser.resolve()))
    except UnexpectedOldWrite as error:
        failed=error;evidence['scenarios'].append(error.proof)
    evidence['passed']=failed is None
    text=json.dumps(evidence,ensure_ascii=False,indent=2)
    if args.report:
        args.report.parent.mkdir(parents=True,exist_ok=True);args.report.write_text(text+'\n',encoding='utf8')
    print(text)
    if failed: raise SystemExit(str(failed))


if __name__=='__main__':main()
