import json
from copy import deepcopy

from utils import (
    PREVIEW_HEIGHT,
    PREVIEW_WIDTH,
    node,
    edge,
    make_properties,
    new_uuid,
)
from constants import (
    OP_READ_POINT_CLOUD,
    OP_VOXEL_DOWNSAMPLE,
    OP_STATISTICAL_OUTLIER_REMOVAL,
    OP_Z_COLORIZE_POINT_CLOUD,
    OP_COMPUTE_NORMALS,
    OP_RANSAC_PLANE_FIT,
    OP_Z_CHANNEL_STATS,
    OP_POINT_TO_PLANE_DISTANCE,
    OP_LINE_FIT_3D,
    OP_CIRCLE_FIT_3D,
    OP_COLORED_POINT_CLOUD_TO_IMAGE,
    OP_SAVE_POINT_CLOUD_BLOB,
    OP_SAVE_IMAGE_BLOB,
    OP_ROI_PARTITION,
    OP_OVERLAY_ROI_MARKERS,
    OP_POINT_CLOUD_CROP,
    OP_PLANE_AREA_MEASURE,
    OP_COMBINE_INSPECTION_RESULTS,
    IMAGE_RESOLUTION,
    PROJECTION_BOUNDS,
)


def _roi_json(name, x, y, width, height):
    return json.dumps(
        {
            "rois": [
                {
                    "name": name,
                    "type": "Rect",
                    "x": x,
                    "y": y,
                    "width": width,
                    "height": height,
                    "rotation": 0,
                }
            ],
        },
        ensure_ascii=False,
    )


def _plane_contour_json(x, y, width, height):
    """把平面 ROI 的像素矩形转换为面积算子使用的世界坐标轮廓。"""
    bounds = PROJECTION_BOUNDS

    def world_point(pixel_x, pixel_y):
        world_x = bounds["minX"] + pixel_x / PREVIEW_WIDTH * (
            bounds["maxX"] - bounds["minX"]
        )
        world_y = bounds["maxY"] - pixel_y / PREVIEW_HEIGHT * (
            bounds["maxY"] - bounds["minY"]
        )
        return {"x": world_x, "y": world_y, "z": 0.0}

    return json.dumps(
        [
            world_point(x, y),
            world_point(x + width, y),
            world_point(x + width, y + height),
            world_point(x, y + height),
        ],
        ensure_ascii=False,
    )


def _roi_node(node_id, title, x, roi_json, mask_name, metadata_name):
    return node(
        node_id,
        OP_ROI_PARTITION,
        x,
        680,
        title,
        make_properties(
            params={"roiJson": roi_json},
            param_sources={"roiJson": "literal"},
            input_bindings={
                "input_mat": "preview_image",
                "projection_mapping": "projection_mapping",
            },
            input_sources={
                "input_mat": "variable",
                "projection_mapping": "variable",
            },
            output_bindings={
                "primary_mask": mask_name,
                "roi_metadata": metadata_name,
            },
            output_sources={
                "primary_mask": "variable",
                "roi_metadata": "variable",
            },
        ),
    )


def _crop_node(node_id, title, x, mask_name, metadata_name, cloud_name):
    bounds = PROJECTION_BOUNDS
    return node(
        node_id,
        OP_POINT_CLOUD_CROP,
        x,
        760,
        title,
        make_properties(
            params={
                "cropMode": "mask",
                "maskWorldMinX": bounds["minX"],
                "maskWorldMaxX": bounds["maxX"],
                "maskWorldMinY": bounds["minY"],
                "maskWorldMaxY": bounds["maxY"],
            },
            param_sources={
                "cropMode": "literal",
                "maskWorldMinX": "literal",
                "maskWorldMaxX": "literal",
                "maskWorldMinY": "literal",
                "maskWorldMaxY": "literal",
            },
            input_bindings={
                "input_point_cloud": "filtered_cloud",
                "roi_mask": mask_name,
                "roi_metadata": metadata_name,
            },
            input_sources={
                "input_point_cloud": "variable",
                "roi_mask": "variable",
                "roi_metadata": "variable",
            },
            output_bindings={"output_point_cloud": cloud_name},
            output_sources={"output_point_cloud": "variable"},
        ),
    )


