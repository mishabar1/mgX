"""Small World map segmentation — recipe from NOTES.md.

shrink to 2000px -> border mask = (min(RGB) > 168 & max-min < 55) -> erode 12
-> seeds (drop < 900px) -> watershed on -distance_transform (full coverage).
Manual fixes per map: extra seeds + merges.
"""
import sys, pickle, json
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi
from skimage.segmentation import watershed

SRC = '/mnt/user-data/uploads/mgX/GameContent/games/smallworld/src/Карты целиком'
MAPS = {n: f'{SRC}/Карта для {n}-х игроков.png' for n in (2, 3, 4, 5)}

# manual fixes per map: seeds = [(x,y),...] in 2000px coords; merges = [[a,b],...] label groups
def load_fixes(n):
    try:
        return json.load(open(f'/root/sw/fixes_{n}.json'))
    except FileNotFoundError:
        return {'seeds': [], 'merges': []}


TARGET = {2: 2000, 3: 2000, 4: 2800, 5: 2800}  # 4/5p boards drawn ~0.7x -> upscale for uniform icon/border px size


def load_small(n, target=None):
    target = target or TARGET[n]
    im = Image.open(MAPS[n]).convert('RGB')
    s = target / im.width
    return im.resize((target, round(im.height * s)), Image.LANCZOS)


PARAMS = {2: dict(erode=12, minsz=900, wminsz=8000), 3: dict(erode=12, minsz=900, wminsz=8000),
          4: dict(erode=12, minsz=900, wminsz=15000), 5: dict(erode=12, minsz=900, wminsz=15000)}


def water_mask(a, minsz=8000):
    R, G, B = a[..., 0], a[..., 1], a[..., 2]
    w = (B - R > 50) & (B > 100)
    w = ndi.binary_closing(w, np.ones((3, 3)), iterations=3)
    lbl, cnt = ndi.label(w)
    sizes = ndi.sum_labels(np.ones_like(lbl), lbl, range(1, cnt + 1))
    big = np.isin(lbl, np.where(sizes >= minsz)[0] + 1)
    return big


def segment(n):
    p = PARAMS[n]
    im2 = load_small(n)
    a = np.asarray(im2).astype(np.int16)
    mn, mx = a.min(2), a.max(2)
    border = (mn > 168) & ((mx - mn) < 55)
    border = ndi.binary_closing(border, structure=np.ones((3, 3)), iterations=4)
    water = water_mask(a, p['wminsz'])
    barrier = border | water
    interior = ~barrier
    er = ndi.binary_erosion(interior, iterations=p['erode'])
    lbl, cnt = ndi.label(er)
    sizes = ndi.sum_labels(np.ones_like(lbl), lbl, range(1, cnt + 1))
    keep = np.where(sizes >= p['minsz'])[0] + 1
    seeds = np.zeros_like(lbl)
    nid = 0
    for k in keep:
        nid += 1
        seeds[lbl == k] = nid
    fx = load_fixes(n)
    for (x, y) in fx['seeds']:
        nid += 1
        yy, xx = np.ogrid[:seeds.shape[0], :seeds.shape[1]]
        seeds[(yy - y) ** 2 + (xx - x) ** 2 <= 8 ** 2] = nid
    dist = ndi.distance_transform_edt(~border)
    ws = watershed(-dist, markers=seeds, mask=~water)
    # append water bodies as their own regions
    wlbl, wcnt = ndi.label(water)
    for v in range(1, wcnt + 1):
        nid += 1
        ws[wlbl == v] = nid
    for grp in fx['merges']:
        tgt = grp[0]
        for g in grp[1:]:
            ws[ws == g] = tgt
    out = np.zeros_like(ws)
    for i, v in enumerate([v for v in np.unique(ws) if v != 0], 1):
        out[ws == v] = i
    return im2, out


def centers(ws):
    """pole-of-inaccessibility per label: max of per-region distance transform.
    The mask is zero-padded so a region touching the image edge still gets an INTERIOR
    marker (otherwise the max lands on the border and the click marker sits off-board)."""
    cs = {}
    for v in [v for v in np.unique(ws) if v != 0]:
        m = np.pad(ws == v, 1)
        d = ndi.distance_transform_edt(m)
        y, x = np.unravel_index(np.argmax(d), d.shape)
        cs[int(v)] = (int(x) - 1, int(y) - 1)
    return cs


def overlay(im2, ws, path, extra=None):
    a = np.asarray(im2).copy()
    # boundaries: pixel whose right/down neighbor differs
    b = np.zeros(ws.shape, bool)
    b[:, :-1] |= ws[:, :-1] != ws[:, 1:]
    b[:-1, :] |= ws[:-1, :] != ws[1:, :]
    b = ndi.binary_dilation(b, iterations=1)
    a[b] = (255, 0, 255)
    im = Image.fromarray(a)
    dr = ImageDraw.Draw(im)
    try:
        f = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 26)
    except Exception:
        f = ImageFont.load_default()
    for v, (x, y) in centers(ws).items():
        x = min(max(x, 25), ws.shape[1] - 25)
        y = min(max(y, 20), ws.shape[0] - 20)
        t = str(v) if not extra else f'{v}:{extra.get(v, "")}'
        w = dr.textlength(t, font=f)
        dr.ellipse([x - 16, y - 16, x + 16, y + 16], fill=(255, 220, 40)) if not extra else \
            dr.rectangle([x - w / 2 - 4, y - 16, x + w / 2 + 4, y + 16], fill=(255, 220, 40))
        dr.text((x - w / 2, y - 14), t, fill=(0, 0, 0), font=f)
    im.save(path)


if __name__ == '__main__':
    for n in [int(a) for a in sys.argv[1:]] or [2, 3, 4, 5]:
        im2, ws = segment(n)
        nreg = len(np.unique(ws)) - (1 if 0 in ws else 0)
        print(f'map {n}: {nreg} regions, shape {ws.shape}')
        pickle.dump(ws, open(f'/root/sw/work/sw{n}_labels.pkl', 'wb'))
        overlay(im2, ws, f'/root/sw/work/sw{n}_ws.png')
        im2.save(f'/root/sw/work/sw{n}_small.png')
