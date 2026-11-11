"""
Textures for the proposed new block types, in the game's block format:
  - stacked blocks: a sheet two tiles tall (top tile = the top block of a stack, bottom tile = the blocks under it),
    each tile mapped onto every face of a cube (the bevels use the tile's outer few pixels)
  - floor pieces: a single tile laid flat (like the hole)
Drawn at 4x and shrunk, so edges are smooth. Tile = 128 px (the stock blocks are 64).
"""
import numpy as np, sys, os
from PIL import Image

T = 128          # final tile size
SS = 4           # supersampling
N = T * SS
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1234)

yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
U = (xx + 0.5) / N   # 0..1 across
V = (yy + 0.5) / N   # 0..1 down

# ------------------------------------------------------------------ noise (tileable)

def vnoise(cells, seed, aniso=(1, 1)):
    """Smooth value noise that wraps at the tile's edges."""
    r = np.random.default_rng(seed)
    cx, cy = cells * aniso[0], cells * aniso[1]
    g = r.random((cy, cx)).astype(np.float32)
    fx = U * cx; fy = V * cy
    x0 = np.floor(fx).astype(int); y0 = np.floor(fy).astype(int)
    tx = fx - x0; ty = fy - y0
    tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty)
    x1 = (x0 + 1) % cx; y1 = (y0 + 1) % cy; x0 %= cx; y0 %= cy
    a = g[y0, x0] * (1 - tx) + g[y0, x1] * tx
    b = g[y1, x0] * (1 - tx) + g[y1, x1] * tx
    return a * (1 - ty) + b * ty

def fbm(cells, seed, octaves=4, aniso=(1, 1)):
    s = 0; amp = 1; tot = 0
    for o in range(octaves):
        s = s + vnoise(cells * 2 ** o, seed + o * 17, aniso) * amp
        tot += amp; amp *= 0.5
    return s / tot

def col(*c):
    return np.array(c, np.float32) / 255.0

def mix(a, b, t):
    t = np.clip(t, 0, 1)[..., None] if np.ndim(t) else np.clip(t, 0, 1)
    return a * (1 - t) + b * t

def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)

def fill(c):
    return np.broadcast_to(c, (N, N, 3)).copy()

def edge_dist():
    """Distance to the tile's nearest edge, 0..0.5."""
    return np.minimum(np.minimum(U, 1 - U), np.minimum(V, 1 - V))

def bevel(img, light=0.18, dark=0.25, width=0.045):
    """Lit top/left edges, shaded bottom/right edges (as the stock blocks look on their bevels)."""
    d = edge_dist()
    k = smooth(width, 0.0, d)
    top_left = (V < U) & (V < 1 - U) | (U < V) & (U < 1 - V)
    shade = np.where(top_left, 1 + light * k, 1 - dark * k)
    return img * shade[..., None]

def disc(cx, cy, r, soft=1.5 / N):
    d = np.sqrt((U - cx) ** 2 + (V - cy) ** 2)
    return smooth(r + soft, r - soft, d)

def line_mask(x0, y0, x1, y1, w):
    px, py = U - x0, V - y0
    dx, dy = x1 - x0, y1 - y0
    L = dx * dx + dy * dy
    t = np.clip((px * dx + py * dy) / L, 0, 1)
    d = np.sqrt((px - t * dx) ** 2 + (py - t * dy) ** 2)
    return smooth(w + 1.5 / N, w - 1.5 / N, d)

def rect_mask(x0, y0, x1, y1, soft=1.5 / N):
    return smooth(x0 - soft, x0 + soft, U) * smooth(x1 + soft, x1 - soft, U) * smooth(y0 - soft, y0 + soft, V) * smooth(y1 + soft, y1 - soft, V)

def crack_mask(seed, n, w=0.006, length=0.5, branch=True):
    """Random jagged cracks."""
    r = np.random.default_rng(seed)
    m = np.zeros((N, N), np.float32)
    for _ in range(n):
        x, y = r.random(2) * 0.8 + 0.1
        ang = r.random() * np.pi * 2
        steps = 7
        for s in range(steps):
            ang += r.normal(0, 0.6)
            seg = length / steps
            nx, ny = x + np.cos(ang) * seg, y + np.sin(ang) * seg
            m = np.maximum(m, line_mask(x, y, nx, ny, w * (1 - s / steps * 0.6)))
            if branch and r.random() < 0.3:
                ba = ang + r.choice([-1, 1]) * (0.6 + r.random())
                m = np.maximum(m, line_mask(nx, ny, nx + np.cos(ba) * seg * 0.8, ny + np.sin(ba) * seg * 0.8, w * 0.6))
            x, y = nx, ny
    return m

