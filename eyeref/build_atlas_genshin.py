from PIL import Image
import numpy as np

SRC = "eyeref/sheet_genshin_clean.png"
DST = "eyeref/atlas_genshin.png"

COLS, ROWS = 4, 2
CELL = 400
TARGET_W = 350

img = Image.open(SRC).convert("RGBA")
W, H = img.size
sw, sh = W // COLS, H // ROWS

atlas = Image.new("RGBA", (COLS * CELL, ROWS * CELL), (0, 0, 0, 0))

# poses: 0 forward, 1 left, 2 right, 3 up, 4 down, 5 half, 6 closed, 7 happy
kinds = ["eye", "eye", "eye", "eye", "eye", "eye", "closed", "happy"]

for i in range(8):
    r, c = divmod(i, COLS)
    tile = img.crop((c * sw, r * sh, (c + 1) * sw, (r + 1) * sh))
    a = np.asarray(tile)
    alpha = a[..., 3]
    ys, xs = np.where(alpha > 12)
    if len(xs) == 0:
        print("EMPTY", i)
        continue
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    crop = tile.crop((x0, y0, x1 + 1, y1 + 1))
    cw, ch = crop.size
    scale = TARGET_W / cw
    nw, nh = TARGET_W, max(1, round(ch * scale))
    crop = crop.resize((nw, nh), Image.LANCZOS)
    cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    px = (CELL - nw) // 2
    kind = kinds[i]
    if kind == "eye":
        cy = int(CELL * 0.56)          # bbox center at 0.56
        py = cy - nh // 2
    elif kind == "closed":
        top = int(CELL * 0.50 - 0.18 * nh)
        py = top
    else:  # happy
        cy = int(CELL * 0.52)
        py = cy - nh // 2
    cell.paste(crop, (px, py), crop)
    atlas.paste(cell, (c * CELL, r * CELL))
    print(f"pose {i}: bbox={cw}x{ch} -> {nw}x{nh}, placed y={py}")

atlas.save(DST)
print("saved", DST, atlas.size)

# previews
for bg_name, bg in (("white", (255, 255, 255, 255)), ("black", (0, 0, 0, 255))):
    canvas = Image.new("RGBA", atlas.size, bg)
    canvas.alpha_composite(atlas)
    canvas.convert("RGB").save(f"eyeref/atlas_g_{bg_name}.png")

# mini render at island size (~44px height for the pair)
mini = Image.new("RGBA", (94, 44), (0, 0, 0, 255))
eye = atlas.crop((0, 0, CELL, CELL))
# simulate the control: height 30*0.95 ~ 28 px per eye height, pair width
h = 40
w = round(h * 2.15)
pair = eye.resize((w // 2, h), Image.LANCZOS)
flip = pair.transpose(Image.FLIP_LEFT_RIGHT)
mini.paste(pair, (4, 2), pair)
mini.paste(flip, (4 + w // 2, 2), flip)
mini = mini.resize((mini.size[0] * 4, mini.size[1] * 4), Image.NEAREST)
mini.convert("RGB").save("eyeref/atlas_g_mini.png")
print("previews saved")
