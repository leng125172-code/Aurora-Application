from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import Any, Optional


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Aurora AI 模型转换脚本")
    parser.add_argument("--target", required=True, choices=["rknn"])
    parser.add_argument("--model-id", required=True)
    parser.add_argument("--model-name", required=True)
    parser.add_argument("--generation-condition")
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--result-json", required=True)
    parser.add_argument("--input", action="append", default=[])
    parser.add_argument("--source-file-id", action="append", default=[])
    parser.add_argument("--file-role", action="append", default=[])
    parser.add_argument("--sort-order", action="append", default=[])
    return parser.parse_args()


def ensure_lengths(args: argparse.Namespace) -> None:
    if len(args.input) == 0:
        raise ValueError("至少需要提供一个输入文件。")

    counts = {
        len(args.input),
        len(args.source_file_id),
        len(args.file_role),
        len(args.sort_order),
    }
    if len(counts) != 1:
        raise ValueError("输入文件参数数量不一致。")


def read_positive_int_env(name: str, default_value: int) -> int:
    raw_value = os.getenv(name)
    if not raw_value:
        return default_value

    try:
        value = int(raw_value)
    except ValueError as ex:
        raise ValueError(f"环境变量 {name} 必须是正整数。") from ex

    if value <= 0:
        raise ValueError(f"环境变量 {name} 必须大于 0。")

    return value


def normalize_conversion_option_key(raw_key: str) -> str:
    normalized = raw_key.strip().lower().replace("-", "_")
    if normalized.startswith("rknn."):
        normalized = normalized[5:]
    elif normalized.startswith("rknn_"):
        normalized = normalized[5:]

    alias_map = {
        "imagesize": "image_size",
        "image_size": "image_size",
        "sequencelength": "sequence_length",
        "sequence_length": "sequence_length",
        "encodersequencelength": "encoder_sequence_length",
        "encoder_sequence_length": "encoder_sequence_length",
        "decodersequencelength": "decoder_sequence_length",
        "decoder_sequence_length": "decoder_sequence_length",
        "featuresequencelength": "feature_sequence_length",
        "feature_sequence_length": "feature_sequence_length",
        "inputsizelist": "input_size_list",
        "input_size_list": "input_size_list",
        "targetplatform": "target_platform",
        "target_platform": "target_platform",
    }
    return alias_map.get(normalized, normalized)


def parse_generation_condition_options(raw_value: Optional[str]) -> dict[str, Any]:
    if not raw_value or not raw_value.strip():
        return {}

    allowed_keys = {
        "image_size",
        "sequence_length",
        "encoder_sequence_length",
        "decoder_sequence_length",
        "feature_sequence_length",
        "input_size_list",
        "target_platform",
    }
    text = raw_value.strip()

    def filter_options(candidate: dict[str, Any]) -> dict[str, Any]:
        filtered: dict[str, Any] = {}
        for key, value in candidate.items():
            normalized_key = normalize_conversion_option_key(str(key))
            if normalized_key in allowed_keys:
                filtered[normalized_key] = value
        return filtered

    if text.startswith("{"):
        try:
            parsed = json.loads(text)
        except json.JSONDecodeError as ex:
            raise ValueError("生成条件中的转换参数 JSON 无法解析。") from ex

        if isinstance(parsed, dict):
            nested = parsed.get("rknn")
            if isinstance(nested, dict):
                return filter_options(nested)
            return filter_options(parsed)

        return {}

    options: dict[str, Any] = {}
    parts = [
        segment.strip() for segment in re.split(r"[;\n,]", text) if segment.strip()
    ]
    for part in parts:
        delimiter = "=" if "=" in part else (":" if ":" in part else None)
        if delimiter is None:
            continue

        raw_key, raw_option_value = part.split(delimiter, 1)
        option_key = normalize_conversion_option_key(raw_key)
        if option_key not in allowed_keys:
            continue

        option_value = raw_option_value.strip()
        if not option_value:
            continue

        if option_key == "input_size_list":
            try:
                options[option_key] = json.loads(option_value)
            except json.JSONDecodeError as ex:
                raise ValueError("生成条件中的 input_size_list 不是合法 JSON。") from ex
            continue

        options[option_key] = option_value

    return options


def read_positive_int_option(
    options: dict[str, Any],
    option_name: str,
    default_value: int,
) -> int:
    if option_name in options:
        raw_value = options[option_name]
        try:
            value = int(raw_value)
        except (TypeError, ValueError) as ex:
            raise ValueError(f"生成条件参数 {option_name} 必须是正整数。") from ex

        if value <= 0:
            raise ValueError(f"生成条件参数 {option_name} 必须大于 0。")

        return value

    return default_value