def save(name, tiles, alpha=None):
    """tiles: list of (N,N,3) arrays stacked top to bottom; alpha: matching list or None."""
    img = np.concatenate(tiles, 0)
    img = np.clip(img, 0, 1)
    if alpha is not None:
        a = np.concatenate(alpha, 0)
        rgba = np.dstack([img, np.clip(a, 0, 1)])
        im = Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), 'RGBA')
    else:
        im = Image.fromarray((img * 255 + 0.5).astype(np.uint8), 'RGB').convert('RGBA')
    im = im.resize((T, T * len(tiles)), Image.LANCZOS)
    im.save(os.path.join(OUT, name + '.png'))

# ------------------------------------------------------------------ wood pieces shared by several blocks

def wood_grain(seed, base, dark, rot=0.0, stretch=10):
    """Horizontal grain."""
    g = fbm(4, seed, 4, aniso=(1, stretch))
    rings = np.sin((V * 22 + g * 6) * np.pi) * 0.5 + 0.5
    streak = vnoise(64, seed + 5, aniso=(1, 8))
    t = rings * 0.55 + streak * 0.45
    return mix(base, dark, smooth(0.45, 0.95, t) * 0.85)

# ------------------------------------------------------------------ the blocks

def rotten(stage):
    """Rotten wood: grey-brown, damp, mossy, worm-holed; stage 1 = cracked (one shot taken)."""
    out = []
    for tile in range(2):
        base = col(146, 124, 92) if tile == 0 else col(158, 136, 102)
        img = wood_grain(11 + tile, base, col(92, 74, 54))
        # damp, darker rot creeping in from below and in patches
        rot = fbm(5, 40 + tile, 4)
        damp = smooth(0.48, 0.7, rot + (V - 0.5) * 0.35)
        img = mix(img, col(78, 66, 50), damp * 0.75)
        # moss on the top tile's upper edge and in the damp
        moss = smooth(0.62, 0.75, fbm(9, 70 + tile, 3)) * (smooth(0.35, 0.0, V) * (1 if tile == 0 else 0.3) + damp * 0.6)
        img = mix(img, col(104, 128, 62), moss * 0.9)
        # worm holes
        r = np.random.default_rng(90 + tile)
        for _ in range(14):
            x, y = r.random(2) * 0.84 + 0.08
            rr = 0.008 + r.random() * 0.01
            img = mix(img, col(40, 32, 24), disc(x, y, rr))
        # splits along the grain
        for k in range(3):
            y = 0.2 + k * 0.28 + r.random() * 0.08
            x0 = r.random() * 0.4; x1 = x0 + 0.25 + r.random() * 0.35
            img = mix(img, col(52, 42, 30), line_mask(x0, y, x1, y + r.normal(0, 0.02), 0.004))
        if stage == 1:
            # one shot taken: big splintered cracks with lighter, freshly broken wood beside them
            c = crack_mask(300 + tile, 3, w=0.011, length=0.75)
            near = crack_mask(300 + tile, 3, w=0.028, length=0.75)
            img = mix(img, col(196, 170, 120), (near - c) * 0.7)
            img = mix(img, col(30, 24, 18), c)
        out.append(bevel(img, 0.12, 0.3))
    return out

def glass():
    out = []; al = []
    for tile in range(2):
        d = edge_dist()
        # a pale tint, with diagonal light streaks; the frame is frosted and nearly opaque
        img = fill(col(176, 226, 240))
        streak = (np.sin((U + V) * np.pi * 3.2) * 0.5 + 0.5) ** 14 + ((np.sin((U + V + 0.18) * np.pi * 3.2) * 0.5 + 0.5) ** 30) * 0.7
        img = mix(img, col(255, 255, 255), streak * 0.85)
        frame = smooth(0.07, 0.055, d)
        img = mix(img, col(214, 236, 242), frame)
        inner = smooth(0.075, 0.07, d) * smooth(0.06, 0.07, d)
        img = mix(img, col(120, 170, 190), inner * 0.8)
        a = 0.38 + streak * 0.45 + frame * 0.55
        out.append(bevel(img, 0.25, 0.15)); al.append(np.clip(a, 0, 1))
    return out, al

