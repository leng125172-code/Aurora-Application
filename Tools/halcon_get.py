import json
import os
import halcon as ha
import requests
import time

from collections import defaultdict

# ========== 翻译配置（本地Ollama） ==========
OLLAMA_URL = "http://127.0.0.1:11434/api/generate"
MODEL_NAME = "translategemma:4b"
trans_cache = {}
MAX_RETRY = 3  # 失败最多重试3次
TIMEOUT_SEC = 30  # 超时放大到30秒


def translate_text(text: str) -> str:
    if not text.strip():
        return ""
    if text in trans_cache:
        return trans_cache[text]
    print(f"翻译中：{text[:40]}...")  # 打印前40字符预览
    payload = {
        "model": MODEL_NAME,
        "prompt": "仅输出简体中文翻译，无多余文字、注释、符号：" + text,
        "temperature": 0,
        "max_new_tokens": 128,
        "stream": False,
    }

    retry = 0
    while retry < MAX_RETRY:
        try:
            resp = requests.post(OLLAMA_URL, json=payload, timeout=TIMEOUT_SEC)
            resp.raise_for_status()
            result = resp.json()
            trans_text = result.get("response", "").strip()
            trans_cache[text] = trans_text
            print(f"翻译成功：{text[:40]}... -> {trans_text[:40]}...")
            return trans_text
        except Exception as e:
            retry += 1
            print(f"[{retry}/{MAX_RETRY}] 翻译超时/异常：{text[:40]}... 错误:{str(e)}")
            time.sleep(1.2)  # 重试前短暂停顿
    # 多次失败兜底返回原文
    trans_cache[text] = text
    print(f"⚠️ 翻译失败，使用原文：{text[:40]}...")
    return text


# ========== 1. 环境初始化 ==========
ha.set_system("language", "zh_CN")
version = ha.get_system("version")[0]
print(f"Halcon 版本: {version}")

all_operators = ha.get_operator_name("")
print(f"算子总数: {len(all_operators)}")

# ========== 2. 创建输出目录 ==========
script_dir = os.path.dirname(os.path.abspath(__file__))
output_dir = os.path.join(script_dir, f"Halcon{version}")
os.makedirs(output_dir, exist_ok=True)
print(f"输出目录: {output_dir}")

# ========== 3. 构建两级分类结构 + 翻译short简介 ==========
category_tree = defaultdict(lambda: defaultdict(dict))

for op_name in all_operators:
    try:
        chapters = ha.get_operator_info(op_name, "chapter")
        short_desc_en = ha.get_operator_info(op_name, "short")[0]
        # 英译中
        short_desc_cn = translate_text(short_desc_en)
    except Exception:
        chapters = []
        short_desc_en = ""
        short_desc_cn = ""

    for full_path in chapters:
        parts = full_path.split(" / ", 1)
        primary = parts[0]
        secondary = parts[1] if len(parts) > 1 else "通用"
        # 存储原文+翻译双字段
        category_tree[primary][secondary][op_name] = {
            "short_en": short_desc_en,
            "short_cn": short_desc_cn,
        }

# ========== 4. 按一级分类生成独立 JSON 文件 ==========
for primary_name, secondary_dict in category_tree.items():
    file_content = {}
    for secondary_name, op_dict in secondary_dict.items():
        op_list = []
        for name, desc_info in op_dict.items():
            op_list.append(
                {
                    "name": name,
                    "short_en": desc_info["short_en"],
                    "short_cn": desc_info["short_cn"],
                }
            )
        file_content[secondary_name] = op_list

    safe_filename = primary_name.replace("/", "_").replace("\\", "_")
    file_path = os.path.join(output_dir, f"{safe_filename}.json")

    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(file_content, f, ensure_ascii=False, indent=2)

# ========== 5. 生成一份全量总览文件 ==========
overview = {
    "version": version,
    "total_operators": len(all_operators),
    "total_primary": len(category_tree),
    "categories": {},
}
for primary_name, secondary_dict in category_tree.items():
    total = sum(len(ops) for ops in secondary_dict.values())
    overview["categories"][primary_name] = {
        "secondary_count": len(secondary_dict),
        "operator_count": total,
    }

overview_path = os.path.join(output_dir, "_分类总览.json")
with open(overview_path, "w", encoding="utf-8") as f:
    json.dump(overview, f, ensure_ascii=False, indent=2)

# ========== 6. 打印统计结果 ==========
print(f"\n✅ 生成完成，共 {len(category_tree)} 个一级分类文件")
print(f"翻译缓存条目数：{len(trans_cache)}")
print("分类清单（按算子数排序）：")
sorted_cats = sorted(
    category_tree.items(),
    key=lambda x: sum(len(v) for v in x[1].values()),
    reverse=True,
)
for name, secondary in sorted_cats:
    total = sum(len(ops) for ops in secondary.values())
    print(f"  {name}：{len(secondary)} 个子分类，{total} 个算子")
