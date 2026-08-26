"""下载 RK3588 最新双目标定照片，并用 Python/OpenCV 独立复算。

密码通过 getpass 交互输入，不写入命令行、脚本或输出文件。
"""

import argparse
import csv
import getpass
import json
import math
import os
import shlex
import sys
from pathlib import Path, PurePosixPath

import paramiko


def ssh_run(client: paramiko.SSHClient, command: str, timeout: int = 120) -> str:
    _, stdout, stderr = client.exec_command(command, timeout=timeout)
    code = stdout.channel.recv_exit_status()
    output = stdout.read().decode("utf-8", "replace")
    error = stderr.read().decode("utf-8", "replace")
    if code != 0:
        raise RuntimeError(f"remote command failed ({code}): {error.strip()}")
    return output


def psql(client: paramiko.SSHClient, password: str, sql: str) -> list[list[str]]:
    command = (
        f"PGPASSWORD={shlex.quote(password)} psql -h 127.0.0.1 -U postgres "
        f"-d LionAbpPro10 -AtF '|' -c {shlex.quote(sql)}"
    )
    return [line.split("|") for line in ssh_run(client, command).splitlines() if line]


def download_latest_stereo_project(
    client: paramiko.SSHClient, password: str, output_root: Path
) -> tuple[Path, dict]:
    latest = psql(
        client,
        password,
        'SELECT "CalibProjectId",COUNT(DISTINCT "PairGroupId"),MAX("CapturedAt") '
        'FROM "AbpProCalibPhotoRecords" WHERE "PhotoType"=2 '
        'GROUP BY "CalibProjectId" ORDER BY MAX("CapturedAt") DESC LIMIT 1;',
    )
    if not latest:
        raise RuntimeError("数据库中没有双目成对照片")
    project_id, recorded_pair_count, latest_capture = latest[0]

    projects = psql(
        client,
        password,
        'SELECT "Name","BoardType","PhysicalCornerRows","PhysicalCornerCols",'
        '"PhysicalSquareSizeMm","CirclePatternRows","CirclePatternCols",'
        '"CircleSpacingMm","MainCameraDeviceId","SecondaryCameraDeviceId" '
        f'FROM "AbpProCalibProjects" WHERE "Id"=\'{project_id}\' AND NOT "IsDeleted";',
    )
    if not projects:
        raise RuntimeError(f"标定项目不存在：{project_id}")
    (
        project_name,
        board_type,
        physical_rows,
        physical_cols,
        physical_spacing,
        circle_rows,
        circle_cols,
        circle_spacing,
        main_camera_id,
        secondary_camera_id,
    ) = projects[0]

    params = psql(
        client,
        password,
        'SELECT "CameraDeviceId","IntrinsicMatrixJson","DistCoeffsJson",'
        'COALESCE("ReprojectionError"::text,\'\') FROM "AbpProCalibCameraParams" '
        f'WHERE "CalibProjectId"=\'{project_id}\' AND NOT "IsDeleted";',
    )
    param_by_camera = {
        row[0]: {
            "intrinsic_matrix": json.loads(row[1]),
            "dist_coeffs": json.loads(row[2]),
            "reprojection_error": float(row[3]) if row[3] else None,
        }
        for row in params
        if row[1] and row[2]
    }
    for camera_id in (main_camera_id, secondary_camera_id):
        if camera_id not in param_by_camera:
            raise RuntimeError(f"相机 {camera_id} 缺少内参")

    rows = psql(
        client,
        password,
        'SELECT "PairGroupId","CameraDeviceId","StereoRole","BlobKey","CapturedAt",'
        '"IsValid","CornerCountDetected" FROM "AbpProCalibPhotoRecords" '
        f'WHERE "CalibProjectId"=\'{project_id}\' AND "PhotoType"=2 '
        'ORDER BY "CapturedAt","PairGroupId","StereoRole";',
    )
    grouped: dict[str, dict] = {}
    for group_id, camera_id, role, blob_key, captured_at, is_valid, corner_count in rows:
        item = grouped.setdefault(group_id, {"pair_group_id": group_id})
        role_name = "main" if camera_id == main_camera_id and role == "0" else "secondary"
        item[role_name] = {
            "camera_id": camera_id,
            "blob_key": blob_key,
            "captured_at": captured_at,
            "server_valid": is_valid.lower() == "t",
            "server_corner_count": int(corner_count),
        }

    output_dir = output_root / project_id
    output_dir.mkdir(parents=True, exist_ok=True)
    pairs: list[dict] = []
    with client.open_sftp() as sftp:
        for index, item in enumerate(grouped.values(), start=1):
            if "main" not in item or "secondary" not in item:
                item["download_status"] = "incomplete"
                pairs.append(item)
                continue
            pair_dir = output_dir / f"pair_{index:03d}_{item['pair_group_id'][:8]}"
            pair_dir.mkdir(parents=True, exist_ok=True)
            for role_name in ("main", "secondary"):
                photo = item[role_name]
                suffix = PurePosixPath(photo["blob_key"]).suffix or ".bmp"
                local_path = pair_dir / f"{role_name}{suffix}"
                remote_path = str(PurePosixPath("/host/calib-photos") / photo["blob_key"])
                sftp.get(remote_path, str(local_path))
                photo["local_path"] = str(local_path.relative_to(output_dir))
            item["download_status"] = "ok"
            pairs.append(item)

    board_type_value = int(board_type)
    metadata = {
        "project_id": project_id,
        "project_name": project_name,
        "recorded_pair_count": int(recorded_pair_count),
        "latest_capture": latest_capture,
        "main_camera_id": main_camera_id,
        "secondary_camera_id": secondary_camera_id,
        "board": {
            "type": board_type_value,
            "rows": int(physical_rows if board_type_value == 0 else circle_rows),
            "cols": int(physical_cols if board_type_value == 0 else circle_cols),
            "spacing_mm": float(physical_spacing if board_type_value == 0 else circle_spacing),
        },
        "camera_params": param_by_camera,
        "pairs": pairs,
    }
    (output_dir / "metadata.json").write_text(
        json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return output_dir, metadata


def _load_gray(path: Path, cv2, np):
    raw = np.fromfile(path, dtype=np.uint8)
    return cv2.imdecode(raw, cv2.IMREAD_GRAYSCALE)


def _find_points(gray, board: dict, cv2, np):
    pattern = (board["cols"], board["rows"])
    if board["type"] == 0:
        found, corners = cv2.findChessboardCornersSB(
            gray,
            pattern,
            flags=cv2.CALIB_CB_NORMALIZE_IMAGE | cv2.CALIB_CB_EXHAUSTIVE,
        )
        return corners.reshape(-1, 2).astype(np.float32) if found else None

    if board["type"] == 3:
        return _find_marked_circle_grid(gray, board, cv2, np)

    flags = cv2.CALIB_CB_SYMMETRIC_GRID
    if board["type"] == 2:
        flags = cv2.CALIB_CB_ASYMMETRIC_GRID
    found, centers = cv2.findCirclesGrid(gray, pattern, flags=flags)
    return centers.reshape(-1, 2).astype(np.float32) if found else None


def _create_blob_detector(width: int, height: int, cv2):
    scale = width * height / (2448.0 * 2048.0)
    params = cv2.SimpleBlobDetector_Params()
    params.minThreshold = 10
    params.maxThreshold = 220
    params.filterByArea = True
    params.minArea = 25 * scale
    params.maxArea = 10000 * scale
    params.filterByCircularity = False
    params.filterByConvexity = True
    params.minConvexity = 0.8
    params.filterByInertia = True
    params.minInertiaRatio = 0.1
    params.maxInertiaRatio = 1.0
    return cv2.SimpleBlobDetector_create(params)


def _order_marked_circle_grid(points, board: dict, cv2, np):
    rows, cols = board["rows"], board["cols"]
    marker_row = int(board.get("marker_row", rows // 2))
    marker_col = int(board.get("marker_col", cols // 2))
    expected = rows * cols - 1
    if len(points) < expected:
        return None

    delta = points[:, None, :] - points[None, :, :]
    distances = np.linalg.norm(delta, axis=2)
    np.fill_diagonal(distances, np.inf)
    nearest = distances.min(axis=1)
    median_nearest = float(np.median(nearest))
    if median_nearest <= 1e-6:
        return None
    adjacent = (distances >= 0.35 * median_nearest) & (
        distances <= 1.8 * median_nearest
    )
    neighbor_counts = adjacent.sum(axis=1)

    visited = np.zeros(len(points), dtype=bool)
    largest: list[int] = []
    for start in range(len(points)):
        if visited[start] or neighbor_counts[start] < 2:
            continue
        component: list[int] = []
        queue = [start]
        visited[start] = True
        while queue:
            current = queue.pop()
            component.append(current)
            for nxt in np.flatnonzero(adjacent[current]):
                if not visited[nxt] and neighbor_counts[nxt] >= 2:
                    visited[nxt] = True
                    queue.append(int(nxt))
        if len(component) > len(largest):
            largest = component

    core_indices = [
        index
        for index in largest
        if nearest[index] <= 2.0 * median_nearest and neighbor_counts[index] >= 2
    ]
    if len(core_indices) < expected:
        return None
    core = points[core_indices]
    corner_indices = [index for index in core_indices if neighbor_counts[index] <= 4]
    if len(corner_indices) < 4:
        corner_indices = core_indices
    corners = points[corner_indices]
    sums = corners[:, 0] + corners[:, 1]
    diffs = corners[:, 0] - corners[:, 1]
    image_corners = np.float32(
        [
            corners[int(np.argmin(sums))],
            corners[int(np.argmax(diffs))],
            corners[int(np.argmax(sums))],
            corners[int(np.argmin(diffs))],
        ]
    )
    ideal_corners = np.float32(
        [[0, 0], [cols - 1, 0], [cols - 1, rows - 1], [0, rows - 1]]
    )
    homography = cv2.getPerspectiveTransform(ideal_corners, image_corners)

    center_ideal = np.float32([[[cols // 2, rows // 2]], [[cols // 2 + 1, rows // 2]], [[cols // 2, rows // 2 + 1]]])
    center_projected = cv2.perspectiveTransform(center_ideal, homography).reshape(-1, 2)
    cell = min(
        float(np.linalg.norm(center_projected[0] - center_projected[1])),
        float(np.linalg.norm(center_projected[0] - center_projected[2])),
    )
    if cell <= 1e-3:
        return None
    threshold_squared = (0.72 * cell) ** 2
    used = np.zeros(len(core), dtype=bool)
    ordered = []
    for row in range(rows):
        for col in range(cols):
            if row == marker_row and col == marker_col:
                continue
            predicted = cv2.perspectiveTransform(
                np.float32([[[col, row]]]), homography
            ).reshape(2)
            squared = np.sum((core - predicted) ** 2, axis=1)
            squared[used] = np.inf
            best = int(np.argmin(squared))
            if squared[best] > threshold_squared:
                return None
            used[best] = True
            ordered.append(core[best])
    return np.asarray(ordered, dtype=np.float32) if len(ordered) == expected else None


def _find_marked_circle_grid(gray, board: dict, cv2, np):
    expected = board["rows"] * board["cols"] - 1

    def detect_at_resolution(image):
        detector = _create_blob_detector(image.shape[1], image.shape[0], cv2)
        masked = cv2.bitwise_and(image, image, mask=(image > 30).astype(np.uint8) * 255)
        variants = [
            masked,
            image,
            cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8)).apply(image),
            cv2.GaussianBlur(image, (3, 3), 0),
            cv2.equalizeHist(image),
        ]
        for variant in variants:
            keypoints = detector.detect(variant)
            if not keypoints:
                continue
            sizes = np.asarray([keypoint.size for keypoint in keypoints])
            if len(keypoints) > expected:
                median_size = float(np.median(sizes[sizes > 0]))
                filtered = [
                    point
                    for point in keypoints
                    if median_size * 0.55 <= point.size <= median_size * 1.8
                ]
                if len(filtered) >= expected:
                    keypoints = filtered
            centers = np.float32([keypoint.pt for keypoint in keypoints])
            ordered = _order_marked_circle_grid(centers, board, cv2, np)
            if os.environ.get("AURORA_STEREO_DEBUG") == "1":
                print(f"[detect] blobs={len(keypoints)}, ordered={ordered is not None}")
            if ordered is not None:
                return ordered
        return None

    if gray.shape[1] >= 1600 or gray.shape[0] >= 1600:
        half = cv2.resize(
            gray, (gray.shape[1] // 2, gray.shape[0] // 2), interpolation=cv2.INTER_AREA
        )
        result = detect_at_resolution(half)
        if result is not None:
            result[:, 0] *= gray.shape[1] / half.shape[1]
            result[:, 1] *= gray.shape[0] / half.shape[0]
            return result
    return detect_at_resolution(gray)


def _object_points(board: dict, cv2, np):
    rows, cols = board["rows"], board["cols"]
    result = np.zeros((rows * cols, 3), np.float32)
    index = 0
    for row in range(rows):
        for col in range(cols):
            if board["type"] == 3 and row == rows // 2 and col == cols // 2:
                continue
            x = (2 * col + row % 2) if board["type"] == 2 else col
            result[index] = (x * board["spacing_mm"], row * board["spacing_mm"], 0)
            index += 1
    return result[:index]


def _pair_epipolar_rms(main_points, secondary_points, fundamental, cv2, np) -> float:
    lines_secondary = cv2.computeCorrespondEpilines(
        main_points.reshape(-1, 1, 2), 1, fundamental
    ).reshape(-1, 3)
    lines_main = cv2.computeCorrespondEpilines(
        secondary_points.reshape(-1, 1, 2), 2, fundamental
    ).reshape(-1, 3)
    main_h = np.column_stack((main_points, np.ones(len(main_points))))
    secondary_h = np.column_stack((secondary_points, np.ones(len(secondary_points))))
    distance_secondary = np.sum(lines_secondary * secondary_h, axis=1) / np.linalg.norm(
        lines_secondary[:, :2], axis=1
    )
    distance_main = np.sum(lines_main * main_h, axis=1) / np.linalg.norm(
        lines_main[:, :2], axis=1
    )
    return float(np.sqrt(np.mean((distance_main**2 + distance_secondary**2) * 0.5)))


def _solve_board_pose(object_points, image_points, matrix, dist, cv2, np):
    success, rotation_vector, translation = cv2.solvePnP(
        object_points,
        image_points,
        matrix,
        dist,
        flags=cv2.SOLVEPNP_ITERATIVE,
    )
    if not success:
        return None
    rotation, _ = cv2.Rodrigues(rotation_vector)
    projected, _ = cv2.projectPoints(
        object_points, rotation_vector, translation, matrix, dist
    )
    residual = projected.reshape(-1, 2) - image_points.reshape(-1, 2)
    reprojection_rms = float(np.sqrt(np.mean(np.sum(residual**2, axis=1))))
    return rotation, translation.reshape(3, 1), reprojection_rms


def _rotation_distance_degrees(left, right, np) -> float:
    relative = left @ right.T
    cosine = float(np.clip((np.trace(relative) - 1.0) * 0.5, -1.0, 1.0))
    return float(np.degrees(np.arccos(cosine)))


def _grid_symmetry_indices(board: dict, np) -> dict[str, object]:
    rows, cols = board["rows"], board["cols"]
    if rows != cols:
        return {"identity": np.arange(rows * cols)}
    positions = [
        (row, col)
        for row in range(rows)
        for col in range(cols)
        if not (board["type"] == 3 and row == rows // 2 and col == cols // 2)
    ]
    by_position = {position: index for index, position in enumerate(positions)}
    last = rows - 1
    transforms = {
        "identity": lambda row, col: (row, col),
        "rot90": lambda row, col: (col, last - row),
        "rot180": lambda row, col: (last - row, last - col),
        "rot270": lambda row, col: (last - col, row),
        "flip_horizontal": lambda row, col: (row, last - col),
        "flip_vertical": lambda row, col: (last - row, col),
        "transpose": lambda row, col: (col, row),
        "anti_transpose": lambda row, col: (last - col, last - row),
    }
    return {
        name: np.asarray([by_position[transform(row, col)] for row, col in positions])
        for name, transform in transforms.items()
    }


def analyze_stereo(output_dir: Path, metadata: dict, deps: Path | None) -> dict:
    if deps:
        sys.path.insert(0, str(deps.resolve()))
    import cv2
    import numpy as np

    board = metadata["board"]
    expected_count = board["rows"] * board["cols"] - (1 if board["type"] == 3 else 0)
    detected: list[dict] = []
    image_size = None
    for index, pair in enumerate(metadata["pairs"], start=1):
        if pair.get("download_status") != "ok":
            continue
        main_gray = _load_gray(output_dir / pair["main"]["local_path"], cv2, np)
        secondary_gray = _load_gray(output_dir / pair["secondary"]["local_path"], cv2, np)
        if main_gray is None or secondary_gray is None:
            continue
        image_size = (main_gray.shape[1], main_gray.shape[0])
        main_points = _find_points(main_gray, board, cv2, np)
        secondary_points = _find_points(secondary_gray, board, cv2, np)
        detected.append(
            {
                "index": index,
                "pair_group_id": pair["pair_group_id"],
                "main_points": main_points,
                "secondary_points": secondary_points,
                "valid": main_points is not None
                and secondary_points is not None
                and len(main_points) == expected_count
                and len(secondary_points) == expected_count,
            }
        )

    valid = [item for item in detected if item["valid"]]
    if not valid or image_size is None:
        raise RuntimeError("Python 未检测到可用于双目标定的完整照片组")
    object_template = _object_points(board, cv2, np)
    object_points = [object_template.copy() for _ in valid]
    main_points = [item["main_points"] for item in valid]
    secondary_points = [item["secondary_points"] for item in valid]
    main_param = metadata["camera_params"][metadata["main_camera_id"]]
    secondary_param = metadata["camera_params"][metadata["secondary_camera_id"]]
    main_matrix = np.asarray(main_param["intrinsic_matrix"], dtype=np.float64).reshape(3, 3)
    secondary_matrix = np.asarray(
        secondary_param["intrinsic_matrix"], dtype=np.float64
    ).reshape(3, 3)
    main_dist = np.asarray(main_param["dist_coeffs"], dtype=np.float64).reshape(-1, 1)
    secondary_dist = np.asarray(
        secondary_param["dist_coeffs"], dtype=np.float64
    ).reshape(-1, 1)

    def calibrate(count: int):
        return cv2.stereoCalibrate(
            object_points[:count],
            main_points[:count],
            secondary_points[:count],
            main_matrix.copy(),
            main_dist.copy(),
            secondary_matrix.copy(),
            secondary_dist.copy(),
            image_size,
            flags=cv2.CALIB_FIX_INTRINSIC,
            criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_MAX_ITER, 100, 1e-5),
        )

    counts = sorted(
        set(
            [min(10, len(valid)), min(15, len(valid)), min(20, len(valid))]
            + list(range(21, len(valid) + 1))
        )
    )
    runs = []
    final_result = None
    for count in counts:
        result = calibrate(count)
        runs.append({"pair_count": count, "rms_px": float(result[0])})
        if count == len(valid):
            final_result = result
    assert final_result is not None
    baseline_count = min(20, len(valid))
    baseline_result = calibrate(baseline_count)
    baseline_rotation = baseline_result[5]
    baseline_translation = baseline_result[6].reshape(3, 1)
    baseline_fundamental = baseline_result[-1]
    undistorted_main = [
        cv2.undistortPoints(points.reshape(-1, 1, 2), main_matrix, main_dist, P=main_matrix)
        .reshape(-1, 2)
        .astype(np.float32)
        for points in main_points
    ]
    symmetry_indices = _grid_symmetry_indices(board, np)
    corrected_secondary = list(secondary_points)
    symmetry_diagnostics = []
    for pair_index in range(len(valid)):
        candidates = []
        for symmetry_name, indices in symmetry_indices.items():
            candidate = secondary_points[pair_index][indices]
            undistorted_candidate = cv2.undistortPoints(
                candidate.reshape(-1, 1, 2),
                secondary_matrix,
                secondary_dist,
                P=secondary_matrix,
            ).reshape(-1, 2)
            error = _pair_epipolar_rms(
                undistorted_main[pair_index],
                undistorted_candidate,
                baseline_fundamental,
                cv2,
                np,
            )
            candidates.append((error, symmetry_name, candidate))
        best_error, best_name, best_candidate = min(candidates, key=lambda item: item[0])
        identity_error = next(item[0] for item in candidates if item[1] == "identity")
        corrected_secondary[pair_index] = best_candidate
        symmetry_diagnostics.append(
            {
                "index": valid[pair_index]["index"],
                "pair_group_id": valid[pair_index]["pair_group_id"],
                "identity_epipolar_rms_px": float(identity_error),
                "best_symmetry": best_name,
                "best_epipolar_rms_px": float(best_error),
            }
        )

    corrected_result = cv2.stereoCalibrate(
        object_points,
        main_points,
        corrected_secondary,
        main_matrix.copy(),
        main_dist.copy(),
        secondary_matrix.copy(),
        secondary_dist.copy(),
        image_size,
        flags=cv2.CALIB_FIX_INTRINSIC,
        criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_MAX_ITER, 100, 1e-5),
    )
    fundamental = corrected_result[-1]
    undistorted_secondary = [
        cv2.undistortPoints(
            points.reshape(-1, 1, 2), secondary_matrix, secondary_dist, P=secondary_matrix
        )
        .reshape(-1, 2)
        .astype(np.float32)
        for points in corrected_secondary
    ]
    pair_errors = [
        _pair_epipolar_rms(
            undistorted_main[i], undistorted_secondary[i], fundamental, cv2, np
        )
        for i in range(len(valid))
    ]
    median = float(np.median(pair_errors))
    mad = float(np.median(np.abs(np.asarray(pair_errors) - median)))
    threshold = median + max(0.25, 3.5 * 1.4826 * mad)

    def rectification_diagnostics(
        calibration_result, alpha=-1.0, zero_disparity=True
    ):
        rotation = calibration_result[5]
        translation = calibration_result[6]
        r1, r2, p1, p2, _, _, _ = cv2.stereoRectify(
            main_matrix,
            main_dist,
            secondary_matrix,
            secondary_dist,
            image_size,
            rotation,
            translation,
            flags=cv2.CALIB_ZERO_DISPARITY if zero_disparity else 0,
            alpha=alpha,
        )
        map1x, map1y = cv2.initUndistortRectifyMap(
            main_matrix, main_dist, r1, p1, image_size, cv2.CV_32FC1
        )
        map2x, map2y = cv2.initUndistortRectifyMap(
            secondary_matrix, secondary_dist, r2, p2, image_size, cv2.CV_32FC1
        )
        width, height = image_size
        main_inside = (
            np.isfinite(map1x)
            & np.isfinite(map1y)
            & (map1x >= 0)
            & (map1x < width - 1)
            & (map1y >= 0)
            & (map1y < height - 1)
        )
        secondary_inside = (
            np.isfinite(map2x)
            & np.isfinite(map2y)
            & (map2x >= 0)
            & (map2x < width - 1)
            & (map2y >= 0)
            & (map2y < height - 1)
        )
        sampled_main = main_inside[::8, ::8]
        sampled_secondary = secondary_inside[::8, ::8]

        def destination_bounds(mask):
            ys, xs = np.nonzero(mask)
            if len(xs) == 0:
                return None
            return {
                "x_min": int(xs.min()),
                "x_max": int(xs.max()),
                "y_min": int(ys.min()),
                "y_max": int(ys.max()),
            }

        return {
            "rms_px": float(calibration_result[0]),
            "alpha": alpha,
            "zero_disparity": zero_disparity,
            "rotation_matrix": rotation.tolist(),
            "rotation_angle_deg": _rotation_distance_degrees(
                rotation, np.eye(3), np
            ),
            "translation_mm": translation.reshape(3).tolist(),
            "rectified_fx": float(p1[0, 0]),
            "rectified_cx1": float(p1[0, 2]),
            "rectified_cx2": float(p2[0, 2]),
            "main_coverage_percent": float(sampled_main.mean() * 100),
            "secondary_coverage_percent": float(sampled_secondary.mean() * 100),
            "overlap_coverage_percent": float(
                (sampled_main & sampled_secondary).mean() * 100
            ),
            "main_valid_destination_bounds": destination_bounds(main_inside),
            "secondary_valid_destination_bounds": destination_bounds(secondary_inside),
            "overlap_destination_bounds": destination_bounds(
                main_inside & secondary_inside
            ),
        }

    inlier_indices = [
        index for index, error in enumerate(pair_errors) if error <= threshold
    ]
    inlier_result = None
    if len(inlier_indices) >= 10 and len(inlier_indices) < len(valid):
        inlier_result = cv2.stereoCalibrate(
            [object_points[index] for index in inlier_indices],
            [main_points[index] for index in inlier_indices],
            [corrected_secondary[index] for index in inlier_indices],
            main_matrix.copy(),
            main_dist.copy(),
            secondary_matrix.copy(),
            secondary_dist.copy(),
            image_size,
            flags=cv2.CALIB_FIX_INTRINSIC,
            criteria=(cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_MAX_ITER, 100, 1e-5),
        )

    pose_diagnostics = []
    for pair_index, item in enumerate(valid):
        main_pose = _solve_board_pose(
            object_template, main_points[pair_index], main_matrix, main_dist, cv2, np
        )
        secondary_pose = _solve_board_pose(
            object_template,
            corrected_secondary[pair_index],
            secondary_matrix,
            secondary_dist,
            cv2,
            np,
        )
        if main_pose is None or secondary_pose is None:
            continue
        main_rotation, main_translation, main_pose_rms = main_pose
        secondary_rotation, secondary_translation, secondary_pose_rms = secondary_pose
        pair_rotation = secondary_rotation @ main_rotation.T
        pair_translation = secondary_translation - pair_rotation @ main_translation
        pose_diagnostics.append(
            {
                "index": item["index"],
                "pair_group_id": item["pair_group_id"],
                "main_pnp_reprojection_rms_px": main_pose_rms,
                "secondary_pnp_reprojection_rms_px": secondary_pose_rms,
                "relative_rotation_delta_from_first20_deg": _rotation_distance_degrees(
                    pair_rotation, baseline_rotation, np
                ),
                "relative_translation_delta_from_first20_mm": float(
                    np.linalg.norm(pair_translation - baseline_translation)
                ),
                "relative_translation_mm": pair_translation.reshape(3).tolist(),
            }
        )
    baseline_pose_rows = [row for row in pose_diagnostics if row["index"] <= baseline_count]
    added_pose_rows = [row for row in pose_diagnostics if row["index"] > baseline_count]

    def summarize_pose_rows(rows):
        if not rows:
            return None
        translation_deltas = np.asarray(
            [row["relative_translation_delta_from_first20_mm"] for row in rows]
        )
        rotation_deltas = np.asarray(
            [row["relative_rotation_delta_from_first20_deg"] for row in rows]
        )
        return {
            "count": len(rows),
            "translation_delta_min_mm": float(translation_deltas.min()),
            "translation_delta_mean_mm": float(translation_deltas.mean()),
            "translation_delta_max_mm": float(translation_deltas.max()),
            "rotation_delta_min_deg": float(rotation_deltas.min()),
            "rotation_delta_mean_deg": float(rotation_deltas.mean()),
            "rotation_delta_max_deg": float(rotation_deltas.max()),
        }

    report = {
        "opencv_version": cv2.__version__,
        "downloaded_pairs": len(metadata["pairs"]),
        "python_detected_pairs": len(valid),
        "python_rejected_detection_pairs": len(detected) - len(valid),
        "incremental_calibration": runs,
        "symmetry_corrected_all_pairs_rms_px": float(corrected_result[0]),
        "symmetry_diagnostics": symmetry_diagnostics,
        "first20_stereo_translation_mm": baseline_translation.reshape(3).tolist(),
        "relative_pose_summary": {
            "first20": summarize_pose_rows(baseline_pose_rows),
            "pairs_after_first20": summarize_pose_rows(added_pose_rows),
        },
        "relative_pose_diagnostics": pose_diagnostics,
        "epipolar_median_px": median,
        "epipolar_mad_px": mad,
        "epipolar_threshold_px": threshold,
        "rectification_diagnostics": {
            "all_pairs": rectification_diagnostics(corrected_result),
            "after_epipolar_outlier_removal": (
                rectification_diagnostics(inlier_result)
                if inlier_result is not None
                else None
            ),
            "removed_pair_indices": [
                valid[index]["index"]
                for index in range(len(valid))
                if index not in inlier_indices
            ],
            "alternative_rectification_settings_after_outlier_removal": (
                {
                    "alpha_0_zero_disparity": rectification_diagnostics(
                        inlier_result, alpha=0.0, zero_disparity=True
                    ),
                    "alpha_0_5_zero_disparity": rectification_diagnostics(
                        inlier_result, alpha=0.5, zero_disparity=True
                    ),
                    "alpha_1_zero_disparity": rectification_diagnostics(
                        inlier_result, alpha=1.0, zero_disparity=True
                    ),
                    "alpha_minus1_without_zero_disparity": rectification_diagnostics(
                        inlier_result, alpha=-1.0, zero_disparity=False
                    ),
                }
                if inlier_result is not None
                else None
            ),
        },
        "per_pair": [
            {
                "index": item["index"],
                "pair_group_id": item["pair_group_id"],
                "epipolar_rms_px": pair_errors[index],
                "outlier": pair_errors[index] > threshold,
            }
            for index, item in enumerate(valid)
        ],
    }
    (output_dir / "stereo_analysis.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    with (output_dir / "stereo_pair_errors.csv").open(
        "w", newline="", encoding="utf-8-sig"
    ) as stream:
        writer = csv.DictWriter(stream, fieldnames=list(report["per_pair"][0]))
        writer.writeheader()
        writer.writerows(report["per_pair"])
    return report


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="10.24.1.143")
    parser.add_argument("--user", default="linaro")
    parser.add_argument("--output-dir", type=Path, default=Path("Artifacts/stereo-calibration"))
    parser.add_argument("--deps", type=Path)
    parser.add_argument("--download-only", action="store_true")
    parser.add_argument("--analyze-existing", type=Path)
    args = parser.parse_args()

    if args.analyze_existing:
        output_dir = args.analyze_existing.resolve()
        metadata = json.loads((output_dir / "metadata.json").read_text(encoding="utf-8"))
    else:
        password = getpass.getpass(f"SSH/数据库密码 ({args.user}@{args.host}): ")
        client = paramiko.SSHClient()
        client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        client.connect(args.host, username=args.user, password=password, timeout=15)
        try:
            output_dir, metadata = download_latest_stereo_project(
                client, password, args.output_dir.resolve()
            )
        finally:
            client.close()
        print(f"downloaded_to={output_dir}")
        print(f"recorded_pairs={metadata['recorded_pair_count']}")
        print(f"board={metadata['board']}")
    if args.download_only:
        return 0

    report = analyze_stereo(output_dir, metadata, args.deps)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
