"""Update allowlisted Central publish files in place. No changes to containers, proxy configuration or data paths."""
import hashlib
import json
import pathlib
import posixpath
import re
import shlex
import time


def _install(ftp, run, files, previous, app, stage, service, check):
    attempted = []
    try:
        for name in sorted(files, key=lambda n: n == 'wwwroot/index.html'):
            attempted.append(name)
            ftp.posix_rename(stage + '/new/' + name, app + '/' + name)
        run('docker restart ' + service)
        check()
    except (Exception, KeyboardInterrupt) as failure:
        incomplete = []
        changed = False
        for name in reversed(attempted):
            staged, live = stage + '/new/' + name, app + '/' + name
            try:
                try:
                    ftp.lstat(staged)
                    continue   # An atomic rename which never happened leaves its source in staging.
                except FileNotFoundError:
                    pass
                changed = True
                if name in previous:
                    restore = stage + '/restore/' + name
                    run('cp -p ' + stage + '/previous/' + name + ' ' + restore)
                    ftp.posix_rename(restore, live)
                else:
                    ftp.posix_rename(live, staged)
            except Exception:
                incomplete.append(name)
        if incomplete:
            raise RuntimeError(f'Rollback incomplete for {", ".join(incomplete)}; did not restart a mixed build. Recovery files: {stage}') from failure
        if changed:
            try:
                run('docker restart ' + service)
            except Exception as restart_failure:
                raise RuntimeError('Previous files restored but Central restart failed. Recovery files: ' + stage) from restart_failure
        raise


def deploy(ssh, output):
    output = pathlib.Path(output).resolve(strict=True)
    app, data, service = '/opt/classnex-central', '/opt/classnex-central-data', 'classnex-central'
    files = {p.relative_to(output).as_posix(): p for p in output.rglob('*') if p.is_file()}
    required = {'ClassNex.Central.dll', 'ClassNex.Core.dll', 'wwwroot/index.html', 'wwwroot/control.css'}
    allowed = re.compile(r'(?:ClassNex\.(?:Central|Core)\.(?:dll|pdb|xml)|ClassNex\.Central\.(?:deps|runtimeconfig|staticwebassets\.endpoints)\.json|YamlDotNet\.dll|web\.config|wwwroot/(?:index\.html|control\.css)(?:\.(?:gz|br))?)')
    if not required.issubset(files) or any(not allowed.fullmatch(n) or p.is_symlink() for n,p in files.items()):
        raise ValueError('Expected only Central framework-dependent publish files: '+str(sorted(files)))

    def run(command):
        _, stdout, stderr = ssh.exec_command(command, timeout=60)
        result = stdout.read().decode().strip(); err = stderr.read().decode().strip()
        if stdout.channel.recv_exit_status(): raise RuntimeError(err or result or 'Remote operation failed')
        return result

    mounts = json.loads(run('docker inspect --format '+shlex.quote('{{json .Mounts}}')+' '+service))
    if {m['Destination']:m['Source'] for m in mounts if m['Type']=='bind'} != {'/app':app, '/app/data':data}:
        raise RuntimeError('Mounts changed; no update performed')
    for path in (app,app+'/wwwroot',data):
        if run('readlink -f '+path)!=path: raise RuntimeError('Unexpected linked path')
    run('test -f '+app+'/ClassNex.Central.dll && test -f '+data+'/central-state.json')
    stage = run('umask 077; mktemp -d /opt/classnex-central-release.XXXXXXXX')
    if not re.fullmatch(r'/opt/classnex-central-release\.[a-zA-Z0-9]{8}',stage): raise RuntimeError('Invalid staging directory')
    print('Rollback directory:',stage,flush=True)
    run('mkdir -p '+stage+'/new/wwwroot '+stage+'/previous/wwwroot '+stage+'/restore/wwwroot')
    previous=set()
    with ssh.open_sftp() as ftp:
        for n,p in files.items():
            dest=posixpath.join(app,n)
            if run('readlink -m '+dest)!=dest: raise RuntimeError('Unexpected linked file: '+n)
            candidate=stage+'/new/'+n
            ftp.put(str(p),candidate); ftp.chmod(candidate,0o644)
            if run('sha256sum '+candidate).split()[0]!=hashlib.sha256(p.read_bytes()).hexdigest(): raise RuntimeError('Hash mismatch: '+n)
            try: ftp.stat(dest)
            except FileNotFoundError: continue
            run('cp -p '+dest+' '+stage+'/previous/'+n);previous.add(n)
        def health_check():
            css=hashlib.sha256(files['wwwroot/control.css'].read_bytes()).hexdigest()
            for i in range(25):
                try:
                    run('set -eu; f=$(curl --fail --silent http://127.0.0.1:7010/control.css | sha256sum); test "${f%% *}" = '+css+'; test "$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:7010/api/v1/auth/me)" = 401');break
                except RuntimeError:
                    if i==24: raise
                    time.sleep(.5)
        _install(ftp, run, files, previous, app, stage, service, health_check)
    return stage
