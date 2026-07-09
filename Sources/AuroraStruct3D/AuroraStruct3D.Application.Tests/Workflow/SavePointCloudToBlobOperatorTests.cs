using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.Workflow;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class SavePointCloudToBlobOperatorTests
{
    [Fact]
    public void PortDefinitions_Should_Expose_PointCloud_And_Mat_Inputs()
    {
        List<IVisionParameter>? inputs = save_point_cloud_to_blob.InputVisionParameters;

        Assert.NotNull(inputs);
        Assert.Collection(
            inputs!,
            input =>
            {
                Assert.IsType<PointCloudData>(input);
                Assert.Equal("input_point_cloud", input.ParameterName);
            },
            input =>
            {
                Assert.IsType<MatImg>(input);
                Assert.Equal("input_mat", input.ParameterName);
            }
        );
    }

    [Fact]
    public void OutputDefinitions_Should_Expose_BlobName_And_DownloadUrl()
    {
        List<IVisionParameter>? outputs = save_point_cloud_to_blob.OutputVisionParameters;

        Assert.NotNull(outputs);
        Assert.Collection(
            outputs!,
            output =>
            {
                VisionParameter<string> port = Assert.IsType<VisionParameter<string>>(output);
                Assert.Equal("blob_name", port.ParameterName);
                Assert.Equal(PortControlType.Input, port.ControlType);
            },
            output =>
            {
                VisionParameter<string> port = Assert.IsType<VisionParameter<string>>(output);
                Assert.Equal("download_url", port.ParameterName);
                Assert.Equal(PortControlType.Download, port.ControlType);
            }
        );
    }

    [Fact]
    public void AmbientStore_Should_Restore_Previous_Instance_After_Dispose()
    {
        TestOperatorFileBlobStore first = new();
        TestOperatorFileBlobStore second = new();

        using IDisposable firstScope = OperatorFileBlobStoreAmbient.Push(first);
        Assert.Same(first, OperatorFileBlobStoreAmbient.Current);

        using (IDisposable secondScope = OperatorFileBlobStoreAmbient.Push(second))
        {
            Assert.Same(second, OperatorFileBlobStoreAmbient.Current);
        }

        Assert.Same(first, OperatorFileBlobStoreAmbient.Current);
    }

    private sealed class TestOperatorFileBlobStore : IOperatorFileBlobStore
    {
        public Task<string> SavePointCloudPlyAsync(
            string fileName,
            byte[] content,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(fileName);

        public string BuildDownloadUrl(string blobName) => blobName;
    }
}
