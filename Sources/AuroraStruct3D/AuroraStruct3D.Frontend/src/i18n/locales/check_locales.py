#!/usr/bin/env python3
"""
检查所有 locale 文件的 key 一致性。
以 zh-CN.ts 为基准，找出其他文件中缺少的 key 或多余的 key。
"""

import re
import os
from pathlib import Path

LOCALES_DIR = Path(__file__).parent
REFERENCE = "zh-CN.ts"
FILES = ["en-US.ts", "de-DE.ts", "ja-JP.ts", "ko-KR.ts"]

KEY_PATTERN = re.compile(
    r"^\s{4,}(\w+)\s*:",
)  # 匹配属性 key（缩进≥4空格）


def extract_keys(filepath: Path) -> list[str]:
    """提取文件中所有属性 key，保留顺序（含重复以便定位）。"""
    keys = []
    with open(filepath, encoding="utf-8") as f:
        for line in f:
            m = KEY_PATTERN.match(line)
            if m:
                keys.append(m.group(1))
    return keys


def extract_keyset(filepath: Path) -> set[str]:
    return set(extract_keys(filepath))


def main():
    ref_path = LOCALES_DIR / REFERENCE
    ref_keys = extract_keyset(ref_path)
    ref_ordered = extract_keys(ref_path)

    print(f"基准文件: {REFERENCE}  共 {len(ref_keys)} 个 key\n")
    print("=" * 60)

    all_ok = True
    for fname in FILES:
        fpath = LOCALES_DIR / fname
        if not fpath.exists():
            print(f"[错误] 文件不存在: {fname}")
            all_ok = False
            continue

        fkeys = extract_keyset(fpath)
        missing = ref_keys - fkeys
        extra = fkeys - ref_keys

        if not missing and not extra:
            print(f"[OK]  {fname}")
        else:
            all_ok = False
            print(f"\n[差异] {fname}")
            if missing:
                # 按基准文件中的出现顺序排列
                ordered_missing = [k for k in ref_ordered if k in missing]
                # 去重（同名 key 可能在多个节出现）
                seen = set()
                ordered_missing_dedup = []
                for k in ordered_missing:
                    if k not in seen:
                        seen.add(k)
                        ordered_missing_dedup.append(k)
                print(f"  缺少的 key ({len(missing)}):")
                for k in ordered_missing_dedup:
                    print(f"    - {k}")
            if extra:
                print(f"  多余的 key ({len(extra)}):")
                for k in sorted(extra):
                    print(f"    + {k}")
            print()

    print("=" * 60)
    if all_ok:
        print("✅ 所有 locale 文件 key 完全一致！")
    else:
        print("❌ 存在不一致，请根据上述报告补充缺失的翻译 key。")


if __name__ == "__main__":
    main()
