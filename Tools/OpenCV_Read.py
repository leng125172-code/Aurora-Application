# 必须在 import clr 之前设置运行时环境变量
import os

os.environ["PYTHONNET_RUNTIME"] = "coreclr"

import clr
import xml.etree.ElementTree as ET
from typing import Dict, Any, List
import json
import requests
import time

# ===================== Ollama翻译配置 =====================
OLLAMA_URL = "http://127.0.0.1:11434/api/generate"
MODEL_NAME = "translategemma:4b"
TRANS_CACHE = {}
MAX_RETRY = 3
TIMEOUT_SEC = 30


def translate_en_to_cn(text: str) -> str:
    """简短英文注释批量翻译，缓存+重试"""
    if not text or not text.strip():
        return ""
    if text in TRANS_CACHE:
        return TRANS_CACHE[text]
    print(f"翻译中：{text[:40]}...")  # 打印前40字符预览
    payload = {
        "model": MODEL_NAME,
        "prompt": "只输出标准简体中文译文，不要额外解释、符号、换行：" + text.strip(),
        "temperature": 0,
        "max_new_tokens": 256,
        "stream": False,
    }

    retry_times = 0
    while retry_times < MAX_RETRY:
        try:
            resp = requests.post(OLLAMA_URL, json=payload, timeout=TIMEOUT_SEC)
            resp.raise_for_status()
            res_json = resp.json()
            cn_text = res_json.get("response", "").strip()
            TRANS_CACHE[text] = cn_text
            print(f"翻译成功：{text[:40]}... -> {cn_text[:40]}...")
            return cn_text
        except Exception as err:
            retry_times += 1
            print(
                f"翻译重试{retry_times}/{MAX_RETRY} | 原文片段：{text[:40]} 错误：{str(err)[:60]}"
            )
            time.sleep(1)
    # 多次失败兜底返回原文
    TRANS_CACHE[text] = text
    print(f"⚠️ 翻译失败，使用原文：{text[:40]}...")
    return text


# ===================== 路径配置 =====================
DLL_PATH = r"D:\GitRepos\Aurora Application\Builds\Release\AuroraStruct3D.HttpApi.Host\net10.0\linux-arm64\OpenCvSharp.dll"
XML_DOC_PATH = r"D:\GitRepos\Aurora Application\Builds\Release\AuroraStruct3D.HttpApi.Host\net10.0\linux-arm64\OpenCvSharp.xml"
OUTPUT_JSON = "OpenCvSharp_BlueprintNodes.json"
DLL_DIR = os.path.dirname(DLL_PATH)

# ===================== 程序集依赖解析 =====================
clr.AddReference("System")
import System


def assembly_resolve(sender, args):
    asm_name = args.Name.split(",")[0]
    local_path = os.path.join(DLL_DIR, f"{asm_name}.dll")
    if os.path.exists(local_path):
        return System.Reflection.Assembly.LoadFrom(local_path)
    return None


System.AppDomain.CurrentDomain.AssemblyResolve += System.ResolveEventHandler(
    assembly_resolve
)

# 过滤掉继承自object的基础方法，避免节点库冗余
OBJECT_BASE_METHODS = {
    "Equals",
    "GetHashCode",
    "GetType",
    "ToString",
    "Finalize",
    "MemberwiseClone",
    "Dispose",
}


# ===================== 类型工具 =====================
def get_type_display_name(typ) -> str:
    """界面显示用的简化类型名"""
    if typ is None:
        return "void"
    try:
        if typ.IsArray:
            return f"{get_type_display_name(typ.GetElementType())}[]"
        if typ.IsByRef:
            return get_type_display_name(typ.GetElementType())
        if typ.IsGenericType and not typ.IsGenericTypeDefinition:
            base_name = typ.Name.split("`")[0]
            gen_args = [get_type_display_name(t) for t in typ.GetGenericArguments()]
            return f"{base_name}<{', '.join(gen_args)}>"
        name = typ.Name
        alias_map = {
            "Boolean": "bool",
            "Int32": "int",
            "Int64": "long",
            "Single": "float",
            "Double": "double",
            "String": "string",
            "Void": "void",
            "Object": "object",
            "Byte": "byte",
            "Int16": "short",
            "UInt32": "uint",
        }
        return alias_map.get(name, name)
    except Exception:
        return "UnknownType"


def get_type_full_name(typ) -> str:
    """反射调用用的完整类型名，空值兜底"""
    if typ is None:
        return "System.Void"
    try:
        if typ.IsByRef:
            return get_type_full_name(typ.GetElementType()) + "&"
        return typ.FullName or typ.Name or "UnknownType"
    except Exception:
        return "UnknownType"


