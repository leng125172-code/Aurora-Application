"""读取指定路径下的 ONNX、RKNN、RKLLM 模型元数据并输出模型信息。"""

from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any

try:
    import onnx
except ImportError:  # pragma: no cover - 运行环境可选依赖
    onnx = None


SUPPORTED_SUFFIXES = {".onnx", ".rknn", ".rkllm"}
PRINTABLE_BYTES_RE = re.compile(rb"[ -~]{4,}")
VERSION_RE = re.compile(r"\b\d+(?:\.\d+){1,3}\b")
RKLLM_MODEL_HINTS = {
    "llama",
    "qwen",
    "chatglm",
    "baichuan",
    "mistral",
    "mixtral",
    "gemma",
    "phi",
    "deepseek",
    "yi",
    "internlm",
    "falcon",
    "rwkv",
    "gpt",
}
RKNN_MODEL_HINTS = {
    "vision-encoder-decoder",
    "trocr",
    "deit",
    "vit",
    "swin",
    "clip",
    "convnext",
    "resnet",
    "mobilenet",
    "efficientnet",
    "yolo",
    "detr",
    "unet",
    "segformer",
    "ocr",
}
VISION_INPUT_HINTS = {"pixel_values", "image", "images", "input_image", "input_tensor"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="扫描指定路径下的 ONNX / RKNN / RKLLM 文件并输出元数据。"
    )
    parser.add_argument("path", nargs="+", help="文件或目录路径，可传多个。")
    parser.add_argument(
        "--no-recursive",
        action="store_true",
        help="当传入目录时不递归扫描子目录。",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="以 JSON 数组输出结果。",
    )
    parser.add_argument(
        "--pretty",
        action="store_true",
        help="JSON 输出时格式化缩进。",
    )
    return parser.parse_args()


def collect_model_files(paths: list[str], recursive: bool) -> list[Path]:
    results: list[Path] = []

    for raw_path in paths:
        path = Path(raw_path).expanduser().resolve()
        if not path.exists():
            continue

        if path.is_file():
            if path.suffix.lower() in SUPPORTED_SUFFIXES:
                results.append(path)
            continue

        if recursive:
            iterator = path.rglob("*")
        else:
            iterator = path.glob("*")

        for item in iterator:
            if item.is_file() and item.suffix.lower() in SUPPORTED_SUFFIXES:
                results.append(item.resolve())

    return sorted(set(results))


def get_common_metadata(file_path: Path) -> dict[str, Any]:
    stat = file_path.stat()
    return {
        "path": str(file_path),
        "name": file_path.name,
        "stem": file_path.stem,
        "format": file_path.suffix.lstrip(".").upper(),
        "sizeBytes": stat.st_size,
        "lastModified": datetime.fromtimestamp(stat.st_mtime).isoformat(),
    }


def extract_printable_strings(data: bytes, limit: int = 20) -> list[str]:
    strings: list[str] = []
    for match in PRINTABLE_BYTES_RE.finditer(data):
        text = match.group().decode("utf-8", errors="ignore").strip()
        if not text:
            continue
        strings.append(text)
        if len(strings) >= limit:
            break
    return strings


def read_binary_header(file_path: Path, max_bytes: int = 4096) -> dict[str, Any]:
    with file_path.open("rb") as handle:
        data = handle.read(max_bytes)

    printable_strings = extract_printable_strings(data)
    version = next(
        (item for item in printable_strings if VERSION_RE.search(item)), None
    )
    return {
        "headerHex": data[:32].hex(" "),
        "headerAscii": "".join(
            chr(byte) if 32 <= byte <= 126 else "." for byte in data[:32]
        ),
        "printableStrings": printable_strings,
        "detectedVersion": version,
    }


def safe_read_text(file_path: Path) -> str | None:
    try:
        return file_path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        try:
            return file_path.read_text(encoding="utf-8-sig")
        except (OSError, UnicodeDecodeError):
            return None
    except OSError:
        return None


def safe_read_json(file_path: Path) -> dict[str, Any] | list[Any] | None:
    text = safe_read_text(file_path)
    if not text:
        return None

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return None


