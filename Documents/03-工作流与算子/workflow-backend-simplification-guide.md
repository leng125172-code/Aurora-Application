# Workflow Save Backend Simplification Guide

## 1. Change Overview

This change performs a backend-only, breaking simplification of workflow save payloads.

Before:

- Frontend must submit `variableCompileRequest` in both create and update APIs.

After:

- Frontend submits only `projectId`, `name`, `graphData`.
- Backend auto-builds `VariableCompileRequestDto` from `graphData` and operator metadata, then runs offline variable compilation.

No backward compatibility for old JSON contract is provided.

## 2. API Contract Changes (Breaking)

### 2.1 Create

- Endpoint: `POST /api/app/workflow`
- Request body now contains:
  - `projectId`
  - `name`
  - `graphData`

### 2.2 Update

- Endpoint: `PUT /api/app/workflow/{id}`
- Request body now contains:
  - `projectId`
  - `name`
  - `graphData`

### 2.3 Removed Input Field

- `variableCompileRequest` is removed from:
  - `CreateWorkflowInput`
  - `UpdateWorkflowInput`

## 3. Backend Auto-Build Rules

The backend auto-builds `VariableCompileRequestDto` in save flow.

### 3.1 Entry and CFG

- `entryNodeId`: first node with `type == "start-node"`.
- `controlFlowEdges`: built from all edges where both `sourceNodeId` and `targetNodeId` are present.

### 3.2 Read References

Reads are collected from:

- `properties.inputBindings[key]` where `inputBindingSources[key] == "variable"`.
- `properties.params[key]` where `paramSources[key] == "variable"`.

Param variable format supports:

- string variable name
- sentinel object `{ "$var": "name" }`

### 3.3 Write References

Writes are collected from:

- `properties.outputBindings[key]` where `outputBindingSources[key] == "variable"`.

### 3.4 Declarations

- Declarations are auto-collected from write targets (`outputBindings` variable names).
- Type is inferred from operator output port metadata.
- Duplicate variable names with conflicting inferred types are rejected.

### 3.5 Sequence

- `sequence` is generated from node order in current `graphData.nodes` list (1-based).

### 3.6 Type Source

Expected type metadata comes from `IOperatorRegistry`:

- input/output ports: `ParameterDescriptor.ParameterTypeName`
- config fields: `ConfigParameterDescriptor.ParameterTypeName`

All auto-generated type names use CLR full names (for example `System.Double`).

## 4. Validation/Error Behavior

Save now fails with readable backend errors for these cases:

1. Missing start node

- Error: `variableCompileRequest.EntryNodeId 不能为空。请确保图中存在 start-node。`

1. Empty CFG edges

- Error: `variableCompileRequest.ControlFlowEdges 不能为空，必须提供完整 CFG。`

1. Variable source marked but value is invalid

- Example: params source is `variable` but value is neither string nor `{ "$var": "..." }`.

1. Output binding has no operator type metadata

- Error indicates node and output port key.

1. Same variable inferred to two different types

- Error indicates conflicting type names.

## 5. Request Examples

### 5.1 Create Request (New)

```json
{
  "projectId": "3a223a2e-bbfb-2e59-11be-139a5f778d90",
  "name": "标准高度差检测",
  "graphData": {
    "nodes": [],
    "edges": []
  }
}
```

### 5.2 Update Request (New)

```json
{
  "projectId": "3a223a2e-bbfb-2e59-11be-139a5f778d90",
  "name": "标准高度差检测",
  "graphData": {
    "nodes": [],
    "edges": []
  }
}
```

## 6. Affected Files

- `Sources/AuroraStruct3D/AuroraStruct3D.Application.Contracts/Workflow/Dtos/WorkflowSaveInputs.cs`
- `Sources/AuroraStruct3D/AuroraStruct3D.Application/Workflow/WorkflowAppService.cs`

## 7. Migration Notes

Frontend side must:

1. Stop sending `variableCompileRequest` in create/update payload.
2. Keep `graphData` source flags and bindings valid.
3. Use save API errors directly for node-level troubleshooting.

## 8. Implementation Notes

- Save flow still performs graph validation first.
- Offline variable compilation remains mandatory before persistence.
- No fallback to legacy payload schema is implemented.
