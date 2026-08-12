#!/usr/bin/env python3
"""Apply the approved JM selector border system without changing item artwork."""

from __future__ import annotations

import argparse
import math
import os
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


EXPECTED_SELECTOR_COUNT = 76
INNER_KEEP_RADIUS_RATIO = 0.780
OUTER_REPLACE_RADIUS_RATIO = 0.825


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-directory", type=Path, required=True)
    parser.add_argument("--template-directory", type=Path, required=True)
    parser.add_argument("--contact-sheet", type=Path)
    return parser.parse_args()


def selector_group(stem: str) -> str:
    name = stem.removeprefix("selector-")
    if name.startswith("set-"):
        return "set"
    if name.startswith("unique-"):
        return "unique"
    if name.startswith("base-mode-"):
        return "mode"
    if name.startswith("base-"):
        return "base"
    if name.startswith(("charm-", "skill-charms-", "popular-")):
        return "charm"
    if name.startswith("materials-"):
        return "materials"
    if name.startswith("socket-"):
        return "socket"
    if name == "quick-craft":
        return "craft"
    raise ValueError(f"Selector group is undefined: {stem}")


def recolor_green_band(image: Image.Image, target_hue: int, saturation_scale: float) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA"), dtype=np.uint8)
    hsv_image = image.convert("RGB").convert("HSV")
    hsv = np.asarray(hsv_image, dtype=np.uint8).copy()
    height, width = hsv.shape[:2]
    y, x = np.ogrid[:height, :width]
    center_x = (width - 1) / 2.0
    center_y = (height - 1) / 2.0
    radius = np.sqrt((x - center_x) ** 2 + (y - center_y) ** 2)
    green = (
        (radius >= min(width, height) * 0.36)
        & (hsv[:, :, 0] >= 42)
        & (hsv[:, :, 0] <= 112)
        & (hsv[:, :, 1] >= 48)
        & (rgba[:, :, 3] > 0)
    )
    hsv[:, :, 0][green] = target_hue
    scaled_saturation = np.clip(
        hsv[:, :, 1][green].astype(np.float32) * saturation_scale,
        0,
        255,
    )
    hsv[:, :, 1][green] = scaled_saturation.astype(np.uint8)
    recolored_rgb = np.asarray(Image.fromarray(hsv, mode="HSV").convert("RGB"), dtype=np.uint8)
    result = np.dstack((recolored_rgb, rgba[:, :, 3]))
    return Image.fromarray(result, mode="RGBA")


def load_templates(directory: Path) -> dict[str, Image.Image]:
    set_border = Image.open(directory / "set.png").convert("RGBA")
    templates = {
        "set": set_border,
        "unique": Image.open(directory / "unique.png").convert("RGBA"),
        "base": Image.open(directory / "base.png").convert("RGBA"),
        "charm": recolor_green_band(set_border, target_hue=199, saturation_scale=0.92),
        "materials": recolor_green_band(set_border, target_hue=18, saturation_scale=0.92),
        "mode": recolor_green_band(set_border, target_hue=132, saturation_scale=0.82),
        "socket": recolor_green_band(set_border, target_hue=157, saturation_scale=0.58),
        "craft": recolor_green_band(set_border, target_hue=7, saturation_scale=1.00),
    }
    dimensions = {image.size for image in templates.values()}
    if len(dimensions) != 1:
        raise ValueError(f"Border templates have inconsistent dimensions: {dimensions}")
    return templates


def radial_blend_mask(size: tuple[int, int]) -> Image.Image:
    width, height = size
    center_x = (width - 1) / 2.0
    center_y = (height - 1) / 2.0
    radius_scale = min(width, height) / 2.0
    inner = radius_scale * INNER_KEEP_RADIUS_RATIO
    outer = radius_scale * OUTER_REPLACE_RADIUS_RATIO
    y, x = np.ogrid[:height, :width]
    radius = np.sqrt((x - center_x) ** 2 + (y - center_y) ** 2)
    normalized = np.clip((radius - inner) / (outer - inner), 0.0, 1.0)
    smooth = normalized * normalized * (3.0 - 2.0 * normalized)
    return Image.fromarray(np.rint(smooth * 255.0).astype(np.uint8), mode="L")