def parse_modelfile(file_path: Path) -> dict[str, Any] | None:
    text = safe_read_text(file_path)
    if not text:
        return None

    result: dict[str, Any] = {}
    parameters: dict[str, str] = {}

    for raw_line in text.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue

        upper = line.upper()
        if upper.startswith("FROM "):
            result["from"] = line[5:].strip()
            continue

        if upper.startswith("PARAMETER "):
            _, _, remainder = line.partition(" ")
            _, _, parameter_value = remainder.partition(" ")
            parameter_name = remainder[: len(remainder) - len(parameter_value)].strip()
            if parameter_name:
                parameters[parameter_name] = parameter_value.strip()
            continue

        key, separator, value = line.partition(":")
        if separator:
            result[key.strip()] = value.strip()

    if parameters:
        result["parameters"] = parameters

    return result or None


def collect_sidecar_metadata(file_path: Path) -> dict[str, Any]:
    metadata: dict[str, Any] = {}
    parent = file_path.parent

    modelfile = parent / "Modelfile"
    if modelfile.exists():
        parsed_modelfile = parse_modelfile(modelfile)
        if parsed_modelfile:
            metadata["modelfile"] = parsed_modelfile

    for candidate_name in (
        "config.json",
        "generation_config.json",
        "tokenizer_config.json",
        "preprocessor_config.json",
    ):
        candidate = parent / candidate_name
        if candidate.exists():
            parsed_json = safe_read_json(candidate)
            if parsed_json is not None:
                metadata[candidate_name] = parsed_json

    return metadata