def crate():
    out = []
    for tile in range(2):
        base = col(212, 168, 104) if tile == 0 else col(224, 182, 114)
        # three planks with gaps
        img = wood_grain(150 + tile, base, col(170, 120, 70), stretch=12)
        for k in (1, 2):
            img = mix(img, col(96, 64, 36), smooth(0.006, 0.0, np.abs(V - k / 3)))
        # frame boards around the edge and a diagonal brace
        d = edge_dist()
        frame = smooth(0.125, 0.118, d)
        fr = wood_grain(170 + tile, col(186, 136, 78), col(140, 96, 54), stretch=6)
        img = mix(img, fr, frame)
        img = mix(img, col(90, 60, 32), smooth(0.13, 0.12, d) * smooth(0.11, 0.12, d))
        brace = line_mask(0.12, 0.88, 0.88, 0.12, 0.055)
        img = mix(img, fr * 1.04, brace)
        img = mix(img, col(90, 60, 32), np.clip(line_mask(0.12, 0.88, 0.88, 0.12, 0.062) - brace, 0, 1))
        # nails in the corners
        for (x, y) in [(0.06, 0.06), (0.94, 0.06), (0.06, 0.94), (0.94, 0.94), (0.06, 0.5), (0.94, 0.5)]:
            img = mix(img, col(70, 70, 76), disc(x, y, 0.016))
            img = mix(img, col(170, 170, 178), disc(x - 0.004, y - 0.004, 0.006))
        out.append(bevel(img, 0.15, 0.3))
    return out

def barrel():
    """Wrapped around a drum: U goes round, V goes down. The red drum, two steel hoops, a hazard band and a flame sign."""
    out = []
    for tile in range(2):
        img = fill(col(196, 44, 36))
        # vertical sheen as it curves away from the light
        sheen = np.cos((U - 0.38) * np.pi * 2) * 0.5 + 0.5
        img = img * (0.72 + 0.4 * sheen[..., None])
        img = mix(img, col(255, 220, 210), smooth(0.93, 1.0, sheen) * 0.35)
        # hoops
        for y in (0.1, 0.9):
            h = smooth(0.035, 0.028, np.abs(V - y))
            img = mix(img, col(90, 90, 96) * (0.7 + 0.5 * sheen[..., None]), h)
        # yellow and black hazard band
        band = smooth(0.11, 0.105, np.abs(V - 0.5))
        stripes = (np.floor((U * 14 + V * 3)) % 2 == 0)
        hz = np.where(stripes[..., None], col(250, 200, 30), col(30, 28, 26)) * (0.75 + 0.35 * sheen[..., None])
        img = mix(img, hz, band)
        # a flame sign on a white diamond, front and back
        for cx in (0.38, 0.88):
            dd = (np.abs(U - cx) + np.abs(V - 0.27)) / 0.15
            img = mix(img, col(250, 248, 240), smooth(1.05, 0.95, dd))
            img = mix(img, col(30, 28, 26), smooth(1.08, 1.0, dd) * smooth(0.95, 1.0, dd))
            fl = np.sqrt(((U - cx) / 0.045) ** 2 + ((V - 0.3) / 0.06) ** 2)
            tip = line_mask(cx, 0.21, cx, 0.3, 0.018)
            img = mix(img, col(230, 60, 30), np.maximum(smooth(1.05, 0.95, fl), tip))
            img = mix(img, col(255, 200, 40), smooth(0.55, 0.45, np.sqrt(((U - cx) / 0.025) ** 2 + ((V - 0.32) / 0.032) ** 2)))
        # scuffs
        img = mix(img, col(120, 30, 24), smooth(0.7, 0.85, fbm(8, 501 + tile, 3)) * 0.5)
        out.append(img)
    # the lid: steel ring around a red top with a bung
    lid = fill(col(178, 40, 34))
    r = np.sqrt((U - 0.5) ** 2 + (V - 0.5) ** 2)
    lid = mix(lid, col(120, 122, 130), smooth(0.43, 0.45, r))
    lid = mix(lid, col(160, 30, 26), smooth(0.38, 0.36, r) * smooth(0.3, 0.32, r))
    lid = mix(lid, col(110, 110, 118), disc(0.68, 0.38, 0.06))
    lid = mix(lid, col(60, 60, 66), disc(0.68, 0.38, 0.03))
    lid = mix(lid, col(255, 230, 220), smooth(0.25, 0.0, np.sqrt((U - 0.36) ** 2 + (V - 0.32) ** 2)) * 0.3)
    return out, lid

