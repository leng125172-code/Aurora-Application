namespace AuroraStruct3D.OpenCV.File.PointCloud;

[Guid("ec67f5da-e934-4e0e-9eb5-46e7f8e02101")]
[Category("数据读取")]
[DisplayName("读取工艺模型")]
[Description("从三维数模库选择已就绪的 OBJ/PLY 工艺模型并读取为参考点云。")]
public sealed class read_product_model : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters => [];

    public static List<IVisionParameter>? OutputVisionParameters =>
        [
            new PointCloudData
            {
                ParameterName = "model_point_cloud",
                DisplayName = "工艺模型点云",
            },
            new VisionParameter<string>
            {
                ParameterName = "model_id",
                DisplayName = "模型ID",
                ParameterType = typeof(string),
            },
        ];

    public static List<IConfigParameter>? ConfigParameters =>
        [
            new ConfigParameter
            {
                Name = "productModelId",
                DisplayName = "工艺模型",
                ParameterType = typeof(string),
                Required = true,
                ControlType = PortControlType.ProductModelSelect,
            },
        ];

    private readonly Guid _productModelId;
    private bool _disposed;

    public read_product_model(string productModelId)
    {
        if (!Guid.TryParse(productModelId, out _productModelId) || _productModelId == Guid.Empty)
            throw new ArgumentException("必须选择有效的工艺模型。", nameof(productModelId));
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string localPath = ProductModelStoreAmbient
            .Current.MaterializeAsync(_productModelId)
            .GetAwaiter()
            .GetResult();

        using var nestedContext = new WorkflowContext();
        nestedContext.Set("point_cloud_path", localPath);
        using var reader = new read_point_cloud();
        reader.Execute(nestedContext);
        PointCloudData model =
            nestedContext.Get<PointCloudData>("output_point_cloud")
            ?? throw new InvalidOperationException("工艺模型读取失败。");
        Mat coordinates =
            model.PointCloud?.Clone()
            ?? throw new InvalidOperationException("工艺模型点云坐标为空。");
        var detachedModel = new PointCloudData { Value = coordinates };
        try
        {
            if (model.HasColors && model.Colors is not null)
                detachedModel.SetColors(model.Colors.Clone());
        }
        catch
        {
            detachedModel.DisposePointCloud();
            throw;
        }

        // nestedContext 在本方法结束时会释放其点云，因此必须向外层传递独立副本。
        context.Set("model_point_cloud", detachedModel);
        context.Set("model_id", _productModelId.ToString("D"));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
