"""Import local KO 1.298 UIFs and their textures. No game archives are unpacked.

Binary format reference: Open-KO/KnightOnline src/N3Base N3UI*.cpp.
Generated layouts are separate from hand-authored overrides.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path
from dxt_to_png import read_dxt, write_png

TYPES = ['base','button','static','progress','image','scrollbar','string',
         'trackbar','edit','area','tooltip','icon','icon_manager','iconslot','list']

class Reader:
    def __init__(self, data, modern):
        self.data, self.at, self.modern = data, 0, modern

    def take(self, size):
        if size < 0 or self.at + size > len(self.data):
            raise ValueError(f'Unexpected EOF at {self.at}, reading {size}')
        result = self.data[self.at:self.at+size]
        self.at += size
        return result

    def unpack(self, fmt):
        return struct.unpack('<'+fmt, self.take(struct.calcsize('<'+fmt)))

    def number(self): return self.unpack('i')[0]

    def string(self):
        n = self.number()
        if not 0 <= n <= 8192: raise ValueError(f'Bad string length {n} at {self.at}')
        return self.take(n).decode('cp949', errors='replace').rstrip('\0')

    def rect(self):
        l,t,r,b = self.unpack('4i')
        return [l,t,r-l,b-t]

    def node(self, kind=0, depth=0):
        if depth > 64 or kind not in range(len(TYPES)): raise ValueError(f'Unknown type {kind}')
        name = self.string()
        count = self.unpack('hh')[0] if self.modern else self.number()
        if not 0 <= count <= 4096: raise ValueError(f'Bad children {count}')
        children = [self.node(self.number(), depth+1) for _ in range(count)]
        n = dict(type=TYPES[kind], id=self.string(), rect=self.rect(), drag=self.rect())
        n['style'], n['tag'] = self.unpack('II')
        n['tooltip'] = self.string()
        n['sounds'] = [self.string(), self.string()]
        n['children'] = children
        if name: n['resourceName'] = name
        if kind == 4:
            n['sourceTexture'] = self.string()
            n['uv'] = list(self.unpack('4f'))
            n['fps'] = self.unpack('f')[0]
        elif kind == 6:
            n['font'] = self.string()
            if n['font']:
                n['size'], flags = self.unpack('II')
                n['bold'], n['italic'] = bool(flags & 1), bool(flags & 2)
            color = self.unpack('I')[0]
            n['color'] = f'#{color & 0xffffff:06x}{color >> 24:02x}'
            n['text'] = self.string()
            n['lineSpacing'] = self.number() if self.modern else 0
        elif kind == 1:
            n['click'] = self.rect()
            n['sounds'] += [self.string(), self.string()]
        elif kind in (2,8):
            n['sounds'].append(self.string())
            if kind == 8: n['sounds'].append(self.string())
        elif kind == 9: n['areaType'] = self.number()
        elif kind == 14:
            n['font'] = self.string()
            if n['font']:
                n['size'], color, bold, italic = self.unpack('4I')
                n.update(color=f'#{color & 0xffffff:06x}{color >> 24:02x}', bold=bool(bold), italic=bool(italic))
        elif kind in (10,11,12,13): raise ValueError(f'Unsupported {TYPES[kind]}')
        return n

def walk(n):
    yield n
    for child in n['children']: yield from walk(child)

def parse(path):
    data = path.read_bytes()
    errors = []
    for modern in (True, False):
        try:
            r = Reader(data, modern)
            n = r.node()
            if r.at != len(data): raise ValueError(f'{len(data)-r.at} unconsumed bytes')
            return n
        except (ValueError, struct.error) as e: errors.append(str(e))
    raise ValueError('; '.join(errors))

def run(source, output):
    layouts = output/'layouts'
    textures = output/'textures'
    layouts.mkdir(parents=True, exist_ok=True)
    textures.mkdir(parents=True, exist_ok=True)
    files = {p.name.lower(): p for p in source.iterdir() if p.is_file()}
    report = dict(source=str(source), layouts={}, textures={}, errors={}, missingTextures=[])
    for path in sorted(source.glob('*.uif')):
        try:
            root = parse(path)
            for n in walk(root):
                if not n.get('sourceTexture'): continue
                key = n['sourceTexture'].replace('\\','/').split('/')[-1].lower()
                texpath = files.get(key)
                if texpath is None:
                    report['missingTextures'].append(dict(layout=path.name, texture=key))
                    continue
                png = Path(key).stem+'.png'
                if png not in report['textures']:
                    raw = texpath.read_bytes()
                    tex = read_dxt(raw)
                    write_png(textures/png, tex.width, tex.height, tex.rgba())
                    report['textures'][png] = dict(width=tex.width, height=tex.height, sha256=hashlib.sha256(raw).hexdigest())
                size = report['textures'][png]
                u,v,r,b = n['uv']
                x,y,x2,y2 = round(u*size['width']),round(v*size['height']),round(r*size['width']),round(b*size['height'])
                n.update(texture=png, src=[min(x,x2),min(y,y2),abs(x2-x),abs(y2-y)], flip=[x2<x,y2<y])
            filename = path.stem.lower()+'.json'
            (layouts/filename).write_text(json.dumps(root, ensure_ascii=False, indent=2), encoding='utf-8')
            report['layouts'][path.stem.lower()] = dict(file='layouts/'+filename, rect=root['rect'], sha256=hashlib.sha256(path.read_bytes()).hexdigest())
        except Exception as e: report['errors'][path.name] = str(e)
    (output/'index.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f"Layouts: {len(report['layouts'])}, textures: {len(report['textures'])}, errors: {len(report['errors'])}, missing textures: {len(report['missingTextures'])}")
    for name,error in report['errors'].items(): print(name, error)

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', type=Path, required=True)
    ap.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1]/'assets')
    args = ap.parse_args()
    run(args.source, args.output)