def ice(stage):
    out = []; al = []
    for tile in range(2):
        g = fbm(4, 600 + tile, 4)
        img = mix(col(186, 228, 246), col(120, 188, 226), smooth(0.3, 0.8, g))
        # frost at the edges, cloudy bubbles inside
        d = edge_dist()
        img = mix(img, col(240, 250, 255), smooth(0.12, 0.0, d) * 0.7)
        img = mix(img, col(230, 246, 255), smooth(0.7, 0.85, fbm(12, 650 + tile, 3)) * 0.5)
        streak = (np.sin((U - V) * np.pi * 2.6 + 0.6) * 0.5 + 0.5) ** 18
        img = mix(img, col(255, 255, 255), streak * 0.7)
        a = 0.8 + smooth(0.12, 0.0, d) * 0.2
        if stage >= 1:
            c = crack_mask(700 + tile + stage * 10, 2 + stage * 2, w=0.005, length=0.6 + stage * 0.1)
            halo = crack_mask(700 + tile + stage * 10, 2 + stage * 2, w=0.016, length=0.6 + stage * 0.1)
            img = mix(img, col(250, 254, 255), (halo - c) * 0.8)
            img = mix(img, col(80, 140, 180), c)
        out.append(bevel(img, 0.3, 0.12)); al.append(np.clip(a, 0, 1))
    return out, al

def steel():
    out = []
    for tile in range(2):
        brushed = vnoise(128, 800 + tile, aniso=(1, 1)) * 0.0 + vnoise(8, 810 + tile, aniso=(1, 24))
        img = mix(col(120, 128, 140), col(160, 168, 180), brushed)
        # a raised diamond tread
        tu = (U * 8) % 1 - 0.5; tv = (V * 8) % 1 - 0.5
        row = np.floor(V * 8) % 2
        tread = np.where(row == 0, np.abs(tu - tv) < 0.12, np.abs(tu + tv) < 0.12) & (np.abs(tu) < 0.35) & (np.abs(tv) < 0.35)
        img = np.where(tread[..., None], img * 1.22, img)
        img = np.where(np.roll(tread, 3, axis=0)[..., None] & ~tread[..., None], img * 0.82, img)
        # a thick border plate with rivets
        d = edge_dist()
        img = mix(img, col(96, 102, 114), smooth(0.105, 0.1, d))
        img = mix(img, col(70, 74, 84), smooth(0.105, 0.1, d) * smooth(0.095, 0.1, d))
        for (x, y) in [(0.05, 0.05), (0.95, 0.05), (0.05, 0.95), (0.95, 0.95), (0.5, 0.05), (0.5, 0.95), (0.05, 0.5), (0.95, 0.5)]:
            img = mix(img, col(60, 62, 70), disc(x + 0.004, y + 0.004, 0.022))
            img = mix(img, col(176, 182, 194), disc(x, y, 0.02))
            img = mix(img, col(230, 234, 240), disc(x - 0.006, y - 0.006, 0.007))
        out.append(bevel(img, 0.3, 0.35))
    return out

def rubber():
    out = []
    for tile in range(2):
        img = fill(col(46, 46, 52)) * (0.9 + 0.2 * fbm(16, 900 + tile, 2))[..., None]
        # ribbed grooves
        rib = np.abs(((V * 6) % 1) - 0.5)
        img = mix(img, col(26, 26, 30), smooth(0.07, 0.04, rib))
        img = mix(img, col(78, 78, 88), smooth(0.11, 0.08, rib) * smooth(0.06, 0.08, rib))
        # a bright bumper ring: a yellow circle (like a playground bumper)
        r = np.sqrt((U - 0.5) ** 2 + (V - 0.5) ** 2)
        ring = smooth(0.3, 0.29, r) * smooth(0.22, 0.23, r)
        img = mix(img, col(250, 196, 40), ring)
        img = mix(img, col(255, 240, 160), ring * smooth(0.35, 0.0, np.sqrt((U - 0.38) ** 2 + (V - 0.32) ** 2)) * 0.8)
        img = mix(img, col(40, 40, 46), smooth(0.22, 0.21, r) * 0.0 + smooth(0.215, 0.2, r) * 0.0)
        # soft rounded edges (rubber, not metal)
        d = edge_dist()
        img = img * (1 - smooth(0.08, 0.0, d) * 0.35)[..., None]
        img = mix(img, col(110, 110, 120), smooth(0.03, 0.0, np.abs(d - 0.06)) * 0.3)
        out.append(img)
    return out