def normalize_rknn_target_platform(raw_value: str) -> Optional[str]:
    normalized = raw_value.strip().lower()
    if not normalized:
        return None

    match = re.search(r"((?:rk|rv)\d{4})", normalized)
    if match is None:
        return None

    return match.group(1)


def resolve_rknn_target_platform(
    options: Optional[dict[str, Any]] = None,
) -> tuple[str, str]:
    if options and options.get("target_platform"):
        target_platform = normalize_rknn_target_platform(
            str(options["target_platform"])
        )
        if target_platform is None:
            raise ValueError(
                "生成条件参数 target_platform 必须包含形如 rk3588 的目标平台标识。"
            )

        return (
            target_platform,
            "RKNN 使用生成条件指定目标平台：" f"{target_platform}",
        )

    configured_value = os.getenv("AURORA_RKNN_TARGET_PLATFORM")
    if configured_value:
        target_platform = normalize_rknn_target_platform(configured_value)
        if target_platform is None:
            raise ValueError(
                "环境变量 AURORA_RKNN_TARGET_PLATFORM 必须包含形如 rk3588 的目标平台标识。"
            )

        return (
            target_platform,
            "RKNN 使用环境变量 AURORA_RKNN_TARGET_PLATFORM 指定目标平台："
            f"{target_platform}",
        )

    compatible_path = Path("/proc/device-tree/compatible")
    if not compatible_path.exists():
        raise ValueError(
            "未找到 /proc/device-tree/compatible，无法自动识别 Rockchip 目标平台；"
            "请设置 AURORA_RKNN_TARGET_PLATFORM。"
        )

    compatible_text = compatible_path.read_bytes().decode("utf-8", errors="ignore")
    for raw_line in compatible_text.replace("\x00", "\n").splitlines():
        target_platform = normalize_rknn_target_platform(raw_line)
        if target_platform is not None:
            return (
                target_platform,
                "RKNN 根据 /proc/device-tree/compatible 自动识别目标平台："
                f"{target_platform}",
            )

    raise ValueError(
        "无法从 /proc/device-tree/compatible 识别 Rockchip 目标平台；"
        "请设置 AURORA_RKNN_TARGET_PLATFORM。"
    )


def load_onnx_graph_inputs(input_path: Path) -> list[tuple[str, list[Any]]]:
    import onnx

    model = onnx.load_model(str(input_path), load_external_data=False)
    initializer_names = {item.name for item in model.graph.initializer}
    graph_inputs: list[tuple[str, list[Any]]] = []

    for value_info in model.graph.input:
        if value_info.name in initializer_names:
            continue

        tensor_type = value_info.type.tensor_type
        if not tensor_type.HasField("shape"):
            continue

        dimensions: list[Any] = []
        for dim in tensor_type.shape.dim:
            if dim.dim_value > 0:
                dimensions.append(int(dim.dim_value))
            else:
                dimensions.append(dim.dim_param or None)

        graph_inputs.append((value_info.name, dimensions))

    return graph_inputs


def try_detect_text_sequence_length_from_initializers(
    input_path: Path,
) -> Optional[int]:
    import onnx

    model = onnx.load_model(str(input_path), load_external_data=False)
    candidates: list[int] = []

    for initializer in model.graph.initializer:
        name = initializer.name.lower()
        if not any(
            token in name
            for token in (
                "position_embedding",
                "position_embeddings",
                "position_ids",
                "embed_positions",
                "wpe",
            )
        ):
            continue

        dims = [int(dim) for dim in initializer.dims if int(dim) > 0]
        if not dims:
            continue

        # BERT/GPT 系列的位置编码通常是 [max_position, hidden_size] 或 [1, max_position, hidden_size]
        if len(dims) >= 2:
            candidates.append(dims[-2] if len(dims) >= 3 and dims[0] == 1 else dims[0])
        else:
            candidates.append(dims[0])

    if not candidates:
        return None

    return max(value for value in candidates if value > 1)


