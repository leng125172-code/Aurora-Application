"""
从 Documents/电机命令2.htm 提取雷赛伺服 Pr0-Pr9 全部参数。
策略：扫描所有 <table>，识别含 0x???? 地址 + Pr?.?? 编号的行。
输出 Tools/leisai-params.json。
"""

import json
import re
from collections import Counter
from pathlib import Path
from bs4 import BeautifulSoup

ROOT = Path(__file__).resolve().parent.parent
HTM = ROOT / "Documents" / "电机命令2.htm"
OUT = ROOT / "Tools" / "leisai-params.json"

html = HTM.read_bytes().decode("gb2312", errors="replace")
soup = BeautifulSoup(html, "lxml")
tables = soup.find_all("table")


def cell(td):
    t = td.get_text(separator=" ", strip=True)
    return re.sub(r"\s+", " ", t).strip()


ADDR_RE = re.compile(r"^0x([0-9A-Fa-f]{2,4})$")
PR_RE = re.compile(r"^Pr\s*(\d)\s*\.\s*(\d{1,2})$")


def norm_pr(s: str):
    m = PR_RE.match(s.strip())
    if not m:
        return None
    return f"Pr{m.group(1)}.{int(m.group(2)):02d}"


def parse_int(s: str):
    s = s.strip()
    if not s or s in ("--", "—", "-"):
        return None
    s2 = s.replace(" ", "").replace(",", "")
    try:
        if s2.lower().startswith("0x"):
            return int(s2, 16)
        return int(s2)
    except Exception:
        return None


def parse_range(s: str):
    s = s.strip().replace(" ", "").replace("O", "0").replace("o", "0")
    if not s or s in ("--", "—"):
        return (None, None)
    m = re.match(r"^(-?\d+)[~\-](-?\d+)$", s)
    if m:
        return (int(m.group(1)), int(m.group(2)))
    return (None, None)


seen = set()
results = []
for ti, t in enumerate(tables):
    rows = t.find_all("tr")
    if len(rows) < 2:
        continue
    for r in rows:
        cells = [cell(td) for td in r.find_all(["td", "th"])]
        addr_idx = pr_idx = -1
        for idx, c in enumerate(cells):
            cnorm = c.replace(" ", "")
            if addr_idx < 0 and ADDR_RE.match(cnorm):
                addr_idx = idx
            if pr_idx < 0 and PR_RE.match(cnorm):
                pr_idx = idx
        if addr_idx < 0 or pr_idx < 0:
            continue
        addr_str = cells[addr_idx].replace(" ", "")
        pr = norm_pr(cells[pr_idx].replace(" ", ""))
        if not pr:
            continue
        address = int(ADDR_RE.match(addr_str).group(1), 16)
        key = (pr, address)
        if key in seen:
            continue
        seen.add(key)
        other_indexed = [
            (i, c) for i, c in enumerate(cells) if i not in (addr_idx, pr_idx)
        ]
        other = [c for _, c in other_indexed]
        name = other[0] if other else ""
        # 找范围列
        range_idx = -1
        for idx, c in enumerate(other):
            cn = c.replace(" ", "")
            if re.search(r"-?\d+[~\-]-?\d+", cn) and "FC" not in cn:
                range_idx = idx
        rng_lo = rng_hi = None
        if range_idx >= 0:
            rng_lo, rng_hi = parse_range(other[range_idx])
        default = None
        unit = ""
        if range_idx >= 0:
            after = other[range_idx + 1 :]
            before = other[1:range_idx]
            definition = " | ".join(before)
            if after:
                default = parse_int(after[0])
                if len(after) > 1:
                    unit = after[1]
        else:
            definition = " | ".join(other[1:])
        group = int(pr.split(".")[0][2])
        results.append(
            {
                "pr": pr,
                "group": group,
                "addressLow": address,
                "name": name,
                "definition": definition,
                "rangeMin": rng_lo,
                "rangeMax": rng_hi,
                "defaultValue": default,
                "unit": unit,
            }
        )

# 按地址去重 + 排序
dedup = {}
for r in results:
    cur = dedup.get(r["addressLow"])
    # 优先选含 range/默认值更完整的版本
    if cur is None:
        dedup[r["addressLow"]] = r
    else:
        score_cur = (
            (cur["rangeMin"] is not None)
            + (cur["defaultValue"] is not None)
            + bool(cur["unit"])
        )
        score_new = (
            (r["rangeMin"] is not None)
            + (r["defaultValue"] is not None)
            + bool(r["unit"])
        )
        if score_new > score_cur:
            dedup[r["addressLow"]] = r

final = sorted(dedup.values(), key=lambda x: x["addressLow"])
OUT.write_text(json.dumps(final, ensure_ascii=False, indent=2), encoding="utf-8")

cnt = Counter(r["group"] for r in final)
log = [f"提取 {len(final)} 个参数 -> {OUT}", "各组数量："]
for g in sorted(cnt):
    log.append(f"  Pr{g}: {cnt[g]}")
(ROOT / "Tools" / "leisai-extract.log").write_text("\n".join(log), encoding="utf-8")
print("\n".join(log))
