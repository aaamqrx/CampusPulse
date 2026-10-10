"""Regenerate the original CampusPulse vector mark and Windows icon (Pillow)."""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "icon"
OUT.mkdir(parents=True, exist_ok=True)
SIZES = (16, 24, 32, 48, 64, 128, 256)
POINTS = ((48, 128), (88, 128), (102, 108), (124, 164), (149, 88), (173, 128), (208, 128))
svg_points = " ".join(f"{x},{y}" for x, y in POINTS)
(OUT / "campuspulse.svg").write_text(f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" role="img" aria-labelledby="title desc">
  <title id="title">CampusPulse</title>
  <desc id="desc">White network pulse between two connected nodes on a teal rounded square.</desc>
  <defs><linearGradient id="teal" x2="0" y2="1"><stop stop-color="#208d9e"/><stop offset="1" stop-color="#155565"/></linearGradient></defs>
  <rect x="8" y="8" width="240" height="240" rx="52" fill="url(#teal)"/>
  <polyline points="{svg_points}" fill="none" stroke="white" stroke-width="14" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="48" cy="128" r="12" fill="white"/><circle cx="208" cy="128" r="12" fill="white"/>
</svg>
''', encoding="utf-8")
scale = 8
size = 256 * scale
canvas = Image.new("RGBA", (size, size))
mask = Image.new("L", (size, size))
ImageDraw.Draw(mask).rounded_rectangle((8*scale, 8*scale, 248*scale, 248*scale), radius=52*scale, fill=255)
gradient = Image.new("RGBA", (size, size))
draw = ImageDraw.Draw(gradient)
for y in range(size):
    t = min(1, max(0, (y / scale - 8) / 240))
    color = tuple(round(a*(1-t)+b*t) for a,b in zip((32,141,158), (21,85,101))) + (255,)
    draw.line((0,y,size,y), fill=color)
canvas.paste(gradient, (0,0), mask)
draw = ImageDraw.Draw(canvas)
draw.line([(x*scale,y*scale) for x,y in POINTS], fill="white", width=14*scale, joint="curve")
for x,y in POINTS:
    r = (12 if x in (48,208) else 7)*scale
    draw.ellipse((x*scale-r,y*scale-r,x*scale+r,y*scale+r), fill="white")
master = canvas.resize((1024,1024), Image.Resampling.LANCZOS)
master.save(OUT / "campuspulse.png")
master.save(OUT / "campuspulse.ico", sizes=[(s,s) for s in SIZES])
preview = Image.new("RGB", (780,360), "#f3f7f9")
pd = ImageDraw.Draw(preview)
pd.rectangle((0,180,780,360), fill="#182c3b")
for row in range(2):
    x = 12
    for s in SIZES:
        icon = master.resize((s,s), Image.Resampling.LANCZOS)
        if s > 128: icon = icon.resize((128,128), Image.Resampling.LANCZOS)
        preview.paste(icon, (x, row*180+16), icon)
        pd.text((x,row*180+153), str(s), fill="white" if row else "#182c3b")
        x += max(55,icon.width+16)
qa = ROOT / ".local" / "preview3"
qa.mkdir(parents=True, exist_ok=True)
preview.save(qa / "icon-review.png")
print("Generated SVG, PNG and ICO frames:", SIZES)
