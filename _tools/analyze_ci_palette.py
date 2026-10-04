"""分析 CI 截图的整体配色：最常见色 + 最高饱和色（强调色候选）。"""
from PIL import Image
from collections import Counter
import colorsys

BASE = r"C:\Users\Lenovo\.dsh\attachments\v1\objects"
IMGS = {
    "shot1_main":     BASE + r"\63\633dd1ba197bc3296d6b75e6b9a5b405bf2eea931f70906e3b91d9aedeef190c",
    "shot2_settings": BASE + r"\f8\f812779a3cac3c42be108440ee593deb1163738936628d5a03cd2a713344ff39",
    "shot3_classplan":BASE + r"\89\8965c6a3877548e30d112e2ac935476f04039dca6ec9619e272dfdc6f074fef8",
    "shot4_timeline": BASE + r"\9f\9fead8eec47940f3499bc00fd296d84c46988d95be1ca93c55af184d4d929517",
    "shot5_subjects": BASE + r"\43\43e2c2fa050866de9aa9e746e752935c2c0be5736606d9e18fbb398ae097c5e3",
}


def hexof(c):
    return "#{:02X}{:02X}{:02X}".format(*c)


for key, path in IMGS.items():
    try:
        im = Image.open(path).convert("RGB")
    except Exception as e:
        print(f"!! {key}: {e}")
        continue

    im = im.resize((im.width // 2, im.height // 2))  # 降采样加速
    px = list(im.getdata())

    print(f"\n=== {key} ===")
    print("  top-common:")
    for c, n in Counter(px).most_common(10):
        print(f"    {hexof(c)}  x{n}")

    # 饱和色：去掉接近灰的（max-min 需足够大）且不要太暗/太亮
    sat = Counter()
    for c in px:
        r, g, b = c
        mx, mn = max(c), min(c)
        if mx == 0 or (mx - mn) < 30:
            continue
        h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
        if 0.15 < l < 0.90:
            sat[c] += 1

    print("  most-saturated:")
    for c, n in sat.most_common(10):
        r, g, b = c
        h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
        print(f"    {hexof(c)}  hue={h*360:6.1f}  sat={s:.2f}  light={l:.2f}  x{n}")
