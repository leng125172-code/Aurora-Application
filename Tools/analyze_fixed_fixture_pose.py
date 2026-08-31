#!/usr/bin/env python3
"""Compare fixed fixture poses using scan-to-CAD nearest-surface distances."""

from __future__ import annotations

import argparse
import json
import math
import struct
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree
from scipy.optimize import minimize


_SCALAR_FORMATS = {
    "char": "b",
    "int8": "b",
    "uchar": "B",
    "uint8": "B",
    "short": "h",
    "int16": "h",
    "ushort": "H",
    "uint16": "H",
    "int": "i",
    "int32": "i",
    "uint": "I",
    "uint32": "I",
    "float": "f",
    "float32": "f",
    "double": "d",
    "float64": "d",
}


@dataclass(frozen=True)
class PlyProperty:
    name: str
    scalar_type: str | None = None
    list_count_type: str | None = None
    list_value_type: str | None = None

    @property
    def is_list(self) -> bool:
        return self.list_count_type is not None


@dataclass(frozen=True)
class PlyElement:
    name: str
    count: int
    properties: tuple[PlyProperty, ...]


def _read_header(stream) -> tuple[str, list[PlyElement]]:
    if stream.readline().strip().lower() != b"ply":
        raise ValueError("Not a PLY file")
    ply_format = ""
    elements: list[PlyElement] = []
    current_name: str | None = None
    current_count = 0
    current_properties: list[PlyProperty] = []
    while True:
        raw = stream.readline()
        if not raw:
            raise ValueError("PLY header has no end_header")
        line = raw.decode("ascii", errors="ignore").strip()
        if line == "end_header":
            if current_name is not None:
                elements.append(
                    PlyElement(current_name, current_count, tuple(current_properties))
                )
            break
        parts = line.split()
        if not parts or parts[0] == "comment":
            continue
        if parts[0] == "format":
            ply_format = parts[1]
        elif parts[0] == "element":
            if current_name is not None:
                elements.append(
                    PlyElement(current_name, current_count, tuple(current_properties))
                )
            current_name = parts[1].lower()
            current_count = int(parts[2])
            current_properties = []
        elif parts[0] == "property":
            if parts[1] == "list":
                current_properties.append(
                    PlyProperty(parts[4].lower(), None, parts[2].lower(), parts[3].lower())
                )
            else:
                current_properties.append(PlyProperty(parts[2].lower(), parts[1].lower()))
    if ply_format not in {"ascii", "binary_little_endian"}:
        raise ValueError(f"Unsupported PLY format: {ply_format}")
    return ply_format, elements


def read_ply(path: Path, include_faces: bool) -> tuple[np.ndarray, np.ndarray | None]:
    with path.open("rb") as stream:
        ply_format, elements = _read_header(stream)
        vertices: np.ndarray | None = None
        faces: list[tuple[int, int, int]] = []
        if ply_format == "ascii":
            for element in elements:
                for _ in range(element.count):
                    tokens = stream.readline().split()
                    offset = 0
                    values: dict[str, float] = {}
                    indices: list[int] = []
                    for prop in element.properties:
                        if prop.is_list:
                            count = int(tokens[offset])
                            offset += 1
                            if include_faces and prop.name in {"vertex_indices", "vertex_index"}:
                                indices = [int(value) for value in tokens[offset : offset + count]]
                            offset += count
                        else:
                            values[prop.name] = float(tokens[offset])
                            offset += 1
                    if element.name == "vertex":
                        if vertices is None:
                            vertices = np.empty((element.count, 3), dtype=np.float32)
                            vertex_row = 0
                        vertices[vertex_row] = values["x"], values["y"], values["z"]
                        vertex_row += 1
                    elif include_faces and element.name == "face" and len(indices) >= 3:
                        faces.extend((indices[0], indices[i], indices[i + 1]) for i in range(1, len(indices) - 1))
        else:
            for element in elements:
                if element.name == "vertex" and all(not prop.is_list for prop in element.properties):
                    names = [prop.name for prop in element.properties]
                    dtype = np.dtype(
                        [(f"p{i}", "<" + _SCALAR_FORMATS[prop.scalar_type]) for i, prop in enumerate(element.properties)]
                    )
                    rows = np.fromfile(stream, dtype=dtype, count=element.count)
                    vertices = np.column_stack(
                        [rows[f"p{names.index(axis)}"] for axis in ("x", "y", "z")]
                    ).astype(np.float32, copy=False)
                    continue
                scalar_readers = [
                    None if prop.is_list else struct.Struct("<" + _SCALAR_FORMATS[prop.scalar_type])
                    for prop in element.properties
                ]
                for _ in range(element.count):
                    indices: list[int] = []
                    for prop, reader in zip(element.properties, scalar_readers):
                        if not prop.is_list:
                            stream.read(reader.size)
                            continue
                        count_reader = struct.Struct("<" + _SCALAR_FORMATS[prop.list_count_type])
                        value_reader = struct.Struct("<" + _SCALAR_FORMATS[prop.list_value_type])
                        count = count_reader.unpack(stream.read(count_reader.size))[0]
                        values = [value_reader.unpack(stream.read(value_reader.size))[0] for _ in range(count)]
                        if include_faces and prop.name in {"vertex_indices", "vertex_index"}:
                            indices = [int(value) for value in values]
                    if include_faces and element.name == "face" and len(indices) >= 3:
                        faces.extend((indices[0], indices[i], indices[i + 1]) for i in range(1, len(indices) - 1))
        if vertices is None:
            raise ValueError(f"PLY has no vertices: {path}")
        return vertices, np.asarray(faces, dtype=np.int32) if include_faces and faces else None


