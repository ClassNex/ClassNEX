"""CI 截图：科目区底部结构 —— 看每个科目字符下方有没有小字（时间标签）。"""
from PIL import Image

CI_PATH = r"C:\Users\Lenovo\.dsh\attachments\v1\objects\63\633dd1ba197bc3296d6b75e6b9a5b405bf2eea931f70906e3b91d9aedeef190c"
im = Image.open(CI_PATH).convert("RGB")


def ch(px):
    v = max(px)
    if v < 100: return " "
    if v < 130: return "."
    if v < 150: return ":"
    if v < 170: return "+"
    if v < 190: return "*"
    if v < 215: return "#"
    return "@"


# 两个科目字符 + 中间空隙：x680..830（早 语 之间），y46..72
print("科目区 x680..830, y46..72（每格 2px）")
for y in range(46, 73):
    line = "".join(ch(im.getpixel((x, y))) for x in range(680, 831, 2))
    print(f"y={y:3d} {line}")
print()
print("另一个区域 x1080..1230（自 物 之间）")
for y in range(46, 73):
    line = "".join(ch(im.getpixel((x, y))) for x in range(1080, 1231, 2))
    print(f"y={y:3d} {line}")
