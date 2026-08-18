"""Masked multi-template, multi-scale symbol detection + hi-res verification."""
import numpy as np, cv2
from PIL import Image

TPL_SPECS = {'mine': (1049, 53), 'magic': (766, 51), 'cavern': (1390, 141), 'tribe': (1253, 98)}
R_DISC, R_SQ = 19, 16
SCALES = (0.85, 1.0, 1.15)


def get_templates():
    im = np.asarray(Image.open('/root/sw/work/sw2_small.png'))
    out = {}
    for k, (x, y) in TPL_SPECS.items():
        r = R_SQ if k == 'tribe' else R_DISC
        tpl = im[y - r:y + r, x - r:x + r].astype(np.uint8)
        if k == 'tribe':
            mask = np.ones(tpl.shape[:2], np.uint8) * 255
        else:
            yy, xx = np.ogrid[:2 * r, :2 * r]
            mask = (((yy - r) ** 2 + (xx - r) ** 2) <= (r - 2) ** 2).astype(np.uint8) * 255
        out[k] = (tpl, mask)
    return out


def detect(rgb, thr=None):
    thr = thr or {'mine': 0.10, 'magic': 0.12, 'cavern': 0.10, 'tribe': 0.10}
    tpls = get_templates()
    found = []
    img = rgb.astype(np.uint8)
    for k, (tpl0, mask0) in tpls.items():
        best = None
        for sc in SCALES:
            w = max(8, round(tpl0.shape[1] * sc))
            h = max(8, round(tpl0.shape[0] * sc))
            tpl = cv2.resize(tpl0, (w, h), interpolation=cv2.INTER_AREA)
            mask = cv2.resize(mask0, (w, h), interpolation=cv2.INTER_NEAREST)
            res = cv2.matchTemplate(img, tpl, cv2.TM_SQDIFF_NORMED, mask=np.dstack([mask] * 3))
            res = np.nan_to_num(res, nan=1.0, posinf=1.0, neginf=1.0)
            full = np.ones(img.shape[:2], np.float32)
            full[h // 2:h // 2 + res.shape[0], w // 2:w // 2 + res.shape[1]] = res
            best = full if best is None else np.minimum(best, full)
        pts = np.where(best <= thr[k])
        cand = sorted(zip(best[pts], pts[1], pts[0]))
        taken = []
        for s, x, y in cand:
            cx, cy = int(x), int(y)
            if all((cx - a) ** 2 + (cy - b) ** 2 > 28 ** 2 for a, b, _ in taken):
                taken.append((cx, cy, float(s)))
        for cx, cy, s in taken:
            found.append((k, cx, cy, round(s, 3)))
    found.sort(key=lambda t: t[3])
    kept = []
    for k, x, y, s in found:
        if all((x - a) ** 2 + (y - b) ** 2 > 20 ** 2 for _, a, b, _ in kept):
            kept.append((k, x, y, s))
    return kept


_ORIG_CACHE = {}


def _orig(n):
    from segment import MAPS
    if n not in _ORIG_CACHE:
        _ORIG_CACHE[n] = Image.open(MAPS[n]).convert('RGB')
    return _ORIG_CACHE[n]


def verify_hires(n, x, y, kind='tribe'):
    """re-check candidate at original scan resolution; returns sqdiff score (lower=better)"""
    from segment import TARGET
    orig = _orig(n)
    f = orig.width / TARGET[n]
    f2 = _orig(2).width / 2000
    tx, ty = TPL_SPECS[kind]
    r0 = R_SQ if kind == 'tribe' else R_DISC
    r2 = round(r0 * f2)
    tpl = np.asarray(_orig(2).crop((round(tx * f2) - r2, round(ty * f2) - r2,
                                    round(tx * f2) + r2, round(ty * f2) + r2)))
    tpl = cv2.resize(tpl, (96, 96), interpolation=cv2.INTER_AREA)
    r = round(r0 * f * 1.6)
    patch = np.asarray(orig.crop((max(0, round(x * f) - r), max(0, round(y * f) - r),
                                  round(x * f) + r, round(y * f) + r)))
    ps = round(96 * 1.6)
    patch = cv2.resize(patch, (ps, ps), interpolation=cv2.INTER_AREA)
    best = 1.0
    for sc in (0.9, 1.0, 1.1):
        t = cv2.resize(tpl, (round(96 * sc), round(96 * sc)), interpolation=cv2.INTER_AREA)
        if t.shape[0] <= patch.shape[0]:
            res = cv2.matchTemplate(patch, t, cv2.TM_SQDIFF_NORMED)
            best = min(best, float(np.nan_to_num(res, nan=1.0).min()))
    return best


def detect_verified(n, rgb):
    """full pipeline: loose detect + hi-res verify borderline candidates"""
    loose = detect(rgb, thr={'mine': 0.12, 'magic': 0.14, 'cavern': 0.12, 'tribe': 0.16})
    out = []
    for k, x, y, s in loose:
        v = verify_hires(n, x, y, k)
        if v < 0.075:
            out.append((k, x, y, round(v, 3)))
    return out


if __name__ == '__main__':
    import sys
    from collections import Counter
    for n in [int(a) for a in sys.argv[1:]] or [2, 3, 4, 5]:
        rgb = np.asarray(Image.open(f'/root/sw/work/sw{n}_small.png'))
        f = detect_verified(n, rgb)
        print(n, Counter(t[0] for t in f), sorted(f, key=lambda t: t[0]))


from scipy import ndimage as _ndi


def _center_comp(mask, minpx=150):
    lbl, c = _ndi.label(mask)
    if c == 0:
        return None
    h, w = mask.shape
    cy, cx = h // 2, w // 2
    best, bd = 0, 1e18
    for v in range(1, c + 1):
        ys, xs = np.where(lbl == v)
        if len(ys) < minpx:
            continue
        d = ((ys - cy) ** 2 + (xs - cx) ** 2).min()
        if d < bd:
            bd, best = d, v
    return (lbl == best) if best else None


def _shape_ok(mm, lo, hi, fill_min):
    h, w = mm.shape
    ys, xs = np.where(mm)
    bh = ys.max() - ys.min() + 1
    bw = xs.max() - xs.min() + 1
    fill = mm.sum() / (bh * bw)
    touches = ys.min() == 0 or xs.min() == 0 or ys.max() == h - 1 or xs.max() == w - 1
    return (0.72 <= bw / bh <= 1.4) and (lo <= bw <= hi) and (lo <= bh <= hi) and fill >= fill_min and not touches


def is_square(rgb, x, y, r=30):
    p = rgb[max(0, y - r):y + r, max(0, x - r):x + r].astype(np.int16)
    mn = p.min(2); mx = p.max(2)
    mm = _center_comp((mn > 155) & (mx - mn < 60))
    if mm is None:
        return False
    ys, xs = np.where(mm)
    bh = ys.max() - ys.min() + 1
    bw = xs.max() - xs.min() + 1
    fill = mm.sum() / (bh * bw)
    touches = ys.min() == 0 or xs.min() == 0 or ys.max() == mm.shape[0] - 1 or xs.max() == mm.shape[1] - 1
    return fill >= 0.80 and 0.68 <= bw / bh <= 1.4 and 19 <= bw <= 42 and 19 <= bh <= 42 and not touches


def is_disc(rgb, x, y, kind, r=38):
    p = rgb[max(0, y - r):y + r, max(0, x - r):x + r].astype(np.int16)
    R, G, B = p[..., 0], p[..., 1], p[..., 2]
    if kind == 'mine':
        m = (R - G > 50) & (R - B > 50) & (R > 120)
    elif kind == 'magic':
        m = (B - R > 40) & (B > 100)
    else:
        m = (R < 95) & (G < 85) & (B < 85)
    m = _ndi.binary_closing(m, np.ones((3, 3)), iterations=2)
    mm = _center_comp(m, minpx=200)
    if mm is None or not _shape_ok(mm, 22, 60, 0.45):
        return False
    if kind == 'cavern':
        ring = p.reshape(-1, 3)
        warm = ((ring[:, 0] > 120) & (ring[:, 0] > ring[:, 2] + 20)).mean()
        if warm < 0.12:
            return False
    return True


def detect_final(n, rgb):
    # disc icons: template score separates cleanly (real <=0.06, junk >=0.11)
    loose = detect(rgb, thr={'mine': 0.09, 'magic': 0.09, 'cavern': 0.09, 'tribe': 0.165})
    out = []
    for k, x, y, s in loose:
        if k == 'tribe':
            if is_square(rgb, x, y):
                out.append((k, x, y, s))
        else:
            out.append((k, x, y, s))
    return out