# ===================== XML注释解析 =====================
class XmlDocParser:
    def __init__(self, xml_path: str):
        self.member_map: Dict[str, Dict[str, Any]] = {}
        if not os.path.exists(xml_path):
            print(f"警告：XML注释文件不存在 {xml_path}")
            return
        tree = ET.parse(xml_path)
        root = tree.getroot()
        for member in root.findall("members/member"):
            member_name = member.attrib.get("name", "")
            if not member_name:
                continue
            summary_node = member.find("summary")
            summary_en = ""
            if summary_node is not None and summary_node.text is not None:
                summary_en = summary_node.text.strip()
            param_dict_en = {}
            for param in member.findall("param"):
                p_name = param.attrib.get("name", "")
                p_text = param.text.strip() if param.text is not None else ""
                param_dict_en[p_name] = p_text
            returns_node = member.find("returns")
            returns_en = ""
            if returns_node is not None and returns_node.text is not None:
                returns_en = returns_node.text.strip()
            self.member_map[member_name] = {
                "summary_en": summary_en,
                "params_en": param_dict_en,
                "returns_en": returns_en,
            }

    def get_method_comment(self, method_info) -> Dict[str, Any]:
        try:
            decl_type = method_info.DeclaringType
            class_full = decl_type.FullName or decl_type.Name or ""
            if not class_full:
                return {"summary_en": "", "params_en": {}, "returns_en": ""}
            raw_params = method_info.GetParameters()
            type_sig_parts = []
            for p in raw_params:
                p_type = p.ParameterType
                try:
                    if p_type.IsByRef:
                        elem_type = p_type.GetElementType()
                        type_name = (
                            elem_type.FullName or elem_type.Name or "UnknownType"
                        )
                        type_sig_parts.append(type_name + "&")
                    else:
                        type_name = p_type.FullName or p_type.Name or "UnknownType"
                        type_sig_parts.append(type_name)
                except Exception:
                    type_sig_parts.append("UnknownType")
            type_sig = ",".join(type_sig_parts)
            member_key = f"M:{class_full}.{method_info.Name}({type_sig})"
            return self.member_map.get(
                member_key, {"summary_en": "", "params_en": {}, "returns_en": ""}
            )
        except Exception:
            return {"summary_en": "", "params_en": {}, "returns_en": ""}


# ===================== 枚举解析 =====================
def parse_enums(all_types) -> List[Dict]:
    enum_list = []
    for typ in all_types:
        if typ is None or not typ.IsPublic or not typ.IsEnum:
            continue
        try:
            names = [str(n) for n in System.Enum.GetNames(typ)]
            values = [int(v) for v in System.Enum.GetValues(typ)]
            enum_list.append(
                {
                    "enum_id": typ.FullName or typ.Name,
                    "enum_name": typ.Name,
                    "namespace": typ.Namespace or "",
                    "underlying_type": get_type_display_name(
                        typ.GetEnumUnderlyingType()
                    ),
                    "items": [{"name": n, "value": v} for n, v in zip(names, values)],
                }
            )
        except Exception:
            continue
    return enum_list


# ===================== 结构体字段解析 =====================
def parse_structs(all_types) -> List[Dict]:
    struct_list = []
    for typ in all_types:
        if typ is None or not typ.IsPublic:
            continue
        if not (typ.IsValueType and not typ.IsEnum):
            continue
        try:
            fields = typ.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
            )
            field_list = []
            for f in fields:
                field_list.append(
                    {
                        "field_name": f.Name,
                        "type_display": get_type_display_name(f.FieldType),
                        "type_full": get_type_full_name(f.FieldType),
                    }
                )
            if field_list:
                struct_list.append(
                    {
                        "struct_id": typ.FullName or typ.Name,
                        "struct_name": typ.Name,
                        "namespace": typ.Namespace or "",
                        "fields": field_list,
                    }
                )
        except Exception:
            continue
    return struct_list


