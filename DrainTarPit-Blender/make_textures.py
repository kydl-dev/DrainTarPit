# DrainTarPit - procedural tileable PBR textures (leather / iron / wood / rope)
# Pure numpy + zlib, so the output is bit-identical in Blender and in plain Python.
# Per material it writes:
#   DTP_<Name>_BaseColor.png   RGB   sRGB albedo
#   DTP_<Name>_Normal.png      RGB   tangent space, OpenGL (Y+), linear
#   DTP_<Name>_Mask.png        RGBA  R=metallic G=AO B=0 A=smoothness   (HDRP MaskMap, also Standard/Custom-Piece metal-gloss)
# Usage: build_all(out_dir, n=1024, preview_dir=None)

import os
import sys
import zlib
import struct
import numpy as np


# ------------------------------------------------------------------ io
def write_png(path, arr):
    h, w, c = arr.shape
    ct = {3: 2, 4: 6}[c]
    raw = b"".join(b"\x00" + arr[y].tobytes() for y in range(h))

    def chunk(t, d):
        body = struct.pack(">I", len(d)) + t + d
        return body + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ct, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 6))
           + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(png)


def to_u8(a):
    return (np.clip(a, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


# ------------------------------------------------------------------ math helpers
def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def norm01(a, lo=1.0, hi=99.0):
    l, h = np.percentile(a, [lo, hi])
    return np.clip((a - l) / (h - l + 1e-9), 0.0, 1.0).astype(np.float32)


def _freqs(n):
    f = np.fft.fftfreq(n) * n  # cycles per tile
    fx, fy = np.meshgrid(f, f)
    return fx, fy


def noise(n, seed, beta=2.0, kmin=1.0, kmax=None, angle=None, ka=1.0, kc=1.0, ax=1.0, ay=1.0):
    # Tileable spectral noise, power ~ 1/f^beta, band limited in cycles per tile.
    # ax/ay stretch features: a LARGE ay makes features long along Y (slow variation along Y).
    # angle/ka/kc: same idea along a rotated axis (ka large -> features long along `angle`).
    r = np.random.default_rng(seed)
    F = np.fft.fft2(r.standard_normal((n, n)))
    fx, fy = _freqs(n)
    if angle is None:
        fr = np.sqrt((fx * ax) ** 2 + (fy * ay) ** 2)
    else:
        c, s = np.cos(angle), np.sin(angle)
        fa = fx * c + fy * s
        fc = -fx * s + fy * c
        fr = np.sqrt((fa * ka) ** 2 + (fc * kc) ** 2)
    fr[0, 0] = 1.0
    amp = fr ** (-beta / 2.0)
    amp = amp * np.where(fr < kmin, (fr / kmin) ** 4, 1.0)
    if kmax:
        amp = amp * np.exp(-((fr / kmax) ** 4))
    amp[0, 0] = 0.0
    a = np.real(np.fft.ifft2(F * amp))
    return (a / (a.std() + 1e-9)).astype(np.float32)


def blur(a, sigma):
    n = a.shape[0]
    fx, fy = _freqs(n)
    fx = fx / n
    fy = fy / n
    g = np.exp(-2.0 * (np.pi ** 2) * sigma ** 2 * (fx ** 2 + fy ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g)).astype(np.float32)


def worley(n, cells, seed):
    # Tileable cellular noise. Returns F1, F2 in cell units.
    r = np.random.default_rng(seed)
    pts = 0.1 + 0.8 * r.random((cells, cells, 2))
    cs = n / cells
    yy, xx = np.mgrid[0:n, 0:n]
    cx = (xx + 0.5) / cs
    cy = (yy + 0.5) / cs
    ix = np.floor(cx).astype(int)
    iy = np.floor(cy).astype(int)
    f1 = np.full((n, n), 1e9, np.float32)
    f2 = np.full((n, n), 1e9, np.float32)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            jx = (ix + dx) % cells
            jy = (iy + dy) % cells
            px = (ix + dx) + pts[jy, jx, 0]
            py = (iy + dy) + pts[jy, jx, 1]
            d = np.sqrt((cx - px) ** 2 + (cy - py) ** 2).astype(np.float32)
            m = d < f1
            f2 = np.where(m, f1, np.minimum(f2, d))
            f1 = np.where(m, d, f1)
    return f1, f2


def scratches(n, seed, count, length=(40, 230)):
    r = np.random.default_rng(seed)
    img = np.zeros((n, n), np.float32)
    for _ in range(count):
        x0, y0 = r.random(2) * n
        ang = r.normal(0.0, 0.45) + (np.pi / 2 if r.random() < 0.28 else 0.0)
        L = r.uniform(*length)
        t = np.linspace(0, L, int(L * 1.5))
        xs = ((x0 + np.cos(ang) * t) % n).astype(int)
        ys = ((y0 + np.sin(ang) * t) % n).astype(int)
        img[ys, xs] += r.uniform(0.4, 1.0)
    return np.clip(blur(img, 0.75) * 5.0, 0.0, 1.0)


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    drow = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx = -dx * strength
    ny = drow * strength  # image rows go down, texture V goes up -> OpenGL green up
    nz = np.ones_like(nx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.dstack([nx / ln, ny / ln, nz / ln]) * 0.5 + 0.5


def cavity_ao(h, radius, gain, floor=0.2):
    return 1.0 - np.clip((blur(h, radius) - h) * gain, 0.0, 1.0 - floor)


def col(*rgb):
    return np.array(rgb, np.float32)[None, None, :]


# ------------------------------------------------------------------ materials
def leather(n, seed=11):
    f1, f2 = worley(n, 44, seed)
    edge = smoothstep(0.0, 0.32, f2 - f1)
    dome = 1.0 - np.clip(f1 / 0.75, 0.0, 1.0) ** 2
    grain = edge * (0.55 + 0.45 * dome)
    sn = noise(n, seed + 2, beta=2.0, kmin=3, kmax=22)
    crease = (1.0 - smoothstep(0.0, 0.22, np.abs(sn))) ** 1.5
    wrinkle = norm01(noise(n, seed + 1, beta=2.6, kmin=2, kmax=9))
    fine = noise(n, seed + 3, beta=0.8, kmin=60, kmax=260) * 0.025
    h = grain * (1.0 - 0.75 * crease) * 0.55 + 0.30 * wrinkle - 0.28 * crease + fine
    h = norm01(h, 0.5, 99.5)

    t = norm01(noise(n, seed + 4, beta=2.8, kmin=1.5, kmax=7))
    tar = smoothstep(0.54, 0.70, t * 0.75 + 0.35 * (1.0 - h))
    tint = noise(n, seed + 5, beta=2.0, kmin=2, kmax=30)
    dry = col(0.30, 0.195, 0.125) * (1.0 + 0.10 * tint[..., None] * col(1.0, 0.9, 0.8))
    dry = dry * (0.50 + 0.50 * h ** 0.8)[..., None]
    wear = smoothstep(0.72, 0.95, h) * (1.0 - tar)
    dry = dry + wear[..., None] * col(0.10, 0.075, 0.05)
    tarc = col(0.060, 0.052, 0.050) * (0.8 + 0.4 * h[..., None])
    k = (0.93 * tar)[..., None]
    alb = dry * (1.0 - k) + tarc * k

    rd = 0.80 - 0.25 * smoothstep(0.7, 0.95, h) + 0.04 * tint
    rt = 0.26 + 0.10 * norm01(noise(n, seed + 6, beta=1.5, kmin=4, kmax=50))
    rough = rd * (1.0 - tar) + rt * tar
    return dict(albedo=alb, rough=rough, metal=np.zeros_like(h), ao=cavity_ao(h, 4, 6.0),
                height=h, nstrength=14.0, stat=float(tar.mean()))


def iron(n, seed=21):
    f1, f2 = worley(n, 12, seed)
    dimple = smoothstep(0.0, 0.85, f1)
    dents = norm01(noise(n, seed + 1, beta=2.6, kmin=2, kmax=14))
    fine = noise(n, seed + 2, beta=1.2, kmin=40, kmax=300)
    pits = noise(n, seed + 3, beta=0.6, kmin=100, kmax=400)
    pitmask = smoothstep(1.7, 2.5, pits)
    sc = scratches(n, seed + 4, 320)
    h = 0.45 * dents + 0.30 * dimple + 0.04 * fine - 0.45 * pitmask - 0.12 * sc
    h = norm01(h, 0.5, 99.5)

    r = norm01(noise(n, seed + 5, beta=2.8, kmin=2, kmax=12))
    pitblur = np.clip(blur(pitmask, 3) * 6.0, 0.0, 1.0)
    rust = smoothstep(0.62, 0.78, r * 0.85 + 0.30 * pitblur + 0.15 * (1.0 - h))

    f01 = norm01(fine)
    metal_c = col(0.24, 0.24, 0.26) * (0.8 + 0.4 * f01[..., None])
    metal_c = metal_c + sc[..., None] * 0.22
    rv = norm01(noise(n, seed + 7, beta=2.0, kmin=3, kmax=40))[..., None]
    rust_c = col(0.42, 0.20, 0.085) * (1.0 - rv) + col(0.25, 0.10, 0.04) * rv
    alb = metal_c * (1.0 - rust[..., None]) + rust_c * rust[..., None]
    alb = alb * (1.0 - 0.6 * pitmask)[..., None]
    alb = alb * (0.65 + 0.35 * h)[..., None]

    m_clean = 0.93 - 0.06 * f01
    metal = m_clean * (1.0 - rust) + 0.10 * rust
    metal = np.maximum(metal, sc * 0.97 * (1.0 - rust))
    rc = 0.38 + 0.20 * norm01(noise(n, seed + 6, beta=1.8, kmin=3, kmax=40))
    rc = rc * (1.0 - 0.4 * sc)
    rough = rc * (1.0 - rust) + 0.88 * rust
    return dict(albedo=alb, rough=rough, metal=metal, ao=cavity_ao(h, 3, 5.0),
                height=h, nstrength=10.0, stat=float(rust.mean()))


def wood(n, seed=31, planks=4):
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:n, 0:n]
    u_all = xx / n * planks
    pid = np.minimum(u_all.astype(int), planks - 1)
    u = (u_all - pid).astype(np.float32)
    ptint = r.uniform(0.82, 1.12, planks)[pid]
    pphase = r.random(planks)[pid]
    pfreq = r.uniform(5.5, 9.0, planks)[pid]
    warp = noise(n, seed + 1, beta=2.6, kmin=1.5, kmax=7, ax=1.0, ay=5.0)
    g = u * pfreq + pphase + 1.4 * warp

    kdark = np.zeros((n, n), np.float32)
    pw = n / planks
    for _ in range(2):
        p = r.integers(0, planks)
        cx = (p + r.uniform(0.3, 0.7)) * pw
        cy = r.random() * n
        dx = xx - cx
        dy = ((yy - cy + n / 2) % n) - n / 2
        r2 = (dx / (pw * 0.12)) ** 2 + (dy / (pw * 0.26)) ** 2
        g = g + 2.8 * np.exp(-r2 * 0.5)
        kdark = kdark + np.exp(-r2 * 2.0)
    kdark = np.clip(kdark, 0.0, 1.0).astype(np.float32)

    ring = g % 1.0
    late = smoothstep(0.62, 0.97, ring)
    fib = norm01(noise(n, seed + 2, beta=1.1, kmin=25, kmax=230, ax=1.0, ay=16.0))
    pores = smoothstep(1.4, 2.4, noise(n, seed + 3, beta=0.5, kmin=60, kmax=300, ax=1.0, ay=6.0))
    edge = np.minimum(u, 1.0 - u)
    seam = 1.0 - smoothstep(0.0, 0.035, edge)
    bevel = 1.0 - smoothstep(0.035, 0.08, edge)
    crk = np.zeros((n, n), np.float32)
    for p in range(planks):
        if r.random() < 0.7:
            c = r.uniform(0.25, 0.75)
            d = np.abs(u - c - 0.04 * warp)
            line = (1.0 - smoothstep(0.0, 0.006, d)) * (pid == p)
            seg = smoothstep(0.3, 0.9, norm01(noise(n, seed + 10 + p, beta=2.5, kmin=1, kmax=4, ax=1.0, ay=3.0)))
            crk = crk + line * seg
    crk = np.clip(crk, 0.0, 1.0).astype(np.float32)
    sc_ = np.clip(seam + crk, 0.0, 1.0)

    h = 0.55 - 0.22 * late + 0.10 * (fib - 0.5) - 0.40 * seam - 0.12 * bevel - 0.35 * crk - 0.12 * pores - 0.10 * kdark
    h = norm01(h, 0.5, 99.5)

    base = col(0.255, 0.158, 0.090)
    dark = col(0.105, 0.060, 0.034)
    c = base + (dark - base) * late[..., None]
    c = c * (0.88 + 0.24 * fib)[..., None] * ptint[..., None]
    c = c * (1.0 - 0.5 * kdark)[..., None]
    weather = smoothstep(0.55, 0.8, norm01(noise(n, seed + 4, beta=2.4, kmin=1.5, kmax=9, ax=1.0, ay=3.0)))
    grey = col(0.20, 0.17, 0.145)
    c = c * (1.0 - 0.35 * weather[..., None]) + grey * 0.35 * weather[..., None]
    c = c * (1.0 - 0.88 * sc_)[..., None]
    c = c * (1.0 - 0.35 * pores)[..., None]

    rough = np.clip(0.74 + 0.14 * fib + 0.12 * pores + 0.06 * weather, 0.0, 1.0)
    ao = cavity_ao(h, 3, 5.0) * (1.0 - 0.6 * sc_)
    return dict(albedo=c, rough=rough, metal=np.zeros_like(h), ao=ao,
                height=h, nstrength=9.0, stat=float(sc_.mean()))


def rope(n, seed=41, strands=3, twist=4):
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:n, 0:n]
    x = xx / n
    y = yy / n
    ph = strands * x + twist * y
    sid = np.floor(ph).astype(int) % strands
    f = (ph % 1.0).astype(np.float32)
    prof = np.sin(np.pi * f) ** 0.65
    ang = np.arctan2(-strands, twist)
    fib1 = norm01(noise(n, seed + 1, beta=1.0, kmin=30, kmax=260, angle=ang, ka=14.0, kc=1.0))
    fib2 = norm01(noise(n, seed + 2, beta=1.0, kmin=40, kmax=300, angle=ang + np.deg2rad(22), ka=10.0, kc=1.0))
    fuzz = noise(n, seed + 3, beta=0.6, kmin=100, kmax=380)
    sv = r.uniform(0.9, 1.1, strands)[sid]
    h = 0.78 * prof * (0.82 + 0.36 * fib1) + 0.16 * fib2 + 0.03 * fuzz
    h = norm01(h, 0.5, 99.5)

    hemp = col(0.50, 0.375, 0.215)
    valley = col(0.17, 0.115, 0.065)
    t = (prof ** 1.2)[..., None]
    c = valley + (hemp - valley) * t
    c = c * (0.82 + 0.36 * fib1)[..., None] * sv[..., None]
    c = c * (0.9 + 0.2 * fib2)[..., None]
    dirty = smoothstep(0.55, 0.85, norm01(noise(n, seed + 4, beta=2.4, kmin=2, kmax=14)))
    c = c * (1.0 - 0.35 * dirty)[..., None]
    rough = np.clip(0.90 + 0.03 * fuzz, 0.0, 1.0)
    ao = 0.30 + 0.70 * prof ** 1.3
    return dict(albedo=c, rough=rough, metal=np.zeros_like(h), ao=ao,
                height=h, nstrength=12.0, stat=float(dirty.mean()))


MATERIALS = [("Leather", leather), ("Iron", iron), ("Wood", wood), ("Rope", rope)]


# ------------------------------------------------------------------ output
def write_set(out_dir, name, m):
    base = to_u8(m["albedo"])
    nrm = to_u8(normal_from_height(m["height"], m["nstrength"]))
    mask = to_u8(np.dstack([m["metal"], m["ao"], np.zeros_like(m["metal"]), 1.0 - m["rough"]]))
    files = {
        "BaseColor": os.path.join(out_dir, "DTP_%s_BaseColor.png" % name),
        "Normal": os.path.join(out_dir, "DTP_%s_Normal.png" % name),
        "Mask": os.path.join(out_dir, "DTP_%s_Mask.png" % name),
    }
    write_png(files["BaseColor"], base)
    write_png(files["Normal"], nrm)
    write_png(files["Mask"], mask)
    return files


def lit_preview(m, tiles=2, size=512):
    # crude Blinn-Phong with the real maps, tiled to expose seams. For judging only.
    n = m["albedo"].shape[0]
    nm = normal_from_height(m["height"], m["nstrength"]) * 2.0 - 1.0
    alb = np.clip(m["albedo"], 0, 1) ** 2.2
    rough = np.clip(m["rough"], 0.05, 1.0)
    metal = m["metal"]
    L = np.array([-0.45, 0.55, 0.70]); L = L / np.linalg.norm(L)
    V = np.array([0.0, 0.0, 1.0])
    H = (L + V); H = H / np.linalg.norm(H)
    ndl = np.clip(nm @ L, 0, 1)
    ndh = np.clip(nm @ H, 0, 1)
    shin = 2.0 / (rough ** 2 * rough ** 2 + 1e-4) - 2.0
    shin = np.clip(shin, 2, 400)
    f0 = 0.04 * (1 - metal[..., None]) + alb * metal[..., None]
    spec = f0 * ((ndh ** shin) * (shin + 8) / 25.0)[..., None]
    dif = alb * (1 - metal[..., None]) * (ndl[..., None] * 1.6 + 0.12) * m["ao"][..., None]
    img = np.clip(dif + spec * m["ao"][..., None], 0, 1) ** (1 / 2.2)
    img = np.tile(img, (tiles, tiles, 1))
    k = img.shape[0] // size
    if k > 1:
        img = img[: size * k, : size * k].reshape(size, k, size, k, 3).mean(axis=(1, 3))
    return img


def build_all(out_dir, n=1024, preview_dir=None):
    os.makedirs(out_dir, exist_ok=True)
    report = {}
    sheet = []
    for name, fn in MATERIALS:
        m = fn(n)
        files = write_set(out_dir, name, m)
        report[name] = {"files": files, "coverage_stat": round(m["stat"], 3),
                        "metal_mean": round(float(m["metal"].mean()), 3),
                        "rough_mean": round(float(m["rough"].mean()), 3)}
        if preview_dir:
            os.makedirs(preview_dir, exist_ok=True)
            sheet.append(lit_preview(m))
            nrm = normal_from_height(m["height"], m["nstrength"])
            write_png(os.path.join(preview_dir, "%s_maps.png" % name),
                      to_u8(np.concatenate([np.clip(m["albedo"], 0, 1), nrm,
                                            np.repeat((1 - m["rough"])[..., None], 3, 2),
                                            np.repeat(m["metal"][..., None], 3, 2)], axis=1)[::2, ::2]))
    if preview_dir:
        write_png(os.path.join(preview_dir, "lit_sheet.png"), to_u8(np.concatenate(sheet, axis=1)))
    return report


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "out"
    prev = sys.argv[2] if len(sys.argv) > 2 else None
    import json
    print(json.dumps(build_all(out, 1024, prev), indent=1))