def simplify_transformers_config(config: dict[str, Any]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key in (
        "model_type",
        "architectures",
        "hidden_size",
        "num_hidden_layers",
        "num_attention_heads",
        "num_key_value_heads",
        "vocab_size",
        "max_position_embeddings",
        "torch_dtype",
    ):
        if key in config:
            result[key] = config[key]
    return result


def append_score(
    scores: dict[str, int],
    reasons: list[str],
    target: str,
    weight: int,
    reason: str,
) -> None:
    scores[target] += weight
    reasons.append(reason)


def contains_hint(value: str | None, hints: set[str]) -> str | None:
    if not value:
        return None

    normalized = value.lower()
    return next((hint for hint in hints if hint in normalized), None)


def infer_runtime_recommendation(
    file_path: Path, metadata: dict[str, Any]
) -> dict[str, Any]:
    suffix = file_path.suffix.lower()
    if suffix == ".rknn":
        return {
            "suggestedTarget": "rknn",
            "confidence": "certain",
            "taskCategory": "已编译 NPU 模型",
            "reasons": ["文件扩展名为 .rknn，已经是瑞芯微 NPU 模型格式。"],
        }

    if suffix == ".rkllm":
        return {
            "suggestedTarget": "rkllm",
            "confidence": "certain",
            "taskCategory": "已编译大语言模型",
            "reasons": ["文件扩展名为 .rkllm，已经是瑞芯微大语言模型格式。"],
        }

    if suffix != ".onnx":
        return {
            "suggestedTarget": "unknown",
            "confidence": "low",
            "taskCategory": "未知",
            "reasons": ["当前仅支持对 ONNX / RKNN / RKLLM 做推荐判断。"],
        }

    sidecar = collect_sidecar_metadata(file_path)
    graph = metadata.get("graph") if isinstance(metadata, dict) else None

    config_data = sidecar.get("config.json")
    config: dict[str, Any] = config_data if isinstance(config_data, dict) else {}

    generation_config_data = sidecar.get("generation_config.json")
    generation_config: dict[str, Any] = (
        generation_config_data if isinstance(generation_config_data, dict) else {}
    )

    tokenizer_config_data = sidecar.get("tokenizer_config.json")
    tokenizer_config: dict[str, Any] = (
        tokenizer_config_data if isinstance(tokenizer_config_data, dict) else {}
    )

    modelfile_data = sidecar.get("modelfile")
    modelfile: dict[str, Any] = (
        modelfile_data if isinstance(modelfile_data, dict) else {}
    )

    scores = {"rknn": 0, "rkllm": 0}
    reasons: list[str] = []

    model_type = config.get("model_type")
    model_type_hint = contains_hint(model_type, RKLLM_MODEL_HINTS)
    if model_type_hint:
        append_score(
            scores,
            reasons,
            "rkllm",
            6,
            f"config.json 的 model_type={model_type}，更像大语言模型家族。",
        )

    vision_model_type_hint = contains_hint(model_type, RKNN_MODEL_HINTS)
    if vision_model_type_hint:
        append_score(
            scores,
            reasons,
            "rknn",
            6,
            f"config.json 的 model_type={model_type}，更像视觉/OCR 模型。",
        )

    architectures = config.get("architectures")
    if isinstance(architectures, list):
        for architecture in architectures:
            if not isinstance(architecture, str):
                continue

            llm_arch_hint = contains_hint(architecture, RKLLM_MODEL_HINTS)
            if llm_arch_hint:
                append_score(
                    scores,
                    reasons,
                    "rkllm",
                    5,
                    f"architectures 包含 {architecture}，更偏向大语言模型。",
                )

            vision_arch_hint = contains_hint(architecture, RKNN_MODEL_HINTS)
            if vision_arch_hint:
                append_score(
                    scores,
                    reasons,
                    "rknn",
                    5,
                    f"architectures 包含 {architecture}，更偏向视觉/OCR 模型。",
                )

            if architecture.lower() == "visionencoderdecodermodel":
                append_score(
                    scores,
                    reasons,
                    "rknn",
                    6,
                    "architectures 包含 VisionEncoderDecoderModel，通常应走 RKNN 视觉推理链路。",
                )

    for nested_key in ("encoder", "decoder"):
        nested_config = config.get(nested_key)
        if not isinstance(nested_config, dict):
            continue

        nested_model_type = nested_config.get("model_type")
        nested_llm_hint = contains_hint(nested_model_type, RKLLM_MODEL_HINTS)
        if nested_llm_hint:
            append_score(
                scores,
                reasons,
                "rkllm",
                4,
                f"{nested_key} 子配置的 model_type={nested_model_type}，存在语言模型特征。",
            )

        nested_vision_hint = contains_hint(nested_model_type, RKNN_MODEL_HINTS)
        if nested_vision_hint:
            append_score(
                scores,
                reasons,
                "rknn",
                4,
                f"{nested_key} 子配置的 model_type={nested_model_type}，存在视觉/OCR 特征。",
            )

    processor_class = tokenizer_config.get("processor_class")
    if isinstance(processor_class, str) and (
        "processor" in processor_class.lower() or "image" in processor_class.lower()
    ):
        append_score(
            scores,
            reasons,
            "rknn",
            3,
            f"tokenizer_config.json 的 processor_class={processor_class}，表明存在视觉前处理。",
        )

    tokenizer_class = tokenizer_config.get("tokenizer_class")
    chat_template = tokenizer_config.get("chat_template")
    if isinstance(chat_template, str) and chat_template.strip():
        append_score(
            scores,
            reasons,
            "rkllm",
            6,
            "tokenizer_config.json 含 chat_template，更像对话式大语言模型。",
        )
    elif isinstance(tokenizer_class, str) and scores["rknn"] == 0:
        append_score(
            scores,
            reasons,
            "rkllm",
            1,
            f"tokenizer_config.json 的 tokenizer_class={tokenizer_class}，存在文本生成特征。",
        )

    if modelfile:
        append_score(
            scores, reasons, "rkllm", 6, "存在 Modelfile，通常对应大语言模型封装。"
        )

    if generation_config:
        if (
            any(
                key in generation_config
                for key in ("temperature", "top_k", "top_p", "do_sample")
            )
            and scores["rknn"] == 0
        ):
            append_score(
                scores,
                reasons,
                "rkllm",
                2,
                "generation_config.json 含采样参数，更偏向文本生成模型。",
            )

    if isinstance(graph, dict):
        inputs = graph.get("inputs")
        if isinstance(inputs, list):
            for item in inputs:
                if not isinstance(item, dict):
                    continue

                input_name = item.get("name")
                if (
                    isinstance(input_name, str)
                    and input_name.lower() in VISION_INPUT_HINTS
                ):
                    append_score(
                        scores,
                        reasons,
                        "rknn",
                        5,
                        f"图输入 {input_name} 带有明显图像张量特征。",
                    )

                shape = item.get("shape")
                if isinstance(shape, list) and len(shape) >= 4:
                    append_score(
                        scores,
                        reasons,
                        "rknn",
                        3,
                        f"图输入 {input_name} 维度为 {shape}，更像图像张量。",
                    )

                if input_name == "input_ids" and scores["rknn"] == 0:
                    append_score(
                        scores,
                        reasons,
                        "rkllm",
                        1,
                        "图输入包含 input_ids，存在文本序列特征。",
                    )

                if input_name == "encoder_hidden_states":
                    append_score(
                        scores,
                        reasons,
                        "rknn",
                        2,
                        "图输入包含 encoder_hidden_states，更像视觉编码器接文本解码器的 OCR 结构。",
                    )

    if "encoder" in file_path.stem.lower() or "decoder" in file_path.stem.lower():
        if scores["rknn"] > 0 and scores["rkllm"] <= 2:
            append_score(
                scores,
                reasons,
                "rknn",
                1,
                f"文件名 {file_path.stem} 呈现编码器/解码器拆分形式，常见于视觉推理导出。",
            )

    if scores["rknn"] == 0 and scores["rkllm"] == 0:
        return {
            "suggestedTarget": "unknown",
            "confidence": "low",
            "taskCategory": "未知",
            "reasons": [
                "未找到足够的模型家族线索，无法稳定判断应转为 RKNN 还是 RKLLM。"
            ],
            "scores": scores,
        }

    suggested_target = "rknn" if scores["rknn"] >= scores["rkllm"] else "rkllm"
    score_gap = abs(scores["rknn"] - scores["rkllm"])
    confidence = "high" if score_gap >= 4 else "medium"
    task_category = (
        "视觉/OCR/检测" if suggested_target == "rknn" else "大语言模型/文本生成"
    )
    summary = (
        "更适合转换为 RKNN" if suggested_target == "rknn" else "更适合转换为 RKLLM"
    )

    return {
        "suggestedTarget": suggested_target,
        "confidence": confidence,
        "taskCategory": task_category,
        "summary": summary,
        "scores": scores,
        "reasons": reasons,
    }


def parse_onnx_metadata(file_path: Path) -> dict[str, Any]:
    if onnx is None:
        return {
            "warning": "未安装 onnx 依赖，无法解析结构化 ONNX 元数据。可执行 pip install onnx 后重试。"
        }

    model = onnx.load(str(file_path), load_external_data=False)
    opsets = [
        {
            "domain": item.domain or "ai.onnx",
            "version": item.version,
        }
        for item in model.opset_import
    ]

    metadata_props = {item.key: item.value for item in model.metadata_props}

    return {
        "irVersion": model.ir_version,
        "producerName": model.producer_name or None,
        "producerVersion": model.producer_version or None,
        "domain": model.domain or None,
        "modelVersion": model.model_version,
        "docString": model.doc_string or None,
        "graph": {
            "name": model.graph.name or None,
            "nodeCount": len(model.graph.node),
            "initializerCount": len(model.graph.initializer),
            "inputs": [format_onnx_value_info(item) for item in model.graph.input],
            "outputs": [format_onnx_value_info(item) for item in model.graph.output],
        },
        "opsets": opsets,
        "metadata": metadata_props,
    }


def format_onnx_value_info(value_info: Any) -> dict[str, Any]:
    if onnx is None:
        raise RuntimeError("当前环境未安装 onnx，无法格式化 ONNX 张量信息。")

    tensor_type = value_info.type.tensor_type
    shape: list[Any] = []

    for dim in tensor_type.shape.dim:
        if dim.HasField("dim_value"):
            shape.append(dim.dim_value)
        elif dim.HasField("dim_param"):
            shape.append(dim.dim_param)
        else:
            shape.append(None)

    elem_type = None
    if tensor_type.elem_type:
        elem_type = onnx.TensorProto.DataType.Name(tensor_type.elem_type)

    return {
        "name": value_info.name,
        "elemType": elem_type,
        "shape": shape,
    }


def parse_rknn_metadata(file_path: Path) -> dict[str, Any]:
    header = read_binary_header(file_path)
    sidecar = collect_sidecar_metadata(file_path)

    simplified_sidecar: dict[str, Any] = {}
    config = sidecar.get("config.json")
    if isinstance(config, dict):
        simplified_sidecar["config"] = simplify_transformers_config(config)

    metadata: dict[str, Any] = {
        "binaryHeader": header,
    }
    if simplified_sidecar:
        metadata["sidecar"] = simplified_sidecar
    return metadata


def parse_rkllm_metadata(file_path: Path) -> dict[str, Any]:
    header = read_binary_header(file_path)
    sidecar = collect_sidecar_metadata(file_path)

    metadata: dict[str, Any] = {
        "binaryHeader": header,
    }

    modelfile = sidecar.get("modelfile")
    if isinstance(modelfile, dict):
        metadata["modelfile"] = modelfile

    config = sidecar.get("config.json")
    if isinstance(config, dict):
        metadata["config"] = simplify_transformers_config(config)

    generation_config = sidecar.get("generation_config.json")
    if isinstance(generation_config, dict):
        metadata["generationConfig"] = {
            key: generation_config[key]
            for key in (
                "max_length",
                "max_new_tokens",
                "do_sample",
                "temperature",
                "top_k",
                "top_p",
            )
            if key in generation_config
        }

    tokenizer_config = sidecar.get("tokenizer_config.json")
    if isinstance(tokenizer_config, dict):
        metadata["tokenizerConfig"] = {
            key: tokenizer_config[key]
            for key in ("tokenizer_class", "model_max_length", "chat_template")
            if key in tokenizer_config
        }

    return metadata


def inspect_model(file_path: Path) -> dict[str, Any]:
    result = get_common_metadata(file_path)
    suffix = file_path.suffix.lower()

    try:
        if suffix == ".onnx":
            result["metadata"] = parse_onnx_metadata(file_path)
        elif suffix == ".rknn":
            result["metadata"] = parse_rknn_metadata(file_path)
        elif suffix == ".rkllm":
            result["metadata"] = parse_rkllm_metadata(file_path)
        else:
            result["metadata"] = {"warning": "不支持的文件格式。"}

        metadata = result.get("metadata")
        if isinstance(metadata, dict):
            result["runtimeRecommendation"] = infer_runtime_recommendation(
                file_path, metadata
            )
    except Exception as exc:  # pragma: no cover - 便于命令行容错
        result["error"] = str(exc)

    return result


def print_human_readable(items: list[dict[str, Any]]) -> None:
    if not items:
        print("未找到 ONNX / RKNN / RKLLM 文件。")
        return

    for index, item in enumerate(items, start=1):
        print(f"[{index}] {item['name']}")
        print(f"  路径: {item['path']}")
        print(f"  格式: {item['format']}")
        print(f"  大小: {item['sizeBytes']} 字节")
        print(f"  修改时间: {item['lastModified']}")

        metadata = item.get("metadata")
        if metadata:
            print("  元数据:")
            print(
                indent_text(json.dumps(metadata, ensure_ascii=False, indent=2), "    ")
            )

        runtime_recommendation = item.get("runtimeRecommendation")
        if runtime_recommendation:
            print("  转换建议:")
            print(
                indent_text(
                    json.dumps(runtime_recommendation, ensure_ascii=False, indent=2),
                    "    ",
                )
            )

        error = item.get("error")
        if error:
            print(f"  错误: {error}")

        print()


def indent_text(text: str, prefix: str) -> str:
    return "\n".join(f"{prefix}{line}" for line in text.splitlines())


def main() -> int:
    args = parse_args()
    files = collect_model_files(args.path, recursive=not args.no_recursive)
    items = [inspect_model(file_path) for file_path in files]

    if args.json:
        json.dump(
            items, sys.stdout, ensure_ascii=False, indent=2 if args.pretty else None
        )
        sys.stdout.write("\n")
        return 0

    print_human_readable(items)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
