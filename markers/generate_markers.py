"""
マーカー画像（M01〜）と印刷用PDFを作るスクリプト。

使い方（Pillowが必要）:
    python3 markers/generate_markers.py M01

・Unity/Assets/HomeCare/Markers/M01.png … アプリに入れる画像
・markers/M01-print.pdf … 印刷用（A4、10cm四方、等倍で印刷する）

絵柄は番号から決まる乱数で作るので、何度作り直しても同じ画像になる。
ARが見分けやすいよう、大きさの違う図形を不規則に重ねて、角や模様を多くしている。
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
UNITY_DIR = ROOT / "Unity" / "Assets" / "HomeCare" / "Markers"
PRINT_DIR = ROOT / "markers"

SIZE_PX = 1024          # アプリに入れる画像の大きさ
MARKER_MM = 100         # 印刷したときの大きさ（10cm）
DPI = 300               # 印刷用PDFの解像度
FONT = "/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf"


def draw_pattern(marker_id: str) -> Image.Image:
    rng = random.Random(f"homecare-{marker_id}")
    img = Image.new("L", (SIZE_PX, SIZE_PX), 255)
    d = ImageDraw.Draw(img)
    # 大きい図形から小さい図形の順に重ねる
    for size, count in ((360, 14), (180, 40), (90, 110), (40, 260)):
        for _ in range(count):
            cx, cy = rng.uniform(0, SIZE_PX), rng.uniform(0, SIZE_PX)
            r = rng.uniform(0.4, 1.0) * size / 2
            shade = rng.choice((0, 0, 0, 60, 130, 200, 255, 255))
            kind = rng.random()
            if kind < 0.45:
                # 不規則な多角形（角を増やす）
                n = rng.randint(3, 6)
                start = rng.uniform(0, 2 * math.pi)
                pts = []
                for i in range(n):
                    a = start + 2 * math.pi * i / n + rng.uniform(-0.4, 0.4)
                    rr = r * rng.uniform(0.5, 1.0)
                    pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
                d.polygon(pts, fill=shade)
            elif kind < 0.75:
                d.ellipse((cx - r, cy - r * rng.uniform(0.5, 1), cx + r, cy + r), fill=shade)
            else:
                w = max(4, int(r * rng.uniform(0.1, 0.3)))
                a = rng.uniform(0, math.pi)
                d.line((cx - r * math.cos(a), cy - r * math.sin(a), cx + r * math.cos(a), cy + r * math.sin(a)),
                       fill=shade, width=w)
    # 上下左右の区別がつくよう、左上だけに黒い四角を置く（印刷時の向きの目印にもなる）
    d.rectangle((0, 0, SIZE_PX * 0.12, SIZE_PX * 0.12), fill=0)
    d.rectangle((SIZE_PX * 0.03, SIZE_PX * 0.03, SIZE_PX * 0.09, SIZE_PX * 0.09), fill=255)
    return img


def make_print_pdf(marker_id: str, pattern: Image.Image, path: Path) -> None:
    mm = DPI / 25.4
    page = Image.new("L", (round(210 * mm), round(297 * mm)), 255)
    d = ImageDraw.Draw(page)
    side = round(MARKER_MM * mm)
    left, top = (page.width - side) // 2, round(40 * mm)
    page.paste(pattern.resize((side, side), Image.LANCZOS), (left, top))
    # 切り取り線の目安（細い枠）
    d.rectangle((left - 1, top - 1, left + side, top + side), outline=180, width=2)

    big = ImageFont.truetype(FONT, round(9 * mm))
    small = ImageFont.truetype(FONT, round(4 * mm))
    d.text((left, top + side + round(6 * mm)), marker_id, font=big, fill=0)
    # 10cmの線：印刷後に定規で測って、10cmになっていれば正しい大きさ
    y = top + side + round(24 * mm)
    d.line((left, y, left + side, y), fill=0, width=4)
    for x in (left, left + side):
        d.line((x, y - round(2 * mm), x, y + round(2 * mm)), fill=0, width=4)
    lines = [
        "この線が10cmになっていることを定規で確かめてください。",
        "印刷するときは「実際のサイズ（100%）」を選び、拡大・縮小しないでください。",
        "光沢のない紙に印刷し、折り曲げずに平らに貼ってください。",
        "黒い四角の目印が左上になる向きで貼ります。",
    ]
    for i, text in enumerate(lines):
        d.text((round(20 * mm), y + round((6 + 7 * i) * mm)), text, font=small, fill=0)
    page.save(path, "PDF", resolution=DPI)


def main(ids):
    UNITY_DIR.mkdir(parents=True, exist_ok=True)
    for marker_id in ids:
        pattern = draw_pattern(marker_id)
        # 白黒1チャンネルの画像はUnityが透明度として読み込むことがあるので、RGBで保存する
        pattern.convert("RGB").save(UNITY_DIR / f"{marker_id}.png", optimize=True)
        make_print_pdf(marker_id, pattern, PRINT_DIR / f"{marker_id}-print.pdf")
        print(f"{marker_id}: 作成しました")


if __name__ == "__main__":
    main(sys.argv[1:] or ["M01"])
