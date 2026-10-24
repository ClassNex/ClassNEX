"""同口径对比：CI 截图 vs 我浮窗截图的文字带高度与字距。"""
from PIL import Image

CI = r"C:\Users\Lenovo\.dsh\attachments\v1\objects\63\633dd1ba197bc3296d6b75e6b9a5b405bf2eea931f70906e3b91d9aedeef190c"
MINE = r"E:\ClassNex\_tmp_mine.png"


def bright(px, th=150):
    r, g, b = px
    return max(r, g, b) > th


def analyze(path, label, x0, x1, y0, y1):
    im = Image.open(path).convert("RGB")
    print(f"\n=== {label}  ({im.size}) ===")

    # 1) 文字带：每行亮像素数
    band = []
    for y in range(y0, y1):
        cnt = sum(1 for x in range(x0, x1, 2) if bright(im.getpixel((x, y))))
        if cnt > 4:
            band.append(y)
    if band:
        top, bot = band[0], band[-1]
        print(f"  文字带 y {top}..{bot}  高度 {bot - top + 1} 物理px")
    else:
        print("  没找到文字带")

    # 2) 字符簇（在文字带中段一行上找）
    mid = (band[0] + band[-1]) // 2 if band else (y0 + y1) // 2
    clusters = []
    start = None
    for x in range(x0, x1):
        c = sum(1 for dy in range(-4, 5) if bright(im.getpixel((x, mid + dy))))
        if c >= 1 and start is None:
            start = x
        elif c < 1 and start is not None:
            clusters.append((start, x - 1))
            start = None
    if start is not None:
        clusters.append((start, x1 - 1))

    if clusters:
        prev_end = None
        for a, b in clusters:
            w = b - a + 1
            gap = f"间隙 {a - prev_end}" if prev_end is not None else "-"
            print(f"  簇 x {a:4d}..{b:4d}  宽 {w:3d}   {gap}")
            prev_end = b
    return im.size


# CI：卡片 x136..1782
analyze(CI, "CI 图1", 136, 1783, 0, 110)
# 我：窗口截图全宽（已按物理像素）
analyze(MINE, "我的浮窗", 0, 1389, 0, 104)
