"""工作流检测统一判定算子演示。"""

from constants import (
    OP_DIMENSION_ANGLE_INSPECTION,
    OP_FLATNESS_INSPECTION,
    OP_SHAPE_INSPECTION,
)
from utils import edge, make_properties, new_uuid, node


def build_workflow_inspection_graph():
    """构建形状、尺寸角度和平面度判定的最小可运行工作流。"""
    id_start = new_uuid()
    id_shape = new_uuid()
    id_dimension_angle = new_uuid()
    id_flatness = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_shape,
            OP_SHAPE_INSPECTION,
            560,
            140,
            "形状检测判定",
            make_properties(
                params={
                    "expectedShape": "rectangle",
                    "expectedCount": 1,
                    "allowAdditional": False,
                },
                param_sources={
                    "expectedShape": "literal",
                    "expectedCount": "literal",
                    "allowAdditional": "literal",
                },
                input_bindings={
                    "detected_count": "1",
                    "detection_json": '{"source":"python-demo","shape":"rectangle"}',
                },
                input_sources={
                    "detected_count": "literal",
                    "detection_json": "literal",
                },
                output_bindings={
                    "result": "shape_result",
                },
                output_sources={
                    "result": "variable",
                },
            ),
        ),
        node(
            id_dimension_angle,
            OP_DIMENSION_ANGLE_INSPECTION,
            560,
            260,
            "尺寸角度检测判定",
            make_properties(
                params={
                    "checkLength": True,
                    "nominalLength": 10.0,
                    "minLengthDeviation": -0.2,
                    "maxLengthDeviation": 0.2,
                    "checkAngle": True,
                    "nominalAngle": 90.0,
                    "minAngleDeviation": -0.5,
                    "maxAngleDeviation": 0.5,
                },
                param_sources={
                    "checkLength": "literal",
                    "nominalLength": "literal",
                    "minLengthDeviation": "literal",
                    "maxLengthDeviation": "literal",
                    "checkAngle": "literal",
                    "nominalAngle": "literal",
                    "minAngleDeviation": "literal",
                    "maxAngleDeviation": "literal",
                },
                input_bindings={
                    "measured_length": "10.1",
                    "measured_angle": "89.8",
                },
                input_sources={
                    "measured_length": "literal",
                    "measured_angle": "literal",
                },
                output_bindings={
                    "result": "dimension_angle_result",
                },
                output_sources={
                    "result": "variable",
                },
            ),
        ),
        node(
            id_flatness,
            OP_FLATNESS_INSPECTION,
            560,
            380,
            "平面度检测判定",
            make_properties(
                params={"maxFlatness": 0.1, "maxAbsoluteDistance": 0.2},
                param_sources={
                    "maxFlatness": "literal",
                    "maxAbsoluteDistance": "literal",
                },
                input_bindings={
                    "measured_flatness": "0.08",
                    "max_absolute_distance": "0.15",
                    "measurement_json": '{"source":"python-demo","unit":"mm"}',
                },
                input_sources={
                    "measured_flatness": "literal",
                    "max_absolute_distance": "literal",
                    "measurement_json": "literal",
                },
                output_bindings={
                    "result": "flatness_result",
                },
                output_sources={
                    "result": "variable",
                },
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            500,
            "结束",
            make_properties(
                input_bindings={
                    "shapeResult": "shape_result",
                    "dimensionAngleResult": "dimension_angle_result",
                    "flatnessResult": "flatness_result",
                },
                input_sources={
                    "shapeResult": "variable",
                    "dimensionAngleResult": "variable",
                    "flatnessResult": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_shape),
        edge(id_shape, id_dimension_angle),
        edge(id_dimension_angle, id_flatness),
        edge(id_flatness, id_end),
    ]
    return {"nodes": nodes, "edges": edges}
