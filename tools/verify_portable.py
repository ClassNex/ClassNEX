import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import struct
import xml.etree.ElementTree as ET
from zipfile import ZipFile


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def verify_executable(image, expected_version=None):
    def word(offset):
        return struct.unpack_from("<H", image, offset)[0]

    def integer(offset):
        return struct.unpack_from("<I", image, offset)[0]

    require(image[:2] == b"MZ", "Not a Windows executable")
    pe = integer(0x3C)
    require(image[pe:pe + 4] == b"PE\0\0" and word(pe + 4) == 0x8664, "Expected Windows x64")
    optional = pe + 24
    require(word(optional) == 0x20B and word(optional + 68) == 2, "Expected a GUI executable, not a console program")
    sections = optional + word(pe + 20)

    def locate(rva):
        for i in range(word(pe + 6)):
            virtual_size, address, size, raw = struct.unpack_from("<IIII", image, sections + i * 40 + 8)
            if address <= rva < address + max(virtual_size, size) and rva - address < size:
                return raw + rva - address
        raise ValueError("Resource address outside executable sections")

    root = locate(integer(optional + 128))

    def entries(offset):
        count = word(offset + 12) + word(offset + 14)
        return dict(struct.unpack_from("<II", image, offset + 16 + i * 8) for i in range(count))

    types = entries(root)

    def resource(kind):
        target = types[kind]
        for _ in range(2):
            require(target & 0x80000000, "Missing PE resource directory")
            target = next(iter(entries(root + (target & 0x7FFFFFFF)).values()))
        require(not target & 0x80000000, "Missing PE resource data")
        rva, size = struct.unpack_from("<II", image, root + target)
        offset = locate(rva)
        return image[offset:offset + size]

    require({3, 14, 24} <= types.keys(), "Missing embedded icon or application manifest")
    reserved, kind, icon_count = struct.unpack_from("<HHH", resource(14))
    require(reserved == 0 and kind == 1 and icon_count > 0 and resource(3), "Empty executable icon")
    manifest = ET.fromstring(resource(24).rstrip(b"\0"))
    execution = [n for n in manifest.iter() if n.tag.rsplit("}", 1)[-1] == "requestedExecutionLevel"]
    require(len(execution) == 1 and execution[0].get("level") == "asInvoker" and execution[0].get("uiAccess") == "false",
            "Executable requests privilege elevation")
    result = {"machine": "x64", "subsystem": "Windows GUI", "iconImages": icon_count, "executionLevel": "asInvoker"}
    if expected_version is not None:
        require(16 in types, "Missing executable version resource")
        info = resource(16)
        versions = set()
        for key in re.finditer(re.escape("ProductVersion\0".encode("utf-16le")), info):
            start = key.start() - 6
            if start < 0 or start % 4:
                continue
            length, chars, value_type = struct.unpack_from("<HHH", info, start)
            value = (key.end() + 3) & ~3
            end = value + chars * 2
            if value_type == 1 and chars and end <= start + length <= len(info):
                versions.add(info[value:end].decode("utf-16le").rstrip("\0"))
        require(versions == {expected_version}, f"Unexpected executable ProductVersion: {sorted(versions)}; expected {expected_version}")
        result["productVersion"] = expected_version
    return result