def resolve_text_sequence_lengths(
    input_path: Path,
    graph_inputs: list[tuple[str, list[Any]]],
    options: dict[str, Any],
) -> tuple[int, int, int]:
    sequence_length = read_positive_int_option(
        options,
        "sequence_length",
        128,
    )
    encoder_sequence_length = read_positive_int_option(
        options,
        "encoder_sequence_length",
        sequence_length,
    )
    decoder_sequence_length = read_positive_int_option(
        options,
        "decoder_sequence_length",
        1,
    )

    if "sequence_length" in options or "encoder_sequence_length" in options:
        return sequence_length, encoder_sequence_length, decoder_sequence_length

    detected_sequence_length = try_detect_text_sequence_length_from_initializers(
        input_path
    )
    if detected_sequence_length is not None:
        encoder_sequence_length = detected_sequence_length
        sequence_length = detected_sequence_length

    positive_text_lengths: list[int] = []
    for input_name, dimensions in graph_inputs:
        normalized_name = input_name.lower()
        is_text_input = (
            normalized_name
            in {
                "input_ids",
                "attention_mask",
                "position_ids",
                "token_type_ids",
                "decoder_input_ids",
                "decoder_attention_mask",
            }
            or normalized_name.endswith("_ids")
            or "mask" in normalized_name
        )
        if not is_text_input:
            continue

        if len(dimensions) < 2:
            continue

        for dim in dimensions[1:]:
            if isinstance(dim, int) and dim > 1:
                positive_text_lengths.append(dim)
                break

    if positive_text_lengths:
        encoder_sequence_length = min(
            encoder_sequence_length, min(positive_text_lengths)
        )
        sequence_length = encoder_sequence_length

    return sequence_length, encoder_sequence_length, decoder_sequence_length


def normalize_input_size_list(
    raw_value: Any,
    input_names: Optional[list[str]],
) -> list[list[int]]:
    ordered_value = raw_value
    if isinstance(raw_value, dict):
        if input_names is None:
            raise ValueError(
                "环境变量 AURORA_RKNN_INPUT_SIZE_LIST 使用对象映射时，必须能够读取 ONNX 输入名。"
            )

        missing_names = [name for name in input_names if name not in raw_value]
        if missing_names:
            raise ValueError(
                "环境变量 AURORA_RKNN_INPUT_SIZE_LIST 缺少输入定义："
                + ", ".join(missing_names)
            )

        ordered_value = [raw_value[name] for name in input_names]

    if not isinstance(ordered_value, list):
        raise ValueError(
            "环境变量 AURORA_RKNN_INPUT_SIZE_LIST 必须是 JSON 数组，或按输入名映射的 JSON 对象。"
        )

    if input_names is not None and len(ordered_value) != len(input_names):
        raise ValueError(
            "环境变量 AURORA_RKNN_INPUT_SIZE_LIST 的输入数量与 ONNX 模型不一致。"
        )

    normalized: list[list[int]] = []
    for index, dims in enumerate(ordered_value):
        if not isinstance(dims, list) or len(dims) == 0:
            raise ValueError(
                f"环境变量 AURORA_RKNN_INPUT_SIZE_LIST 的第 {index + 1} 个输入尺寸无效。"
            )

        normalized_dims: list[int] = []
        for dim in dims:
            if not isinstance(dim, int) or dim <= 0:
                raise ValueError(
                    f"环境变量 AURORA_RKNN_INPUT_SIZE_LIST 的第 {index + 1} 个输入存在非正整数维度。"
                )

            normalized_dims.append(dim)

        normalized.append(normalized_dims)

    return normalized


def has_dynamic_dimensions(graph_inputs: list[tuple[str, list[Any]]]) -> bool:
    return any(
        any(not isinstance(dim, int) or dim <= 0 for dim in dimensions)
        for _, dimensions in graph_inputs
    )


def has_name_hint(name: str, hints: tuple[str, ...]) -> bool:
    return any(hint in name for hint in hints)


def is_decoder_context(
    input_path: Path,
    graph_inputs: list[tuple[str, list[Any]]],
) -> bool:
    normalized_stem = input_path.stem.lower()
    normalized_input_names = [name.lower() for name, _ in graph_inputs]
    return (
        "decoder" in normalized_stem
        or any(name.startswith("decoder_") for name in normalized_input_names)
        or "encoder_hidden_states" in normalized_input_names
        or any("past_key_values" in name for name in normalized_input_names)
    )


