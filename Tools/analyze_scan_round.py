#!/usr/bin/env python3
"""Analyze a ZIP downloaded from the calib-scan download-round API.

Requires numpy and opencv-python.
Example: python Tools/analyze_scan_round.py scan-round.zip --output scan-analysis
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import tempfile
import zipfile
from pathlib import Path

import cv2
import numpy as np


def load_gray(path: Path, max_width: int = 800) -> np.ndarray:
    image = cv2.imdecode(np.fromfile(path, dtype=np.uint8), cv2.IMREAD_GRAYSCALE)
    if image is None:
        raise RuntimeError(f"无法读取图片：{path}")
    if image.shape[1] > max_width:
        scale = max_width / image.shape[1]
        image = cv2.resize(image, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
    return image


def frame_stats(image: np.ndarray) -> dict[str, float]:
    p01, p10, p50, p90, p99 = np.percentile(image, [1, 10, 50, 90, 99])
    return {
        "mean": float(image.mean()), "std": float(image.std()),
        "p01": float(p01), "p10": float(p10), "p50": float(p50),
        "p90": float(p90), "p99": float(p99),
        "crushed_ratio": float(np.mean(image <= 8)),
        "saturated_ratio": float(np.mean(image >= 250)),
        "laplacian_variance": float(cv2.Laplacian(image, cv2.CV_64F).var()),
    }


def sequence_metrics(images: list[np.ndarray]) -> dict[str, float]:
    stack = np.stack(images).astype(np.float32)
    count = stack.shape[0]
    angles = np.arange(count, dtype=np.float32) * (2 * math.pi / count)
    cos_term = np.tensordot(np.cos(angles), stack, axes=(0, 0))
    sin_term = np.tensordot(np.sin(angles), stack, axes=(0, 0))
    mean = stack.mean(axis=0)
    modulation = 2 * np.sqrt(cos_term ** 2 + sin_term ** 2) / count
    fitted = np.stack([
        mean + 2 * cos_term / count * math.cos(a) + 2 * sin_term / count * math.sin(a)
        for a in angles
    ])
    residual = np.sqrt(np.mean((stack - fitted) ** 2, axis=0))
    return {
        "modulation_mean": float(modulation.mean()),
        "modulation_p10": float(np.percentile(modulation, 10)),
        "low_modulation_ratio_lt8": float(np.mean(modulation < 8)),
        "residual_mean": float(residual.mean()),
        "poor_sinusoid_ratio": float(np.mean(
            (residual > 4) & (residual / np.maximum(modulation, 1) > .35))),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="分析一轮结构光扫描图片")
    parser.add_argument("archive", type=Path, help="download-round 接口返回的 ZIP")
    parser.add_argument("--output", type=Path, default=Path("scan-analysis"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="aurora-scan-") as temp:
        root = Path(temp)
        with zipfile.ZipFile(args.archive) as archive:
            archive.extractall(root)
        manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
        rows: list[dict] = []
        by_role: dict[str, list[tuple[dict, np.ndarray]]] = {}
        for entry in manifest["entries"]:
            if entry["frameIndex"] < 0:
                continue
            image = load_gray(root / entry["fileName"])
            rows.append({**entry, **frame_stats(image)})
            by_role.setdefault(entry["cameraRole"], []).append((entry, image))

        report: dict = {"manifest": manifest, "phase_sequences": {}}
        for role, items in by_role.items():
            groups: dict[str, list[np.ndarray]] = {}
            for entry, image in items:
                if "相移" not in entry.get("label", ""):
                    continue
                direction = entry["label"].split()[0]
                groups.setdefault(direction, []).append(image)
            report["phase_sequences"][role] = {
                direction: sequence_metrics(images)
                for direction, images in groups.items() if len(images) >= 3
            }

        with (args.output / "frames.csv").open("w", newline="", encoding="utf-8-sig") as file:
            writer = csv.DictWriter(file, fieldnames=list(rows[0].keys()))
            writer.writeheader()
            writer.writerows(rows)
        (args.output / "report.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(report["phase_sequences"], ensure_ascii=False, indent=2))
        print(f"分析完成：{args.output.resolve()}")


if __name__ == "__main__":
    main()
