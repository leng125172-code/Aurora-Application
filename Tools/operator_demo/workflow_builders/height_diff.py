import json

from utils import node, edge, make_properties, new_uuid
from constants import (
    OP_READ_POINT_CLOUD,
    OP_VOXEL_DOWNSAMPLE,
    OP_STATISTICAL_OUTLIER_REMOVAL,
    OP_Z_COLORIZE_POINT_CLOUD,
    OP_SAVE_POINT_CLOUD_BLOB,
    OP_COLORED_POINT_CLOUD_TO_IMAGE,
    OP_RANSAC_PLANE_FIT,
    OP_Z_CHANNEL_STATS,
    OP_HEIGHT_DIFF_EVAL,
    OP_ANNOTATE_HEIGHT_DIFF,
    OP_SAVE_IMAGE_BLOB,
    OP_ROI_PARTITION,
    OP_OVERLAY_ROI_MARKERS,
    OP_POINT_CLOUD_CROP,
    IMAGE_RESOLUTION,
    PROJECTION_BOUNDS,
)


def _roi_json(name, x, y, width=140, height=120):
    """创建可在工作流 ROI 编辑器中继续调整的默认矩形选区。"""
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


def _mask_crop_properties(mask_variable, metadata_variable, output_variable):
    bounds = PROJECTION_BOUNDS
    return make_properties(
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
            "roi_mask": mask_variable,
            "roi_metadata": metadata_variable,
        },
        input_sources={
            "input_point_cloud": "variable",
            "roi_mask": "variable",
            "roi_metadata": "variable",
        },
        output_bindings={"output_point_cloud": output_variable},
        output_sources={"output_point_cloud": "variable"},
    )