def apply_border(source: Image.Image, border: Image.Image, mask: Image.Image) -> Image.Image:
    source = source.convert("RGBA")
    if source.size != border.size:
        raise ValueError(f"Selector and border dimensions differ: {source.size} != {border.size}")
    result = Image.composite(border, source, mask)

    # The central item artwork must be byte-identical inside the protected radius.
    source_pixels = np.asarray(source)
    result_pixels = np.asarray(result)
    width, height = source.size
    y, x = np.ogrid[:height, :width]
    center_x = (width - 1) / 2.0
    center_y = (height - 1) / 2.0
    protected = np.sqrt((x - center_x) ** 2 + (y - center_y) ** 2) <= (
        min(width, height) / 2.0 * INNER_KEEP_RADIUS_RATIO
    )
    if not np.array_equal(source_pixels[protected], result_pixels[protected]):
        raise ValueError("Central selector artwork changed while applying its border.")
    return result


def already_has_border(source: Image.Image, border: Image.Image) -> bool:
    """Avoid repeatedly blending the narrow antialias transition on reruns."""
    source_pixels = np.asarray(source.convert("RGBA"))
    border_pixels = np.asarray(border.convert("RGBA"))
    width, height = source.size
    y, x = np.ogrid[:height, :width]
    center_x = (width - 1) / 2.0
    center_y = (height - 1) / 2.0
    outer = np.sqrt((x - center_x) ** 2 + (y - center_y) ** 2) >= (
        min(width, height) / 2.0 * OUTER_REPLACE_RADIUS_RATIO
    )
    return np.array_equal(source_pixels[outer], border_pixels[outer])


def write_atomic(image: Image.Image, destination: Path) -> None:
    temporary = destination.with_name(destination.name + ".jmnew.png")
    image.save(temporary, format="PNG", optimize=True)
    os.replace(temporary, destination)


def build_contact_sheet(files: list[Path], destination: Path) -> None:
    thumb_size = 98
    cell_width = 180
    cell_height = 135
    columns = 8
    rows = math.ceil(len(files) / columns)
    sheet = Image.new("RGB", (cell_width * columns, cell_height * rows), "#090b0d")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default()
    for index, path in enumerate(files):
        row, column = divmod(index, columns)
        x = column * cell_width
        y = row * cell_height
        icon = Image.open(path).convert("RGBA").resize((thumb_size, thumb_size), Image.Resampling.LANCZOS)
        sheet.alpha_composite(icon, (x + (cell_width - thumb_size) // 2, y + 4)) if sheet.mode == "RGBA" else sheet.paste(
            icon,
            (x + (cell_width - thumb_size) // 2, y + 4),
            icon,
        )
        label = path.stem.removeprefix("selector-")
        box = draw.textbbox((0, 0), label, font=font)
        text_width = box[2] - box[0]
        draw.text((x + (cell_width - text_width) // 2, y + 108), label, fill="#f1f1f1", font=font)
        draw.rectangle((x, y, x + cell_width - 1, y + cell_height - 1), outline="#30343a")
    destination.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(destination, format="PNG", optimize=True)


def main() -> None:
    args = parse_args()
    source_directory = args.source_directory.resolve()
    template_directory = args.template_directory.resolve()
    selector_files = sorted(
        path
        for path in source_directory.glob("selector-*.png")
        if path.name != "selector-contact-sheet.png"
    )
    if len(selector_files) != EXPECTED_SELECTOR_COUNT:
        raise ValueError(
            f"Expected {EXPECTED_SELECTOR_COUNT} selector sources, found {len(selector_files)}."
        )

    templates = load_templates(template_directory)
    template_size = next(iter(templates.values())).size
    mask = radial_blend_mask(template_size)
    counts: dict[str, int] = {}
    skipped = 0
    for selector_file in selector_files:
        group = selector_group(selector_file.stem)
        source = Image.open(selector_file).convert("RGBA")
        if already_has_border(source, templates[group]):
            skipped += 1
        else:
            themed = apply_border(source, templates[group], mask)
            write_atomic(themed, selector_file)
        counts[group] = counts.get(group, 0) + 1

    if args.contact_sheet:
        build_contact_sheet(selector_files, args.contact_sheet.resolve())

    summary = ", ".join(f"{key}={counts[key]}" for key in sorted(counts))
    print(f"Applied selector borders: {summary}; already themed={skipped}")


if __name__ == "__main__":
    main()
