import argparse
import csv
import math
import os
import shlex
import sys
from collections import Counter
from pathlib import Path, PurePosixPath

import paramiko


def ssh_run(client: paramiko.SSHClient, command: str) -> str:
    _, stdout, stderr = client.exec_command(command, timeout=60)
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


def download_latest_batch(client, password: str, output_dir: Path) -> tuple[list[Path], dict]:
    camera_rows = psql(
        client,
        password,
        'SELECT "Id","Name","DeviceIndex","HardwareId" FROM "AbpProCameraDevices" '
        "WHERE \"Name\"='相机 #1' ORDER BY \"CreationTime\" DESC LIMIT 1;",
    )
    if not camera_rows:
        raise RuntimeError("database does not contain 相机 #1")
    camera_id, camera_name, device_index, hardware_id = camera_rows[0]

    latest_rows = psql(
        client,
        password,
        'SELECT "CalibProjectId" FROM "AbpProCalibPhotoRecords" '
        f"WHERE \"CameraDeviceId\"='{camera_id}' AND \"PhotoType\"=0 "
        'ORDER BY "CapturedAt" DESC LIMIT 1;',
    )
    if not latest_rows:
        raise RuntimeError("相机 #1 没有内参照片")
    project_id = latest_rows[0][0]

    rows = psql(
        client,
        password,
        'SELECT "BlobKey","CapturedAt","IsValid","CornerCountDetected" '
        'FROM "AbpProCalibPhotoRecords" '
        f"WHERE \"CameraDeviceId\"='{camera_id}' AND \"CalibProjectId\"='{project_id}' "
        'AND "PhotoType"=0 ORDER BY "CapturedAt";',
    )
    board_rows = psql(
        client,
        password,
        'SELECT "BoardType","PhysicalCornerRows","PhysicalCornerCols" '
        f'FROM "AbpProCalibProjects" WHERE "Id"=\'{project_id}\';',
    )
    board_type, board_rows_count, board_cols_count = board_rows[0] if board_rows else ("", "", "")

    output_dir.mkdir(parents=True, exist_ok=True)
    downloaded = []
    metadata_by_name = {}
    with client.open_sftp() as sftp:
        for blob_key, captured_at, is_valid, corner_count in rows:
            remote_path = str(PurePosixPath("/host/calib-photos") / blob_key)
            local_path = output_dir / PurePosixPath(blob_key).name
            sftp.get(remote_path, str(local_path))
            downloaded.append(local_path)
            metadata_by_name[local_path.name] = {
                "captured_at": captured_at,
                "server_valid": is_valid,
                "server_corner_count": corner_count,
            }

    return downloaded, {
        "camera_id": camera_id,
        "camera_name": camera_name,
        "device_index": device_index,
        "hardware_id": hardware_id,
        "project_id": project_id,
        "board_type": board_type,
        "board_rows": board_rows_count,
        "board_cols": board_cols_count,
        "photo_metadata": metadata_by_name,
    }


def entropy(gray, np) -> float:
    counts = np.bincount(gray.ravel(), minlength=256)
    probabilities = counts[counts > 0] / gray.size
    return float(-(probabilities * np.log2(probabilities)).sum())


