from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Aurora AI 模型转换脚本")
    parser.add_argument("--target", required=True, choices=["rknn", "rkllm"])
    parser.add_argument("--model-id", required=True)
    parser.add_argument("--model-name", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--result-json", required=True)
    parser.add_argument("--input", action="append", default=[])
    parser.add_argument("--source-file-id", action="append", default=[])
    parser.add_argument("--file-role", action="append", default=[])
    parser.add_argument("--sort-order", action="append", default=[])
    return parser.parse_args()


def ensure_lengths(args: argparse.Namespace) -> None:
    counts = {
        len(args.input),
        len(args.source_file_id),
        len(args.file_role),
        len(args.sort_order),
    }
    if len(counts) != 1:
        raise ValueError("输入文件参数数量不一致。")


def convert_to_rknn(input_path: Path, output_path: Path) -> None:
    from rknn.api import RKNN  # type: ignore

    rknn = RKNN(verbose=False)
    rknn.config(target_platform="rk3588")
    ret = rknn.load_onnx(model=str(input_path))
    if ret != 0:
        raise RuntimeError(f"加载 ONNX 失败：{input_path}")

    ret = rknn.build(do_quantization=False)
    if ret != 0:
        raise RuntimeError(f"构建 RKNN 失败：{input_path}")

    ret = rknn.export_rknn(str(output_path))
    if ret != 0:
        raise RuntimeError(f"导出 RKNN 失败：{output_path}")


def convert_to_rkllm(input_path: Path, output_path: Path) -> None:
    external_command = os.getenv("AURORA_RKLLM_CONVERTER_CMD")
    if not external_command:
        raise RuntimeError(
            "未配置 RKLLM 转换命令。请设置环境变量 AURORA_RKLLM_CONVERTER_CMD。"
        )

    command = external_command.format(input=str(input_path), output=str(output_path))
    completed = subprocess.run(command, shell=True, capture_output=True, text=True)
    if completed.returncode != 0:
        raise RuntimeError(
            completed.stderr.strip() or completed.stdout.strip() or "RKLLM 转换失败"
        )


def build_output_name(input_path: Path, target: str) -> str:
    suffix = ".rknn" if target == "rknn" else ".rkllm"
    return f"{input_path.stem}{suffix}"


def main() -> int:
    args = parse_args()
    ensure_lengths(args)
    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    result: dict[str, Any] = {
        "success": False,
        "message": "",
        "standardOutput": "",
        "standardError": "",
        "outputFiles": [],
    }

    try:
        for index, raw_input in enumerate(args.input):
            input_path = Path(raw_input).resolve()
            output_name = build_output_name(input_path, args.target)
            output_path = output_dir / output_name

            if args.target == "rknn":
                convert_to_rknn(input_path, output_path)
            else:
                convert_to_rkllm(input_path, output_path)

            result["outputFiles"].append(
                {
                    "sourceFileId": args.source_file_id[index],
                    "filePath": str(output_path),
                    "fileName": output_name,
                    "displayName": output_path.stem,
                    "fileFormat": output_path.suffix.lstrip(".").upper(),
                    "fileRole": int(args.file_role[index]),
                    "sortOrder": int(args.sort_order[index]),
                }
            )

        result["success"] = True
        result["message"] = (
            f"{args.target.upper()} 转换完成，共生成 {len(result['outputFiles'])} 个文件。"
        )
    except Exception as ex:
        result["success"] = False
        result["message"] = str(ex)
        result["standardError"] = str(ex)

    Path(args.result_json).write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return 0 if result["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
