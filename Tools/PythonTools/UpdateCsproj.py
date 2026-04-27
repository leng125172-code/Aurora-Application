from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as element_tree
from dataclasses import dataclass, field
from pathlib import Path


COMMON_PROPS_RELATIVE_PATH = r"..\..\..\common.props"
DEFAULT_OLLAMA_URL = "http://127.0.0.1:11434/api/generate"
EXCLUDED_DIRECTORY_NAMES = {"bin", "obj", ".git", ".vs"}


@dataclass
class ProjectUpdateResult:
    """记录单个项目文件的处理结果。"""

    project_path: Path
    changed: bool = False
    messages: list[str] = field(default_factory=list)


class OllamaClient:
    """调用 Ollama 生成项目描述。"""

    def __init__(self, model: str, endpoint: str, timeout_seconds: int) -> None:
        self.model = model
        self.endpoint = endpoint
        self.timeout_seconds = timeout_seconds

    def generate_description(
        self,
        project_name: str,
        project_path: Path,
        sdk_name: str,
        project_references: list[str],
        package_references: list[str],
        existing_description: str | None,
    ) -> str:
        """生成或更新项目描述。"""

        reference_text = ", ".join(project_references) if project_references else "无"
        package_text = ", ".join(package_references[:12]) if package_references else "无"
        if len(package_references) > 12:
            package_text = f"{package_text} 等"

        prompt = (
            "你是 .NET 项目元数据生成助手。\n"
            "请根据下面信息，为一个 .csproj 的 <Description> 生成一条简洁、专业、适合作为 NuGet 包描述的中文文本。\n"
            "要求：\n"
            "1. 只输出最终描述文本，不要输出解释、标题、引号、Markdown。\n"
            "2. 长度控制在 18 到 60 个中文字符之间。\n"
            "3. 如果项目名包含 Application、Domain、HttpApi、EntityFrameworkCore、DbMigrator、WebGateway、Test、Contracts 等语义，请结合语义生成更准确描述。\n"
            "4. 如果已有描述，请基于现有描述优化，而不是照抄项目名。\n\n"
            f"项目名：{project_name}\n"
            f"项目路径：{project_path}\n"
            f"SDK：{sdk_name or '未知'}\n"
            f"项目引用：{reference_text}\n"
            f"包引用：{package_text}\n"
            f"现有描述：{existing_description or '无'}\n"
        )

        payload = json.dumps(
            {
                "model": self.model,
                "prompt": prompt,
                "stream": False,
            }
        ).encode("utf-8")

        request = urllib.request.Request(
            self.endpoint,
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )

        try:
            with urllib.request.urlopen(request, timeout=self.timeout_seconds) as response:
                response_text = response.read().decode("utf-8")
        except urllib.error.HTTPError as error:
            detail = error.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"Ollama 请求失败：HTTP {error.code}，{detail}") from error
        except urllib.error.URLError as error:
            raise RuntimeError(f"无法连接到 Ollama：{error}") from error

        try:
            response_payload = json.loads(response_text)
        except json.JSONDecodeError as error:
            raise RuntimeError(f"Ollama 返回了无法解析的 JSON：{response_text}") from error

        description = " ".join(str(response_payload.get("response", "")).split()).strip()
        if not description:
            raise RuntimeError(f"Ollama 未返回有效描述：{response_payload}")

        return description


def create_argument_parser() -> argparse.ArgumentParser:
    """创建命令行参数解析器。"""

    default_root = Path(__file__).resolve().parents[2]

    parser = argparse.ArgumentParser(
        description="批量更新 Aurora Application 下的 .csproj 文件。"
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=default_root,
        help="仓库根目录，默认使用当前脚本推导出的仓库根目录。",
    )
    parser.add_argument(
        "--model",
        default="llama3.2:3b",
        help="Ollama 模型名称，默认值为 llama3.2:3b。",
    )
    parser.add_argument(
        "--ollama-url",
        default=DEFAULT_OLLAMA_URL,
        help="Ollama generate 接口地址。",
    )
    parser.add_argument(
        "--timeout",
        type=int,
        default=120,
        help="调用 Ollama 的超时时间（秒）。",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="仅输出将要发生的变更，不写回文件。",
    )

    return parser


