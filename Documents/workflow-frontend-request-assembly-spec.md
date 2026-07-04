# Workflow Frontend Request Assembly Spec

## 1. Objective

This spec defines how frontend must assemble workflow save payloads after backend strict validation changes.

Required behavior:

- Save request must include full VariableCompileRequestDto.
- VariableCompileRequestDto must include EntryNodeId and ControlFlowEdges.
- Backend performs graph validation + offline variable compile before persistence.

## 2. API Endpoints

- Create workflow: POST /api/app/workflow
- Update workflow: PUT /api/app/workflow/{id}

## 3. Save Payload Structure

Both create and update payloads must contain:

- projectId: Guid
- name: string
- graphData: WorkflowGraphDto
- variableCompileRequest: VariableCompileRequestDto

## 4. VariableCompileRequestDto Fields

Mandatory fields for save:

- projectId: must equal workflow payload projectId
- workflowId:
  - Create: frontend may send temporary value; backend overwrites with new workflow id
  - Update: frontend should send existing workflow id
- declarations: local variable declarations
- imports: cross-workflow readable variable imports
- reads: all variable reads with location/sequence/type expectation
- writes: all variable writes with location/sequence/type expectation
- entryNodeId: graph start node id (type == start-node)
- controlFlowEdges: all executable edges as FromNodeId -> ToNodeId
- defUseAnalysisMode: Conservative or Strict
- suppressedDiagnosticCodes: optional

## 5. Assembly Algorithm

### 5.1 Build graphData

Use editor graph model directly:

- nodes -> graphData.nodes
- edges -> graphData.edges

### 5.2 Build entryNodeId

- Find node where type == start-node.
- Use that node.id as entryNodeId.
- If none exists, block save in frontend.

### 5.3 Build controlFlowEdges

- For each edge in graphData.edges:
  - Require sourceNodeId and targetNodeId not empty.
  - Append { fromNodeId: sourceNodeId, toNodeId: targetNodeId }.
- If result is empty, block save in frontend.

### 5.4 Build declarations

From variable panel state (not inferred from graph):

- name
- typeName
- visibility
- mutability
- isRequiredInit
- defaultValueJson

### 5.5 Build reads

Collect all variable reads from node properties:

1) inputBindings

- For each inputBindings[key] = variableName:
- If inputBindingSources[key] == variable, emit read.

1) params

- For each params[key]:
- If paramSources[key] == variable:
  - resolve variable name from:
    - object sentinel { "$var": "name" }
    - or plain string value
  - emit read.

read item fields:

- ownerWorkflowId: current workflow id for local variable, source workflow id for imported variable
- variableName
- location: node.id + ":" + section + ":" + key
- sequence: node topological order index (1-based)
- expectedTypeName: input/config expected type if available

### 5.6 Build writes

Collect all variable writes from outputBindings:

- For each outputBindings[key] = variableName:
- Require outputBindingSources[key] == variable.
- Emit write with:
  - ownerWorkflowId: current workflow id for local writes
  - variableName
  - location: node.id + ":outputBindings:" + key
  - sequence: node topological order index (1-based)
  - expectedTypeName: output port type if available

## 6. Frontend Pre-Save Validation

Block save if any condition fails:

- graphData missing
- start-node missing
- variableCompileRequest missing
- entryNodeId missing
- controlFlowEdges empty
- source maps key set mismatch:
  - params <-> paramSources
  - inputBindings <-> inputBindingSources
  - outputBindings <-> outputBindingSources
- any outputBindingSources value is not variable

## 7. Recommended Save Flow

1) Build graphData.
2) Build variableCompileRequest from graph + variable panel state.
3) Call save API once with combined payload.
4) If backend returns compile diagnostics errors, show node-scoped errors and stop.

## 8. Create Request Example

{
  "projectId": "11111111-1111-1111-1111-111111111111",
  "name": "Height Diff Workflow",
  "graphData": {
    "nodes": [],
    "edges": []
  },
  "variableCompileRequest": {
    "projectId": "11111111-1111-1111-1111-111111111111",
    "workflowId": "00000000-0000-0000-0000-000000000000",
    "declarations": [],
    "imports": [],
    "reads": [],
    "writes": [],
    "entryNodeId": "start-node-id",
    "controlFlowEdges": [
      { "fromNodeId": "start-node-id", "toNodeId": "node-1" }
    ],
    "defUseAnalysisMode": "Conservative",
    "suppressedDiagnosticCodes": []
  }
}

## 9. Notes

- Backend has disabled Def-Use linear fallback.
- Frontend must always send path-sensitive CFG data.
- Keep sequence deterministic by topological sort of graphData.
