from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "CipherVault.App" / "Resources" / "CipherVault.ico"
CANVAS = 1024


def cubic(start, control1, control2, end, steps=32):
    points = []
    for index in range(steps + 1):
        t = index / steps
        inverse = 1 - t
        points.append(
            (
                inverse**3 * start[0]
                + 3 * inverse**2 * t * control1[0]
                + 3 * inverse * t**2 * control2[0]
                + t**3 * end[0],
                inverse**3 * start[1]
                + 3 * inverse**2 * t * control1[1]
                + 3 * inverse * t**2 * control2[1]
                + t**3 * end[1],
            )
        )
    return points


def rounded_rectangle(draw, box, radius, fill):
    left, top, right, bottom = box
    draw.rectangle((left + radius, top, right - radius, bottom), fill=fill)
    draw.rectangle((left, top + radius, right, bottom - radius), fill=fill)
    draw.ellipse((left, top, left + radius * 2, top + radius * 2), fill=fill)
    draw.ellipse((right - radius * 2, top, right, top + radius * 2), fill=fill)
    draw.ellipse((left, bottom - radius * 2, left + radius * 2, bottom), fill=fill)
    draw.ellipse((right - radius * 2, bottom - radius * 2, right, bottom), fill=fill)


image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)

shield = [(512, 62)]
shield += cubic((512, 62), (420, 140), (280, 156), (178, 204))
shield += [(178, 470)]
shield += cubic((178, 470), (178, 710), (316, 880), (512, 958))
shield += cubic((512, 958), (708, 880), (846, 710), (846, 470))
shield += [(846, 204)]
shield += cubic((846, 204), (744, 156), (604, 140), (512, 62))
draw.polygon(shield, fill="#087CF0")

# White lock body and shackle keep the mark legible down to 16 px.
draw.ellipse((350, 302, 674, 626), fill="white")
draw.ellipse((426, 378, 598, 626), fill="#087CF0")
rounded_rectangle(draw, (326, 474, 698, 758), radius=68, fill="white")
draw.ellipse((469, 554, 555, 640), fill="#087CF0")
rounded_rectangle(draw, (493, 616, 531, 700), radius=18, fill="#087CF0")

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
image.save(
    OUTPUT,
    format="ICO",
    sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
)
print(OUTPUT)