def mirror():
    out = []
    for tile in range(2):
        d = edge_dist()
        # a polished face: sky above, horizon, a dark band below, with glints
        sky = mix(col(232, 240, 252), col(150, 176, 210), V)
        img = sky
        img = mix(img, col(70, 80, 96), smooth(0.58, 0.62, V + (U - 0.5) * 0.15) * 0.9)
        streak = (np.sin((U + V * 0.5) * np.pi * 2.4) * 0.5 + 0.5) ** 20
        img = mix(img, col(255, 255, 255), streak * 0.8)
        # a brass frame
        fr = smooth(0.105, 0.1, d)
        brass = mix(col(160, 120, 50), col(236, 200, 110), smooth(0.0, 0.1, d))
        img = mix(img, brass, fr)
        img = mix(img, col(110, 80, 30), smooth(0.105, 0.1, d) * smooth(0.092, 0.1, d))
        out.append(bevel(img, 0.3, 0.3))
    return out

def hay():
    out = []
    for tile in range(2):
        # straw: many thin strands, mostly lengthwise
        img = fill(col(222, 186, 96))
        r = np.random.default_rng(1000 + tile)
        strands = np.zeros((N, N), np.float32); dark = np.zeros((N, N), np.float32)
        for k in range(160):
            x, y = r.random(2)
            ang = r.normal(0, 0.25)
            L = 0.15 + r.random() * 0.3
            m = line_mask(x, y, x + np.cos(ang) * L, y + np.sin(ang) * L, 0.0035)
            if r.random() < 0.65: strands = np.maximum(strands, m)
            else: dark = np.maximum(dark, m)
        img = mix(img, col(250, 226, 140), strands * 0.9)
        img = mix(img, col(170, 128, 52), dark * 0.8)
        img = img * (0.85 + 0.25 * fbm(6, 1050 + tile, 3))[..., None]
        # two twine bands
        for x in (0.3, 0.7):
            band = smooth(0.022, 0.015, np.abs(U - x))
            tw = mix(col(120, 80, 40), col(176, 126, 70), (np.sin(V * 120) * 0.5 + 0.5))
            img = mix(img, tw, band)
        # loose ends at the edges
        d = edge_dist()
        img = img * (1 - smooth(0.06, 0.0, d) * 0.25)[..., None]
        out.append(img)
    return out

def grate():
    out = []; al = []
    for tile in range(2):
        img = fill(col(92, 98, 108))
        # a heavy frame and a grid of square bars; the holes are see-through
        bars = 6
        bu = np.abs((U * bars) % 1 - 0.5); bv = np.abs((V * bars) % 1 - 0.5)
        hole = (bu < 0.32) & (bv < 0.32)
        d = edge_dist()
        frame = d < 0.07
        hole &= ~frame
        # bars are round: lighter along their middles
        lit = np.where(bu >= 0.32, 1 - (0.5 - bu) / 0.18 * 0.0, 1.0)
        shade = 0.85 + 0.35 * np.cos(np.minimum(np.where(bu >= 0.32, 0.5 - bu, 1), np.where(bv >= 0.32, 0.5 - bv, 1)) / 0.18 * np.pi / 2) * 0
        img = img * np.where(bu >= 0.32, 1.0 + 0.3 * (1 - (0.5 - bu) / 0.18), 1.0)[..., None]
        img = img * np.where(bv >= 0.32, 1.0 + 0.2 * (1 - (0.5 - bv) / 0.18), 1.0)[..., None]
        img = np.where(frame[..., None], col(80, 86, 96), img)
        img = mix(img, col(140, 104, 70), smooth(0.68, 0.82, fbm(8, 1100 + tile, 3)) * 0.5)  # rust
        a = np.where(hole, 0.0, 1.0)
        out.append(bevel(img, 0.25, 0.3)); al.append(a)
    return out, al

