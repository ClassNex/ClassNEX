"""按用户 CI 截图（图1/图3/图5）还原课表，生成 CSES 示例文件。

时间点取自图4；科目/教师取自图5；周一~周六格子取自图3；周日取自图1。
"""
import io, os

OUT = r"E:\ClassNex\src\ClassNex\Assets\timetable.yaml"

# 图5 科目表：名称 / 简称 / 教师
SUBJECTS = [
    ("语文", "语", "杨媛"), ("数学", "数", "林巧婷"), ("英语", "英", None),
    ("历史", "历", "张凌毅"), ("政治", "政", None), ("物理", "物", None),
    ("化学", "化", None), ("生物", "生", None), ("地理", "地", None),
    ("信息技术", "信", None), ("体育", "体", None), ("自习", "自", None),
    ("通用技术", "技", None), ("音乐", "音", None), ("美术", "美", None),
    ("选修课", "选", None), ("社团", "社", None), ("心理", "心", None),
    ("早读", "早", None), ("班会", "班", None), ("午辅导", "午", None),
    ("晚辅导", "晚", None), ("校本课程", "校", None), ("晚自习", "晚自习", None),
]

# 图4 时间点
TP = [
    ("07:15:00", "07:55:00"), ("08:00:00", "08:45:00"), ("09:00:00", "09:45:00"),
    ("10:20:00", "11:05:00"), ("11:20:00", "12:00:00"), ("12:40:00", "13:20:00"),
    ("14:15:00", "15:00:00"), ("15:15:00", "16:00:00"), ("16:15:00", "17:00:00"),
    ("17:15:00", "18:30:00"), ("18:50:00", "21:35:00"),
]

# 每天 11 格（与 TP 一一对应）；周一到周六取自图3，周日取自图1
WEEK = {
    1: ["早读", "语文", "数学", "历史", "历史", "政治", "自习", "物理", "物理", "选修课", "晚自习"],
    2: ["早读", "英语", "语文", "体育", "数学", "历史", "生物", "音乐", "生物", "晚辅导", "晚自习"],
    3: ["早读", "地理", "英语", "美术", "数学", "数学", "物理", "语文", "政治", "晚辅导", "晚自习"],
    4: ["早读", "数学", "物理", "语文", "历史", "午辅导", "英语", "生物", "班会", "校本课程", "晚自习"],
    5: ["早读", "英语", "数学", "体育", "语文", "午辅导", "物理", "政治", "历史", "晚辅导", "晚自习"],
    6: ["早读", "地理", "语文", "英语", "体育", "午辅导", "数学", "政治", "生物", "晚辅导", "晚自习"],
    7: ["早读", "语文", "数学", "历史", "历史", "政治", "自习", "物理", "生物", "选修课", "晚自习"],
}
DAY_NAME = {1: "周一", 2: "周二", 3: "周三", 4: "周四", 5: "周五", 6: "周六", 7: "周日"}

lines = []
lines.append("version: 1")
lines.append("")
lines.append("# 周课表示例（按 CI 截图还原；时间点取自 CI「时间表」，科目取自 CI「科目」）")
lines.append("subjects:")
for name, short, teacher in SUBJECTS:
    lines.append(f"  - name: {name}")
    lines.append(f"    simplified_name: {short}")
    if teacher:
        lines.append(f"    teacher: {teacher}")
lines.append("")
lines.append("schedules:")

for day in range(1, 8):
    lines.append(f"  - name: {DAY_NAME[day]}")
    lines.append(f"    enable_day: {day}")
    lines.append("    weeks: all")
    lines.append("    classes:")
    for (start, end), subj in zip(TP, WEEK[day]):
        lines.append(f"      - subject: {subj}")
        lines.append(f'        start_time: "{start}"')
        lines.append(f'        end_time: "{end}"')

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(lines) + "\n")

print("written:", OUT)
print("subjects:", len(SUBJECTS), " days:", len(WEEK), " classes/day:", len(TP))