def _radical_inverse(indices: np.ndarray, base: int) -> np.ndarray:
    remaining = indices.astype(np.uint64, copy=True)
    result = np.zeros(indices.shape, dtype=np.float64)
    factor = 1.0 / base
    while np.any(remaining):
        result += (remaining % base) * factor
        remaining //= base
        factor /= base
    return result


def sample_surface(
    vertices: np.ndarray,
    faces: np.ndarray | None,
    spacing_mm: float,
    max_points: int,
) -> np.ndarray:
    if faces is None or len(faces) == 0:
        if len(vertices) <= max_points:
            return vertices
        indices = np.arange(max_points, dtype=np.int64) * len(vertices) // max_points
        return vertices[indices]
    a, b, c = vertices[faces[:, 0]], vertices[faces[:, 1]], vertices[faces[:, 2]]
    areas = np.linalg.norm(np.cross(b - a, c - a), axis=1).astype(np.float64) * 0.5
    usable = np.isfinite(areas) & (areas > 1e-12)
    faces, areas = faces[usable], areas[usable]
    cumulative = np.cumsum(areas)
    total_area = float(cumulative[-1])
    count = min(max_points, max(1, math.ceil(total_area / (spacing_mm * spacing_mm))))
    targets = (np.arange(count, dtype=np.float64) + 0.5) * total_area / count
    selected = faces[np.searchsorted(cumulative, targets, side="left")]
    va, vb, vc = vertices[selected[:, 0]], vertices[selected[:, 1]], vertices[selected[:, 2]]
    sequence = np.arange(1, count + 1, dtype=np.uint64)
    root = np.sqrt(_radical_inverse(sequence, 2))
    second = _radical_inverse(sequence, 3)
    w0 = 1.0 - root
    w1 = root * (1.0 - second)
    w2 = root * second
    return (va * w0[:, None] + vb * w1[:, None] + vc * w2[:, None]).astype(np.float32)


def transform_zyx(
    points: np.ndarray,
    pose: tuple[float, ...],
    scale: float = 1.0,
) -> np.ndarray:
    rx, ry, rz, tx, ty, tz = pose
    rx, ry, rz = np.deg2rad([rx, ry, rz])
    sx, cx = math.sin(rx), math.cos(rx)
    sy, cy = math.sin(ry), math.cos(ry)
    sz, cz = math.sin(rz), math.cos(rz)
    rotation = np.array(
        [
            [cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx],
            [sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx],
            [-sy, cy * sx, cy * cx],
        ],
        dtype=np.float64,
    )
    return ((points * scale) @ rotation.T + np.array([tx, ty, tz])).astype(np.float32)


