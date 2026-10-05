"""分析用户录屏帧：整体布局 + 找出变化区域。"""
import glob
from PIL import Image

FRAMES = sorted(glob.glob(r"E:\ClassNex\_tools\frames2\frame_*.png"))


def ch(px):
    r, g, b = px
    v = max(r, g, b)
    if v >= 200: return "@"
    if v >= 165: return "*"
    if v >= 120: return "+"
    if v >= 70: return "."
    return " "


print("=== 各帧尺寸与整体亮度分布 ===")
for p in FRAMES:
    im = Image.open(p).convert("RGB")
    print(f"  {p.split(chr(92))[-1]}  {im.size}")

im1 = Image.open(FRAMES[0]).convert("RGB")
w, h = im1.size
print(f"\n=== 第1帧 整体布局（每 16px 采样）===")
for y in range(0, h, 24):
    line = "".join(ch(im1.getpixel((x, y))) for x in range(0, w, 12))
    print(f"y={y:4d} {line}")

# 帧间差异：找变化最大的区域
print("\n=== 帧间差异（每帧与第1帧比较，统计变化像素块的 y 范围）===")
base = im1
for p in FRAMES[1:]:
    im = Image.open(p).convert("RGB")
    changed = []
    for y in range(0, h, 8):
        cnt = 0
        for x in range(0, w, 8):
            a = base.getpixel((x, y)); b = im.getpixel((x, y))
            if abs(a[0]-b[0]) + abs(a[1]-b[1]) + abs(a[2]-b[2]) > 60:
                cnt += 1
        if cnt > 0:
            changed.append((y, cnt))
    if changed:
        ys = [c[0] for c in changed]
        print(f"  {p.split(chr(92))[-1]}: 变化行 y {min(ys)}..{max(ys)}，共 {len(changed)} 行")
    else:
        print(f"  {p.split(chr(92))[-1]}: 无变化")
