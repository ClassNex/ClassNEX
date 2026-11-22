"""Exercise the real Central HTTP API against a loopback-only OAuth provider."""
import argparse
import base64
import hashlib
import http.client
import http.server
import json
import os
import pathlib
import socket
import subprocess
import tempfile
import threading
import time
import urllib.parse as url
import struct
import zlib


class Provider(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, status, data):
        self.binary(status, 'application/json', json.dumps(data).encode())

    def binary(self, status, mime, content):
        self.send_response(status)
        self.send_header('Content-Type', mime)
        self.send_header('Content-Length', str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def do_POST(self):
        if self.path == '/v1/chat/completions':
            payload = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
            if self.headers.get('Authorization') != 'Bearer local-test-value' or not payload.get('stream'):
                return self.reply(400,{})
            event = json.dumps({'choices':[{'delta':{'content':'本地课表联测通过'}}]},ensure_ascii=False)
            content = ('data: '+event+'\n\ndata: [DONE]\n\n').encode()
            self.send_response(200); self.send_header('Content-Type','text/event-stream'); self.send_header('Content-Length',str(len(content))); self.end_headers(); self.wfile.write(content)
            return
        fields = url.parse_qs(self.rfile.read(int(self.headers['Content-Length'])).decode())
        verifier = fields.get('code_verifier', [''])[0]
        digest = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b'=').decode()
        code = fields.get('code', [''])[0]
        expected = self.server.codes.pop(code, None)
        if self.path != '/api/login/oauth/access_token' or not expected or (digest, fields.get('redirect_uri', [''])[0]) != expected[:2] or expected[2] == 'fail':
            return self.reply(400, {'error': 'invalid_grant'})
        self.reply(200, {'access_token': 'fixture-' + expected[2], 'token_type': 'Bearer'})

    def do_GET(self):
        parsed = url.urlsplit(self.path)
        if parsed.path.startswith('/fixture/'):
            action = parsed.path[len('/fixture/'):]
            if action in ('session', 'second', 'broken', 'fail'):
                self.server.identity = action
            elif action == 'hold':
                self.server.avatar_started.clear(); self.server.avatar_release.clear()
            elif action == 'release':
                self.server.avatar_release.set()
            elif action == 'avatar-status':
                return self.reply(200, self.server.avatar_started.is_set())
            elif action == 'avatar-a':
                self.server.avatar_started.set(); self.server.avatar_release.wait(18)
                def chunk(t, v):
                    return struct.pack('>I', len(v)) + t + v + struct.pack('>I', zlib.crc32(t+v))
                png = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR', struct.pack('>IIBBBBB', 2, 2, 8, 2, 0, 0, 0))+chunk(b'IDAT', zlib.compress((b'\x00'+b'\x88\x22\x33'*2)*2))+chunk(b'IEND', b'')
                return self.binary(200, 'image/png', png)
            elif action == 'bad-image':
                return self.binary(200, 'image/png', b'not an image')
            else:
                return self.reply(404,{})
            return self.reply(200, True)
        if parsed.path == '/login/oauth/authorize':
            query = url.parse_qs(parsed.query)
            callback = query['redirect_uri'][0]
            if url.urlsplit(callback).hostname not in ('127.0.0.1', 'localhost') or query['code_challenge_method'] != ['S256']:
                return self.reply(400, {})
            code = os.urandom(12).hex()
            self.server.codes[code] = (query['code_challenge'][0], callback, self.server.identity)
            self.send_response(302)
            self.send_header('Location', callback + '?' + url.urlencode({'state': query['state'][0], 'code': code}))
            self.end_headers()
            return
        profiles = {
            'Bearer fixture-session': {'sub': 'classnex-fixture', 'name': '课堂测试', 'email': 'fixture@example.invalid', 'preferred_username': 'fixture', 'picture': '/fixture/avatar-a'},
            'Bearer fixture-second': {'sub': 'classnex-second', 'name': '课表测试乙', 'preferred_username': 'second'},
            'Bearer fixture-broken': {'sub': 'classnex-broken', 'name': '头像测试', 'preferred_username': 'broken', 'picture': '/fixture/bad-image'},
        }
        profile = profiles.get(self.headers.get('Authorization'))
        if parsed.path != '/api/userinfo' or profile is None: return self.reply(401, {})
        self.reply(200, profile)


def request(port, method, path, body=None, headers=None):
    conn = http.client.HTTPConnection('127.0.0.1', port, timeout=5)
    try:
        conn.request(method, path, json.dumps(body).encode() if body is not None else None,
                     {'Content-Type': 'application/json', **(headers or {})})
        r = conn.getresponse()
        return r.status, dict(r.getheaders()), r.read()
    finally:
        conn.close()


