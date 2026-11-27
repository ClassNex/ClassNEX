"""Device-pairing checks used by the isolated Central OAuth fixture."""

import datetime as dt
from concurrent.futures import ThreadPoolExecutor
import json
import re
import urllib.parse as url


def exercise_pairing(request, port, provider_port, host, owner):
    assert url.urlsplit(host).hostname == '127.0.0.1'

    def api(method, path, auth=None, data=None, expect=200):
        status, headers, content = request(port, method, '/api/v1/' + path, data, auth)
        assert status == expect, (method, path, status, expect)
        return headers, json.loads(content) if content else None

    def start():
        h, t = api('POST', 'pairing/start', data={'displayName':'Room A', 'machineName':'fixture', 'appVersion':'test'})
        assert h['Cache-Control'] == 'no-store' and re.fullmatch(r'[2-9A-HJ-NP-Z]{4}-[2-9A-HJ-NP-Z]{4}', t['code'])
        assert 0 < (dt.datetime.fromisoformat(t['expiresAt']) - dt.datetime.now(dt.timezone.utc)).total_seconds() <= 300
        return t

    def device(t):
        return {'Authorization':'ClassNexDevice ' + t['deviceToken']}

    def status(t, code, expected=None):
        h, result = api('GET', 'pairing/status?clientId=' + t['clientId'], device(t), expect=code)
        assert h['Cache-Control'] == 'no-store'
        if expected: assert result['state'] == expected

    assert request(provider_port, 'GET', '/fixture/second')[0] == 200
    s, h, _ = request(port, 'GET', '/auth/login'); assert s == 302
    link = url.urlsplit(h['Location']); assert link.netloc == f'127.0.0.1:{provider_port}'
    s, h, _ = request(provider_port, 'GET', link.path + '?' + link.query); assert s == 302
    callback = url.urlsplit(h['Location']); assert callback.netloc == url.urlsplit(host).netloc
    s, h, _ = request(port, 'GET', '/auth/callback?' + callback.query); assert s == 302
    other = {'Cookie':h['Set-Cookie'].split(';', 1)[0]}
    assert request(provider_port, 'GET', '/fixture/session')[0] == 200
    assert api('GET', 'auth/me', other)[1]['subject'] == 'classnex-second'
    other_class = api('POST', 'classes', other, {'name':'Room B'})[1]['classId']

    t = start(); d = device(t); code = {'code':t['code']}; read = 'client/assignment?clientId=' + t['clientId']
    status(t, 200, 'pending')
    api('GET', read, d, expect=401)
    api('POST', 'pairing/claim', data=code, expect=401)
    api('POST', 'pairing/claim', {'Authorization':'Bearer fixture-session'}, code, 401)
    api('POST', 'pairing/claim', {**owner, 'Origin':'https://different-origin.invalid'}, code, 403)
    api('POST', 'pairing/claim', owner, {**code, 'classId':other_class}, 404)
    _, linked = api('POST', 'pairing/claim', owner, code)
    assert linked['clientId'] == t['clientId'] and linked['className'] == 'Room A' and 'deviceToken' not in linked
    api('POST', 'pairing/claim', owner, code, 404)
    room = 'classes/' + linked['classId']; status(t, 200, 'linked')
    api('GET', 'devices', d, expect=401); api('GET', 'auth/me', d, expect=401)
    api('GET', room, other, expect=404)
    assert t['clientId'] not in [x['clientId'] for x in api('GET', 'devices', other)[1]]
    api('DELETE', 'devices/' + t['clientId'], other, expect=404)

    seed = 'version: 1\nsubjects:\n  - name: Math\nschedules: []\n'
    api('PUT', room+'/timetable', owner, {'version':1,'subjects':[],'schedules':[]})
    assert not api('GET', read, d)[1]['timetableYaml']
    baseline = api('GET', read, d)[1]['revision']
    api('POST', 'client/report', d, {'clientId':t['clientId'], 'appVersion':'test','machineName':'fixture',
        'settingsJson':'{"themeMode":"system","email":"room@example.invalid"}', 'timetableYaml':seed, 'error':None})
    room_snapshot = api('GET', room, owner)[1]
    assert room_snapshot['revision'] > baseline and json.loads(room_snapshot['settingsJson']) == {'themeMode':'system'}
    _, web_edit = api('PUT', room, owner, {'settingsJson':'{"themeMode":"dark"}', 'timetableYaml':seed+'# edited by teacher\n','message':'fixture'})
    from_web = api('GET', read, d)[1]
    assert from_web['revision'] == web_edit['revision'] and from_web['timetableYaml'] == web_edit['timetableYaml']
    assert json.loads(from_web['settingsJson']) == {'themeMode':'dark'}
    with ThreadPoolExecutor(max_workers=2) as pool:
        edits = [pool.submit(api, 'PUT', room+'/settings', owner, {'settingsJson':'{"themeMode":"light","fontScale":1.25}'}),
                 pool.submit(api, 'PUT', room+'/timetable', owner, {'version':1,'subjects':[{'name':'History'}],'schedules':[]})]
        for edit in edits: edit.result()
    together = api('GET', read, d)[1]
    assert together['revision'] == from_web['revision']+2 and json.loads(together['settingsJson'])['fontScale'] == 1.25 and 'History' in together['timetableYaml']
    change = {'clientId':t['clientId'], 'settingsJson':'{"themeMode":"light","centralManagement":{},"userName":"local-only"}', 'timetableYaml':seed+'# edited in classroom\n'}
    _, revision = api('POST', 'client/publish', d, change)
    modified = api('GET', room, owner)[1]
    assert revision['revision'] > from_web['revision'] and json.loads(modified['settingsJson']) == {'themeMode':'light'} and modified['timetableYaml'] == change['timetableYaml']

    second = start(); api('POST', 'pairing/claim', owner, {'code':second['code'], 'classId':linked['classId']})
    api('GET', read, device(second), expect=401)
    api('POST', 'client/publish', device(second), change, 401)
    api('DELETE', 'pairing/device?clientId='+second['clientId'], device(second), expect=204)
    status(second, 401)
    unclaimed = start(); api('DELETE', 'pairing/device?clientId='+unclaimed['clientId'], device(unclaimed), expect=204)
    status(unclaimed, 401)
    abandoned = start()

    def after_restart():
        status(t, 200, 'linked'); status(abandoned, 401)
        assert api('GET', read, d)[1]['revision'] == revision['revision']
        api('DELETE', 'devices/'+t['clientId'], owner, expect=204); status(t, 401)
        api('GET', read, d, expect=401); api('POST','client/publish',d,change,401)
        assert api('GET', room, owner)[1]['timetableYaml'] == change['timetableYaml']
    return after_restart
