"""生成 ClassNex 应用图标（日历样式）为多尺寸 .ico。"""
from PIL import Image, ImageDraw

SIZE = 256
img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 圆角方形底（蓝色）
d.rounded_rectangle([6, 6, SIZE - 6, SIZE - 6], radius=54, fill=(0, 120, 212, 255))

# 顶部装订条
d.rounded_rectangle([52, 40, 116, 76], radius=10, fill=(255, 255, 255, 255))
d.rounded_rectangle([140, 40, 204, 76], radius=10, fill=(255, 255, 255, 255))

# 日历标题栏
d.rounded_rectangle([38, 88, SIZE - 38, 132], radius=14, fill=(255, 255, 255, 255))

# 网格（2 行 3 列）
for row in range(2):
    for col in range(3):
        x = 42 + col * 60
        y = 150 + row * 52
        d.rounded_rectangle([x, y, x + 46, y + 36], radius=8, fill=(255, 255, 255, 235))

out = r"E:\ClassNex\src\ClassNex\Assets\icon.ico"
img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("saved:", out)