def main() -> int:
    """脚本入口。"""

    parser = create_argument_parser()
    arguments = parser.parse_args()

    root_path = arguments.root.resolve()
    if not root_path.exists():
        print(f"仓库根目录不存在：{root_path}", file=sys.stderr)
        return 1

    project_files = find_csproj_files(root_path)
    if not project_files:
        print(f"未在目录中找到任何 .csproj 文件：{root_path}", file=sys.stderr)
        return 1

    project_index = build_project_index(project_files)
    ollama_client = OllamaClient(arguments.model, arguments.ollama_url, arguments.timeout)
    results: list[ProjectUpdateResult] = []

    try:
        for project_file in project_files:
            result = update_project_file(
                project_file=project_file,
                project_index=project_index,
                ollama_client=ollama_client,
                dry_run=arguments.dry_run,
            )
            results.append(result)
    except Exception as error:
        print(f"处理失败：{error}", file=sys.stderr)
        return 1

    changed_count = 0
    for result in results:
        status_text = "已更新" if result.changed else "无变化"
        print(f"[{status_text}] {result.project_path}")
        for message in result.messages:
            print(f"  - {message}")
        if result.changed:
            changed_count += 1

    print(
        f"处理完成，共扫描 {len(results)} 个项目文件，"
        f"{'计划变更' if arguments.dry_run else '实际变更'} {changed_count} 个。"
    )
    return 0


def find_csproj_files(root_path: Path) -> list[Path]:
    """递归查找仓库中的 .csproj 文件。"""

    project_files: list[Path] = []
    for candidate in root_path.rglob("*.csproj"):
        if should_skip_path(candidate):
            continue
        project_files.append(candidate.resolve())

    return sorted(project_files, key=lambda item: str(item).lower())


def should_skip_path(path: Path) -> bool:
    """判断路径是否应跳过。"""

    return any(part.lower() in EXCLUDED_DIRECTORY_NAMES for part in path.parts)


def build_project_index(project_files: list[Path]) -> dict[str, list[Path]]:
    """按项目文件名构建索引。"""

    index: dict[str, list[Path]] = {}
    for project_file in project_files:
        key = project_file.name.lower()
        index.setdefault(key, []).append(project_file)

    return index


def update_project_file(
    project_file: Path,
    project_index: dict[str, list[Path]],
    ollama_client: OllamaClient,
    dry_run: bool,
) -> ProjectUpdateResult:
    """更新单个项目文件。"""

    parser = element_tree.XMLParser(target=element_tree.TreeBuilder(insert_comments=True))
    tree = element_tree.parse(project_file, parser=parser)
    project_element = tree.getroot()
    namespace_uri = get_namespace(project_element.tag)
    result = ProjectUpdateResult(project_path=project_file)
    project_name = project_file.stem

    ensure_import(project_element, namespace_uri, result)
    property_group = ensure_primary_property_group(project_element, namespace_uri, result)
    ensure_property_value(property_group, namespace_uri, "OutputType", "Library", result, force_update=False)
    ensure_property_value(property_group, namespace_uri, "RootNamespace", project_name, result, force_update=True)

    description_element = find_direct_child(property_group, namespace_uri, "Description")
    existing_description = description_element.text.strip() if description_element is not None and description_element.text else None

    sdk_name = project_element.attrib.get("Sdk", "")
    project_references = collect_reference_file_names(project_element, namespace_uri, "ProjectReference")
    package_references = collect_package_reference_names(project_element, namespace_uri)
    generated_description = ollama_client.generate_description(
        project_name=project_name,
        project_path=project_file,
        sdk_name=sdk_name,
        project_references=project_references,
        package_references=package_references,
        existing_description=existing_description,
    )

    ensure_property_value(
        property_group,
        namespace_uri,
        "Description",
        generated_description,
        result,
        force_update=True,
    )

    update_project_references(project_file, project_element, namespace_uri, project_index, result)

    if result.changed:
        element_tree.indent(tree, space="    ")
        if not dry_run:
            tree.write(project_file, encoding="utf-8", xml_declaration=False, short_empty_elements=True)

    return result


def ensure_import(
    project_element: element_tree.Element,
    namespace_uri: str,
    result: ProjectUpdateResult,
) -> None:
    """确保项目文件包含公共属性导入。"""

    import_element = find_direct_child(project_element, namespace_uri, "Import")
    if import_element is None:
        import_element = element_tree.Element(get_qualified_name(namespace_uri, "Import"))
        import_element.set("Project", COMMON_PROPS_RELATIVE_PATH)
        project_element.insert(0, import_element)
        result.changed = True
        result.messages.append("已添加 Import 节点。")
        return

    if import_element.get("Project") != COMMON_PROPS_RELATIVE_PATH:
        import_element.set("Project", COMMON_PROPS_RELATIVE_PATH)
        result.changed = True
        result.messages.append("已更新 Import 节点路径。")


def ensure_primary_property_group(
    project_element: element_tree.Element,
    namespace_uri: str,
    result: ProjectUpdateResult,
) -> element_tree.Element:
    """获取主 PropertyGroup，不存在则创建。"""

    property_groups = [
        child
        for child in list(project_element)
        if is_local_name(child.tag, "PropertyGroup")
    ]

    for property_group in property_groups:
        if "Condition" not in property_group.attrib:
            return property_group

    property_group = element_tree.Element(get_qualified_name(namespace_uri, "PropertyGroup"))
    insert_index = 1 if find_direct_child(project_element, namespace_uri, "Import") is not None else 0
    project_element.insert(insert_index, property_group)
    result.changed = True
    result.messages.append("已添加主 PropertyGroup 节点。")
    return property_group


