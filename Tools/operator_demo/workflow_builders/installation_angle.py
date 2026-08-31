"""安装角度检测演示工作流。

这个工作流刻意把“选哪个平面”“在平面上选哪个区域”“提取区域全部点”
拆成三个独立算子，便于前端配置 ROI，也避免使用二维投影 ROI 造成坐标误差。
默认 ROI 为空时，定义平面区域算子会自动采用拟合内点的完整边界。
"""

from constants import (
    OP_DEFINE_PLANE_ROI,
    OP_EXTRACT_POINTS_IN_PLANE_ROI,
    OP_FITTED_PLANE_MESH,
    OP_INSTALLATION_AXIS_TO_AXIS,
    OP_INSTALLATION_AXIS_TO_PLANE,
    OP_INSTALLATION_PLANE_ANGLE,
    OP_INSTALLATION_TWIST,
    OP_LINE_FIT_3D,
    OP_RANSAC_PLANE_FIT,
    OP_READ_POINT_CLOUD,
    OP_SELECT_FITTED_PLANE,
    OP_STATISTICAL_OUTLIER_REMOVAL,
    OP_SVD_PLANE_FIT,
    OP_VOXEL_DOWNSAMPLE,
    OP_Z_COLORIZE_POINT_CLOUD,
    OP_COLORED_CLOUD_TO_TILTED_IMAGE,
    OP_COMBINE_INSPECTION_RESULTS,
    OP_SAVE_IMAGE_BLOB,
)
from utils import edge, make_properties, new_uuid, node


def _operator_properties(
    inputs=None, outputs=None, params=None, literal_inputs=None
):
    inputs = inputs or {}
    outputs = outputs or {}
    params = params or {}
    literal_inputs = literal_inputs or {}
    all_inputs = {**inputs, **literal_inputs}
    return make_properties(
        params=params,
        param_sources={name: "literal" for name in params},
        input_bindings=all_inputs,
        input_sources={
            name: ("literal" if name in literal_inputs else "variable")
            for name in all_inputs
        },
        output_bindings=outputs,
        output_sources={name: "variable" for name in outputs},
    )


def _plane_fit_properties(source, prefix, seed):
    return _operator_properties(
        inputs={"input_point_cloud": source},
        outputs={
            "plane_params": f"{prefix}_coarse_params",
            "inlier_points": f"{prefix}_coarse_points",
            "outlier_points": f"{prefix}_remaining_cloud",
        },
        params={
            "distanceThreshold": 0.05,
            "maxIterations": 1000,
            "probability": 0.99,
            "refinePlane": "true",
            "normalConstraint": "none",
            "seed": seed,
        },
    )


def _selector_properties(plane_name, prefix):
    return _operator_properties(
        inputs={
            "plane_a_params": "plane_a_coarse_params",
            "plane_a_points": "plane_a_coarse_points",
            "plane_b_params": "plane_b_coarse_params",
            "plane_b_points": "plane_b_coarse_points",
            "plane_c_params": "plane_c_coarse_params",
            "plane_c_points": "plane_c_coarse_points",
        },
        outputs={
            "selected_plane_params": f"{prefix}_selected_params",
            "selected_plane_points": f"{prefix}_selected_points",
            "selected_plane_name": f"{prefix}_selected_name",
        },
        params={"planeName": plane_name},
    )


def _roi_properties(prefix):
    return _operator_properties(
        inputs={
            "plane_params": f"{prefix}_selected_params",
            "plane_points": f"{prefix}_selected_points",
        },
        outputs={"plane_roi_json": f"{prefix}_roi_json"},
        params={"roiJson": '{"points":[]}'},
    )


def _extract_properties(prefix):
    return _operator_properties(
        inputs={
            "input_point_cloud": "raw_cloud",
            "plane_params": f"{prefix}_selected_params",
            "plane_roi_json": f"{prefix}_roi_json",
        },
        outputs={
            "selected_points": f"{prefix}_roi_points",
            "remaining_points": f"{prefix}_remaining_points",
            "selected_count": f"{prefix}_selected_count",
        },
        params={"planeDistanceThreshold": 0.05},
    )


