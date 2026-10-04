"""从用户提供的 CI 运行截图里取实际像素色值。"""
from PIL import Image
from collections import Counter

BASE = r"C:\Users\Lenovo\.dsh\attachments\v1\objects"
IMGS = {
    "1-主界面":   BASE + r"\63\633dd1ba197bc3296d6b75e6b9a5b405bf2eea931f70906e3b91d9aedeef190c",
    "2-设置组件": BASE + r"\f8\f812779a3cac3c42be108440ee593deb1163738936628d5a03cd2a713344ff39",
    "3-课表编辑": BASE + r"\89\8965c6a3877548e30d112e2ac935476f04039dca6ec9619e272dfdc6f074fef8",
    "4-时间表":   BASE + r"\9f\9fead8eec47940f3499bc00fd296d84c46988d95be1ca93c55af184d4d929517",
    "5-科目":     BASE + r"\43\43e2c2fa050866de9aa9e746e752935c2c0be5736606d9e18fbb398ae097c5e3",
}

PREVIEW = (1708, 960)  # 我看到的预览尺寸，坐标按它估算

POINTS = {
    "1-主界面": [
        ("卡片底色-左", 600, 20), ("卡片底色-右", 1120, 20),
        ("进度条", 1300, 86), ("课程文字", 178, 42), ("当前课标题", 1265, 42),
    ],
    "2-设置组件": [
        ("左导航背景", 100, 760), ("导航选中项", 160, 481),
        ("标题下划线", 500, 274), ("内容背景", 900, 860), ("底部信息条", 900, 883),
    ],
    "3-课表编辑": [
        ("窗口背景", 820, 820), ("表头行", 300, 205),
        ("普通单元格", 430, 304), ("选中单元格", 372, 253),
        ("右侧选中科目", 843, 315), ("工具条文字", 300, 117),
    ],
    "4-时间表": [
        ("上课块(青)", 720, 210), ("课间块(灰)", 720, 403),
        ("右侧空白", 1700, 500), ("左侧列表背景", 100, 500), ("时间轴背景", 900, 850),
    ],
    "5-科目": [
        ("表头行", 300, 164), ("普通行", 590, 207),
        ("选中行", 590, 667), ("右侧面板背景", 1500, 400), ("窗口背景", 900, 860),
    ],
}


def region_dominant(im, cx, cy, half=6):
    """取中心周围方块里出现最多的颜色，避免取到文字边缘。"""
    w, h = im.size
    box = []
    for x in range(max(0, cx - half), min(w, cx + half + 1)):
        for y in range(max(0, cy - half), min(h, cy + half + 1)):
            box.append(im.getpixel((x, y)))
    if not box:
        return (0, 0, 0)
    return Counter(box).most_common(1)[0][0]


for key, path in IMGS.items():
    try:
        im = Image.open(path).convert("RGB")
    except Exception as e:
        print(f"!! {key}: 打不开 {e}")
        continue

    w, h = im.size
    sx, sy = w / PREVIEW[0], h / PREVIEW[1]
    print(f"\n=== {key}  ({w}x{h}, scale {sx:.3f},{sy:.3f}) ===")
    for name, px, py in POINTS.get(key, []):
        x, y = int(px * sx), int(py * sy)
        x, y = max(0, min(w - 1, x)), max(0, min(h - 1, y))
        r, g, b = im.getpixel((x, y))
        dr, dg, db = region_dominant(im, x, y)
        print(f"  {name:<14} 点(#{r:02X}{g:02X}{b:02X})  区域主色(#{dr:02X}{dg:02X}{db:02X})")
