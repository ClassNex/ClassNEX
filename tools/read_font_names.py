"""读取 TTF 的 name 表，输出字体家族名（Avalonia 的 avares://...#Family 需要）。"""
import struct, sys

def read_name_table(path):
    with open(path, "rb") as f:
        data = f.read()

    tag = data[:4]
    num_tables = struct.unpack(">H", data[4:6])[0]
    name_offset = None
    for i in range(num_tables):
        off = 12 + i * 16
        t = data[off:off + 4]
        if t == b"name":
            name_offset = struct.unpack(">I", data[off + 8:off + 12])[0]
            break
    if name_offset is None:
        return {}

    fmt, count, str_off = struct.unpack(">HHH", data[name_offset:name_offset + 6])
    results = {}
    for i in range(count):
        rec = name_offset + 6 + i * 12
        pid, eid, lid, nid, length, offset = struct.unpack(">HHHHHH", data[rec:rec + 12])
        raw = data[name_offset + str_off + offset: name_offset + str_off + offset + length]
        try:
            if pid == 3 or (pid == 0):
                text = raw.decode("utf-16-be")
            else:
                text = raw.decode("latin-1")
        except Exception:
            continue
        results.setdefault(nid, text)
    return results


for p in sys.argv[1:]:
    names = read_name_table(p)
    print(f"=== {p.split(chr(92))[-1]} ===")
    for nid, label in ((1, "Family(1)"), (2, "Subfamily(2)"), (4, "FullName(4)"),
                       (6, "PostScript(6)"), (16, "TypographicFamily(16)"), (17, "TypographicSubfamily(17)")):
        if nid in names:
            print(f"  {label:24s} = {names[nid]}")
