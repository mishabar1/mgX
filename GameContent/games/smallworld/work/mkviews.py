"""Review views for map n:
  work/rev{n}_blob.png  - colored labels + ids + coordinate grid (working coords)
  work/rev{n}_anno.png  - map + boundaries + 'id:terrain initial+symbols+B(order)' tags + grid
  work/rev{n}_raw.png   - plain map + grid
  work/sheet_{n}.png    - per-region contact sheet (from analyze.py)
Usage: python3 mkviews.py <n>
"""
import sys, json, pickle
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi
from segment import centers

FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'


def grid(im, step=200):
    dr = ImageDraw.Draw(im)
    f = ImageFont.truetype(FONT, 22)
    for x in range(step, im.width, step):
        dr.line([x, 0, x, im.height], fill=(255, 255, 255), width=1)
        dr.text((x + 3, 3), str(x), fill=(255, 0, 0), font=f)
        dr.text((x + 3, im.height - 30), str(x), fill=(255, 0, 0), font=f)
    for y in range(step, im.height, step):
        dr.line([0, y, im.width, y], fill=(255, 255, 255), width=1)
        dr.text((3, y + 3), str(y), fill=(255, 0, 0), font=f)
        dr.text((im.width - 80, y + 3), str(y), fill=(255, 0, 0), font=f)
    return im


def main(n):
    ws = pickle.load(open(f'/root/sw/work/sw{n}_labels.pkl', 'rb'))
    rgb = np.asarray(Image.open(f'/root/sw/work/sw{n}_small.png'))
    regs = {r['id']: r for r in json.load(open(f'/root/sw/work/regions_{n}.json'))['regions']}
    cs = centers(ws)
    f = ImageFont.truetype(FONT, 26)
    fs = ImageFont.truetype(FONT, 20)

    # blob
    rng = np.random.RandomState(7)
    cols = rng.randint(30, 255, (ws.max() + 1, 3))
    blob = Image.fromarray((rgb * 0.45 + cols[ws] * 0.55).astype(np.uint8))
    dr = ImageDraw.Draw(blob)
    for v, (x, y) in cs.items():
        x = min(max(x, 30), ws.shape[1] - 30); y = min(max(y, 20), ws.shape[0] - 20)
        t = str(v)
        w = dr.textlength(t, font=f)
        dr.rectangle([x - w / 2 - 4, y - 17, x + w / 2 + 4, y + 17], fill=(255, 255, 255))
        dr.text((x - w / 2, y - 15), t, fill=(0, 0, 0), font=f)
    grid(blob).save(f'/root/sw/work/rev{n}_blob.png')

    # annotated: boundaries + id:terrain/symbols
    a = rgb.copy()
    b = np.zeros(ws.shape, bool)
    b[:, :-1] |= ws[:, :-1] != ws[:, 1:]
    b[:-1, :] |= ws[:-1, :] != ws[1:, :]
    a[ndi.binary_dilation(b)] = (255, 0, 255)
    anno = Image.fromarray(a)
    dr = ImageDraw.Draw(anno)
    TL = {'farm': 'FARM', 'forest': 'FRST', 'hill': 'HILL', 'swamp': 'SWMP', 'mountain': 'MNT', 'water': 'WATR'}
    SM = {'mine': 'M', 'magic': 'S', 'cavern': 'C', 'tribe': 'T'}
    for v, (x, y) in cs.items():
        r = regs.get(v)
        if not r:
            continue
        t = f"{v}:{TL.get(r['terrain'], '?')}"
        if r.get('water'):
            t = f"{v}:{r['water'].upper()}"
        if r['symbols']:
            t += ''.join(SM.get(s, '?') for s in r['symbols'])
        if r['isBorder']:
            t += '+B'
        x = min(max(x, 60), ws.shape[1] - 60); y = min(max(y, 20), ws.shape[0] - 20)
        w = dr.textlength(t, font=fs)
        dr.rectangle([x - w / 2 - 3, y - 13, x + w / 2 + 3, y + 13], fill=(255, 235, 60))
        dr.text((x - w / 2, y - 11), t, fill=(0, 0, 0), font=fs)
    grid(anno).save(f'/root/sw/work/rev{n}_anno.png')

    grid(Image.fromarray(rgb.copy())).save(f'/root/sw/work/rev{n}_raw.png')
    print('views written for map', n, '| regions:', len(regs))


if __name__ == '__main__':
    for n in [int(x) for x in sys.argv[1:]] or [2, 3, 4, 5]:
        main(n)