def ensure_property_value(
    property_group: element_tree.Element,
    namespace_uri: str,
    property_name: str,
    property_value: str,
    result: ProjectUpdateResult,
    force_update: bool,
) -> None:
    """确保属性存在并按要求更新。"""

    property_element = find_direct_child(property_group, namespace_uri, property_name)
    if property_element is None:
        property_element = element_tree.SubElement(
            property_group,
            get_qualified_name(namespace_uri, property_name),
        )
        property_element.text = property_value
        result.changed = True
        result.messages.append(f"已添加 {property_name} 节点。")
        return

    current_value = (property_element.text or "").strip()
    if force_update and current_value != property_value:
        property_element.text = property_value
        result.changed = True
        result.messages.append(f"已更新 {property_name} 节点。")


def collect_reference_file_names(
    project_element: element_tree.Element,
    namespace_uri: str,
    element_name: str,
) -> list[str]:
    """收集引用的项目文件名。"""

    file_names: list[str] = []
    for item_group in iter_direct_children(project_element, "ItemGroup"):
        for child in list(item_group):
            if not is_local_name(child.tag, element_name):
                continue

            include_value = child.get("Include")
            if not include_value:
                continue

            file_names.append(Path(include_value.replace("/", "\\")).name)

    return file_names


def collect_package_reference_names(
    project_element: element_tree.Element,
    namespace_uri: str,
) -> list[str]:
    """收集包引用名称。"""

    package_names: list[str] = []
    for item_group in iter_direct_children(project_element, "ItemGroup"):
        for child in list(item_group):
            if not is_local_name(child.tag, "PackageReference"):
                continue

            include_value = child.get("Include")
            if include_value:
                package_names.append(include_value)

    return package_names


def update_project_references(
    project_file: Path,
    project_element: element_tree.Element,
    namespace_uri: str,
    project_index: dict[str, list[Path]],
    result: ProjectUpdateResult,
) -> None:
    """按仓库实际位置更新项目引用路径。"""

    project_directory = project_file.parent

    for item_group in iter_direct_children(project_element, "ItemGroup"):
        for child in list(item_group):
            if not is_local_name(child.tag, "ProjectReference"):
                continue

            include_value = child.get("Include")
            if not include_value:
                continue

            reference_file_name = Path(include_value.replace("/", "\\")).name.lower()
            matched_projects = project_index.get(reference_file_name, [])
            if not matched_projects:
                raise RuntimeError(
                    f"未找到 ProjectReference 对应项目：{project_file} -> {include_value}"
                )

            if len(matched_projects) > 1:
                matched_text = ", ".join(str(item) for item in matched_projects)
                raise RuntimeError(
                    f"ProjectReference 匹配到多个项目，无法确定目标：{project_file} -> {include_value} -> {matched_text}"
                )

            target_project = matched_projects[0]
            relative_path = os.path.relpath(target_project, start=project_directory).replace("/", "\\")
            if child.get("Include") != relative_path:
                child.set("Include", relative_path)
                result.changed = True
                result.messages.append(
                    f"已更新项目引用：{Path(include_value).name} -> {relative_path}"
                )


def iter_direct_children(
    parent_element: element_tree.Element,
    local_name: str,
):
    """遍历指定名称的直接子节点。"""

    for child in list(parent_element):
        if is_local_name(child.tag, local_name):
            yield child


def find_direct_child(
    parent_element: element_tree.Element,
    namespace_uri: str,
    local_name: str,
) -> element_tree.Element | None:
    """查找指定名称的直接子节点。"""

    for child in list(parent_element):
        if is_local_name(child.tag, local_name):
            return child

    return None


def is_local_name(tag_name: object, local_name: str) -> bool:
    """判断节点名是否匹配指定本地名。"""

    if not isinstance(tag_name, str):
        return False

    if tag_name.startswith("{"):
        return tag_name.rsplit("}", maxsplit=1)[-1] == local_name

    return tag_name == local_name


def get_namespace(tag_name: str) -> str:
    """获取默认命名空间。"""

    if tag_name.startswith("{"):
        return tag_name[1:].split("}", maxsplit=1)[0]

    return ""


def get_qualified_name(namespace_uri: str, local_name: str) -> str:
    """构造带命名空间的节点名。"""

    if not namespace_uri:
        return local_name

    return f"{{{namespace_uri}}}{local_name}"


if __name__ == "__main__":
    sys.exit(main())