def bush():
    """Leaves, with a ragged see-through edge."""
    out = []; al = []
    for tile in range(2):
        img = fill(col(58, 112, 48))
        r = np.random.default_rng(1200 + tile)
        leaf_shade = np.zeros((N, N), np.float32)
        leaf_lit = np.zeros((N, N), np.float32)
        for k in range(130):
            x, y = r.random(2)
            ang = r.random() * np.pi
            L = 0.05 + r.random() * 0.05
            m = np.zeros((N, N), np.float32)
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    m = np.maximum(m, line_mask(x + dx, y + dy, x + dx + np.cos(ang) * L, y + dy + np.sin(ang) * L, 0.022))
            if r.random() < 0.5: leaf_lit = np.maximum(leaf_lit, m * (0.6 + r.random() * 0.4))
            else: leaf_shade = np.maximum(leaf_shade, m)
        img = mix(img, col(36, 78, 34), leaf_shade * 0.8)
        img = mix(img, col(118, 176, 72), leaf_lit * 0.9)
        img = img * (0.8 + 0.3 * (1 - V * 0.6))[..., None]
        # a few berries
        for _ in range(5):
            x, y = r.random(2) * 0.8 + 0.1
            img = mix(img, col(200, 40, 50), disc(x, y, 0.014))
        d = edge_dist()
        a = np.clip(smooth(0.0, 0.05, d) + (leaf_lit + leaf_shade) * 0.8, 0, 1)
        out.append(img); al.append(a)
    return out, al

def mud():
    g = fbm(4, 1300, 5)
    img = mix(col(104, 76, 50), col(70, 52, 36), smooth(0.35, 0.7, g))
    # wet puddles that catch the light
    p = fbm(3, 1310, 4)
    pud = smooth(0.6, 0.64, p)
    img = mix(img, col(64, 52, 42), pud)
    img = mix(img, col(200, 196, 190), pud * ((np.sin((U + V) * np.pi * 5) * 0.5 + 0.5) ** 20) * 0.6)
    # tyre ruts
    for x in (0.28, 0.72):
        rut = smooth(0.06, 0.04, np.abs(U - x - (vnoise(3, 1320, aniso=(1, 1)) - 0.5) * 0.04))
        img = mix(img, col(58, 42, 28), rut * 0.7)
        tread = (np.floor(V * 22) % 2 == 0) & (np.abs(U - x) < 0.04)
        img = np.where(tread[..., None], img * 0.82, img)
    # soft edge where it meets the floor
    d = edge_dist()
    a = smooth(0.0, 0.08, d + (fbm(8, 1330, 3) - 0.5) * 0.08)
    return img, a

def icefloor():
    g = fbm(3, 1400, 5)
    img = mix(col(200, 236, 250), col(150, 206, 236), smooth(0.3, 0.75, g))
    # skate scratches and a shine
    r = np.random.default_rng(1410)
    for _ in range(18):
        x, y = r.random(2); ang = r.random() * np.pi; L = 0.2 + r.random() * 0.4
        img = mix(img, col(250, 254, 255), line_mask(x, y, x + np.cos(ang) * L, y + np.sin(ang) * L, 0.0025) * 0.7)
    img = mix(img, col(255, 255, 255), ((np.sin((U - V) * np.pi * 2) * 0.5 + 0.5) ** 16) * 0.6)
    img = mix(img, col(120, 180, 220), crack_mask(1420, 3, w=0.003, length=0.4) * 0.6)
    d = edge_dist()
    a = smooth(0.0, 0.06, d)
    return img, a

def conveyor(frame):
    """A belt with chevrons pointing up the tile; frame shifts them (for movement)."""
    img = fill(col(48, 50, 56))
    # rubber texture and seams
    img = img * (0.9 + 0.2 * vnoise(24, 1500, aniso=(1, 1)))[..., None]
    vv = (V + frame / 4 / 3) % 1
    seam = np.abs(((vv * 6) % 1) - 0.5)
    img = mix(img, col(30, 30, 34), smooth(0.04, 0.02, 0.5 - seam))
    # yellow chevrons
    for k in range(3):
        cy = (k + 0.5) / 3
        dvv = ((vv - cy + 0.5) % 1) - 0.5
        chev = np.abs(dvv + np.abs(U - 0.5) * 0.7) < 0.045
        img = np.where((chev & (np.abs(U - 0.5) < 0.3))[..., None], col(250, 200, 40), img)
    # steel side rails with rollers
    rail = (U < 0.09) | (U > 0.91)
    rollers = (np.abs(((V * 8) % 1) - 0.5) < 0.18)
    img = np.where(rail[..., None], np.where(rollers[..., None], col(170, 176, 186), col(100, 106, 116)), img)
    return img

