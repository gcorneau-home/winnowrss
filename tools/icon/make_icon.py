"""Generates the WinnowRSS icon: a W with an RSS dot and two waves above its middle peak.

The geometry below is the single source, on a 256x256 grid. It is written out as SVG and
rendered to PNG and ICO (each size drawn separately, with slightly heavier strokes when small).

Usage (from the repository root, needs Pillow): python tools/icon/make_icon.py
Outputs: assets/icon/winnow.svg, winnow.ico (16-256 px) and winnow-<size>.png
"""
import io
import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[2] / 'assets' / 'icon'
ICO_SIZES = (16, 20, 24, 32, 48, 64, 256)
PNG_SIZES = (64, 128, 256, 512)
SUPERSAMPLING = 4

# Background: rounded square, vertical gradient.
GRADIENT_TOP, GRADIENT_BOTTOM = (0x1F, 0x6F, 0xEB), (0x6E, 0x3C, 0xD8)
CORNER_RADIUS = 56
# Glyph (white). Arc radii are stroke centerlines.
W_POINTS = [(30, 112), (78, 222), (128, 146), (178, 222), (226, 112)]
W_STROKE = 28
DOT_CENTER, DOT_RADIUS = (128, 104), 15
WAVE_CENTER, WAVE_RADII, WAVE_STROKE, WAVE_SPAN = (128, 112), (40, 78), 20, 84


def weight_for(size):
    """Stroke multiplier: small icons get thicker lines so they stay readable."""
    return 1.2 if size <= 24 else 1.0


def wave_ends(radius):
    for angle in (-90 - WAVE_SPAN / 2, -90 + WAVE_SPAN / 2):
        yield (WAVE_CENTER[0] + radius * math.cos(math.radians(angle)),
               WAVE_CENTER[1] + radius * math.sin(math.radians(angle)))


def svg():
    hex_color = lambda c: '#%02X%02X%02X' % c
    w = ' '.join(f'{x},{y}' for x, y in W_POINTS)
    waves = []
    for r in WAVE_RADII:
        (x1, y1), (x2, y2) = wave_ends(r)
        waves.append(f'M{x1:.2f},{y1:.2f} A{r},{r} 0 0 1 {x2:.2f},{y2:.2f}')
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="{hex_color(GRADIENT_TOP)}"/>
      <stop offset="1" stop-color="{hex_color(GRADIENT_BOTTOM)}"/>
    </linearGradient>
  </defs>
  <rect width="256" height="256" rx="{CORNER_RADIUS}" fill="url(#bg)"/>
  <g fill="none" stroke="#FFFFFF" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="{w}" stroke-width="{W_STROKE}"/>
    <path d="{' '.join(waves)}" stroke-width="{WAVE_STROKE}"/>
  </g>
  <circle cx="{DOT_CENTER[0]}" cy="{DOT_CENTER[1]}" r="{DOT_RADIUS}" fill="#FFFFFF"/>
</svg>
'''


def render(size):
    s = size * SUPERSAMPLING
    k = s / 256
    weight = weight_for(size)
    img = Image.new('RGBA', (s, s), (0, 0, 0, 0))

    gradient = Image.new('RGBA', (s, s))
    gd = ImageDraw.Draw(gradient)
    for y in range(s):
        t = y / (s - 1)
        gd.line([(0, y), (s, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(GRADIENT_TOP, GRADIENT_BOTTOM)) + (255,))
    mask = Image.new('L', (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius=CORNER_RADIUS * k, fill=255)
    img.paste(gradient, (0, 0), mask)

    d = ImageDraw.Draw(img)
    disc = lambda x, y, r: d.ellipse([(x - r) * k, (y - r) * k, (x + r) * k, (y + r) * k], fill='white')

    stroke = W_STROKE * weight
    d.line([(x * k, y * k) for x, y in W_POINTS], fill='white', width=round(stroke * k), joint='curve')
    for x, y in (W_POINTS[0], W_POINTS[-1]):
        disc(x, y, stroke / 2)

    stroke = WAVE_STROKE * weight
    cx, cy = WAVE_CENTER
    for r in WAVE_RADII:
        outer = r + stroke / 2  # Pillow draws arc width inward from the bounding box
        d.arc([(cx - outer) * k, (cy - outer) * k, (cx + outer) * k, (cy + outer) * k],
              -90 - WAVE_SPAN / 2, -90 + WAVE_SPAN / 2, fill='white', width=round(stroke * k))
        for x, y in wave_ends(r):
            disc(x, y, stroke / 2)

    disc(*DOT_CENTER, DOT_RADIUS * weight)
    return img.resize((size, size), Image.LANCZOS)


def ico(images):
    """ICO file with one PNG-compressed entry per size (supported since Windows Vista)."""
    blobs = []
    for img in images:
        buffer = io.BytesIO()
        img.save(buffer, 'PNG')
        blobs.append(buffer.getvalue())
    header = struct.pack('<HHH', 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries = b''
    for img, blob in zip(images, blobs):
        side = img.width % 256  # 0 means 256
        entries += struct.pack('<BBBBHHII', side, side, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    return header + entries + b''.join(blobs)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'winnow.svg').write_text(svg(), encoding='utf-8')
    (OUT / 'winnow.ico').write_bytes(ico([render(n) for n in ICO_SIZES]))
    for n in PNG_SIZES:
        render(n).save(OUT / f'winnow-{n}.png')
    print(f'Icon written to {OUT}')


if __name__ == '__main__':
    main()
