import json
import sys
import time
from pathlib import Path

import requests

BASE = "http://10.24.1.143:5000"
s = requests.Session()
s.trust_env = False
s.get(f"{BASE}/api/abp/application-configuration", timeout=30).raise_for_status()
r = s.post(f"{BASE}/api/app/account/login", json={"name": "admin", "password": "1q2w3E*"}, timeout=30)
r.raise_for_status()
token = r.json().get("accessToken") or r.json().get("token")
s.headers["Authorization"] = f"Bearer {token}"

r = s.get(f"{BASE}/api/app/calib-project", params={"filter": "标定板-调试", "skipCount": 0, "maxResultCount": 50}, timeout=30)
r.raise_for_status()
projects = [x for x in r.json().get("items", []) if x.get("name") == "标定板-调试（勿删）"]
if not projects:
    projects = [x for x in r.json().get("items", []) if "标定板-调试" in x.get("name", "")]
if len(projects) != 1:
    raise RuntimeError(f"无法唯一定位标定项目：{[(x.get('id'), x.get('name')) for x in projects]}")
project = projects[0]
project_id = project["id"]
print(json.dumps({"projectId": project_id, "name": project["name"]}, ensure_ascii=False))

status = s.get(f"{BASE}/api/app/calib-scan/status/{project_id}", timeout=30)
status.raise_for_status()
if status.json().get("isRunning"):
    raise RuntimeError("目标项目已有扫描正在运行，测试未接管现有会话")

r = s.post(f"{BASE}/api/app/calib-scan/start", json={
    "calibProjectId": project_id,
    "enableTableFilter": True,
    "tableClearanceMm": 3,
}, timeout=60)
r.raise_for_status()
print("scan started")

round_index = 0
deadline = time.time() + 240
try:
    while time.time() < deadline:
        time.sleep(3)
        r = s.get(f"{BASE}/api/app/calib-scan/status/{project_id}", timeout=30)
        r.raise_for_status()
        data = r.json()
        metrics = data.get("latestMetrics") or {}
        round_index = int(metrics.get("roundIndex") or 0)
        frame_index = int(metrics.get("frameIndexInRound") or 0)
        pattern_count = int(metrics.get("patternCount") or 0)
        print(f"round={round_index} frame={frame_index}/{pattern_count} state={data.get('state')}")
        if round_index >= 1 and pattern_count > 0 and frame_index >= pattern_count - 1:
            time.sleep(5)
            break
        if data.get("state") == 4:
            raise RuntimeError(data.get("errorMessage") or "扫描失败")
finally:
    stop = s.post(f"{BASE}/api/app/calib-scan/stop", json={"calibProjectId": project_id}, timeout=90)
    stop.raise_for_status()
    print("scan stopped")

if round_index < 1:
    raise RuntimeError("超时前没有完成任何扫描轮次")
download = s.get(f"{BASE}/api/app/calib-scan/download-round/{project_id}/{round_index}", timeout=180)
download.raise_for_status()
path = Path("Artifacts") / f"calib-scan-{project_id}-round-{round_index:04d}.zip"
path.parent.mkdir(exist_ok=True)
path.write_bytes(download.content)
print(json.dumps({"archive": str(path.resolve()), "bytes": len(download.content), "roundIndex": round_index}, ensure_ascii=False))
