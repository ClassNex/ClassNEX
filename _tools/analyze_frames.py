"""对比：录屏帧 vs 当前屏幕的顶部区域文字（白字@ 亮灰* 深色#）。"""
from PIL import Image


def ch(px):
    r, g, b = px
    v = max(r, g, b)
    if v >= 200: return "@"
    if v >= 165: return "*"
    if v >= 110: return " "
    return "#"


def show(path, label):
    im = Image.open(path).convert("RGB")
    w, h = im.size
    print(f"\n===== {label} ({w}x{h}) 顶部文字层 =====")
    for y in range(12, 90, 4):
        line = "".join(ch(im.getpixel((x, y))) for x in range(0, w, 3))
        print(f"y={y:3d} {line}")


show(r"E:\ClassNex\_tools\frames\frame_1_1.3s.png", "录屏帧1")
show(r"E:\ClassNex\_tmp_now.png", "当前屏幕")
