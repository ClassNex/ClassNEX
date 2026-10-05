"""通过 GitHub REST API 创建/更新 ClassNEX 的预发布并上传 zip 资产。
用法: python gh_release.py <zip路径>
凭据从环境变量 GITHUB_TOKEN 读取；代理走 HTTPS_PROXY；
标签/标题从 GH_TAG / GH_TITLE 读取（缺省 Alpha / ClassNEX 26w41a_Alpha）。
"""
import json, os, sys, urllib.request

TOKEN = os.environ["GITHUB_TOKEN"]
ZIP = sys.argv[1]
ZIP_NAME = os.path.basename(ZIP)
TAG = os.environ.get("GH_TAG", "Alpha")
TITLE = os.environ.get("GH_TITLE", "ClassNEX 26w41a_Alpha")
BODY = open(r"E:\ClassNex\RELEASE_NOTES.md", encoding="utf-8").read()
API = "https://api.github.com/repos/ClassNex/ClassNEX/releases"


def call(url, method="GET", data=None, headers=None, timeout=1800):
    req = urllib.request.Request(url, data=data, headers=headers or {}, method=method)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        raw = resp.read()
        try:
            return json.loads(raw) if raw else None
        except Exception:
            return None


auth = {
    "Authorization": f"Bearer {TOKEN}",
    "Accept": "application/vnd.github+json",
}

# 1) 若已存在同 tag 的 release，先删除（同一 tag 不能重复发布）
try:
    old = call(API + f"/tags/{TAG}", headers=auth, timeout=60)
    if old and "id" in old:
        call(f"{API}/{old['id']}", method="DELETE", headers=auth, timeout=60)
        print(f"已删除旧 release #{old['id']}")
except Exception as e:
    print("查询旧 release 失败（忽略）:", e)

# 2) 创建预发布
payload = {
    "tag_name": TAG,
    "name": TITLE,
    "body": BODY,
    "prerelease": True,
    "draft": False,
}
rel = call(API, method="POST",
           data=json.dumps(payload).encode("utf-8"),
           headers={**auth, "Content-Type": "application/json"}, timeout=120)
print("release:", rel["id"], rel["html_url"])

# 3) 上传 zip 资产
upload_url = f"https://uploads.github.com/repos/ClassNex/ClassNEX/releases/{rel['id']}/assets?name={ZIP_NAME}"
with open(ZIP, "rb") as f:
    data = f.read()
asset = call(upload_url, method="POST", data=data,
             headers={**auth, "Content-Type": "application/zip"})
print("asset:", asset["browser_download_url"])

