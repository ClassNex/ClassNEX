"""重置开发数据：时间表清空（启动时按课表重建），并重新生成干净示例课表。"""
import io, json, subprocess, sys, os

SETTINGS = r"E:\ClassNex\src\ClassNex\bin\Debug\net8.0\data\Settings.json"
YAML_SRC = r"E:\ClassNex\src\ClassNex\Assets\timetable.yaml"
YAML_DST = r"E:\ClassNex\src\ClassNex\bin\Debug\net8.0\data\timetable.yaml"

# 1. 时间表清空（Times = []）→ 下次启动 EnsureFromProfile 会按课表重建
with io.open(SETTINGS, "r", encoding="utf-8") as f:
    s = json.load(f)
s["TimeLayout"] = {"Times": []}
with io.open(SETTINGS, "w", encoding="utf-8") as f:
    json.dump(s, f, ensure_ascii=False, indent=2)
print("TimeLayout 已清空（Times=[]，启动时自动重建）")

# 2. 重新生成示例课表并覆盖 data 副本
subprocess.run([sys.executable, r"E:\ClassNex\_tools\make_sample_timetable.py"], check=True)
with io.open(YAML_SRC, "r", encoding="utf-8") as f:
    content = f.read()
with io.open(YAML_DST, "w", encoding="utf-8", newline="\n") as f:
    f.write(content)
print("data/timetable.yaml 已覆盖为干净示例")

# 3. 校验
with io.open(YAML_DST, "r", encoding="utf-8") as f:
    text = f.read()
bad = [l for l in text.splitlines() if "start_time: 07:55:00" in l or "start_time: 08:45:00" in l]
print(f"残留垃圾课程: {len(bad)}")
print(f"晚自习 18:50 出现次数: {text.count('start_time: \"18:50:00\"')} (应为 7)")
