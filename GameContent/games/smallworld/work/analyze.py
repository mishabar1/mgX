"""Per-region analysis: terrain, symbols, adjacency, centers, isBorder.
Outputs work/regions_N.json (raw) + contact sheet + annotated overlay for review.
Manual overrides merged from overrides.json: {"3": {"12": {"terrain": "swamp", "symbols": ["mine"]}}}
"""
import sys, json, pickle
import numpy as np
import cv2
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi
from segment import centers

FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'

def _comps(mask, amin, amax, dmin, dmax, fill_min):
    lbl, c = ndi.label(mask)
    out = []
    objs = ndi.find_objects(lbl)
    for v in range(1, c + 1):
        sl = objs[v - 1]
        m = lbl[sl] == v
        a = int(m.sum())
        if not amin <= a <= amax:
            continue
        h, w = m.shape
        if not (dmin <= w <= dmax and dmin <= h <= dmax):
            continue
        if not 0.7 <= w / h <= 1.45:
            continue
        if a / (w * h) < fill_min:
            continue
        cy = sl[0].start + h // 2
        cx = sl[1].start + w // 2
        out.append((cx, cy, a))
    return out


def detect_symbols(rgb, bigwater):
    """color+shape disc/square detection, scale ~30px icons. returns [(kind,x,y)]"""
    a = rgb.astype(np.int16)
    R, G, B = a[..., 0], a[..., 1], a[..., 2]
    found = []
    close = lambda m: ndi.binary_closing(m, np.ones((3, 3)), iterations=2)
    mine = close((R > 130) & (R - G > 55) & (R - B > 55))
    for x, y, _ in _comps(mine, 300, 1400, 20, 46, 0.55):
        found.append(('mine', x, y))
    magic = close((B - R > 50) & (B > 110)) & ~ndi.binary_dilation(bigwater, iterations=3)
    for x, y, _ in _comps(magic, 300, 1500, 20, 46, 0.45):
        found.append(('magic', x, y))
    cavern = close((R < 90) & (G < 80) & (B < 80))
    for x, y, _ in _comps(cavern, 280, 1400, 20, 46, 0.52):
        # golden ring check: annulus should be warm/bright
        y0, y1 = max(0, y - 26), min(a.shape[0], y + 26)
        x0, x1 = max(0, x - 26), min(a.shape[1], x + 26)
        ring = a[y0:y1, x0:x1].reshape(-1, 3)
        warm = ((ring[:, 0] > 120) & (ring[:, 0] > ring[:, 2] + 20)).mean()
        if warm > 0.18:
            found.append(('cavern', x, y))
    mn = a.min(2); mx = a.max(2)
    tribe = (mn > 160) & (mx - mn < 55)
    for x, y, _ in _comps(tribe, 350, 1100, 20, 40, 0.72):
        found.append(('tribe', x, y))
    return found


def classify_terrain(a_hsv, mask):
    """a_hsv: HSV image (cv2, H 0-179). mask: region bool. Returns terrain string + debug fracs."""
    m = ndi.binary_erosion(mask, iterations=4)
    if m.sum() < 200:
        m = mask
    H = a_hsv[..., 0][m].astype(int)
    S = a_hsv[..., 1][m].astype(int)
    V = a_hsv[..., 2][m].astype(int)
    n = len(H)
    blue = ((H > 95) & (H < 135) & (S > 80)).sum() / n
    grey = ((S < 55) & (V > 70)).sum() / n           # incl. snow/rock
    orange_bright = ((H >= 8) & (H <= 25) & (S > 110) & (V > 150)).sum() / n
    orange_dark = ((H >= 5) & (H <= 25) & (S > 80) & (V <= 150)).sum() / n
    green_light = ((H >= 30) & (H <= 50) & (S > 60) & (V > 120)).sum() / n
    green_dark = ((H >= 30) & (H <= 70) & (V <= 120)).sum() / n
    dark = (V < 70).sum() / n
    fr = dict(blue=blue, grey=grey, ob=orange_bright, od=orange_dark,
              gl=green_light, gd=green_dark, dark=dark)
    if blue > 0.45:
        return 'water', fr
    if grey > 0.42:
        return 'mountain', fr
    # farm vs swamp: farms are bright orange; swamps brown-orange + murk
    if orange_bright > 0.28 and orange_bright > orange_dark * 1.1:
        return 'farm', fr
    if (orange_dark + orange_bright) > 0.25:
        return 'swamp', fr
    if green_light > green_dark:
        return 'hill', fr
    if (green_dark + dark) > 0.3:
        return 'forest', fr
    if grey > 0.25:
        return 'mountain', fr
    return max([('hill', green_light), ('forest', green_dark), ('farm', orange_bright),
                ('swamp', orange_dark), ('mountain', grey)], key=lambda t: t[1])[0], fr