def infer_rknn_input_shape(
    input_path: Path,
    graph_inputs: list[tuple[str, list[Any]]],
    input_name: str,
    dimensions: list[Any],
    options: dict[str, Any],
) -> list[int]:
    batch_size = read_positive_int_env("AURORA_RKNN_BATCH_SIZE", 1)
    sequence_length, encoder_sequence_length, decoder_sequence_length = (
        resolve_text_sequence_lengths(input_path, graph_inputs, options)
    )
    feature_sequence_length = read_positive_int_option(
        options,
        "feature_sequence_length",
        encoder_sequence_length,
    )
    image_size = read_positive_int_option(
        options,
        "image_size",
        224,
    )

    normalized_name = input_name.lower()
    decoder_context = is_decoder_context(input_path, graph_inputs)
    is_text_input = (
        normalized_name
        in {
            "input_ids",
            "attention_mask",
            "position_ids",
            "token_type_ids",
            "decoder_input_ids",
            "decoder_attention_mask",
        }
        or normalized_name.endswith("_ids")
        or "mask" in normalized_name
    )
    is_decoder_sequence_input = (
        normalized_name.startswith("decoder_")
        or normalized_name in {"decoder_input_ids", "decoder_attention_mask"}
        or (
            decoder_context
            and normalized_name
            in {"input_ids", "attention_mask", "position_ids", "token_type_ids"}
        )
    )
    is_feature_sequence_input = normalized_name in {
        "encoder_hidden_states",
        "inputs_embeds",
        "image_embeds",
        "vision_embeds",
        "visual_embeds",
    } or has_name_hint(
        normalized_name,
        ("hidden_states", "embeds", "features", "feature", "encoder_outputs"),
    )
    is_image_input = normalized_name in {
        "pixel_values",
        "image",
        "images",
        "input_image",
    } or has_name_hint(
        normalized_name,
        ("pixel", "image", "vision", "visual"),
    )

    resolved: list[int] = []
    rank = len(dimensions)
    text_sequence_axis = next(
        (
            index
            for index in range(1, rank)
            if index == 1
            or any(
                token in str(dimensions[index] or "").lower()
                for token in ("sequence", "seq", "token", "length", "step", "time")
            )
        ),
        1 if rank > 1 else -1,
    )

    for index, dim in enumerate(dimensions):
        if (
            isinstance(dim, int)
            and dim > 0
            and not (
                index == text_sequence_axis
                and (
                    is_text_input
                    or is_decoder_sequence_input
                    or is_feature_sequence_input
                )
            )
        ):
            resolved.append(dim)
            continue

        symbolic = str(dim or "").lower()

        if index == 0 or "batch" in symbolic:
            resolved.append(batch_size)
            continue

        if rank >= 4 and index == 1 and "channel" in symbolic:
            resolved.append(3)
            continue

        if rank >= 4 and index >= rank - 2:
            resolved.append(image_size)
            continue

        if "channel" in symbolic:
            resolved.append(3)
            continue

        if is_image_input and rank >= 4:
            resolved.append(image_size)
            continue

        if is_feature_sequence_input and (
            any(
                token in symbolic
                for token in ("sequence", "seq", "token", "length", "step", "time")
            )
            or (rank == 3 and 0 < index < rank - 1)
        ):
            resolved.append(feature_sequence_length)
            continue

        if is_decoder_sequence_input and any(
            token in symbolic
            for token in ("sequence", "seq", "token", "length", "step", "time")
        ):
            resolved.append(decoder_sequence_length)
            continue

        if (
            is_text_input
            or index == text_sequence_axis
            or any(
                token in symbolic
                for token in ("sequence", "seq", "token", "length", "step", "time")
            )
        ):
            resolved.append(encoder_sequence_length)
            continue

        resolved.append(1)

    return resolved


def resolve_rknn_load_inputs(
    input_path: Path,
    generation_condition: Optional[str] = None,
) -> tuple[Optional[list[str]], Optional[list[list[int]]], Optional[str]]:
    options = parse_generation_condition_options(generation_condition)
    raw_input_size_list: Any = options.get("input_size_list")

    try:
        graph_inputs = load_onnx_graph_inputs(input_path)
    except ModuleNotFoundError as ex:
        if isinstance(raw_input_size_list, dict):
            raise ValueError(
                "生成条件中的 input_size_list 使用对象映射时，需要安装 onnx 模块以解析输入顺序；"
                "或者直接改为按输入顺序传入 JSON 数组。"
            ) from ex

        if isinstance(raw_input_size_list, list):
            input_size_list = normalize_input_size_list(raw_input_size_list, None)
            mapping = {str(index): dims for index, dims in enumerate(input_size_list)}
            return (
                None,
                input_size_list,
                "RKNN 使用生成条件中的 input_size_list 指定输入尺寸："
                + json.dumps(mapping, ensure_ascii=False),
            )

        return (
            None,
            None,
            "未安装 onnx，跳过 RKNN 动态输入推断；如模型包含动态维度，请安装 onnx 或在生成条件中设置 input_size_list。",
        )

    input_names = [name for name, _ in graph_inputs]

    if raw_input_size_list is not None:
        input_size_list = normalize_input_size_list(
            raw_input_size_list, input_names or None
        )
        mapping = {
            input_names[index] if index < len(input_names) else str(index): dims
            for index, dims in enumerate(input_size_list)
        }
        return (
            input_names,
            input_size_list,
            "RKNN 使用生成条件中的 input_size_list 指定输入尺寸："
            + json.dumps(mapping, ensure_ascii=False),
        )

    if not graph_inputs or not has_dynamic_dimensions(graph_inputs):
        return input_names or None, None, None

    input_size_list = [
        infer_rknn_input_shape(input_path, graph_inputs, name, dimensions, options)
        for name, dimensions in graph_inputs
    ]
    mapping = {
        name: input_size_list[index] for index, (name, _) in enumerate(graph_inputs)
    }
    return (
        input_names,
        input_size_list,
        "RKNN 检测到动态输入，自动推断输入尺寸："
        + json.dumps(mapping, ensure_ascii=False),
    )


