#!/usr/bin/env python3
"""Workflow runtime regression test script.

This script validates the task and execution endpoints documented in
Documents/工作流任务列表接口清单.md against a target Aurora service.

Example:
  python Tools/workflow_runtime_regression.py \
    --base-url http://10.25.71.143:5000 \
    --username admin \
    --password "1q2w3E*"
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import uuid
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any


@dataclass
class ApiResult:
    ok: bool
    code: int
    text: str
    url: str


class ApiClient:
    def __init__(self, base_url: str, timeout_seconds: int) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds
        self.token: str | None = None

    def call(
        self,
        method: str,
        path: str,
        body: Any = None,
        query: dict[str, Any] | None = None,
        expect_codes: set[int] | None = None,
    ) -> ApiResult:
        url = f"{self.base_url}{path}"
        if query:
            filtered = {k: v for k, v in query.items() if v is not None and v != ""}
            if filtered:
                url = f"{url}?{urllib.parse.urlencode(filtered)}"

        data = None
        headers = {"Content-Type": "application/json"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        if body is not None:
            data = json.dumps(body).encode("utf-8")

        request = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(
                request, timeout=self.timeout_seconds
            ) as response:
                text = response.read().decode("utf-8", "ignore")
                code = int(response.getcode())
        except urllib.error.HTTPError as ex:
            code = int(ex.code)
            text = ex.read().decode("utf-8", "ignore")
        except urllib.error.URLError as ex:
            return ApiResult(ok=False, code=-1, text=str(ex.reason), url=url)

        ok = 200 <= code < 300
        if expect_codes is not None:
            ok = code in expect_codes
        return ApiResult(ok=ok, code=code, text=text, url=url)


class RegressionRunner:
    def __init__(self, client: ApiClient, username: str, password: str) -> None:
        self.client = client
        self.username = username
        self.password = password
        self.results: list[tuple[str, str, int, str]] = []

    def record(self, name: str, result: ApiResult) -> None:
        status = "PASS" if result.ok else "FAIL"
        message = result.text.replace("\n", " ")[:180]
        self.results.append((name, status, result.code, message))

    def record_skip(self, name: str, reason: str) -> None:
        self.results.append((name, "SKIP", 0, reason[:180]))

    def require_json(self, text: str, fallback: Any) -> Any:
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return fallback

    def run(self) -> int:
        project_id: str | None = None
        workflow_id: str | None = None
        task_a: str | None = None
        task_b: str | None = None
        execution_id: str | None = None

        # 0) Login
        result = self.client.call(
            "POST",
            "/api/app/account/login",
            body={"name": self.username, "password": self.password},
        )
        self.record("login", result)
        if not result.ok:
            self.print_summary(project_id, workflow_id, task_a, task_b, execution_id)
            return 1

        login_payload = self.require_json(result.text, {})
        self.client.token = login_payload.get("token")
        if not self.client.token:
            print("Missing token in login response")
            self.print_summary(project_id, workflow_id, task_a, task_b, execution_id)
            return 1

        # 1) Get or create project
        result = self.client.call(
            "POST",
            "/Projects/Page",
            body={
                "pageIndex": 1,
                "pageSize": 50,
                "skipCount": 0,
                "sorting": "",
                "filter": "",
            },
        )
        self.record("projects-page", result)
        if not result.ok:
            self.print_summary(project_id, workflow_id, task_a, task_b, execution_id)
            return 1

        project_items = self.require_json(result.text, {}).get("items") or []
        if project_items:
            project_id = project_items[0].get("id")
            self.record_skip("projects-create", "project already exists")
            self.record_skip(
                "projects-page-after-create",
                "project already exists; no create verification needed",
            )
        else:
            project_name = f"smoke-proj-{uuid.uuid4().hex[:8]}"
            result = self.client.call(
                "POST",
                "/Projects/Create",
                body={
                    "owner": "admin",
                    "companyName": "smoke",
                    "projectName": project_name,
                    "remark": "workflow runtime regression",
                    "supportTenant": False,
                },
                expect_codes={200, 201, 204},
            )
            self.record("projects-create", result)

            result = self.client.call(
                "POST",
                "/Projects/Page",
                body={
                    "pageIndex": 1,
                    "pageSize": 50,
                    "skipCount": 0,
                    "sorting": "",
                    "filter": project_name,
                },
            )
            self.record("projects-page-after-create", result)
            if result.ok:
                new_items = self.require_json(result.text, {}).get("items") or []
                if new_items:
                    project_id = new_items[0].get("id")

        if not project_id:
            print("Unable to obtain project id")
            self.print_summary(project_id, workflow_id, task_a, task_b, execution_id)
            return 1

        # 2) Get or create workflow
        result = self.client.call(
            "GET", "/api/app/workflow", query={"projectId": project_id}
        )
        self.record("workflow-list", result)
        workflow_items = self.require_json(result.text, []) if result.ok else []

        if not workflow_items:
            workflow_name = f"smoke-wf-{uuid.uuid4().hex[:8]}"
            graph_data = {
                "nodes": [
                    {"id": "n1", "type": "start-node", "x": 0, "y": 0},
                    {"id": "n2", "type": "end-node", "x": 200, "y": 0},
                ],
                "edges": [
                    {
                        "id": "e1",
                        "sourceNodeId": "n1",
                        "targetNodeId": "n2",
                        "sourceAnchorIndex": 0,
                        "targetAnchorIndex": 0,
                    }
                ],
            }
            result = self.client.call(
                "POST",
                "/api/app/workflow",
                body={
                    "projectId": project_id,
                    "name": workflow_name,
                    "graphData": graph_data,
                },
            )
            self.record("workflow-create", result)

            result = self.client.call(
                "GET", "/api/app/workflow", query={"projectId": project_id}
            )
            self.record("workflow-list-after-create", result)
            if result.ok:
                workflow_items = self.require_json(result.text, [])
        else:
            self.record_skip("workflow-create", "workflow already exists")
            self.record_skip(
                "workflow-list-after-create",
                "workflow already exists; no create verification needed",
            )

        if not workflow_items:
            print("Unable to obtain workflow id")
            self.print_summary(project_id, workflow_id, task_a, task_b, execution_id)
            return 1
        workflow_id = workflow_items[0].get("id")

        # 3.0 Start task
        result = self.client.call(
            "POST",
            "/api/app/workflow/tasks",
            body={
                "name": f"md-start-{uuid.uuid4().hex[:6]}",
                "projectId": project_id,
                "startType": 0,
                "onErrorAction": 0,
            },
        )
        self.record("md-3.0-post-tasks", result)
        if result.ok:
            task_a = self.require_json(result.text, {}).get("taskId")

        # 3.1 Create task
        result = self.client.call(
            "POST",
            "/api/app/workflow/tasks",
            body={
                "name": f"md-create-{uuid.uuid4().hex[:6]}",
                "projectId": project_id,
                "startType": 0,
                "onErrorAction": 1,
            },
        )
        self.record("md-3.1-post-tasks", result)
        if result.ok:
            task_b = self.require_json(result.text, {}).get("taskId")

        # 3.2 List tasks
        result = self.client.call(
            "GET",
            "/api/app/workflow/tasks",
            query={"ProjectId": project_id, "SkipCount": 0, "MaxResultCount": 20},
        )
        self.record("md-3.2-get-tasks", result)

        # 3.3 Task detail
        if task_b:
            result = self.client.call("GET", f"/api/app/workflow/tasks/{task_b}")
        else:
            result = ApiResult(False, -1, "task_b missing", "")
        self.record("md-3.3-get-task-detail", result)

        # Wait for task to leave Running before update/delete operations.
        if task_b:
            for _ in range(25):
                poll = self.client.call("GET", f"/api/app/workflow/tasks/{task_b}")
                if poll.ok:
                    status = self.require_json(poll.text, {}).get("status")
                    if status != 1:
                        break
                time.sleep(1)

        # 3.4 Cancel task
        if task_b:
            result = self.client.call(
                "POST",
                f"/api/app/workflow/tasks/{task_b}/cancel",
                expect_codes={200, 202, 204},
            )
        else:
            result = ApiResult(False, -1, "task_b missing", "")
        self.record("md-3.4-cancel-task", result)

        # 3.5 Update task
        if task_b:
            result = self.client.call(
                "PUT",
                f"/api/app/workflow/tasks/{task_b}",
                body={"name": f"md-updated-{uuid.uuid4().hex[:6]}", "onErrorAction": 0},
            )
        else:
            result = ApiResult(False, -1, "task_b missing", "")
        self.record("md-3.5-update-task", result)

        # 3.6 Delete task
        if task_b:
            result = self.client.call(
                "DELETE",
                f"/api/app/workflow/tasks/{task_b}",
                expect_codes={200, 202, 204},
            )
        else:
            result = ApiResult(False, -1, "task_b missing", "")
        self.record("md-3.6-delete-task", result)

        # 3.7.1 GET execution list
        result = self.client.call(
            "GET",
            "/api/app/workflow/executions",
            query={
                "projectId": project_id,
                "taskId": task_a or "",
                "includeVariables": "false",
            },
        )
        self.record("md-3.7.1-get-executions", result)

        # 3.7.2 POST execution start (debug mode)
        if task_a:
            trigger = {
                "projectId": project_id,
                "taskId": task_a,
                "mode": 1,
                "variableReadTimeoutMs": 1000,
            }
        else:
            trigger = {
                "projectId": project_id,
                "workflowId": workflow_id,
                "mode": 1,
                "variableReadTimeoutMs": 1000,
            }
        result = self.client.call("POST", "/api/app/workflow/executions", body=trigger)
        self.record("md-3.7.2-post-executions", result)
        if result.ok:
            execution_id = self.require_json(result.text, {}).get("executionId")

        # 3.7.4 GET execution status
        if execution_id:
            result = self.client.call(
                "GET",
                f"/api/app/workflow/executions/{execution_id}",
                query={"includeVariables": "false"},
            )
        else:
            result = ApiResult(False, -1, "execution missing", "")
        self.record("md-3.7.4-get-execution-status", result)

        # 3.7.3 POST steps
        if execution_id:
            result = self.client.call(
                "POST",
                f"/api/app/workflow/executions/{execution_id}/steps",
                body={"steps": 1, "includeVariables": False},
            )
        else:
            result = ApiResult(False, -1, "execution missing", "")
        self.record("md-3.7.3-post-steps", result)

        # 3.7.5 DELETE execution
        if execution_id:
            result = self.client.call(
                "DELETE",
                f"/api/app/workflow/executions/{execution_id}",
                expect_codes={200, 202, 204},
            )
        else:
            result = ApiResult(False, -1, "execution missing", "")
        self.record("md-3.7.5-delete-execution", result)

        self.print_summary(project_id, workflow_id, task_a, task_b, execution_id)

        failures = [item for item in self.results if item[1] == "FAIL"]
        return 0 if not failures else 1

    def print_summary(
        self,
        project_id: str | None,
        workflow_id: str | None,
        task_a: str | None,
        task_b: str | None,
        execution_id: str | None,
    ) -> None:
        print(f"PROJECT_ID {project_id}")
        print(f"WORKFLOW_ID {workflow_id}")
        print(f"TASK_A {task_a}")
        print(f"TASK_B {task_b}")
        print(f"EXECUTION_ID {execution_id}")

        failures = [item for item in self.results if item[1] == "FAIL"]
        print(f"TOTAL {len(self.results)} FAILED {len(failures)}")
        for name, status, code, message in self.results:
            print(f"{status} | {name:34} | {code:4} | {message}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Workflow runtime regression test")
    parser.add_argument("--base-url", required=True, help="Aurora service base url")
    parser.add_argument("--username", required=True, help="Login username")
    parser.add_argument("--password", required=True, help="Login password")
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=30,
        help="HTTP timeout for each request (default: 30)",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    client = ApiClient(base_url=args.base_url, timeout_seconds=args.timeout_seconds)
    runner = RegressionRunner(
        client=client, username=args.username, password=args.password
    )
    return runner.run()


if __name__ == "__main__":
    sys.exit(main())