def analyze(n):
    ws = pickle.load(open(f'/root/sw/work/sw{n}_labels.pkl', 'rb'))
    im = Image.open(f'/root/sw/work/sw{n}_small.png')
    rgb = np.asarray(im)
    hsv = cv2.cvtColor(rgb, cv2.COLOR_RGB2HSV)
    H, W = ws.shape
    ids = [int(v) for v in np.unique(ws) if v != 0]
    cs = centers(ws)

    # water/sea flags
    edge = np.zeros_like(ws, bool)
    edge[0, :] = edge[-1, :] = True
    edge[:, 0] = edge[:, -1] = True

    # adjacency
    adj = {i: set() for i in ids}
    for i in ids:
        m = ws == i
        d = ndi.binary_dilation(m, iterations=3)
        for j in np.unique(ws[d & ~m]):
            j = int(j)
            if j != 0 and j != i and (ws[d & ~m] == j).sum() >= 20:
                adj[i].add(j)
    for i in ids:
        for j in list(adj[i]):
            adj[j].add(i)

    # water ground truth from mask (appended comps are exactly bigwater)
    from segment import water_mask, PARAMS
    bigwater = water_mask(rgb.astype(np.int16), PARAMS[n]['wminsz'])

    # symbols: precomputed by symbols.py (work/symbols.json); fallback = inline detect
    try:
        syms = [tuple(t) for t in json.load(open('/root/sw/work/symbols.json'))[str(n)]]
        syms = [(k, x, y) for k, x, y, *_ in syms]
    except Exception:
        syms = [(k, x, y) for k, x, y in detect_symbols(rgb, bigwater)]

    regions = {}
    for i in ids:
        m = ws == i
        wfrac = bigwater[m].mean()
        if wfrac > 0.6:
            terr, fr = 'water', {}
        else:
            terr, fr = classify_terrain(hsv, m)
            if terr == 'water':  # land region cannot be water; pick next best
                terr = 'mountain'
        regions[i] = dict(id=i, terrain=terr, cx=round(cs[i][0] / W * 100, 2), cy=round(cs[i][1] / H * 100, 2),
                          area=int(m.sum()), adj=sorted(adj[i]), symbols=[],
                          touchesEdge=bool((m & edge).sum() > 10), fr={k: round(v, 2) for k, v in fr.items()})
    for k, x, y in syms:
        i = int(ws[min(y, H - 1), min(x, W - 1)])
        if i != 0:
            regions[i]['symbols'].append(k)

    # water regions: seas (touch edge) vs lake
    for i in ids:
        r = regions[i]
        if r['terrain'] == 'water':
            r['water'] = 'sea' if r['touchesEdge'] else 'lake'
    # isBorder = touches edge OR adjacent to a sea
    seas = {i for i in ids if regions[i].get('water') == 'sea'}
    for i in ids:
        regions[i]['isBorder'] = regions[i]['touchesEdge'] or bool(seas & set(regions[i]['adj']))

    # manual overrides
    try:
        ov = json.load(open(f'/root/sw/overrides_{n}.json'))
    except FileNotFoundError:
        ov = {}
    for k, v in ov.items():
        regions[int(k)].update(v)

    json.dump({'map': n, 'w': W, 'h': H, 'regions': [regions[i] for i in ids]},
              open(f'/root/sw/work/regions_{n}.json', 'w'), indent=1)
    return ws, rgb, regions, syms


def contact_sheet(n, ws, rgb, regions, cols=6, tile=300):
    ids = sorted(regions)
    rows = (len(ids) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * tile, rows * (tile + 26)), (24, 24, 24))
    dr = ImageDraw.Draw(sheet)
    f = ImageFont.truetype(FONT, 17)
    for k, i in enumerate(ids):
        m = ws == i
        ys, xs = np.where(m)
        y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
        pad = 12
        y0, y1 = max(0, y0 - pad), min(ws.shape[0], y1 + pad)
        x0, x1 = max(0, x0 - pad), min(ws.shape[1], x1 + pad)
        crop = rgb[y0:y1, x0:x1].copy()
        sub = m[y0:y1, x0:x1]
        dim = ~sub
        crop[dim] = (crop[dim] * 0.35).astype(np.uint8)
        b = np.zeros(sub.shape, bool)
        b[:, :-1] |= sub[:, :-1] != sub[:, 1:]
        b[:-1, :] |= sub[:-1, :] != sub[1:, :]
        crop[ndi.binary_dilation(b)] = (255, 0, 255)
        c = Image.fromarray(crop)
        c.thumbnail((tile, tile))
        x, y = (k % cols) * tile, (k // cols) * (tile + 26)
        sheet.paste(c, (x + (tile - c.width) // 2, y + (tile - c.height) // 2))
        r = regions[i]
        t = f"{i}: {r['terrain']}" + (f" [{','.join(r['symbols'])}]" if r['symbols'] else '')
        dr.text((x + 4, y + tile + 3), t, fill=(255, 255, 100), font=f)
    sheet.save(f'/root/sw/work/sheet_{n}.png')


if __name__ == '__main__':
    for n in [int(a) for a in sys.argv[1:]] or [2, 3, 4, 5]:
        ws, rgb, regions, syms = analyze(n)
        contact_sheet(n, ws, rgb, regions)
        from collections import Counter
        print(f'map {n}: {len(regions)} regions',
              Counter(r['terrain'] for r in regions.values()),
              'symbols:', Counter(s[0] for s in syms))
