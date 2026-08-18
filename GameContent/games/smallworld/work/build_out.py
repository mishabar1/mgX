"""Build the server-ready assets:
  out/smallworld/map_N.png        - ~2200px wide board image (served as the TOKEN item)
  out/smallworld/regions_N.json   - clean region list for SmallWorldGameFlow
"""
import json, os
from PIL import Image
from segment import MAPS

OUT = '/root/sw/out/smallworld'
os.makedirs(OUT, exist_ok=True)
TERR = {'farm': 'FARM', 'forest': 'FOREST', 'hill': 'HILL', 'swamp': 'SWAMP',
        'mountain': 'MOUNTAIN', 'water': 'WATER'}
SYM = {'mine': 'MINE', 'magic': 'MAGIC', 'cavern': 'CAVERN', 'tribe': 'TRIBE'}

index = {}
for n in (2, 3, 4, 5):
    im = Image.open(MAPS[n]).convert('RGB')
    w = 2200
    im2 = im.resize((w, round(im.height * w / im.width)), Image.LANCZOS)
    im2.save(f'{OUT}/map_{n}.png', optimize=True)

    src = json.load(open(f'/root/sw/work/regions_{n}.json'))
    regs = []
    for r in src['regions']:
        # skip seas: they are board edge, not playable regions. lakes stay (water region, unplayable
        # but adjacency-relevant) — the flow marks water regions non-conquerable.
        regs.append(dict(
            id=r['id'],
            terrain=TERR[r['terrain']],
            water=r.get('water', ''),                       # '', 'sea' or 'lake'
            cx=r['cx'], cy=r['cy'],                          # % of image, region centre marker
            symbols=[SYM[s] for s in r['symbols']],
            adj=r['adj'],
            isBorder=bool(r['isBorder']),
            area=r['area'],
        ))
    data = dict(players=n, w=im2.width, h=im2.height, regions=regs)
    json.dump(data, open(f'{OUT}/regions_{n}.json', 'w'), indent=1)
    land = [r for r in regs if r['terrain'] != 'WATER']
    index[n] = dict(file=f'map_{n}.png', size=[im2.width, im2.height],
                    regions=len(regs), land=len(land))
    print(f'map {n}: {im2.width}x{im2.height}, {len(regs)} regions ({len(land)} land)')

json.dump(index, open(f'{OUT}/maps_index.json', 'w'), indent=1)
