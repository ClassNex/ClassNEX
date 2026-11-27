"""Read-only post-deployment checks through the public TLS front door; no user or device credentials."""
import argparse
import hashlib
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args):
        return None


def check(published, base):
    public = base.rstrip('/')
    origin = urlsplit(public)
    if origin.scheme != 'https' or not origin.hostname or origin.username or origin.password or origin.query or origin.fragment:
        raise ValueError('Expected a public HTTPS URL without credentials')
    opener = build_opener(NoRedirect)

    def read(path, status):
        req = Request(public + path, headers={'Accept-Encoding': 'identity', 'User-Agent': 'ClassNEX-Deployment-Check'})
        try:
            response = opener.open(req, timeout=15)
        except HTTPError as exc:
            response = exc
        with response:
            if response.status != status:
                raise RuntimeError(f'{path}: expected HTTP {status}, got {response.status}')
            return response.read(2_000_000), response.headers

    for path, name in [('/', 'index.html'), ('/control.css', 'control.css')]:
        live, _ = read(path, 200)
        if hashlib.sha256(live).digest() != hashlib.sha256((published/'wwwroot'/name).read_bytes()).digest():
            raise RuntimeError(f'{name}: public content differs from the release')
        print(name, '200; SHA-256 matches')
    for path in ('/api/v1/auth/me', '/api/v1/auth/profile', '/api/v1/classes', '/api/v1/pairing/status?clientId=unregistered'):
        read(path, 401)
        print(path.split('?')[0], '401 (anonymous)')
    _, headers = read('/auth/login', 302)
    authorize = urlsplit(headers.get('Location', ''))
    params = parse_qs(authorize.query)
    if (authorize.scheme != 'https' or not authorize.hostname or params.get('redirect_uri') != [public+'/auth/callback'] or
            params.get('code_challenge_method') != ['S256'] or not params.get('state') or not params.get('code_challenge')):
        raise RuntimeError('Public login callback or PKCE does not match the deployment')
    print('OAuth 302:', authorize.hostname, '→', public+'/auth/callback', '(S256)')


if __name__ == '__main__':
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('published', type=Path)
    cli.add_argument('--base', default='https://classnex.tech/Control')
    opts = cli.parse_args()
    check(opts.published.resolve(strict=True), opts.base)
