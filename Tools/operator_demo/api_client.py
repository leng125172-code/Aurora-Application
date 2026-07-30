import json
import os
import urllib.request

import requests

_access_token = None
_session = None
_base_url = None


def _read_response_body(response):
    """按 Content-Type 读取响应，兼容工作流源码等 text/plain 返回值。"""
    if response.status_code == 204 or not response.content:
        return None

    content_type = (response.headers.get("Content-Type") or "").lower()
    if "json" in content_type:
        return response.json()
    return response.text


def _debug_event(hypothesis_id, location, msg, data=None, run_id="pre-fix"):
    # #region debug-point D:report-http-evidence
    _env_path = os.path.join(
        os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
        ".dbg",
        "workflow-create-500.env",
    )
    _url = "http://127.0.0.1:7777/event"
    _session_id = "workflow-create-500"
    try:
        with open(_env_path, "r", encoding="utf-8") as _file:
            _content = _file.read()
        for _line in _content.splitlines():
            if _line.startswith("DEBUG_SERVER_URL="):
                _url = _line.split("=", 1)[1].strip() or _url
            elif _line.startswith("DEBUG_SESSION_ID="):
                _session_id = _line.split("=", 1)[1].strip() or _session_id
    except Exception:
        pass

    try:
        _payload = json.dumps(
            {
                "sessionId": _session_id,
                "runId": run_id,
                "hypothesisId": hypothesis_id,
                "location": location,
                "msg": f"[DEBUG] {msg}",
                "data": data or {},
            }
        ).encode("utf-8")
        urllib.request.urlopen(
            urllib.request.Request(
                _url,
                data=_payload,
                headers={"Content-Type": "application/json"},
            ),
            timeout=3,
        ).read()
    except Exception:
        pass
    # #endregion


def set_base_url(url):
    global _base_url
    _base_url = url


def _get_session():
    global _session
    if _session is not None:
        return _session

    _session = requests.Session()
    # Aurora 服务通常位于局域网。不要让 requests 自动继承 HTTP(S)_PROXY，
    # 否则 10.x/192.168.x 地址可能被错误转发到本机代理端口。
    _session.trust_env = False

    config_url = f"{_base_url}/api/abp/application-configuration"
    resp = _session.get(config_url, timeout=30)
    resp.raise_for_status()

    for cookie in _session.cookies:
        if cookie.name.startswith(".AspNetCore.Antiforgery"):
            if cookie.value:
                _session.headers["X-XSRF-TOKEN"] = cookie.value
            break

    anti_forgery = resp.headers.get("X-XSRF-TOKEN") or resp.headers.get("Set-Cookie")
    if anti_forgery and "X-XSRF-TOKEN" not in _session.headers:
        _session.headers["X-XSRF-TOKEN"] = anti_forgery

    return _session


def login(username, password):
    global _access_token
    print("正在进行身份认证...")
    session = _get_session()
    url = f"{_base_url}/api/app/account/login"
    payload = {
        "name": username,
        "password": password,
    }
    resp = session.post(url, json=payload, timeout=30)
    resp.raise_for_status()
    result = resp.json()
    _access_token = (
        result.get("accessToken") or result.get("access_token") or result.get("token")
    )
    if not _access_token:
        raise RuntimeError("登录失败：未获取到 token")
    session.headers["Authorization"] = f"Bearer {_access_token}"
    print("✓ 身份认证成功")


def api_post(path, body):
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")

    session = _get_session()
    url = f"{_base_url}{path}"
    _debug_event(
        "D",
        "api_client.py:api_post:before",
        "发送 POST 请求",
        {"path": path, "url": url, "bodyKeys": sorted(list(body.keys()))},
    )
    resp = session.post(url, json=body, timeout=30)
    
    # 打印错误信息
    try:
        resp.raise_for_status()
    except requests.exceptions.HTTPError as e:
        _debug_event(
            "D",
            "api_client.py:api_post:error",
            "POST 请求失败",
            {
                "path": path,
                "statusCode": resp.status_code,
                "responseText": resp.text[:4000],
            },
        )
        print(f"\n✗ HTTP 错误详情：")
        print(f"  状态码：{resp.status_code}")
        print(f"  响应内容：{resp.text}")
        raise

    _debug_event(
        "D",
        "api_client.py:api_post:success",
        "POST 请求成功",
        {"path": path, "statusCode": resp.status_code},
    )
    
    return _read_response_body(resp)