def build_height_diff_graph(point_cloud_path, extended_annotation=True):
    id_start = new_uuid()
    id_read = new_uuid()
    id_downsample = new_uuid()
    id_denoise = new_uuid()
    id_colorize = new_uuid()
    id_export_cloud = new_uuid()
    id_preview = new_uuid()
    id_reference_roi = new_uuid()
    id_measure_roi = new_uuid()
    id_reference_crop = new_uuid()
    id_measure_crop = new_uuid()
    id_plane_fit = new_uuid()
    id_reference_stats = new_uuid()
    id_z_stats = new_uuid()
    id_height_diff = new_uuid()
    id_reference_overlay = new_uuid()
    id_measure_overlay = new_uuid()
    id_annotate = new_uuid()
    id_export_image = new_uuid()
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
            560,
            360,
            "Z轴着色点云",
            make_properties(
                input_bindings={"input_point_cloud": "filtered_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "colored_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_export_cloud,
            OP_SAVE_POINT_CLOUD_BLOB,
            280,
            440,
            "彩色点云存Blob",
            make_properties(
                params={"fileName": "height-diff-cloud.ply"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_point_cloud": "colored_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"download_url": "cloud_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_preview,
            OP_COLORED_POINT_CLOUD_TO_IMAGE,
            560,
            440,
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
        node(
            id_reference_roi,
            OP_ROI_PARTITION,
            360,
            520,
            "基准面选区",
            make_properties(
                # OpenCV Hershey fonts only support ASCII. Keep the persisted ROI
                # label ASCII so result annotations never render as question marks.
                params={"roiJson": _roi_json("REFERENCE", 70, 120)},
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
                    "primary_mask": "reference_roi_mask",
                    "roi_metadata": "reference_roi_metadata",
                },
                output_sources={
                    "primary_mask": "variable",
                    "roi_metadata": "variable",
                },
            ),
        ),
        node(
            id_measure_roi,
            OP_ROI_PARTITION,
            760,
            520,
            "测量区域选区",
            make_properties(
                params={"roiJson": _roi_json("MEASURE", 300, 120)},
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
                    "primary_mask": "measure_roi_mask",
                    "roi_metadata": "measure_roi_metadata",
                },
                output_sources={
                    "primary_mask": "variable",
                    "roi_metadata": "variable",
                },
            ),
        ),
        node(
            id_reference_crop,
            OP_POINT_CLOUD_CROP,
            360,
            600,
            "裁剪基准面",
            _mask_crop_properties(
                "reference_roi_mask", "reference_roi_metadata", "reference_cloud"
            ),
        ),
        node(
            id_measure_crop,
            OP_POINT_CLOUD_CROP,
            760,
            600,
            "裁剪测量区域",
            _mask_crop_properties(
                "measure_roi_mask", "measure_roi_metadata", "measure_cloud"
            ),
        ),
        node(
            id_reference_stats,
            OP_Z_CHANNEL_STATS,
            360,
            680,
            "基准面高度统计",
            make_properties(
                input_bindings={"input_point_cloud": "reference_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "centroid_z": "reference_height",
                    "z_stats_json": "reference_z_stats",
                },
                output_sources={
                    "centroid_z": "variable",
                    "z_stats_json": "variable",
                },
            ),
        ),
        node(
            id_plane_fit,
            OP_RANSAC_PLANE_FIT,
            560,
            520,
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
                input_bindings={"input_point_cloud": "reference_cloud"},
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
            840,
            520,
            "Z轴高度统计",
            make_properties(
                input_bindings={"input_point_cloud": "measure_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={
                    "centroid_z": "measure_height",
                    "z_stats_json": "measure_z_stats",
                },
                output_sources={"centroid_z": "variable", "z_stats_json": "variable"},
            ),
        ),
        node(
            id_height_diff,
            OP_HEIGHT_DIFF_EVAL,
            560,
            600,
            "高度差评估",
            make_properties(
                params={"minDiff": -0.2, "maxDiff": 0.2},
                param_sources={"minDiff": "literal", "maxDiff": "literal"},
                input_bindings={
                    "height_a": "measure_height",
                    "height_b": "reference_height",
                },
                input_sources={
                    "height_a": "variable",
                    "height_b": "variable",
                },
                output_bindings={
                    "result": "height_diff_result",
                },
                output_sources={
                    "result": "variable",
                },
            ),
        ),
        node(
            id_reference_overlay,
            OP_OVERLAY_ROI_MARKERS,
            440,
            680,
            "叠加基准面选区",
            make_properties(
                params={
                    "overlayAlpha": 0.25,
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
                    "roi_metadata": "reference_roi_metadata",
                },
                input_sources={
                    "input_mat": "variable",
                    "roi_metadata": "variable",
                },
                output_bindings={"output_mat": "reference_overlay_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_measure_overlay,
            OP_OVERLAY_ROI_MARKERS,
            680,
            680,
            "叠加测量区域",
            make_properties(
                params={
                    "overlayAlpha": 0.25,
                    "borderThickness": 1,
                    "showLabel": "true",
                },
                param_sources={
                    "overlayAlpha": "literal",
                    "borderThickness": "literal",
                    "showLabel": "literal",
                },
                input_bindings={
                    "input_mat": "reference_overlay_image",
                    "roi_metadata": "measure_roi_metadata",
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
            id_annotate,
            OP_ANNOTATE_HEIGHT_DIFF,
            560,
            760,
            "结果标注",
            make_properties(
                input_bindings={
                    "input_mat": "roi_overlay_image",
                    "inspection_result": "height_diff_result",
                    "roi_metadata_a": "measure_roi_metadata",
                    "roi_metadata_b": "reference_roi_metadata",
                },
                input_sources={
                    "input_mat": "variable",
                    "inspection_result": "variable",
                    "roi_metadata_a": "variable",
                    "roi_metadata_b": "variable",
                },
                output_bindings={"output_mat": "annotated_image"},
                output_sources={"output_mat": "variable"},
            ),
        ),
        node(
            id_export_image,
            OP_SAVE_IMAGE_BLOB,
            560,
            840,
            "结果图存Blob",
            make_properties(
                params={"fileName": "height-diff-result.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "annotated_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={
                    "blob_name": "result_image_blob_name",
                    "download_url": "result_image_url",
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
            940,
            "结束",
            make_properties(
                input_bindings={
                    "cloudUrl": "cloud_url",
                    "resultImageBlobName": "result_image_blob_name",
                    "heightDiffResult": "height_diff_result",
                },
                input_sources={
                    "cloudUrl": "variable",
                    "resultImageBlobName": "variable",
                    "heightDiffResult": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_read),
        edge(id_read, id_downsample),
        edge(id_downsample, id_denoise),
        edge(id_denoise, id_colorize),
        edge(id_colorize, id_export_cloud),
        edge(id_colorize, id_preview),
        edge(id_preview, id_reference_roi),
        edge(id_preview, id_measure_roi),
        edge(id_reference_roi, id_reference_crop),
        edge(id_measure_roi, id_measure_crop),
        edge(id_reference_crop, id_plane_fit),
        edge(id_reference_crop, id_reference_stats),
        edge(id_measure_crop, id_z_stats),
        edge(id_measure_crop, id_height_diff),
        edge(id_reference_stats, id_height_diff),
        edge(id_z_stats, id_height_diff),
        edge(id_plane_fit, id_height_diff),
        edge(id_preview, id_reference_overlay),
        edge(id_reference_roi, id_reference_overlay),
        edge(id_reference_overlay, id_measure_overlay),
        edge(id_measure_roi, id_measure_overlay),
        edge(id_measure_overlay, id_annotate),
        edge(id_height_diff, id_annotate),
        edge(id_annotate, id_export_image),
        edge(id_export_cloud, id_end),
        edge(id_export_image, id_end),
    ]

    return {"nodes": nodes, "edges": edges}