def _svd_properties(prefix):
    return _operator_properties(
        inputs={"input_point_cloud": f"{prefix}_roi_points"},
        outputs={
            "plane_params": f"{prefix}_plane_params",
            "fitted_plane_points": f"{prefix}_fitted_plane_points",
            "fitting_error": f"{prefix}_plane_fitting_error",
        },
    )


def _line_fit_properties(prefix):
    return _operator_properties(
        inputs={"input_point_cloud": f"{prefix}_roi_points"},
        outputs={
            "line_params": f"{prefix}_axis_params",
            "inlier_points": f"{prefix}_axis_points",
            "outlier_points": f"{prefix}_axis_outliers",
            "fitting_error": f"{prefix}_axis_error",
            "result": f"{prefix}_axis_result",
        },
        params={
            "distanceThreshold": 0.05,
            "maxIterations": 1000,
            "probability": 0.99,
            "referenceAxis": "z",
            "minAngle": 0.0,
            "maxAngle": 180.0,
        },
    )


def _inspection_outputs(prefix, number_ports):
    return {"result": f"{prefix}_result"}


def build_installation_angle_graph(point_cloud_path, relaxed_demo=False):
    ids = {
        name: new_uuid()
        for name in (
            "start read downsample denoise fit_a fit_b fit_c select_ref select_measured "
            "roi_ref roi_measured extract_ref extract_measured svd_ref svd_measured "
            "mesh_ref mesh_measured line_ref line_measured plane_angle axis_plane "
            "axis_axis twist colorize tilted_view save_tilted combine_primary "
            "combine_final end"
        ).split()
    }

    plane_angle_params = {
        "nominalTiltX": 0.0,
        "nominalTiltY": 0.0,
        "minDeviationX": -180.0 if relaxed_demo else -0.5,
        "maxDeviationX": 180.0 if relaxed_demo else 0.5,
        "minDeviationY": -180.0 if relaxed_demo else -0.5,
        "maxDeviationY": 180.0 if relaxed_demo else 0.5,
        "maxTotalDeviation": 360.0 if relaxed_demo else 0.7,
        "maxFitRmse": 1000.0 if relaxed_demo else 0.05,
        "minPointCount": 3 if relaxed_demo else 30,
    }
    common_angle_params = {
        "maxFitRmse": 1000.0 if relaxed_demo else 0.05,
        "minPointCount": 3 if relaxed_demo else 20,
    }

    nodes = [
        node(ids["start"], "start-node", 720, 20, "开始", make_properties()),
        node(
            ids["read"],
            OP_READ_POINT_CLOUD,
            720,
            100,
            "读取点云",
            _operator_properties(
                outputs={"output_point_cloud": "raw_cloud"},
                literal_inputs={"point_cloud_path": point_cloud_path},
            ),
        ),
        node(
            ids["downsample"],
            OP_VOXEL_DOWNSAMPLE,
            720,
            180,
            "体素降采样（仅用于粗定位）",
            _operator_properties(
                inputs={"input_point_cloud": "raw_cloud"},
                outputs={"output_point_cloud": "downsampled_cloud"},
                params={"voxelSize": 0.5},
            ),
        ),
        node(
            ids["denoise"],
            OP_STATISTICAL_OUTLIER_REMOVAL,
            720,
            260,
            "统计滤波",
            _operator_properties(
                inputs={"input_point_cloud": "downsampled_cloud"},
                outputs={"output_point_cloud": "filtered_cloud"},
                params={"k": 20, "stddevMultiplier": 1.0},
            ),
        ),
        node(ids["fit_a"], OP_RANSAC_PLANE_FIT, 260, 360, "拟合平面A", _plane_fit_properties("filtered_cloud", "plane_a", 11)),
        node(ids["fit_b"], OP_RANSAC_PLANE_FIT, 720, 360, "从A外点拟合平面B", _plane_fit_properties("plane_a_remaining_cloud", "plane_b", 22)),
        node(ids["fit_c"], OP_RANSAC_PLANE_FIT, 1180, 360, "从B外点拟合平面C", _plane_fit_properties("plane_b_remaining_cloud", "plane_c", 33)),
        node(ids["select_ref"], OP_SELECT_FITTED_PLANE, 470, 470, "功能1：选择基准平面", _selector_properties("A", "reference")),
        node(ids["select_measured"], OP_SELECT_FITTED_PLANE, 970, 470, "功能1：选择安装平面", _selector_properties("B", "measured")),
        node(ids["roi_ref"], OP_DEFINE_PLANE_ROI, 470, 570, "功能2：选择基准面区域", _roi_properties("reference")),
        node(ids["roi_measured"], OP_DEFINE_PLANE_ROI, 970, 570, "功能2：选择安装面区域", _roi_properties("measured")),
        node(ids["extract_ref"], OP_EXTRACT_POINTS_IN_PLANE_ROI, 470, 670, "功能3：提取基准ROI全部点", _extract_properties("reference")),
        node(ids["extract_measured"], OP_EXTRACT_POINTS_IN_PLANE_ROI, 970, 670, "功能3：提取安装ROI全部点", _extract_properties("measured")),
        node(ids["svd_ref"], OP_SVD_PLANE_FIT, 360, 790, "基准面精拟合", _svd_properties("reference")),
        node(ids["svd_measured"], OP_SVD_PLANE_FIT, 860, 790, "安装面精拟合", _svd_properties("measured")),
        node(
            ids["mesh_ref"],
            OP_FITTED_PLANE_MESH,
            160,
            900,
            "基准面连续网格",
            _operator_properties(
                inputs={
                    "plane_params": "reference_plane_params",
                    "inlier_points": "reference_fitted_plane_points",
                },
                outputs={
                    "mesh_vertices": "reference_mesh_vertices",
                    "mesh_indices": "reference_mesh_indices",
                    "mesh_normals": "reference_mesh_normals",
                    "boundary_json": "reference_mesh_boundary",
                    "mesh_info": "reference_mesh_info",
                },
                params={"boundaryPadding": 0.0},
            ),
        ),
        node(
            ids["mesh_measured"],
            OP_FITTED_PLANE_MESH,
            660,
            900,
            "安装面连续网格",
            _operator_properties(
                inputs={
                    "plane_params": "measured_plane_params",
                    "inlier_points": "measured_fitted_plane_points",
                },
                outputs={
                    "mesh_vertices": "measured_mesh_vertices",
                    "mesh_indices": "measured_mesh_indices",
                    "mesh_normals": "measured_mesh_normals",
                    "boundary_json": "measured_mesh_boundary",
                    "mesh_info": "measured_mesh_info",
                },
                params={"boundaryPadding": 0.0},
            ),
        ),
        node(ids["line_ref"], OP_LINE_FIT_3D, 1160, 790, "基准方向/轴线拟合", _line_fit_properties("reference")),
        node(ids["line_measured"], OP_LINE_FIT_3D, 1460, 790, "安装方向/轴线拟合", _line_fit_properties("measured")),
        node(
            ids["plane_angle"],
            OP_INSTALLATION_PLANE_ANGLE,
            360,
            1040,
            "第一阶段：安装平面角度检测",
            _operator_properties(
                inputs={
                    "reference_plane_params": "reference_plane_params",
                    "reference_points": "reference_roi_points",
                    "measured_plane_params": "measured_plane_params",
                    "measured_points": "measured_roi_points",
                },
                outputs=_inspection_outputs(
                    "plane_angle",
                    (
                        "tilt_x", "tilt_y", "total_tilt", "deviation_x",
                        "deviation_y", "total_deviation", "reference_rmse",
                        "measured_rmse",
                    ),
                ),
                params=plane_angle_params,
            ),
        ),
        node(
            ids["axis_plane"],
            OP_INSTALLATION_AXIS_TO_PLANE,
            760,
            1040,
            "第二阶段：轴线与平面检测",
            _operator_properties(
                inputs={
                    "reference_plane_params": "reference_plane_params",
                    "reference_points": "reference_roi_points",
                    "axis_params": "measured_axis_params",
                    "axis_points": "measured_axis_points",
                },
                outputs=_inspection_outputs(
                    "axis_plane",
                    ("tilt_x", "tilt_y", "axis_to_normal_angle", "axis_to_plane_angle"),
                ),
                params={
                    **common_angle_params,
                    "nominalTiltX": 0.0,
                    "nominalTiltY": 0.0,
                    "maxDeviationX": 180.0 if relaxed_demo else 0.5,
                    "maxDeviationY": 180.0 if relaxed_demo else 0.5,
                    "maxTotalDeviation": 360.0 if relaxed_demo else 0.7,
                },
            ),
        ),
        node(
            ids["axis_axis"],
            OP_INSTALLATION_AXIS_TO_AXIS,
            1160,
            1040,
            "第二阶段：轴线夹角检测",
            _operator_properties(
                inputs={
                    "reference_axis_params": "reference_axis_params",
                    "reference_axis_points": "reference_axis_points",
                    "measured_axis_params": "measured_axis_params",
                    "measured_axis_points": "measured_axis_points",
                },
                outputs=_inspection_outputs("axis_axis", ("axis_angle", "angle_deviation")),
                params={
                    **common_angle_params,
                    "nominalAngle": 0.0,
                    "maxAngleDeviation": 180.0 if relaxed_demo else 0.5,
                },
            ),
        ),
        node(
            ids["twist"],
            OP_INSTALLATION_TWIST,
            1560,
            1040,
            "第二阶段：平面内旋转检测",
            _operator_properties(
                inputs={
                    "reference_plane_params": "reference_plane_params",
                    "reference_direction_params": "reference_axis_params",
                    "reference_direction_points": "reference_axis_points",
                    "measured_direction_params": "measured_axis_params",
                    "measured_direction_points": "measured_axis_points",
                },
                outputs=_inspection_outputs("twist", ("twist_z", "twist_deviation")),
                params={
                    **common_angle_params,
                    "nominalTwist": 0.0,
                    "maxTwistDeviation": 180.0 if relaxed_demo else 0.5,
                },
            ),
        ),
        node(
            ids["colorize"],
            OP_Z_COLORIZE_POINT_CLOUD,
            1760,
            470,
            "结果点云Z轴着色",
            _operator_properties(
                inputs={"input_point_cloud": "raw_cloud"},
                outputs={"output_point_cloud": "result_colored_cloud"},
                params={"colorMap": "jet", "autoRange": "true"},
            ),
        ),
        node(
            ids["tilted_view"],
            OP_COLORED_CLOUD_TO_TILTED_IMAGE,
            1760,
            650,
            "最终斜视图（Y→X→Z）",
            _operator_properties(
                inputs={"input_point_cloud": "result_colored_cloud"},
                outputs={
                    "output_image": "tilted_result_image",
                    "projection_mapping": "tilted_projection_mapping",
                },
                params={
                    "tiltAngleY": 30.0,
                    "tiltAngleX": 30.0,
                    "tiltAngleZ": 30.0,
                    "outputWidth": 1024,
                    "outputHeight": 768,
                    "backgroundColor": "#00000000",
                    "autoFit": "false",
                    "imageResolution": 1024,
                },
            ),
        ),
        node(
            ids["save_tilted"],
            OP_SAVE_IMAGE_BLOB,
            1760,
            850,
            "斜视图结果存Blob",
            _operator_properties(
                inputs={"input_mat": "tilted_result_image"},
                outputs={
                    "blob_name": "tilted_result_blob_name",
                    "download_url": "tilted_result_url",
                },
                params={"fileName": "installation-angle-tilted-view.png"},
            ),
        ),
        node(
            ids["combine_primary"],
            OP_COMBINE_INSPECTION_RESULTS,
            720,
            1140,
            "安装角前三项汇总",
            _operator_properties(
                inputs={
                    "flatness_result": "plane_angle_result",
                    "line_result": "axis_plane_result",
                    "circle_result": "axis_axis_result",
                },
                outputs={"result": "installation_primary_result"},
            ),
        ),
        node(
            ids["combine_final"],
            OP_COMBINE_INSPECTION_RESULTS,
            1120,
            1140,
            "安装角最终汇总",
            _operator_properties(
                inputs={
                    "flatness_result": "installation_primary_result",
                    "line_result": "twist_result",
                    "circle_result": "plane_angle_result",
                },
                outputs={"result": "installation_result"},
            ),
        ),
        node(
            ids["end"],
            "end-node",
            940,
            1200,
            "结束/最终结果",
            make_properties(
                input_bindings={"安装角汇总结果": "installation_result"},
                input_sources={"安装角汇总结果": "variable"},
            ),
        ),
    ]

    edges = [
        edge(ids["start"], ids["read"]),
        edge(ids["read"], ids["downsample"]),
        edge(ids["downsample"], ids["denoise"]),
        edge(ids["denoise"], ids["fit_a"]),
        edge(ids["fit_a"], ids["fit_b"]),
        edge(ids["fit_b"], ids["fit_c"]),
        edge(ids["fit_a"], ids["select_ref"]),
        edge(ids["fit_b"], ids["select_ref"]),
        edge(ids["fit_c"], ids["select_ref"]),
        edge(ids["fit_a"], ids["select_measured"]),
        edge(ids["fit_b"], ids["select_measured"]),
        edge(ids["fit_c"], ids["select_measured"]),
        edge(ids["select_ref"], ids["roi_ref"]),
        edge(ids["select_measured"], ids["roi_measured"]),
        edge(ids["roi_ref"], ids["extract_ref"]),
        edge(ids["roi_measured"], ids["extract_measured"]),
        edge(ids["extract_ref"], ids["svd_ref"]),
        edge(ids["extract_measured"], ids["svd_measured"]),
        edge(ids["extract_ref"], ids["line_ref"]),
        edge(ids["extract_measured"], ids["line_measured"]),
        edge(ids["svd_ref"], ids["mesh_ref"]),
        edge(ids["svd_measured"], ids["mesh_measured"]),
        edge(ids["svd_ref"], ids["plane_angle"]),
        edge(ids["svd_measured"], ids["plane_angle"]),
        edge(ids["svd_ref"], ids["axis_plane"]),
        edge(ids["line_measured"], ids["axis_plane"]),
        edge(ids["line_ref"], ids["axis_axis"]),
        edge(ids["line_measured"], ids["axis_axis"]),
        edge(ids["svd_ref"], ids["twist"]),
        edge(ids["line_ref"], ids["twist"]),
        edge(ids["line_measured"], ids["twist"]),
        edge(ids["read"], ids["colorize"]),
        edge(ids["colorize"], ids["tilted_view"]),
        edge(ids["tilted_view"], ids["save_tilted"]),
        edge(ids["plane_angle"], ids["end"]),
        edge(ids["axis_plane"], ids["end"]),
        edge(ids["axis_axis"], ids["end"]),
        edge(ids["twist"], ids["end"]),
        edge(ids["plane_angle"], ids["combine_primary"]),
        edge(ids["axis_plane"], ids["combine_primary"]),
        edge(ids["axis_axis"], ids["combine_primary"]),
        edge(ids["combine_primary"], ids["combine_final"]),
        edge(ids["twist"], ids["combine_final"]),
        edge(ids["combine_final"], ids["end"]),
        edge(ids["mesh_ref"], ids["end"]),
        edge(ids["mesh_measured"], ids["end"]),
        edge(ids["save_tilted"], ids["end"]),
    ]
    if not relaxed_demo:
        summary_ids = {ids["combine_primary"], ids["combine_final"]}
        nodes = [item for item in nodes if item["id"] not in summary_ids]
        edges = [
            item
            for item in edges
            if item["sourceNodeId"] not in summary_ids
            and item["targetNodeId"] not in summary_ids
        ]
        end_properties = next(
            item["properties"] for item in nodes if item["id"] == ids["end"]
        )
        end_properties["inputBindings"] = {
            "平面夹角结果": "plane_angle_result",
            "轴线对平面结果": "axis_plane_result",
            "轴线夹角结果": "axis_axis_result",
            "平面内旋转结果": "twist_result",
            "斜视图结果": "tilted_result_url",
        }
        end_properties["inputBindingSources"] = {
            name: "variable" for name in end_properties["inputBindings"]
        }
        end_properties["inputBindingDisplayNames"] = {
            "平面夹角结果": "① 平面相对安装角（X/Y 方向）",
            "轴线对平面结果": "② 轴线相对基准面角度",
            "轴线夹角结果": "③ 基准轴线与安装轴线夹角",
            "平面内旋转结果": "④ 平面内旋转（Twist）",
            "斜视图结果": "检测区域斜视图",
        }
    else:
        end_properties = next(
            item["properties"] for item in nodes if item["id"] == ids["end"]
        )
        end_properties["inputBindingDisplayNames"] = {
            "安装角汇总结果": "安装角总体判定",
        }
    return {"nodes": nodes, "edges": edges}