def parse_pose(value: str) -> tuple[str, tuple[float, ...]]:
    parts = value.split(",")
    if len(parts) != 7:
        raise argparse.ArgumentTypeError("pose must be name,rx,ry,rz,tx,ty,tz")
    return parts[0], tuple(float(item) for item in parts[1:])


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--scan", type=Path, required=True)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--pose", type=parse_pose, action="append", required=True)
    parser.add_argument("--spacing", type=float, default=0.5)
    parser.add_argument("--threshold", type=float, default=3.0)
    parser.add_argument("--max-search", type=float, default=10.0)
    parser.add_argument("--max-model-points", type=int, default=1_000_000)
    parser.add_argument(
        "--optimize",
        action="store_true",
        help="Refine yaw and translation locally before reporting full-cloud metrics",
    )
    parser.add_argument("--optimize-sample", type=int, default=80_000)
    parser.add_argument("--yaw-window", type=float, default=10.0)
    parser.add_argument("--translation-window", type=float, default=5.0)
    parser.add_argument(
        "--optimize-scale",
        action="store_true",
        help="Diagnostic only: include isotropic scan scale in local refinement",
    )
    parser.add_argument("--scale-window", type=float, default=0.05)
    parser.add_argument(
        "--footprint-tolerance",
        type=float,
        default=0.0,
        help="Ignore aligned scan points farther than this XY distance from the CAD footprint",
    )
    parser.add_argument("--overlay-dir", type=Path)
    args = parser.parse_args()

    scan, _ = read_ply(args.scan, include_faces=False)
    model_vertices, model_faces = read_ply(args.model, include_faces=True)
    model = sample_surface(model_vertices, model_faces, args.spacing, args.max_model_points)
    tree = cKDTree(model)
    footprint_tree = cKDTree(model[:, :2]) if args.footprint_tolerance > 0 else None
    print(json.dumps({"scanPoints": len(scan), "modelPoints": len(model)}, ensure_ascii=False))
    for name, pose in args.pose:
        if args.optimize:
            stride = max(1, len(scan) // args.optimize_sample)
            optimization_scan = scan[::stride][: args.optimize_sample]
            pose_start = [pose[2], pose[3], pose[4], pose[5]]
            start = np.asarray(
                ([1.0] if args.optimize_scale else []) + pose_start,
                dtype=np.float64,
            )
            pose_bounds = [
                (start[0] - args.yaw_window, start[0] + args.yaw_window),
                (pose_start[1] - args.translation_window, pose_start[1] + args.translation_window),
                (pose_start[2] - args.translation_window, pose_start[2] + args.translation_window),
                (pose_start[3] - args.translation_window, pose_start[3] + args.translation_window),
            ]
            if args.optimize_scale:
                pose_bounds[0] = (pose_start[0] - args.yaw_window, pose_start[0] + args.yaw_window)
                bounds = [(1.0 - args.scale_window, 1.0 + args.scale_window), *pose_bounds]
            else:
                bounds = pose_bounds

            def objective(parameters: np.ndarray) -> float:
                scale = float(parameters[0]) if args.optimize_scale else 1.0
                pose_parameters = parameters[1:] if args.optimize_scale else parameters
                candidate = (pose[0], pose[1], *pose_parameters)
                candidate_points = transform_zyx(optimization_scan, candidate, scale)
                if footprint_tree is not None:
                    candidate_lateral_distances, _ = footprint_tree.query(
                        candidate_points[:, :2],
                        k=1,
                        distance_upper_bound=args.footprint_tolerance,
                        workers=-1,
                    )
                    candidate_points = candidate_points[
                        np.isfinite(candidate_lateral_distances)
                    ]
                    if len(candidate_points) < len(optimization_scan) * 0.1:
                        return args.max_search
                candidate_distances, _ = tree.query(
                    candidate_points,
                    k=1,
                    distance_upper_bound=args.max_search,
                    workers=-1,
                )
                candidate_distances[~np.isfinite(candidate_distances)] = args.max_search
                return float(np.mean(candidate_distances))

            optimized = minimize(
                objective,
                start,
                method="Powell",
                bounds=bounds,
                options={"xtol": 1e-4, "ftol": 1e-5, "maxiter": 80},
            )
            scale = float(optimized.x[0]) if args.optimize_scale else 1.0
            pose_parameters = optimized.x[1:] if args.optimize_scale else optimized.x
            pose = (pose[0], pose[1], *pose_parameters)
            print(
                json.dumps(
                    {
                        "name": name,
                        "optimizationSuccess": bool(optimized.success),
                        "optimizationMessage": str(optimized.message),
                        "optimizationEvaluations": int(optimized.nfev),
                        "optimizationObjective": float(optimized.fun),
                        "optimizedScale": scale,
                        "optimizedPose": pose,
                    },
                    ensure_ascii=False,
                )
            )
        scale = scale if args.optimize else 1.0
        aligned = transform_zyx(scan, pose, scale)
        if footprint_tree is not None:
            lateral_distances, _ = footprint_tree.query(
                aligned[:, :2],
                k=1,
                distance_upper_bound=args.footprint_tolerance,
                workers=-1,
            )
            footprint_mask = np.isfinite(lateral_distances)
        else:
            footprint_mask = np.ones(len(aligned), dtype=bool)
        inspected = aligned[footprint_mask]
        distances, _ = tree.query(
            inspected,
            k=1,
            distance_upper_bound=args.max_search,
            workers=-1,
        )
        valid = np.isfinite(distances)
        valid_distances = distances[valid]
        over_threshold = valid & (distances > args.threshold)
        result = {
            "name": name,
            "pose": pose,
            "scale": scale,
            "footprintTolerance": args.footprint_tolerance,
            "inspectedPointCount": int(footprint_mask.sum()),
            "ignoredPointCount": int((~footprint_mask).sum()),
            "validPointCount": int(valid.sum()),
            "invalidPointCount": int((~valid).sum()),
            "meanDistance": float(valid_distances.mean()),
            "p50Distance": float(np.percentile(valid_distances, 50)),
            "p95Distance": float(np.percentile(valid_distances, 95)),
            "maxDistance": float(valid_distances.max()),
            "overThresholdCount": int(over_threshold.sum()),
            "overThresholdRatio": float(over_threshold.sum() / len(inspected)),
        }
        print(json.dumps(result, ensure_ascii=False))
        if args.overlay_dir is not None:
            import cv2

            args.overlay_dir.mkdir(parents=True, exist_ok=True)
            resolution = 1000
            margin = 3.0
            minimum = model[:, :2].min(axis=0) - margin
            maximum = model[:, :2].max(axis=0) + margin
            span = np.maximum(maximum - minimum, 1e-6)

            def pixels(points: np.ndarray) -> np.ndarray:
                result_pixels = np.rint(
                    (points[:, :2] - minimum) / span * (resolution - 1)
                ).astype(np.int32)
                result_pixels[:, 1] = resolution - 1 - result_pixels[:, 1]
                return result_pixels

            canvas = np.full((resolution, resolution, 3), 255, dtype=np.uint8)
            model_pixels = pixels(model)
            canvas[model_pixels[:, 1], model_pixels[:, 0]] = (185, 185, 185)
            ignored_pixels = pixels(aligned[~footprint_mask])
            ignored_inside = (
                (ignored_pixels[:, 0] >= 0)
                & (ignored_pixels[:, 0] < resolution)
                & (ignored_pixels[:, 1] >= 0)
                & (ignored_pixels[:, 1] < resolution)
            )
            ignored_pixels = ignored_pixels[ignored_inside]
            canvas[ignored_pixels[:, 1], ignored_pixels[:, 0]] = (0, 165, 255)
            inspected_pixels = pixels(inspected)
            canvas[inspected_pixels[:, 1], inspected_pixels[:, 0]] = (210, 80, 40)
            anomaly_pixels = inspected_pixels[over_threshold]
            canvas[anomaly_pixels[:, 1], anomaly_pixels[:, 0]] = (0, 0, 255)
            cv2.putText(
                canvas,
                f"{name}: blue=kept orange=fixture red=>{args.threshold:g}mm",
                (20, 35),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.75,
                (0, 0, 0),
                2,
                cv2.LINE_AA,
            )
            output_path = args.overlay_dir / f"{name}-xy-overlay.png"
            if not cv2.imwrite(str(output_path), canvas):
                raise RuntimeError(f"Failed to write overlay: {output_path}")
            print(json.dumps({"name": name, "overlay": str(output_path)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
