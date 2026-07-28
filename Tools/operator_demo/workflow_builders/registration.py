from utils import node, edge, make_properties, new_uuid
from constants import (
    OP_READ_POINT_CLOUD,
    OP_VOXEL_DOWNSAMPLE,
    OP_ICP_REGISTRATION,
    OP_APPLY_TRANSFORM,
    OP_Z_COLORIZE_POINT_CLOUD,
    OP_COLORED_POINT_CLOUD_TO_IMAGE,
    OP_SAVE_POINT_CLOUD_BLOB,
    OP_SAVE_IMAGE_BLOB,
    IMAGE_RESOLUTION,
)


def build_3d_registration_graph(reference_point_cloud_path, target_point_cloud_path):
    id_start = new_uuid()
    id_read = new_uuid()
    id_read_target = new_uuid()
    id_downsample1 = new_uuid()
    id_downsample2 = new_uuid()
    id_icp = new_uuid()
    id_apply_transform = new_uuid()
    id_colorize_aligned = new_uuid()
    id_preview_aligned = new_uuid()
    id_export_cloud = new_uuid()
    id_export_image = new_uuid()
    id_end = new_uuid()

    nodes = [
        node(id_start, "start-node", 560, 40, "开始", make_properties()),
        node(
            id_read,
            OP_READ_POINT_CLOUD,
            560,
            120,
            "读取参考点云",
            make_properties(
                input_bindings={"point_cloud_path": reference_point_cloud_path},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "reference_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_read_target,
            OP_READ_POINT_CLOUD,
            740,
            120,
            "读取目标点云",
            make_properties(
                input_bindings={"point_cloud_path": target_point_cloud_path},
                input_sources={"point_cloud_path": "literal"},
                output_bindings={"output_point_cloud": "target_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_downsample1,
            OP_VOXEL_DOWNSAMPLE,
            380,
            200,
            "参考点云下采样",
            make_properties(
                params={"voxelSize": 0.2},
                param_sources={"voxelSize": "literal"},
                input_bindings={"input_point_cloud": "reference_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "reference_downsampled"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_downsample2,
            OP_VOXEL_DOWNSAMPLE,
            740,
            200,
            "目标点云下采样",
            make_properties(
                params={"voxelSize": 0.2},
                param_sources={"voxelSize": "literal"},
                input_bindings={"input_point_cloud": "target_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "target_downsampled"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_icp,
            OP_ICP_REGISTRATION,
            560,
            300,
            "ICP配准",
            make_properties(
                params={"maxCorrespondenceDistance": 0.1, "maxIterations": 50},
                param_sources={
                    "maxCorrespondenceDistance": "literal",
                    "maxIterations": "literal",
                },
                input_bindings={
                    "source_cloud": "target_downsampled",
                    "target_cloud": "reference_downsampled",
                },
                input_sources={"source_cloud": "variable", "target_cloud": "variable"},
                output_bindings={
                    "transform_matrix": "transform_matrix",
                    "aligned_cloud": "aligned_cloud",
                    "stats_json": "registration_result",
                },
                output_sources={
                    "transform_matrix": "variable",
                    "aligned_cloud": "variable",
                    "stats_json": "variable",
                },
            ),
        ),
        node(
            id_apply_transform,
            OP_APPLY_TRANSFORM,
            560,
            400,
            "应用变换",
            make_properties(
                input_bindings={
                    "input_point_cloud": "target_downsampled",
                    "transform_matrix": "transform_matrix",
                },
                input_sources={
                    "input_point_cloud": "variable",
                    "transform_matrix": "variable",
                },
                output_bindings={"output_point_cloud": "transformed_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_colorize_aligned,
            OP_Z_COLORIZE_POINT_CLOUD,
            560,
            460,
            "配准点云Z轴着色",
            make_properties(
                params={"colorMap": "jet", "autoRange": "true"},
                param_sources={"colorMap": "literal", "autoRange": "literal"},
                input_bindings={"input_point_cloud": "transformed_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_point_cloud": "aligned_colored_cloud"},
                output_sources={"output_point_cloud": "variable"},
            ),
        ),
        node(
            id_preview_aligned,
            OP_COLORED_POINT_CLOUD_TO_IMAGE,
            560,
            480,
            "配准点云可视化",
            make_properties(
                params={"autoBounds": "true", "imageResolution": IMAGE_RESOLUTION},
                param_sources={"autoBounds": "literal", "imageResolution": "literal"},
                input_bindings={"input_point_cloud": "aligned_colored_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"output_image": "aligned_image"},
                output_sources={"output_image": "variable"},
            ),
        ),
        node(
            id_export_cloud,
            OP_SAVE_POINT_CLOUD_BLOB,
            380,
            560,
            "配准点云存Blob",
            make_properties(
                params={"fileName": "registered-pointcloud.ply"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_point_cloud": "transformed_cloud"},
                input_sources={"input_point_cloud": "variable"},
                output_bindings={"download_url": "aligned_cloud_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_export_image,
            OP_SAVE_IMAGE_BLOB,
            740,
            560,
            "配准图存Blob",
            make_properties(
                params={"fileName": "registered-visualization.png"},
                param_sources={"fileName": "literal"},
                input_bindings={"input_mat": "aligned_image"},
                input_sources={"input_mat": "variable"},
                output_bindings={"download_url": "aligned_image_url"},
                output_sources={"download_url": "variable"},
            ),
        ),
        node(
            id_end,
            "end-node",
            560,
            660,
            "结束",
            make_properties(
                input_bindings={
                    "alignedCloudUrl": "aligned_cloud_url",
                    "alignedImageUrl": "aligned_image_url",
                    "registrationResult": "registration_result",
                },
                input_sources={
                    "alignedCloudUrl": "variable",
                    "alignedImageUrl": "variable",
                    "registrationResult": "variable",
                },
            ),
        ),
    ]

    edges = [
        edge(id_start, id_read),
        edge(id_read, id_downsample1),
        edge(id_start, id_read_target),
        edge(id_read_target, id_downsample2),
        edge(id_downsample1, id_icp),
        edge(id_downsample2, id_icp),
        edge(id_icp, id_apply_transform),
        edge(id_apply_transform, id_colorize_aligned),
        edge(id_colorize_aligned, id_preview_aligned),
        edge(id_preview_aligned, id_export_cloud),
        edge(id_preview_aligned, id_export_image),
        edge(id_export_cloud, id_end),
        edge(id_export_image, id_end),
    ]

    return {"nodes": nodes, "edges": edges}
