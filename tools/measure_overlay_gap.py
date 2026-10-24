"""量出「主界面卡片」在截图中的位置与留白：我的 vs CI。"""
from PIL import Image

BASE = r"C:\Users\Lenovo\.dsh\attachments\v1\objects"
SHOTS = {
    "MINE(mine)":  BASE + r"\e0\e0228d8281f32356dab3b4585f8f29ed54d19f23cb09c1222c813ed2101a7860",
    "CI(ci)":      BASE + r"\63\633dd1ba197bc3296d6b75e6b9a5b405bf2eea931f70906e3b91d9aedeef190c",
}


def is_card(px):
    """卡片是深色半透明（RGB 各通道都偏暗且接近），壁纸是亮的青蓝色。"""
    r, g, b = px
    mx, mn = max(px), min(px)
    return mx < 145 and (mx - mn) < 45


for label, path in SHOTS.items():
    try:
        im = Image.open(path).convert("RGB")
    except Exception as e:
        print(f"!! {label}: {e}")
        continue

    w, h = im.size
    print(f"\n=== {label}  尺寸 {w}x{h} ===")

    # 只在顶部 1/4 区域找卡片（主界面在顶部）
    top_limit = int(h * 0.25)

    rows = []
    for y in range(0, top_limit):
        count = sum(1 for x in range(0, w, 4) if is_card(im.getpixel((x, y))))
        rows.append((y, count))

    card_rows = [y for y, c in rows if c > 20]
    if not card_rows:
        print("  未找到卡片")
        continue

    y_top, y_bottom = card_rows[0], card_rows[-1]

    # 卡片水平范围（取卡片中间那一行）
    mid = (y_top + y_bottom) // 2
    xs = [x for x in range(w) if is_card(im.getpixel((x, mid)))]
    if not xs:
        print("  未找到水平范围")
        continue

    x_left, x_right = xs[0], xs[-1]

    scale = 1917 / w
    print(f"  屏幕逻辑宽 1536 → 本图缩放 {scale:.3f}")
    print(f"  卡片 bbox: x {x_left}..{x_right}  y {y_top}..{y_bottom}")
    print(f"  卡片尺寸: {x_right - x_left + 1} x {y_bottom - y_top + 1} (图像像素)")
    print(f"  距屏幕顶部: {y_top} 图像像素")
    print(f"  距左 / 距右: {x_left} / {w - 1 - x_right}")
    print(f"  水平居中偏差: {abs(x_left - (w - 1 - x_right))} (越小越居中)")

    # 卡片内部：找第一个「非卡片色」像素 → 估算上下内边距
    inner_top = None
    for y in range(y_top, y_bottom):
        if not is_card(im.getpixel(((x_left + x_right) // 2, y))):
            inner_top = y
            break
    if inner_top:
        print(f"  上内边距(内容起始 - 卡片顶): {inner_top - y_top} 图像像素")
