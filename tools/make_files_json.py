"""按 CI 发布包格式生成 files.json（每文件 SHA512 清单）。用法：make_files_json.py <发布根目录> <版本> [序号]"""
import base64
import hashlib
import json
import os
import sys


def sha512_b64(path):
    digest = hashlib.sha512()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            digest.update(chunk)
    return base64.b64encode(digest.digest()).decode()


def build_manifest(release_root, version, number):
    app_dir = os.path.join(release_root, f"app-{version}-{number}")
    files = {}
    for dirpath, _, names in os.walk(app_dir):
        for name in names:
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, app_dir).replace("\\", "/")
            files[rel] = {
                "ArchiveDownloadUrl": "",
                "FileSha512": sha512_b64(full),
                "ArchiveSha512": "",
            }

    launcher = os.path.join(release_root, "ClassNex.exe")
    return {
        "Components": {
            "app": {
                "Files": files,
                "AllowDiffUpdate": True,
                "Root": "app-$(version)-$(number)/",
            },
            "launcher": {
                "Files": {
                    "ClassNex.exe": {
                        "ArchiveDownloadUrl": "",
                        "FileSha512": sha512_b64(launcher),
                        "ArchiveSha512": "",
                    }
                },
                "AllowDiffUpdate": False,
                "Root": "",
            },
        },
        "Variables": {"number": number, "version": version, "thisFileDir": ""},
        "ArchiveUrl": "",
        "ArchiveSha512": "",
    }


def main():
    release_root = sys.argv[1]
    version = sys.argv[2]
    number = sys.argv[3] if len(sys.argv) > 3 else "0"
    manifest = build_manifest(release_root, version, number)
    target = os.path.join(release_root, "files.json")
    with open(target, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    print(f"files.json written: {len(manifest['Components']['app']['Files'])} app files")


if __name__ == "__main__":
    main()