def gate(open_):
    """Closed: iron bars over a dark gap in a stone frame. Open: the frame with its bars slid down into the floor."""
    out = []; al = []
    for tile in range(2):
        d = edge_dist()
        stone = mix(col(150, 146, 140), col(118, 114, 110), fbm(6, 1600 + tile, 4))
        # stone frame
        frame = (U < 0.16) | (U > 0.84) | (V < 0.12 if tile == 0 else np.zeros_like(U, bool))
        img = fill(col(24, 22, 22)) if not open_ else fill(col(24, 22, 22))
        img = np.where(frame[..., None], stone, img)
        # mortar lines in the frame
        mort = (np.abs(((V * 4) % 1) - 0.5) > 0.47) & frame
        img = np.where(mort[..., None], img * 0.7, img)
        a = np.ones((N, N), np.float32)
        if not open_:
            bu = np.abs(((U - 0.16) / 0.68 * 5) % 1 - 0.5)
            inside = ~frame
            bar = inside & (bu > 0.33)
            cross = inside & (np.abs(V - 0.5) < 0.04)
            shade = 0.8 + 0.6 * (bu - 0.33) / 0.17
            iron = col(84, 88, 98) * np.where(bar, shade, 1.0)[..., None]
            img = np.where((bar | cross)[..., None], iron, img)
            if tile == 1:
                spike = inside & (bu > 0.33) & (V > 0.9)
                img = np.where(spike[..., None], col(150, 156, 168), img)
            a = np.where(inside & ~(bar | cross), 0.55, 1.0)
        else:
            a = np.where(~frame, 0.0, 1.0)
        # a yellow stripe on the frame shows it moves
        stripe = frame & (np.abs(U - 0.5) > 0.34) & (np.abs(U - 0.5) < 0.37)
        img = np.where(stripe[..., None], col(250, 200, 40), img)
        out.append(bevel(img, 0.2, 0.3)); al.append(a)
    return out, al

def spikes(up):
    """A steel plate with holes; raised, a spike stands in each (drawn as a model; here its tip shows)."""
    img = fill(col(108, 112, 122)) * (0.9 + 0.15 * fbm(10, 1700, 3))[..., None]
    d = edge_dist()
    img = mix(img, col(250, 200, 40), smooth(0.07, 0.065, d) * (np.floor((U + V) * 10) % 2 == 0))
    img = mix(img, col(30, 28, 26), smooth(0.07, 0.065, d) * (np.floor((U + V) * 10) % 2 == 1))
    for i in range(3):
        for j in range(3):
            cx, cy = 0.25 + i * 0.25, 0.25 + j * 0.25
            img = mix(img, col(30, 30, 34), disc(cx, cy, 0.06))
            if up:
                # a cone seen from above: lit on the upper left
                rr = np.sqrt((U - cx) ** 2 + (V - cy) ** 2)
                ang = np.arctan2(V - cy, U - cx)
                lit = 0.6 + 0.4 * np.cos(ang + 2.4)
                img = mix(img, col(200, 206, 216) * lit[..., None], smooth(0.058, 0.052, rr))
                img = mix(img, col(255, 255, 255), disc(cx, cy, 0.008))
    return img