def build_3d_point_cloud_processing_graph(point_cloud_path, relaxed_demo=False):
    id_start = new_uuid()
    id_read = new_uuid()
    id_downsample = new_uuid()
    id_denoise = new_uuid()
    id_colorize = new_uuid()
    id_normal = new_uuid()
    id_plane_fit = new_uuid()
    id_z_stats = new_uuid()
    id_plane_distance = new_uuid()
    id_plane_area = new_uuid()
    id_line_fit = new_uuid()
    id_circle_fit = new_uuid()
    id_preview = new_uuid()
    id_plane_roi, id_line_roi, id_circle_roi = new_uuid(), new_uuid(), new_uuid()
    id_plane_crop, id_line_crop, id_circle_crop = new_uuid(), new_uuid(), new_uuid()
    id_plane_overlay, id_line_overlay, id_circle_overlay = (
        new_uuid(),
        new_uuid(),
        new_uuid(),
    )
    id_result = new_uuid()
    id_export_cloud = new_uuid()
    id_export_image = new_uuid()
    id_export_distance_image = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_read,
            OP_READ_POINT_CLOUD,
            560,
            120,
            "读取点云",
            make_properties(
                input_bindings={"point_cloud_path": point_cloud_path},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "raw_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_downsample,
            OP_VOXEL_DOWNSAMPLE,
            560,
            200,
            "体素下采样",
            make_properties(
                params={"voxelSize": 0.2},
                param_sources={"voxelSize": "literal"},
                input_bindings={"input_point_cloud": "raw_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "downsampled_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_denoise,
            OP_STATISTICAL_OUTLIER_REMOVAL,
            560,
            280,
            "统计滤波去噪",
            make_properties(
                params={"k": 12, "stddevMultiplier": 3.0},
                param_sources={"k": "literal", "stddevMultiplier": "literal"},
                input_bindings={"input_point_cloud": "downsampled_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "filtered_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_colorize,
            OP_Z_COLORIZE_POINT_CLOUD,
            280,
            360,
            "Z轴着色",
            make_properties(
                input_bindings={"input_point_cloud": "filtered_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "colored_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_normal,
            OP_COMPUTE_NORMALS,
            560,
            360,
            "法向量计算",
            make_properties(
                params={"knnCount": 20, "consistentOrientation": "true"},
                param_sources={
                    "knnCount": "literal",
                    "consistentOrientation": "literal",
                },
                input_bindings={"input_point_cloud": "filtered_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"normal_cloud": "normal_cloud"},
                output_sources={"normal_cloud": "variable"},
            ),
        ),
        node(
            id_plane_fit,
            OP_RANSAC_PLANE_FIT,
            280,
            440,
            "RANSAC平面拟合",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1000,
                    "probability": 0.99,
                    "refinePlane": "true",
                    "normalConstraint": "z-up",
                    "seed": 0,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                    "refinePlane": "literal",
                    "normalConstraint": "literal",
                    "seed": "literal",
                },
                input_bindings={"input_point_cloud": "plane_roi_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "plane_params": "plane_params",
                    "inlier_points": "inlier_cloud",
                },
                output_sources={
                    "plane_params": "variable",
                    "inlier_points": "variable",
                },
            ),
        ),
        node(
            id_z_stats,
            OP_Z_CHANNEL_STATS,
            560,
            440,
            "Z轴高度统计",
            make_properties(
                input_bindings={"input_point_cloud": "filtered_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "centroid_z": "centroid_z",
                    "z_stats_json": "z_stats_result",
                },
                output_sources={"centroid_z": "variable", "z_stats_json": "variable"},
            ),
        ),
        node(
            id_plane_distance,
            OP_POINT_TO_PLANE_DISTANCE,
            280,
            520,
            "平面度检测",
            make_properties(
                params={
                    "distanceThreshold": 100.0 if relaxed_demo else 0.1,
                    "imageResolution": IMAGE_RESOLUTION,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "imageResolution": "literal",
                },
                input_bindings={
                    "input_point_cloud": "plane_roi_cloud",
                    "plane_params": "plane_params",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "plane_params": "variable",
                },
                output_bindings={
                    "distance_mat": "distance_mat",
                    "distance_image": "distance_image",
                    "result": "flatness_result",
                },
                output_sources={
                    "distance_mat": "variable",
                    "distance_image": "variable",
                    "result": "variable",
                },
            ),
        ),
        node(
            id_line_fit,
            OP_LINE_FIT_3D,
            560,
            520,
            "3D直线拟合（角度检测）",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1000,
                    "probability": 0.99,
                    "referenceAxis": "x",
                    "minAngle": 0.0,
                    "maxAngle": 180.0 if relaxed_demo else 5.0,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                    "referenceAxis": "literal",
                    "minAngle": "literal",
                    "maxAngle": "literal",
                },
                input_bindings={"input_point_cloud": "line_roi_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "line_params": "line_params",
                    "inlier_points": "line_inlier_cloud",
                    "outlier_points": "line_outlier_cloud",
                    "fitting_error": "line_fitting_error",
                    "result": "line_result",
                },
                output_sources={
                    "line_params": "variable",
                    "inlier_points": "variable",
                    "outlier_points": "variable",
                    "fitting_error": "variable",
                    "result": "variable",
                },
            ),
        ),
        node(
            id_circle_fit,
            OP_CIRCLE_FIT_3D,
            840,
            520,
            "3D圆拟合（形状检测）",
            make_properties(
                params={
                    "distanceThreshold": 0.01,
                    "maxIterations": 1000,
                    "probability": 0.99,
                    "minRadius": 0.0 if relaxed_demo else 1.0,
                    "maxRadius": 1000000.0 if relaxed_demo else 100.0,
                },
                param_sources={
                    "distanceThreshold": "literal",
                    "maxIterations": "literal",
                    "probability": "literal",
                    "minRadius": "literal",
                    "maxRadius": "literal",
                },
                input_bindings={"input_point_cloud": "circle_roi_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "circle_params": "circle_params",
                    "inlier_points": "circle_inlier_cloud",
                    "outlier_points": "circle_outlier_cloud",
                    "fitting_error": "circle_fitting_error",
                    "result": "circle_result",
                },
                output_sources={
                    "circle_params": "variable",
                    "inlier_points": "variable",
                    "outlier_points": "variable",
                    "fitting_error": "variable",
                    "result": "variable",
                },
            ),
        ),
        node(
            id_preview,
            OP_COLORED_POINT_CLOUD_TO_IMAGE,
            560,
            600,
            "点云投影成图",
            make_properties(
                params={"autoBounds": "true", "imageResolution": IMAGE_RESOLUTION},
                param_sources={"autoBounds": "literal", "imageResolution": "literal"},
                input_bindings={"input_point_cloud": "colored_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "output_image": "preview_image",
                    "projection_mapping": "projection_mapping",
                },
                output_sources={
                    "output_image": "variable",
                    "projection_mapping": "variable",
                },
            ),
        ),
        _roi_node(
            id_plane_roi,
            "平面度选区",
            240,
            _roi_json("平面度区域", 40, 80, 180, 180),
            "plane_roi_mask",
            "plane_roi_metadata",
        ),
        _roi_node(
            id_line_roi,
            "角度检测选区",
            560,
            _roi_json("直线区域", 210, 120, 120, 180),
            "line_roi_mask",
            "line_roi_metadata",
        ),
        _roi_node(
            id_circle_roi,
            "圆形检测选区",
            880,
            _roi_json("圆形区域", 350, 100, 120, 120),
            "circle_roi_mask",
            "circle_roi_metadata",
        ),
        _crop_node(
            id_plane_crop,
            "裁剪平面度区域",
            240,
            "plane_roi_mask",
            "plane_roi_metadata",
            "plane_roi_cloud",
        ),
        _crop_node(
            id_line_crop,
            "裁剪角度检测区域",
            560,
            "line_roi_mask",
            "line_roi_metadata",
            "line_roi_cloud",
        ),
        _crop_node(
            id_circle_crop,
            "裁剪圆形检测区域",
            880,
            "circle_roi_mask",
            "circle_roi_metadata",
            "circle_roi_cloud",
        ),
        node(
            id_plane_overlay,
            OP_OVERLAY_ROI_MARKERS,
            240,
            840,
            "叠加平面度选区",
            make_properties(
                params={
                    "overlayAlpha": 0.2,
                    "borderThickness": 1,
                    "showLabel": "true",
                },
                param_sources={
                    "overlayAlpha": "literal",
                    "borderThickness": "literal",
                    "showLabel": "literal",
                },
                input_bindings={
                    "input_mat": "preview_image",
                    "roi_metadata": "plane_roi_metadata",
                },
                input_sources={
                    "input_mat": "variable",
                    "roi_metadata": "variable",
                },
                output_bindings={"output_mat": "plane_overlay_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_plane_area,
            OP_PLANE_AREA_MEASURE,
            80,
            520,
            "平面面积测量",
            make_properties(
                input_bindings={
                    "plane_points": "inlier_cloud",
                    "plane_params": "plane_params",
                    "contour_json": _plane_contour_json(40, 80, 180, 180),
                },
                input_sources={
                    "plane_points": "variable",
                    "plane_params": "variable",
                    "contour_json": "literal",
                },
                output_bindings={
                    "output_point_cloud": "area_annotated_cloud",
                    "measurement_json": "plane_area_result",
                    "area": "plane_area",
                },
                output_sources={
                    "output_point_cloud": "variable",
                    "measurement_json": "variable",
                    "area": "variable",
                },
            ),
        ),
        node(
            id_line_overlay,
            OP_OVERLAY_ROI_MARKERS,
            560,
            840,
            "叠加角度检测选区",
            make_properties(
                params={
                    "overlayAlpha": 0.2,
                    "borderThickness": 1,
                    "showLabel": "true",
                },
                param_sources={
                    "overlayAlpha": "literal",
                    "borderThickness": "literal",
                    "showLabel": "literal",
                },
                input_bindings={
                    "input_mat": "plane_overlay_image",
                    "roi_metadata": "line_roi_metadata",
                },
                input_sources={
                    "input_mat": "variable",
                    "roi_metadata": "variable",
                },
                output_bindings={"output_mat": "line_overlay_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_circle_overlay,
            OP_OVERLAY_ROI_MARKERS,
            880,
            840,
            "叠加圆形检测选区",
            make_properties(
                params={
                    "overlayAlpha": 0.2,
                    "borderThickness": 1,
                    "showLabel": "true",
                },
                param_sources={
                    "overlayAlpha": "literal",
                    "borderThickness": "literal",
                    "showLabel": "literal",
                },
                input_bindings={
                    "input_mat": "line_overlay_image",
                    "roi_metadata": "circle_roi_metadata",
                },
                input_sources={
                    "input_mat": "variable",
                    "roi_metadata": "variable",
                },
                output_bindings={"output_mat": "roi_overlay_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_result,
            OP_COMBINE_INSPECTION_RESULTS,
            560,
            840,
            "检测结果汇总",
            make_properties(
                input_bindings={
                    "flatness_result": "flatness_result",
                    "line_result": "line_result",
                    "circle_result": "circle_result",
                },
                input_sources={
                    "flatness_result": "variable",
                    "line_result": "variable",
                    "circle_result": "variable",
                },
                output_bindings={"result": "inspection_result"},
                output_sources={"result": "variable"},
            ),
        ),
        node(
            id_export_cloud,
            OP_SAVE_POINT_CLOUD_BLOB,
            280,
            700,
            "点云存Blob",
            make_properties(
                params={"fileName": "processed-pointcloud.ply"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_point_cloud": "colored_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"download_url": "cloud_download_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_export_image,
            OP_SAVE_IMAGE_BLOB,
            560,
            700,
            "投影图存Blob",
            make_properties(
                params={"fileName": "pointcloud-projection.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "roi_overlay_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "blob_name": "image_blob_name",
                    "download_url": "image_download_url",
                },
                output_sources={
                    "blob_name": "variable",
                    "download_url": "variable",
                },
            ),
        ),
        node(
            id_export_distance_image,
            OP_SAVE_IMAGE_BLOB,
            840,
            700,
            "平面度热力图",
            make_properties(
                params={"fileName": "flatness-heatmap.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "distance_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "blob_name": "flatness_heatmap_blob_name",
                    "download_url": "flatness_heatmap_url",
                },
                output_sources={
                    "blob_name": "variable",
                    "download_url": "variable",
                },
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            800,
            "结束",
            make_properties(
                input_bindings={"inspectionResult": "inspection_result"},
                input_sources={"inspectionResult": "variable"},
            ),
        ),
    ]

    edges = [
        edge(id_start, id_read),
        edge(id_read, id_downsample),
        edge(id_downsample, id_denoise),
        edge(id_denoise, id_colorize),
        edge(id_denoise, id_normal),
        edge(id_denoise, id_z_stats),
        edge(id_preview, id_plane_roi),
        edge(id_preview, id_line_roi),
        edge(id_preview, id_circle_roi),
        edge(id_plane_roi, id_plane_crop),
        edge(id_line_roi, id_line_crop),
        edge(id_circle_roi, id_circle_crop),
        edge(id_preview, id_plane_overlay),
        edge(id_plane_roi, id_plane_overlay),
        edge(id_plane_overlay, id_line_overlay),
        edge(id_line_roi, id_line_overlay),
        edge(id_line_overlay, id_circle_overlay),
        edge(id_circle_roi, id_circle_overlay),
        edge(id_plane_crop, id_plane_fit),
        edge(id_line_crop, id_line_fit),
        edge(id_circle_crop, id_circle_fit),
        edge(id_plane_fit, id_plane_distance),
        edge(id_plane_fit, id_plane_area),
        edge(id_colorize, id_preview),
        edge(id_preview, id_export_cloud),
        edge(id_circle_overlay, id_export_image),
        edge(id_plane_distance, id_export_distance_image),
        edge(id_plane_distance, id_result),
        edge(id_line_fit, id_result),
        edge(id_circle_fit, id_result),
        edge(id_result, id_end),
        edge(id_export_cloud, id_end),
        edge(id_export_image, id_end),
        edge(id_export_distance_image, id_end),
        edge(id_plane_area, id_end),
    ]

    return {"nodes": nodes, "edges": edges}


def build_split_3d_point_cloud_graphs(point_cloud_path):
    """从综合示例提取按功能拆分的清晰工作流。"""
    full_graph = build_3d_point_cloud_processing_graph(point_cloud_path)

    def focused_graph(
        titles,
        end_bindings,
        terminal_titles,
        input_overrides=None,
        extra_edges=None,
    ):
        graph = deepcopy(full_graph)
        title_to_node = {
            node_data["text"]["value"]: node_data for node_data in graph["nodes"]
        }
        selected_titles = {"开始", "结束", *titles}
        selected_nodes = [
            node_data
            for node_data in graph["nodes"]
            if node_data["text"]["value"] in selected_titles
        ]
        selected_ids = {node_data["id"] for node_data in selected_nodes}
        end_node = title_to_node["结束"]

        for title, bindings in (input_overrides or {}).items():
            properties = title_to_node[title]["properties"]
            properties["inputBindings"].update(bindings)
            properties["inputBindingSources"].update(
                {name: "variable" for name in bindings}
            )

        end_node["properties"] = make_properties(
            input_bindings=end_bindings,
            input_sources={name: "variable" for name in end_bindings},
        )

        selected_edges = [
            edge_data
            for edge_data in graph["edges"]
            if edge_data["sourceNodeId"] in selected_ids
            and edge_data["targetNodeId"] in selected_ids
            and edge_data["targetNodeId"] != end_node["id"]
        ]
        for terminal_title in terminal_titles:
            selected_edges.append(edge(title_to_node[terminal_title]["id"], end_node["id"]))
        for source_title, target_title in extra_edges or []:
            selected_edges.append(
                edge(title_to_node[source_title]["id"], title_to_node[target_title]["id"])
            )

        return {"nodes": selected_nodes, "edges": selected_edges}

    common = {
        "读取点云",
        "体素下采样",
        "统计滤波去噪",
        "Z轴着色",
        "点云投影成图",
    }

    preprocessing = focused_graph(
        common | {"Z轴高度统计", "点云存Blob", "投影图存Blob"},
        {
            "pointCloudUrl": "cloud_download_url",
            "imageBlobName": "image_blob_name",
            "imageUrl": "image_download_url",
            "centroidZ": "centroid_z",
            "zStats": "z_stats_result",
        },
        {"点云存Blob", "投影图存Blob", "Z轴高度统计"},
        {"投影图存Blob": {"input_mat": "preview_image"}},
        [("点云投影成图", "投影图存Blob")],
    )

    flatness = focused_graph(
        common
        | {
            "平面度选区",
            "裁剪平面度区域",
            "叠加平面度选区",
            "RANSAC平面拟合",
            "平面度检测",
            "投影图存Blob",
            "平面度热力图",
        },
        {
            "roiImageUrl": "image_download_url",
            "heatmapUrl": "flatness_heatmap_url",
            "result": "flatness_result",
        },
        {"投影图存Blob", "平面度热力图", "平面度检测"},
        {"投影图存Blob": {"input_mat": "plane_overlay_image"}},
        [("叠加平面度选区", "投影图存Blob")],
    )

    area = focused_graph(
        common
        | {
            "平面度选区",
            "裁剪平面度区域",
            "叠加平面度选区",
            "RANSAC平面拟合",
            "平面面积测量",
            "投影图存Blob",
        },
        {
            "planeArea": "plane_area",
        },
        {"投影图存Blob", "平面面积测量"},
        {"投影图存Blob": {"input_mat": "plane_overlay_image"}},
        [("叠加平面度选区", "投影图存Blob")],
    )

    line_angle = focused_graph(
        common
        | {
            "角度检测选区",
            "裁剪角度检测区域",
            "叠加角度检测选区",
            "3D直线拟合（角度检测）",
            "投影图存Blob",
        },
        {
            "roiImageUrl": "image_download_url",
            "result": "line_result",
        },
        {"投影图存Blob", "3D直线拟合（角度检测）"},
        {
            "叠加角度检测选区": {"input_mat": "preview_image"},
            "投影图存Blob": {"input_mat": "line_overlay_image"},
        },
        [
            ("点云投影成图", "叠加角度检测选区"),
            ("叠加角度检测选区", "投影图存Blob"),
        ],
    )

    circle = focused_graph(
        common
        | {
            "圆形检测选区",
            "裁剪圆形检测区域",
            "叠加圆形检测选区",
            "3D圆拟合（形状检测）",
            "投影图存Blob",
        },
        {
            "roiImageUrl": "image_download_url",
            "result": "circle_result",
        },
        {"投影图存Blob", "3D圆拟合（形状检测）"},
        {
            "叠加圆形检测选区": {"input_mat": "preview_image"},
            "投影图存Blob": {"input_mat": "roi_overlay_image"},
        },
        [
            ("点云投影成图", "叠加圆形检测选区"),
            ("叠加圆形检测选区", "投影图存Blob"),
        ],
    )

    return {
        "3D点云预处理与投影演示": preprocessing,
        "3D平面度检测演示": flatness,
        "3D平面面积测量演示": area,
        "3D直线角度检测演示": line_angle,
        "3D圆形检测演示": circle,
    }
