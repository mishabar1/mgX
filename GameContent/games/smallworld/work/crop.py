"""Zoom helper: python3 crop.py <n> <x> <y> [r=150] [--anno]
Writes work/crop.png: raw map (or annotated with boundaries) around (x,y) in working coords, 3x zoom.
"""
import sys, pickle
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

n = int(sys.argv[1]); x = int(sys.argv[2]); y = int(sys.argv[3])
r = int(sys.argv[4]) if len(sys.argv) > 4 and sys.argv[4].isdigit() else 150
rgb = np.asarray(Image.open(f'/root/sw/work/sw{n}_small.png')).copy()
if '--anno' in sys.argv:
    ws = pickle.load(open(f'/root/sw/work/sw{n}_labels.pkl', 'rb'))
    b = np.zeros(ws.shape, bool)
    b[:, :-1] |= ws[:, :-1] != ws[:, 1:]
    b[:-1, :] |= ws[:-1, :] != ws[1:, :]
    rgb[ndi.binary_dilation(b)] = (255, 0, 255)
c = rgb[max(0, y - r):y + r, max(0, x - r):x + r]
z = min(3, max(1, 900 // max(c.shape[:2])))
im = Image.fromarray(c)
im = im.resize((im.width * z, im.height * z), Image.LANCZOS)
im.save('/root/sw/work/crop.png')
print(f'crop at ({x},{y}) r={r} zoom={z} -> work/crop.png')
