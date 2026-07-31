from utils import node, edge, make_properties, new_uuid
from constants import (
    OP_READ_IMAGE,
    OP_COLOR_TO_GRAYSCALE,
    OP_GAUSSIAN_BLUR,
    OP_CANNY_EDGE,
    OP_OTSU_THRESHOLD,
    OP_CONTOUR_ANALYSIS,
    OP_SAVE_IMAGE_BLOB,
    OP_INSTALLATION_ANGLE_FEATURE,
    IMAGE_RESOLUTION,
)

ANGLE_FEATURE_TEMPLATE = """{
  "features": [
    {
      "name": "特征1",
      "start": {"x": 80, "y": 160},
      "end": {"x": 260, "y": 160},
      "searchRegion": [
        {"x": 60, "y": 130}, {"x": 280, "y": 130},
        {"x": 280, "y": 190}, {"x": 60, "y": 190}
      ]
    },
    {
      "name": "特征2",
      "start": {"x": 320, "y": 220},
      "end": {"x": 320, "y": 420},
      "searchRegion": [
        {"x": 290, "y": 200}, {"x": 350, "y": 200},
        {"x": 350, "y": 440}, {"x": 290, "y": 440}
      ]
    }
  ]
}"""


def build_2d_image_processing_graph(image_path, include_angle_feature=False):
    id_start = new_uuid()
    id_read = new_uuid()
    id_gray = new_uuid()
    id_blur = new_uuid()
    id_canny = new_uuid()
    id_otsu = new_uuid()
    id_contour = new_uuid()
    id_export_edge = new_uuid()
    id_export_contour = new_uuid()
    id_angle_feature = new_uuid()
    id_export_angle = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_read,
            OP_READ_IMAGE,
            560,
            120,
            "读取图像",
            make_properties(
                input_bindings={"img_path": image_path},
                input_sources={"img_path": "literal"},
                output_bindings={"output_mat": "raw_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_gray,
            OP_COLOR_TO_GRAYSCALE,
            380,
            220,
            "转灰度图",
            make_properties(
                input_bindings={"input_mat": "raw_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "gray_mat": "gray_image",
                    "output_point_cloud": "gray_point_cloud",
                },
                output_sources={
                    "gray_mat": "variable",
                    "output_point_cloud": "variable",
                },
            ),
        ),
        node(
            id_blur,
            OP_GAUSSIAN_BLUR,
            740,
            220,
            "高斯模糊",
            make_properties(
                params={"kernelSize": 5, "sigmaX": 1.0},
                param_sources={"kernelSize": "literal", "sigmaX": "literal"},
                input_bindings={"input_mat": "gray_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"output_mat": "blur_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_canny,
            OP_CANNY_EDGE,
            740,
            320,
            "Canny边缘检测",
            make_properties(
                params={"lowThreshold": 50, "highThreshold": 150, "apertureSize": 3},
                param_sources={
                    "lowThreshold": "literal",
                    "highThreshold": "literal",
                    "apertureSize": "literal",
                },
                input_bindings={"input_mat": "blur_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"output_mat": "edge_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_otsu,
            OP_OTSU_THRESHOLD,
            380,
            320,
            "Otsu阈值分割",
            make_properties(
                params={"maxValue": 255},
                param_sources={"maxValue": "literal"},
                input_bindings={"input_mat": "gray_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "output_mat": "threshold_image",
                    "threshold_value": "otsu_threshold_value",
                },
                output_sources={
                    "output_mat": "variable",
                    "threshold_value": "variable",
                },
            ),
        ),
        node(
            id_contour,
            OP_CONTOUR_ANALYSIS,
            560,
            420,
            "轮廓分析",
            make_properties(
                params={
                    "minArea": 10.0,
                    "maxArea": 1000000000.0,
                    "epsilonFactor": 0.01,
                    "minCount": 1,
                    "maxCount": 1000000,
                },
                param_sources={
                    "minArea": "literal",
                    "maxArea": "literal",
                    "epsilonFactor": "literal",
                    "minCount": "literal",
                    "maxCount": "literal",
                },
                input_bindings={"input_mat": "threshold_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "contours_json": "contour_result",
                    "contour_count": "contour_count",
                    "is_ok": "contour_ok",
                },
                output_sources={
                    "contours_json": "variable",
                    "contour_count": "variable",
                    "is_ok": "variable",
                },
            ),
        ),
        node(
            id_export_edge,
            OP_SAVE_IMAGE_BLOB,
            740,
            520,
            "边缘图存Blob",
            make_properties(
                params={"fileName": "edge-detection.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "edge_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "download_url": "edge_image_url",
                    "blob_name": "edge_image_blob_name",
                },
                output_sources={
                    "download_url": "variable",
                    "blob_name": "variable",
                },
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            620,
            "结束",
            make_properties(
                input_bindings={
                    "edgeImageUrl": "edge_image_url",
                    "contourCount": "contour_count",
                    "contours": "contour_result",
                    "isOk": "contour_ok",
                },
                input_sources={
                    "edgeImageUrl": "variable",
                    "contourCount": "variable",
                    "contours": "variable",
                    "isOk": "variable",
                },
            ),
        ),
    ]

    if include_angle_feature:
        nodes.extend(
            [
                node(
                    id_angle_feature,
                    OP_INSTALLATION_ANGLE_FEATURE,
                    980,
                    420,
                    "按标准图特征检测安装角度",
                    make_properties(
                        params={
                            "featureTemplateJson": ANGLE_FEATURE_TEMPLATE,
                            "angleType": "acute",
                            "nominalAngle": 90.0,
                            "minDeviation": -1.0,
                            "maxDeviation": 1.0,
                            "minimumLineLength": 20.0,
                            "maximumDirectionError": 20.0,
                        },
                        param_sources={
                            "featureTemplateJson": "literal",
                            "angleType": "literal",
                            "nominalAngle": "literal",
                            "minDeviation": "literal",
                            "maxDeviation": "literal",
                            "minimumLineLength": "literal",
                            "maximumDirectionError": "literal",
                        },
                        input_bindings={"actual_image": "raw_image"},
                        input_sources={"actual_image": "variable"},
                        output_bindings={
                            "result_image": "angle_result_image",
                            "measured_angle": "angle_measured",
                            "angle_deviation": "angle_deviation",
                            "is_valid": "angle_is_valid",
                            "is_ok": "angle_is_ok",
                            "inspection_status": "angle_status",
                            "result_json": "angle_result_json",
                        },
                        output_sources={
                            "result_image": "variable",
                            "measured_angle": "variable",
                            "angle_deviation": "variable",
                            "is_valid": "variable",
                            "is_ok": "variable",
                            "inspection_status": "variable",
                            "result_json": "variable",
                        },
                    ),
                ),
                node(
                    id_export_angle,
                    OP_SAVE_IMAGE_BLOB,
                    980,
                    520,
                    "安装角度结果图存Blob",
                    make_properties(
                        params={"fileName": "installation-angle-result.png"},
                        param_sources={"fileName": "literal"},
                        input_bindings={"input_mat": "angle_result_image"},
                        input_sources={"input_mat": "variable"},
                        output_bindings={
                            "download_url": "angle_result_image_url",
                            "blob_name": "angle_result_image_blob_name",
                        },
                        output_sources={
                            "download_url": "variable",
                            "blob_name": "variable",
                        },
                    ),
                ),
            ]
        )
        end_properties = next(
            item["properties"] for item in nodes if item["id"] == id_end
        )
        end_properties["inputBindings"].update(
            {
                "angleResultImageBlobName": "angle_result_image_blob_name",
                "angleStatus": "angle_status",
                "measuredAngle": "angle_measured",
                "angleDeviation": "angle_deviation",
                "angleResult": "angle_result_json",
            }
        )
        end_properties["inputBindingSources"].update(
            {
                "angleResultImageBlobName": "variable",
                "angleStatus": "variable",
                "measuredAngle": "variable",
                "angleDeviation": "variable",
                "angleResult": "variable",
            }
        )

    edges = [
        edge(id_start, id_read),
        edge(id_read, id_gray),
        edge(id_gray, id_blur),
        edge(id_gray, id_otsu),
        edge(id_blur, id_canny),
        edge(id_otsu, id_contour),
        edge(id_canny, id_export_edge),
        edge(id_contour, id_end),
        edge(id_export_edge, id_end),
    ]

    if include_angle_feature:
        edges.extend(
            [
                edge(id_read, id_angle_feature),
                edge(id_angle_feature, id_export_angle),
                edge(id_export_angle, id_end),
            ]
        )

    return {"nodes": nodes, "edges": edges}