def inspect(path, release_version=None):
    with ZipFile(path) as archive:
        entries = archive.infolist()
        seen = set()
        for entry in entries:
            name = entry.filename.rstrip("/")
            pieces = name.casefold().split("/")
            require(not re.search(r'[<>:"\\|?*\x00-\x1f]', name) and
                    all(p and not p.endswith((".", " ")) and not re.fullmatch(r"(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", p) for p in pieces),
                    "Unsafe Windows archive path")
            require(name.casefold() not in seen and (entry.external_attr >> 16) & 0o170000 != 0o120000,
                    "Archive contains a duplicate path or symlink")
            require(not {"data", "plugins", "logs", ".git", ".classnex-review"} & set(pieces) and
                    pieces[-1] not in {"timetable.yaml", "settings.json", "account.json", "account_avatar.png", "crash.log"}, "User or review data in archive")
            seen.add(name.casefold())
        files = {e.filename for e in entries if not e.is_dir()}
        require(archive.read("PackageType") == b"folder", "PackageType must contain exactly b'folder' without BOM or newline")
        manifest = json.loads(archive.read("files.json"))
        components = manifest["Components"]
        require(set(components) == {"app", "launcher"}, "Unexpected package components")
        version, number = (manifest["Variables"][k] for k in ("version", "number"))
        require(re.fullmatch(r"[a-zA-Z0-9_.-]+", version) and str(number).isdecimal(), "Invalid package version or number")
        if release_version is not None:
            require(version == release_version and number == "0", f"Expected release {release_version}, package number 0")
            require(path.name == f"ClassNEX-{release_version}-Alpha-win-x64.zip", "Unexpected release ZIP filename")
        payload = f"app-{version}-{number}"
        roots = {e.filename.rstrip("/").split("/")[0] for e in entries}
        require(roots == {payload, "ClassNex.exe", "PackageType", "files.json"}, "Expected exactly the four portable root entries")
        app, launcher = components["app"], components["launcher"]
        require(app["Root"] == "app-$(version)-$(number)/" and app["AllowDiffUpdate"] is True and
                launcher["Root"] == "" and launcher["AllowDiffUpdate"] is False and set(launcher["Files"]) == {"ClassNex.exe"}, "Unexpected manifest component roots")
        mapped = {f"{payload}/{name}": record for name, record in app["Files"].items()} | launcher["Files"]
        require(set(mapped) == files - {"PackageType", "files.json"}, "SHA512 manifest and archive contain different files")
        for name, record in mapped.items():
            with archive.open(name) as stream:
                digest = hashlib.file_digest(stream, "sha512").digest()
            require(base64.b64encode(digest).decode("ascii") == record["FileSha512"], f"SHA512 mismatch: {name}")
        # All expected payload names are ASCII. Compare their casing as on Windows, not Unicode lookalikes.
        app_paths = {name.lower(): f"{payload}/{name}" for name in app["Files"] if name.isascii()}
        required = {"ClassNex.exe", "ClassNex.dll", "ClassNex.runtimeconfig.json", "ClassNex.Core.dll", "ClassNex.Plugin.dll", "ClassNex.Notification.Service.dll",
                    "ClassNex.AI.dll", "Avalonia.dll", "AvaloniaFluentUI.dll", "coreclr.dll", "hostfxr.dll", "hostpolicy.dll", "System.Private.CoreLib.dll",
                    "ClassNex.WinUI.dll", "ClassNex.pri", "Licenses/ClassNex.txt", "Licenses/lucide-LICENSE.txt", "Licenses/HarmonyOS-Sans-LICENSE.txt"}
        if release_version is not None:
            required.update({"Licenses/Fluent-LICENSE.txt", "Microsoft.UI.Xaml.dll", "Assets/icon.ico", "Assets/Fonts/HarmonyOS_Sans_SC_Regular.ttf"})
        else:
            required.add("BuildInfo.json")
        missing = sorted(name for name in required if name.lower() not in app_paths)
        require(not missing, f"Incomplete self-contained desktop payload: {missing}")
        require(app_paths.keys().isdisjoint({"classnex.notification.dll", "classnex.plugins.dll", "classnex.central.dll"}), "Obsolete or server-only assembly in desktop payload")
        runtime = json.loads(archive.read(app_paths["classnex.runtimeconfig.json"]))["runtimeOptions"]
        frameworks = {i["name"]: i["version"] for i in runtime.get("includedFrameworks", [])}
        require("Microsoft.NETCore.App" in frameworks and "frameworks" not in runtime and "framework" not in runtime, "Runtime is not self-contained")
        provenance = {}
        if release_version is None:
            build = json.loads(archive.read(app_paths["buildinfo.json"]))
            require(build["kind"] == "refactor-candidate" and re.fullmatch(r"[0-9a-f]{40}", build["sourceCommit"]) and
                    build["baseCommit"] == "60d7b575b1b866a0bc388c9b80986c546950a2c7", "Invalid candidate provenance")
            provenance = {"sourceCommit": build["sourceCommit"], "baseCommit": build["baseCommit"]}
        else:
            require("buildinfo.json" not in app_paths, "Release contains candidate provenance")
            repo = Path(__file__).resolve().parents[1]
            assets = repo / "src/ClassNex/Assets"
            licenses = {"ClassNex.txt": repo / "LICENSE", "lucide-LICENSE.txt": assets / "Fonts/lucide-LICENSE.txt",
                        "HarmonyOS-Sans-LICENSE.txt": assets / "Fonts/HarmonyOS-Sans-LICENSE.txt", "Fluent-LICENSE.txt": assets / "SettingsIcons/Fluent-LICENSE.txt"}
            for name, source in licenses.items():
                actual = archive.read(app_paths[f"licenses/{name.lower()}"])
                require(actual and actual.replace(b"\r\n", b"\n") == source.read_bytes().replace(b"\r\n", b"\n"), f"Wrong license: {name}")
        executables = {name: verify_executable(archive.read(name), release_version) for name in ("ClassNex.exe", app_paths["classnex.exe"])}
        payload_bytes = sum(archive.getinfo(name).file_size for name in mapped if name.startswith(payload + "/"))
    with path.open("rb") as stream:
        archive_hash = hashlib.file_digest(stream, "sha512").hexdigest()
    return {"archive": str(path.resolve()), "archiveBytes": path.stat().st_size, "archiveSha512": archive_hash,
            "version": version, **provenance, "payload": payload, "payloadFiles": len(app["Files"]), "payloadBytes": payload_bytes,
            "filesHashVerified": len(mapped), "includedFrameworks": frameworks, "executables": executables}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Verify a ClassNEX portable ZIP; defaults to the refactor candidate format. No extraction or network access.")
    parser.add_argument("archive", type=Path)
    parser.add_argument("--release-version", help="Verify a release ZIP against the expected desktop version instead of a candidate commit.")
    args = parser.parse_args()
    print(json.dumps(inspect(args.archive, args.release_version), ensure_ascii=False, indent=2))