def analyze_image(path: Path, metadata: dict, cv2, np) -> dict:
    raw = np.fromfile(path, dtype=np.uint8)
    unchanged = cv2.imdecode(raw, cv2.IMREAD_UNCHANGED)
    if unchanged is None:
        raise RuntimeError(f"cannot decode {path}")
    gray = unchanged if unchanged.ndim == 2 else cv2.cvtColor(unchanged, cv2.COLOR_BGR2GRAY)
    height, width = gray.shape
    mean = float(gray.mean())
    stddev = float(gray.std())
    p01, p05, p50, p95, p99 = [float(x) for x in np.percentile(gray, [1, 5, 50, 95, 99])]
    under = float((gray <= 5).mean() * 100)
    over = float((gray >= 250).mean() * 100)
    laplacian_var = float(cv2.Laplacian(gray, cv2.CV_64F).var())
    dynamic_range = p99 - p01
    score = 100.0
    score -= min(35.0, under * 2.0 + over * 2.0)
    score -= max(0.0, 35.0 - min(35.0, stddev))
    score -= max(0.0, 20.0 - min(20.0, laplacian_var / 50.0))
    if mean < 40 or mean > 215:
        score -= 10.0
    score = max(0.0, min(100.0, score))
    return {
        "file": path.name,
        "captured_at": metadata.get("captured_at", ""),
        "width": width,
        "height": height,
        "channels": 1 if unchanged.ndim == 2 else unchanged.shape[2],
        "is_grayscale": unchanged.ndim == 2,
        "mean": round(mean, 3),
        "stddev": round(stddev, 3),
        "p01": round(p01, 1),
        "p05": round(p05, 1),
        "median": round(p50, 1),
        "p95": round(p95, 1),
        "p99": round(p99, 1),
        "dynamic_range_p01_p99": round(dynamic_range, 1),
        "underexposed_pct": round(under, 4),
        "overexposed_pct": round(over, 4),
        "laplacian_variance": round(laplacian_var, 3),
        "entropy_bits": round(entropy(gray, np), 4),
        "quality_score": round(score, 1),
        "server_valid": metadata.get("server_valid", ""),
        "server_corner_count": metadata.get("server_corner_count", ""),
    }


def write_report(output_dir: Path, context: dict, analyses: list[dict]) -> None:
    csv_path = output_dir / "intrinsic_analysis.csv"
    with csv_path.open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=list(analyses[0]))
        writer.writeheader()
        writer.writerows(analyses)

    scores = [row["quality_score"] for row in analyses]
    sharpness = [row["laplacian_variance"] for row in analyses]
    report_path = output_dir / "intrinsic_analysis.md"
    lines = [
        "# 相机 #1 最新内参图分析",
        "",
        f"- 相机序列号：`{context['hardware_id']}`",
        f"- 运行索引：`{context['device_index']}`",
        f"- 标定项目：`{context['project_id']}`",
        f"- 标定板：类型 `{context['board_type']}`，{context['board_rows']} × {context['board_cols']}",
        f"- 图片数量：{len(analyses)}",
        f"- 全部单通道灰度：{'是' if all(x['is_grayscale'] for x in analyses) else '否'}",
        f"- 质量分：平均 {sum(scores)/len(scores):.1f}，最低 {min(scores):.1f}，最高 {max(scores):.1f}",
        f"- 清晰度（Laplacian 方差）：最低 {min(sharpness):.1f}，最高 {max(sharpness):.1f}",
        "",
        "| 文件 | 均值 | 标准差 | 欠曝% | 过曝% | 清晰度 | 熵 | 质量分 | 服务端有效 | 角点数 |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---|---:|",
    ]
    for row in analyses:
        lines.append(
            f"| {row['file']} | {row['mean']} | {row['stddev']} | "
            f"{row['underexposed_pct']} | {row['overexposed_pct']} | "
            f"{row['laplacian_variance']} | {row['entropy_bits']} | "
            f"{row['quality_score']} | {row['server_valid']} | {row['server_corner_count']} |"
        )
    report_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", required=True)
    parser.add_argument("--user", required=True)
    parser.add_argument("--output-dir", required=True, type=Path)
    parser.add_argument("--deps", type=Path)
    args = parser.parse_args()
    if args.deps:
        sys.path.insert(0, str(args.deps.resolve()))
    import cv2
    import numpy as np

    password = os.environ.get("AURORA_REMOTE_PASSWORD")
    if not password:
        raise RuntimeError("AURORA_REMOTE_PASSWORD is not set")
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(args.host, username=args.user, password=password, timeout=15)
    try:
        files, context = download_latest_batch(client, password, args.output_dir)
    finally:
        client.close()
    analyses = [
        analyze_image(path, context["photo_metadata"].get(path.name, {}), cv2, np)
        for path in files
    ]
    write_report(args.output_dir, context, analyses)
    print(f"downloaded={len(files)}")
    print(f"output={args.output_dir.resolve()}")
    print(f"all_grayscale={all(x['is_grayscale'] for x in analyses)}")
    print(f"min_score={min(x['quality_score'] for x in analyses)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
