import json

d = json.load(open("Tools/leisai-params.json", encoding="utf-8"))
out = [f"total {len(d)}"]
for r in d:
    out.append(
        f"  {r['pr']:8s} 0x{r['addressLow']:04X} {r['name'][:25]:25s} rng=[{r['rangeMin']},{r['rangeMax']}] def={r['defaultValue']} unit='{r['unit']}'"
    )
open("Tools/_check_out.txt", "w", encoding="utf-8").write("\n".join(out))
