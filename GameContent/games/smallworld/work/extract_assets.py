"""Extract race banners + power badges from PnP pages 1-4 (150dpi renders).
Writes work/pnp_extract/item_PP_KK.png (white->alpha) + an indexed contact sheet for manual naming.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi

OUT = '/root/sw/work/pnp_extract'
os.makedirs(OUT, exist_ok=True)
items = []
for p in range(1, 5):
    im = Image.open(f'/root/sw/work/pnphi-0{p}.png').convert('RGB')
    a = np.asarray(im).astype(np.int16)
    nonwhite = ~((a.min(2) > 225))
    nonwhite = ndi.binary_closing(nonwhite, np.ones((5, 5)), iterations=3)
    lbl, c = ndi.label(nonwhite)
    objs = ndi.find_objects(lbl)
    for v in range(1, c + 1):
        sl = objs[v - 1]
        h = sl[0].stop - sl[0].start
        w = sl[1].stop - sl[1].start
        if w < 150 or h < 100 or w * h < 40000:
            continue
        items.append((p, sl[0].start, sl[1].start, h, w))

items.sort(key=lambda t: (t[0], t[2] // 300, t[1]))  # page, column band, y
sheet_tiles = []
for i, (p, y0, x0, h, w) in enumerate(items):
    im = Image.open(f'/root/sw/work/pnphi-0{p}.png').convert('RGB')
    pad = 4
    crop = im.crop((max(0, x0 - pad), max(0, y0 - pad), x0 + w + pad, y0 + h + pad))
    ca = np.asarray(crop.convert('RGBA')).copy()
    white = (ca[..., :3].min(2) > 232)
    # only make white transparent when connected to the crop border (keep white inside art)
    border_white = np.zeros_like(white)
    border_white[0, :] = white[0, :]; border_white[-1, :] = white[-1, :]
    border_white[:, 0] = white[:, 0]; border_white[:, -1] = white[:, -1]
    lblw, _ = ndi.label(white)
    edge_ids = set(np.unique(lblw[0, :])) | set(np.unique(lblw[-1, :])) | set(np.unique(lblw[:, 0])) | set(np.unique(lblw[:, -1]))
    edge_ids.discard(0)
    ca[np.isin(lblw, list(edge_ids)), 3] = 0
    out = Image.fromarray(ca)
    out.save(f'{OUT}/item_{p}_{i:02d}.png')
    t = out.copy()
    t.thumbnail((260, 130))
    sheet_tiles.append((f'{p}_{i:02d}', t, round(w / h, 2)))

cols = 6
rows = (len(sheet_tiles) + cols - 1) // cols
sheet = Image.new('RGB', (cols * 270, rows * 160), (40, 40, 40))
dr = ImageDraw.Draw(sheet)
f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 16)
for i, (name, t, ar) in enumerate(sheet_tiles):
    x, y = (i % cols) * 270, (i // cols) * 160
    sheet.paste(t, (x + 5, y + 5))
    dr.text((x + 5, y + 138), f'{name} ar{ar}', fill=(255, 255, 0), font=f)
sheet.save('/root/sw/work/pnp_extract_sheet.png')
print(len(items), 'items extracted')