def run(central, desktop):
    with tempfile.TemporaryDirectory(prefix='classnex-central-test-') as temp:
        root = pathlib.Path(temp)
        provider = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Provider)
        provider.codes = {}
        provider.identity = 'session'
        provider.avatar_started, provider.avatar_release = threading.Event(), threading.Event()
        provider.avatar_release.set()
        provider_port = provider.server_port
        threading.Thread(target=provider.serve_forever, daemon=True).start()
        with socket.socket() as s:
            s.bind(('127.0.0.1', 0)); port = s.getsockname()[1]
        host = f'http://127.0.0.1:{port}'
        casdoor = f'http://127.0.0.1:{provider_port}'
        settings = {**os.environ, 'CASDOOR_URL': casdoor, 'CASDOOR_INTERNAL_URL': casdoor,
                    'CASDOOR_CLIENT_ID': 'fixture', 'CASDOOR_CLIENT_SECRET': '', 'CENTRAL_PUBLIC_URL': host + '/Control',
                    'CENTRAL_DATA_DIRECTORY': str(root/'server'), 'ASPNETCORE_URLS': host,
                    'NO_PROXY': '127.0.0.1,localhost', 'no_proxy': '127.0.0.1,localhost'}
        def start():
            child = subprocess.Popen(['dotnet', str(central)], env=settings, cwd=root, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            for _ in range(160):
                try:
                    if request(port, 'GET', '/api/v1/auth/me')[0] == 401: return child
                except OSError: pass
                if child.poll() is not None: break
                time.sleep(.1)
            child.terminate(); child.wait(timeout=10); raise RuntimeError('Central did not listen on the loopback fixture')
        central_process = None
        try:
            central_process = start()
            status, h, _ = request(port, 'GET', '/auth/login'); assert status == 302
            auth = url.urlsplit(h['Location']); assert auth.netloc == f'127.0.0.1:{provider_port}'
            args = url.parse_qs(auth.query); assert args['redirect_uri'] == [host+'/Control/auth/callback'] and 'prompt' not in args
            status, h, _ = request(provider_port, 'GET', auth.path+'?'+auth.query); assert status == 302
            callback = url.urlsplit(h['Location']); assert callback.path == '/Control/auth/callback'
            status, h, _ = request(port, 'GET', '/auth/callback?'+callback.query)
            assert status == 302 and h['Location'] == host+'/Control/', (status,h.get('Location'))
            cookie = {'Cookie':h['Set-Cookie'].split(';')[0]}
            def api(path, method='GET', body=None, auth=cookie):
                status, _, data = request(port, method, '/api/v1/'+path, body, auth)
                assert status in (200,204), (path,status)
                return json.loads(data) if data else None
            assert api('auth/me')['subject'] == 'classnex-fixture'
            bearer = {'Authorization':'Bearer fixture-session'}
            assert api('auth/profile', auth=bearer)['email'] == 'fixture@example.invalid'
            record = api('classes','POST', {'name':'课堂测试'}); cls = record['classId']
            api('client/device','POST', {'clientId':'desk','displayName':'匿名桌面','machineName':'fixture','appVersion':'test'}, bearer)
            api('devices/desk','PUT',{'classId':cls})
            timetable = 'version: 1\nsubjects: []\nschedules: []\n'
            api('classes/'+cls, 'PUT', {'settingsJson':'{"themeMode":"dark"}', 'timetableYaml':timetable, 'message':'test'})
            pull = api('client/assignment?clientId=desk',auth=bearer)
            assert json.loads(pull['settingsJson'])['themeMode']=='dark' and pull['timetableYaml']==timetable
            rev=api('client/publish','POST',{'clientId':'desk','settingsJson':'{"themeMode":"light"}','timetableYaml':timetable},bearer)
            assert rev['revision'] > pull['revision'] and json.loads(api('classes/'+cls)['settingsJson'])['themeMode']=='light'
            if desktop:
                app_dir = root/'desktop'/'app'
                data = root/'desktop'/'data'
                data.mkdir(parents=True)
                (app_dir.parent/'.classnex-review').touch()
                (data/'Settings.json').write_text(json.dumps({'IsOobeCompleted':True,'IsNexVoiceWakeEnabled':False,'CentralManagement':{'IsEnabled':False}}),encoding='utf-8')
                subprocess.run(['dotnet','publish',str(desktop),'-c','Debug','--no-build','--no-restore','--no-self-contained','-o',str(app_dir)],check=True,stdout=subprocess.DEVNULL)
                client = subprocess.run([str(app_dir/'ClassNex.exe'), '--review-account', casdoor, host], cwd=app_dir, env=settings, timeout=60)
                if client.returncode:
                    reason = data/'account-review-error.txt'
                    raise AssertionError(reason.read_text(encoding='utf-8') if reason.exists() else f'Desktop exited {client.returncode}')
            central_process.terminate(); central_process.wait(timeout=10); central_process = start()
            assert api('auth/me')['subject']=='classnex-fixture' and any(c['classId']==cls for c in api('classes'))
            status, h, _ = request(port,'GET','/auth/logout',headers=cookie)
            assert status==302 and h['Location']==host+'/Control/' and request(port,'GET','/api/v1/auth/me',headers=cookie)[0]==401
            print('PASS: PKCE, /Control callback, profile, assignment, two-way settings/timetable, persisted session, logout' + (', desktop login, account switching/cancel/failure, avatar ownership/race/fallback, missing email, legacy identity, automatic bidirectional desktop sync, AI streaming' if desktop else ''))
        finally:
            if central_process and central_process.poll() is None:
                central_process.terminate(); central_process.wait(timeout=10)
            provider.shutdown(); provider.server_close()


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--central',type=pathlib.Path,default=pathlib.Path(__file__).resolve().parents[1]/'src/ClassNex.Central/bin/Debug/net10.0/ClassNex.Central.dll')
    parser.add_argument('--desktop',type=pathlib.Path,help='Optional built Debug ClassNex.csproj; publishes a clean disposable app and tests its loopback callback')
    opts=parser.parse_args(); run(opts.central.resolve(strict=True),opts.desktop.resolve(strict=True) if opts.desktop else None)