# ===================== 方法节点解析 =====================
def parse_method_nodes(all_types, xml_parser: XmlDocParser) -> List[Dict]:
    node_list = []
    node_index = 0

    for typ in all_types:
        if typ is None or not typ.IsPublic or typ.IsEnum:
            continue
        class_full = typ.FullName or typ.Name or ""
        class_name = typ.Name
        ns = typ.Namespace or "OpenCvSharp"
        category = f"{ns}/{class_name}"

        try:
            methods = typ.GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
            )
        except Exception:
            continue

        for m in methods:
            if m.IsConstructor or m.IsPrivate:
                continue
            if m.Name in OBJECT_BASE_METHODS:
                continue

            try:
                raw_comment = xml_parser.get_method_comment(m)

                # 翻译摘要
                summary_en = raw_comment["summary_en"]
                summary_cn = translate_en_to_cn(summary_en)

                # 返回值翻译
                ret_en = raw_comment["returns_en"]
                ret_cn = translate_en_to_cn(ret_en)

                # 返回值
                ret_type_display = get_type_display_name(m.ReturnType)
                ret_type_full = get_type_full_name(m.ReturnType)
                has_return = ret_type_display != "void"

                # 参数引脚
                input_pins = []
                output_pins = []

                # 成员方法第一个引脚为Target自身
                if not m.IsStatic:
                    input_pins.append(
                        {
                            "pin_name": "Target",
                            "type_display": class_name,
                            "type_full": class_full,
                            "is_target": True,
                            "desc_en": f"{class_name} instance object",
                            "desc_cn": f"{translate_en_to_cn(f'{class_name} instance object')}",
                        }
                    )

                raw_params = m.GetParameters()
                for p in raw_params:
                    p_name = p.Name or "unnamed"
                    param_en_text = raw_comment["params_en"].get(p_name, "")
                    param_cn_text = translate_en_to_cn(param_en_text)

                    is_output = p.IsOut or (p.ParameterType.IsByRef and p.IsOut)
                    pin_info = {
                        "pin_name": p_name,
                        "type_display": get_type_display_name(p.ParameterType),
                        "type_full": get_type_full_name(p.ParameterType),
                        "is_output": is_output,
                        "is_by_ref": p.ParameterType.IsByRef,
                        "desc_en": param_en_text,
                        "desc_cn": param_cn_text,
                    }
                    if is_output:
                        output_pins.append(pin_info)
                    else:
                        input_pins.append(pin_info)

                # 返回值作为主输出引脚
                if has_return:
                    output_pins.insert(
                        0,
                        {
                            "pin_name": "ReturnValue",
                            "type_display": ret_type_display,
                            "type_full": ret_type_full,
                            "is_return": True,
                            "desc_en": ret_en,
                            "desc_cn": ret_cn,
                        },
                    )

                node_id = f"node_{node_index:06d}"
                node_index += 1

                node_list.append(
                    {
                        "node_id": node_id,
                        "node_name": m.Name,
                        "category": category,
                        "declaring_class": class_full,
                        "is_static": m.IsStatic,
                        "summary_en": summary_en,
                        "summary_cn": summary_cn,
                        "input_pins": input_pins,
                        "output_pins": output_pins,
                        "overload_key": f"{class_full}.{m.Name}({','.join(p['type_full'] for p in input_pins[1:] if not p.get('is_target'))})",
                    }
                )
            except Exception:
                continue
    return node_list


# ===================== 注释覆盖率统计 =====================
def calc_comment_stats(method_nodes: List[Dict]) -> Dict:
    total_methods = len(method_nodes)
    empty_summary_en = 0
    total_input_pins = 0
    empty_input_en = 0
    total_output_pins = 0
    empty_output_en = 0

    for node in method_nodes:
        if not node["summary_en"].strip():
            empty_summary_en += 1

        for pin in node["input_pins"]:
            total_input_pins += 1
            if not pin["desc_en"].strip():
                empty_input_en += 1

        for pin in node["output_pins"]:
            total_output_pins += 1
            if not pin["desc_en"].strip():
                empty_output_en += 1

    def ratio(part, total):
        return round(part / total * 100, 2) if total > 0 else 0.0

    return {
        "total_methods": total_methods,
        "empty_summary_en_count": empty_summary_en,
        "empty_summary_ratio": ratio(empty_summary_en, total_methods),
        "total_input_pins": total_input_pins,
        "empty_input_desc_en_count": empty_input_en,
        "empty_input_ratio": ratio(empty_input_en, total_input_pins),
        "total_output_pins": total_output_pins,
        "empty_output_desc_en_count": empty_output_en,
        "empty_output_ratio": ratio(empty_output_en, total_output_pins),
        "trans_cache_size": len(TRANS_CACHE),
    }


# ===================== 主入口 =====================
def main():
    xml_parser = XmlDocParser(XML_DOC_PATH)
    asm = System.Reflection.Assembly.LoadFrom(DLL_PATH)

    try:
        all_types = asm.GetTypes()
    except System.Reflection.ReflectionTypeLoadException as ex:
        all_types = [t for t in ex.Types if t is not None]
        print(f"警告：{len(ex.LoaderExceptions)} 个类型加载失败，已跳过")

    print(f"共读取 {len(all_types)} 个类型，开始解析+翻译注释...")

    enums = parse_enums(all_types)
    structs = parse_structs(all_types)
    methods = parse_method_nodes(all_types, xml_parser)
    comment_stats = calc_comment_stats(methods)

    result = {
        "version": "1.0",
        "assembly": "OpenCvSharp",
        "target_framework": "net10.0",
        "stats": comment_stats,
        "enums": enums,
        "structs": structs,
        "method_nodes": methods,
    }

    with open(OUTPUT_JSON, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    # 控制台打印统计
    print("\n" + "=" * 60)
    print("📊 导出&翻译统计")
    print("=" * 60)
    s = comment_stats
    print(f"方法总数：{s['total_methods']}")
    print(
        f"英文空摘要方法：{s['empty_summary_en_count']} ({s['empty_summary_ratio']}%)"
    )
    print(f"翻译缓存独立文本数：{s['trans_cache_size']}")
    print(f"\n输入引脚总数：{s['total_input_pins']}")
    print(
        f"输入无英文描述：{s['empty_input_desc_en_count']} ({s['empty_input_ratio']}%)"
    )
    print(f"\n输出引脚总数：{s['total_output_pins']}")
    print(
        f"输出无英文描述：{s['empty_output_desc_en_count']} ({s['empty_output_ratio']}%)"
    )
    print("=" * 60)
    print(f"\n✅ 完成！输出文件：{OUTPUT_JSON}")


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"❌ 导出失败: {e}")
        import traceback

        traceback.print_exc()