def convert_to_rknn(
    input_path: Path,
    output_path: Path,
    generation_condition: Optional[str] = None,
) -> None:
    from rknn.api import RKNN  # type: ignore

    conversion_options = parse_generation_condition_options(generation_condition)
    rknn = RKNN(verbose=False)
    try:
        target_platform, platform_message = resolve_rknn_target_platform(
            conversion_options
        )
        print(platform_message)
        rknn.config(target_platform=target_platform)

        input_names, input_size_list, load_message = resolve_rknn_load_inputs(
            input_path,
            generation_condition,
        )
        if load_message:
            print(load_message)

        load_kwargs: dict[str, Any] = {}
        if input_names:
            load_kwargs["inputs"] = input_names
        if input_size_list:
            load_kwargs["input_size_list"] = input_size_list

        try:
            ret = rknn.load_onnx(model=str(input_path), **load_kwargs)
        except Exception as ex:
            if input_size_list:
                raise RuntimeError(
                    f"加载 ONNX 失败：{input_path}。已使用 input_size_list={input_size_list}。原始错误：{ex}"
                ) from ex
            if load_message:
                raise RuntimeError(
                    f"加载 ONNX 失败：{input_path}。{load_message} 原始错误：{ex}"
                ) from ex
            raise

        if ret != 0:
            details = (
                f"，input_size_list={input_size_list}"
                if input_size_list is not None
                else ""
            )
            raise RuntimeError(f"加载 ONNX 失败：{input_path}{details}")

        ret = rknn.build(do_quantization=False)
        if ret != 0:
            raise RuntimeError(f"构建 RKNN 失败：{input_path}")

        ret = rknn.export_rknn(str(output_path))
        if ret != 0:
            raise RuntimeError(f"导出 RKNN 失败：{output_path}")
    finally:
        release = getattr(rknn, "release", None)
        if callable(release):
            release()


def file_role_text(raw_value: str) -> str:
    try:
        role = int(raw_value)
    except ValueError:
        return raw_value

    if role == 1:
        return "SplitEncoder"
    if role == 2:
        return "SplitDecoder"
    if role == 0:
        return "SingleWholeModel"
    return str(role)


def build_output_name(
    input_path: Path,
    source_file_id: str,
    used_output_names: set[str],
) -> str:
    base_name = f"{input_path.stem}.rknn"
    if base_name not in used_output_names:
        used_output_names.add(base_name)
        return base_name

    source_suffix = source_file_id.replace("-", "")[:8] or "file"
    candidate = f"{input_path.stem}-{source_suffix}.rknn"
    if candidate not in used_output_names:
        used_output_names.add(candidate)
        return candidate

    sequence = 2
    while True:
        candidate = f"{input_path.stem}-{source_suffix}-{sequence}.rknn"
        if candidate not in used_output_names:
            used_output_names.add(candidate)
            return candidate

        sequence += 1


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
    used_output_names: set[str] = set()

    try:
        print(
            f"开始执行 {args.target.upper()} 转换：模型 {args.model_name} ({args.model_id})，"
            f"输入文件 {len(args.input)} 个。"
        )

        for index, raw_input in enumerate(args.input):
            input_path = Path(raw_input).resolve()
            output_name = build_output_name(
                input_path,
                args.source_file_id[index],
                used_output_names,
            )
            output_path = output_dir / output_name
            role_text = file_role_text(args.file_role[index])
            print(
                f"开始转换文件：{input_path.name}，角色={role_text}，排序={args.sort_order[index]}。"
            )

            try:
                convert_to_rknn(
                    input_path,
                    output_path,
                    args.generation_condition,
                )
            except Exception as ex:
                raise RuntimeError(
                    f"文件 {input_path.name}（角色={role_text}，排序={args.sort_order[index]}）转换失败：{ex}"
                ) from ex

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