def lava(frame):
    t = frame / 4
    g = fbm(4, 1800, 5) + vnoise(3, 1810) * 0.3 * np.sin(t * np.pi * 2)
    hot = smooth(0.35, 0.75, g)
    img = mix(col(200, 40, 10), col(255, 200, 60), hot)
    # a dark crust floating on top, cracked
    # (the crust drifts slowly across, wrapping round, so the four frames loop)
    cr = np.roll(fbm(5, 1820, 4), frame * N // 16, axis=1)
    crust = smooth(0.55, 0.62, cr)
    img = mix(img, col(48, 30, 26), crust)
    # the crust's cracked edges glow
    img = mix(img, col(255, 130, 30), smooth(0.03, 0.0, np.abs(cr - 0.555)) * 0.8)
    img = mix(img, col(255, 240, 160), (hot ** 6) * 0.6)
    # a stone lip around it
    d = edge_dist()
    lip = smooth(0.06, 0.05, d)
    img = mix(img, col(70, 56, 50), lip)
    img = mix(img, col(255, 110, 40), smooth(0.08, 0.06, d) * (1 - lip) * 0.6)
    return img

def magnet():
    out = []
    for tile in range(2):
        img = steel()[tile] * 0.92
        # a horseshoe magnet: red arm, blue arm, silver tips
        r = np.sqrt((U - 0.5) ** 2 + (V - 0.42) ** 2)
        arc = (smooth(0.3, 0.29, r) * smooth(0.15, 0.16, r)) * (V < 0.42)
        armL = rect_mask(0.21, 0.42, 0.35, 0.78); armR = rect_mask(0.65, 0.42, 0.79, 0.78)
        shape = np.clip(arc + armL + armR, 0, 1)
        red = U < 0.5
        mag = np.where(red[..., None], col(220, 50, 46), col(48, 96, 220))
        mag = mag * (0.8 + 0.4 * smooth(0.3, 0.15, r))[..., None]
        img = mix(img, mag, shape)
        tips = rect_mask(0.21, 0.7, 0.35, 0.8) + rect_mask(0.65, 0.7, 0.79, 0.8)
        img = mix(img, col(220, 224, 232), np.clip(tips, 0, 1))
        # field lines
        for k in range(3):
            rr = 0.36 + k * 0.05
            fl = smooth(0.006, 0.0, np.abs(np.sqrt((U - 0.5) ** 2 + (V - 0.95) ** 2) - rr * 0.6)) * (V < 0.95) * (V > 0.62)
            img = mix(img, col(255, 236, 110), fl * 0.7)
        out.append(img)
    return out

def plate(pressed):
    img = mix(col(140, 136, 130), col(116, 112, 108), fbm(5, 1900, 4))
    d = edge_dist()
    img = mix(img, col(90, 86, 82), smooth(0.03, 0.0, d))
    inner = rect_mask(0.18, 0.18, 0.82, 0.82)
    top = col(250, 200, 40) * (0.75 if pressed else 1.0)
    img = mix(img, top * (0.9 + 0.15 * fbm(10, 1910, 2))[..., None], inner)
    img = mix(img, col(60, 50, 30), np.clip(rect_mask(0.17, 0.17, 0.83, 0.83) - inner, 0, 1))
    # a hand-press arrow icon
    ar = np.clip(line_mask(0.5, 0.3, 0.5, 0.62, 0.035) + np.clip(line_mask(0.36, 0.5, 0.5, 0.68, 0.035) + line_mask(0.64, 0.5, 0.5, 0.68, 0.035), 0, 1), 0, 1)
    img = mix(img, col(40, 36, 30), ar * 0.85)
    return img

# ------------------------------------------------------------------ write them all

save('block_rotten', rotten(0))
save('block_rotten_cracked', rotten(1))
t, a = glass(); save('block_glass', t, a)
save('block_crate', crate())
t, lid = barrel(); save('block_barrel', t); save('block_barrel_lid', [lid])
for s in range(3):
    t, a = ice(s); save(f'block_ice_{s}', t, a)
save('block_steel', steel())
save('block_rubber', rubber())
save('block_mirror', mirror())
save('block_hay', hay())
t, a = grate(); save('block_grate', t, a)
t, a = bush(); save('block_bush', t, a)
img, a = mud(); save('floor_mud', [img], [a])
img, a = icefloor(); save('floor_ice', [img], [a])
for f in range(4): save(f'floor_conveyor_{f}', [conveyor(f)])
t, a = gate(False); save('block_gate_closed', t, a)
t, a = gate(True); save('block_gate_open', t, a)
save('floor_spikes_down', [spikes(False)]); save('floor_spikes_up', [spikes(True)])
for f in range(4): save(f'floor_lava_{f}', [lava(f)])
save('block_magnet', magnet())
save('floor_plate', [plate(False)]); save('floor_plate_pressed', [plate(True)])
print('done', len(os.listdir(OUT)))
