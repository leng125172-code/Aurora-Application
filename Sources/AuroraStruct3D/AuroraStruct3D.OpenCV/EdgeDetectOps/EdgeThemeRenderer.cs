using AuroraStruct3D.OpenCV.Workflow;

namespace AuroraStruct3D.OpenCV.EdgeDetectOps;

/// <summary>
/// 将单通道边缘强度图转换为适合前端叠加显示的透明 BGRA 图像。
/// </summary>
internal static class EdgeThemeRenderer
{
    public static Mat Render(Mat edgeMask)
    {
        if (edgeMask.Type() != MatType.CV_8UC1)
            throw new ArgumentException("边缘蒙版必须为 CV_8UC1。", nameof(edgeMask));

        byte color = WorkflowDisplayThemeAmbient.Current == WorkflowDisplayTheme.Dark
            ? (byte)255
            : (byte)0;

        using Mat colorChannel = new Mat(edgeMask.Size(), MatType.CV_8UC1, Scalar.All(color));
        Mat output = new Mat();
        Cv2.Merge(new[] { colorChannel, colorChannel, colorChannel, edgeMask }, output);
        return output;
    }
}
