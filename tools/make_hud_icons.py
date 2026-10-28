"""Render the status bar's icon row (src/Windows/ClassicHudIcons.cs) from game-icons.net glyphs.

The sources in `tools/source/game-icons/` are the white-on-transparent 512 px PNGs of game-icons.net
(CC BY 3.0, see THIRD_PARTY_NOTICES.md). Each glyph is reduced to 24 px, filled with the nation's vertical
gradient, given a 1 px outline and a soft drop shadow, and placed at (2, 2) of a 28 px cell (the row's button
size). One strip per nation and state is written to `assets/textures/hud-icons/<nation>-<state>.png`, with
the cells in ICONS order:

  normal, hover (brighter), pressed (darker; the button draws it 1 px lower), disabled (grey and dim),
  glow (a soft halo the button pulses behind the glyph while it asks for attention).

Usage:
  python -I tools/make_hud_icons.py
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

REPO = Path(__file__).resolve().parent.parent
SOURCE = REPO / 'tools' / 'source' / 'game-icons'
OUT = REPO / 'assets' / 'textures' / 'hud-icons'

# Cell order; ClassicHudIcons.Order must match.
ICONS = [
    ('quests', 'lorc_scroll-unfurled'),
    ('store', 'lorc_cut-diamond'),
    ('achievements', 'delapouite_trophy-cup'),
    ('attendance', 'delapouite_calendar'),
    ('genie', 'lorc_magic-lamp'),
    ('events', 'delapouite_knight-banner'),
    ('daily-rank', 'lorc_laurel-crown'),
    ('time', 'lorc_hourglass'),
    ('mail', 'lorc_envelope'),
    # The client's own Discord mark (Client/assets/ui/icons/brand/discord.svg, Simple Icons, CC0), rasterised at 512 px.
    ('discord', 'simpleicons_discord'),
]
GLYPH, CELL, INSET = 24, 28, 2

# Gradient top, gradient bottom, outline, attention glow.
NATIONS = {
    'human': ((255, 246, 205), (222, 172, 82), (82, 52, 16), (255, 224, 140)),
    'karus': ((250, 252, 255), (160, 168, 180), (90, 14, 14), (255, 112, 92)),
}
BRIGHTNESS = {'normal': 1.0, 'hover': 1.18, 'pressed': 0.78}


def glyph_alpha(source):
    """The glyph's coverage at 24 px, padded into the 28 px cell."""
    g = Image.open(SOURCE / f'{source}.png').convert('RGBA')
    g = g.resize((GLYPH * 4, GLYPH * 4), Image.LANCZOS).resize((GLYPH, GLYPH), Image.LANCZOS)
    cell = Image.new('L', (CELL, CELL))
    cell.paste(g.getchannel('A'), (INSET, INSET))
    return cell


def gradient(top, bottom, k):
    grad = np.zeros((CELL, CELL, 3))
    for r in range(CELL):
        t = min(1.0, max(0.0, (r - INSET) / (GLYPH - 1)))
        grad[r] = [min(255.0, (top[i] * (1 - t) + bottom[i] * t) * k) for i in range(3)]
    return grad


def layer(rgb, alpha):
    img = Image.new('RGBA', (CELL, CELL), tuple(int(c) for c in rgb) + (255,))
    img.putalpha(alpha)
    return img


def render(alpha, palette, state):
    top, bottom, edge, glow = palette
    ring = alpha.filter(ImageFilter.MaxFilter(3))
    if state == 'glow':
        halo = ring.filter(ImageFilter.GaussianBlur(2.2)).point(lambda v: min(255, int(v * 0.95)))
        return layer(glow, halo)
    out = Image.new('RGBA', (CELL, CELL))
    # Soft drop shadow, then the 1 px outline, then the gradient face.
    shadow = ring.filter(ImageFilter.GaussianBlur(1)).point(lambda v: int(v * 0.6))
    out.alpha_composite(layer((0, 0, 0), shadow), (1, 2))
    if state == 'disabled':
        face = gradient(top, bottom, 1.0)
        grey = (face @ np.array([0.30, 0.59, 0.11])) * 0.62
        face_img = Image.fromarray(np.dstack([grey, grey, grey, np.asarray(alpha, dtype=float)]).astype('uint8'), 'RGBA')
        out.alpha_composite(layer((34, 34, 34), ring))
        out.alpha_composite(face_img)
        a = np.asarray(out).astype(float)
        a[:, :, 3] *= 0.8
        return Image.fromarray(a.astype('uint8'), 'RGBA')
    face = gradient(top, bottom, BRIGHTNESS[state])
    face_img = Image.fromarray(np.dstack([face, np.asarray(alpha, dtype=float)]).astype('uint8'), 'RGBA')
    out.alpha_composite(layer(edge, ring))
    out.alpha_composite(face_img)
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    alphas = [glyph_alpha(source) for _, source in ICONS]
    for nation, palette in NATIONS.items():
        for state in ('normal', 'hover', 'pressed', 'disabled', 'glow'):
            strip = Image.new('RGBA', (CELL * len(ICONS), CELL))
            for i, alpha in enumerate(alphas):
                strip.alpha_composite(render(alpha, palette, state), (i * CELL, 0))
            path = OUT / f'{nation}-{state}.png'
            strip.save(path, optimize=True)
            print(path.relative_to(REPO), strip.size)


if __name__ == '__main__':
    main()
