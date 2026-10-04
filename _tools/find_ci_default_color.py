"""在 CI 程序集里定位 #00BFFF 等色值的上下文，判断哪个是「默认强调色」。"""
import re, os

BASE = r"E:\ClassNex\ClassIsland_app_windows_x64_full_folder (2)\app-2.1.0.1-0"
KEYS = ["#00BFFF", "#589499", "#426E71", "#7FFFD4", "#1E90FF"]


def printable(bs):
    out = []
    for b in bs:
        out.append(chr(b) if 32 <= b < 127 else ".")
    return "".join(out)


def u16_printable(bs):
    out = []
    for i in range(0, len(bs) - 1, 2):
        lo, hi = bs[i], bs[i + 1]
        out.append(chr(lo) if hi == 0 and 32 <= lo < 127 else ".")
    return "".join(out)


for dll in ["ClassIsland.dll", "ClassIsland.Core.dll"]:
    path = os.path.join(BASE, dll)
    if not os.path.exists(path):
        continue
    data = open(path, "rb").read()
    print(f"\n########## {dll} ({len(data)} bytes) ##########")

    for key in KEYS:
        ka = key.encode("ascii")
        ku = key.encode("utf-16-le")

        for tag, pat in (("ASCII", ka), ("UTF16", ku)):
            start = 0
            hits = 0
            while True:
                i = data.find(pat, start)
                if i < 0 or hits >= 3:
                    break
                hits += 1
                start = i + 1
                lo, hi = max(0, i - 160), min(len(data), i + 160)
                chunk = data[lo:hi]
                ctx = u16_printable(chunk) if tag == "UTF16" else printable(chunk)
                ctx = re.sub(r"\.{4,}", " .. ", ctx)
                print(f"\n[{key}] {tag} @0x{i:X}")
                print(f"   {ctx}")
            if hits:
                break