def api_get(path, params=None):
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")

    session = _get_session()
    url = f"{_base_url}{path}"
    resp = session.get(url, params=params, timeout=30)
    resp.raise_for_status()
    return _read_response_body(resp)


def api_put(path, body):
    global _access_token, _session
    if not _access_token:
        raise RuntimeError("未登录，无法发送请求")

    session = _get_session()
    url = f"{_base_url}{path}"
    resp = session.put(url, json=body, timeout=30)
    try:
        resp.raise_for_status()
    except requests.exceptions.HTTPError:
        _debug_event(
            "D",
            "api_client.py:api_put:error",
            "PUT 请求失败",
            {
                "path": path,
                "statusCode": resp.status_code,
                "responseText": resp.text[:4000],
            },
        )
        print("\n✗ HTTP PUT 错误详情：")
        print(f"  请求：{url}")
        print(f"  状态码：{resp.status_code}")
        print(f"  响应内容：{resp.text or '<空响应>'}")
        raise
    return _read_response_body(resp)


def upload_operator_file(project_id, operator_id, file_path):
    """通过 operator-file multipart 接口上传算子文件。"""
    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"待上传文件不存在：{file_path}")

    session = _get_session()
    url = f"{_base_url}/api/app/operator-file/upload"
    file_name = os.path.basename(file_path)
    print(f"      上传算子文件：{file_name} ({os.path.getsize(file_path)} bytes) ...")
    with open(file_path, "rb") as stream:
        response = session.post(
            url,
            params={"projectId": project_id, "operatorId": operator_id},
            files={"file": (file_name, stream, "application/octet-stream")},
            timeout=300,
        )
    try:
        response.raise_for_status()
    except requests.exceptions.HTTPError:
        print("\n✗ 算子文件上传失败：")
        print(f"  请求：{response.request.url}")
        print(f"  状态码：{response.status_code}")
        print(f"  响应内容：{response.text or '<空响应>'}")
        raise

    result = response.json()
    blob_name = result.get("blobName")
    if not result.get("success") or not blob_name:
        raise RuntimeError(f"算子文件上传响应无效：{result}")
    print(f"      ✓ 上传成功，BlobName：{blob_name}")
    return result


def confirm_operator_file(project_id, operator_id, blob_name):
    """工作流保存成功后确认 Blob 已被使用，避免被过期清理。"""
    api_post(
        "/api/app/operator-file/confirm",
        {
            "projectId": project_id,
            "operatorId": operator_id,
            "blobName": blob_name,
        },
    )
    print(f"      ✓ 已确认算子文件：{blob_name}")


def find_project_by_code(project_code):
    def _norm(value):
        return (value or "").strip().casefold()

    result = api_get(
        "/api/app/project-info",
        {
            "filter": project_code,
            "skipCount": 0,
            "maxResultCount": 200,
        },
    )

    items = result.get("items") or []
    target_code = _norm(project_code)

    for item in items:
        item_code = (
            item.get("projectCode") or item.get("project_code") or item.get("code")
        )
        if _norm(item_code) == target_code:
            return item

    if len(items) == 1:
        return items[0]

    return None


def try_find_project_by_code(project_code):
    try:
        return find_project_by_code(project_code)
    except requests.exceptions.RequestException as e:
        print(f"      ! 查询已有项目失败，将继续创建：{e}")
        return None


def build_fallback_project_code(base_code):
    suffix = new_uuid()[:6].upper()
    return f"{base_code}-{suffix}"


def find_workflow_by_name(project_id, workflow_name):
    def _norm(value):
        return (value or "").strip().casefold()

    result = api_get(
        "/api/app/workflow",
        {
            "projectId": project_id,
        },
    )

    if isinstance(result, list):
        items = result
    else:
        items = result.get("items") or []

    target_name = _norm(workflow_name)
    for item in items:
        item_name = item.get("name")
        if _norm(item_name) == target_name:
            return item

    return None


from utils import new_uuid
