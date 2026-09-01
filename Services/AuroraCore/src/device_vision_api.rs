//! Local Device/Vision Host proxy exposed through the Core gRPC boundary.

use crate::contracts::device_gateway_client::DeviceGatewayClient;
use crate::contracts::device_gateway_server::DeviceGateway;
use crate::contracts::vision_gateway_client::VisionGatewayClient;
use crate::contracts::vision_gateway_server::VisionGateway;
use crate::contracts::{
    CaptureFrameRequest, DeviceDescriptor, ExecuteVisionOperatorRequest, FrameDescriptor,
    GetDeviceRequest, ListDevicesReply, ListDevicesRequest, ListVisionOperatorsReply,
    ListVisionOperatorsRequest, VisionOperatorReply,
};
use tonic::transport::Channel;
use tonic::{Request, Response, Status};

/// Read-only device discovery and camera-frame proxy.
#[derive(Clone)]
pub struct DeviceGatewayProxy {
    client: DeviceGatewayClient<Channel>,
}

impl DeviceGatewayProxy {
    /// Creates a proxy using a lazy local Device/Vision Host channel.
    pub fn new(channel: Channel) -> Self {
        Self {
            client: DeviceGatewayClient::new(channel),
        }
    }
}

#[tonic::async_trait]
impl DeviceGateway for DeviceGatewayProxy {
    async fn list_devices(
        &self,
        request: Request<ListDevicesRequest>,
    ) -> Result<Response<ListDevicesReply>, Status> {
        self.client.clone().list_devices(request).await
    }

    async fn get_device(
        &self,
        request: Request<GetDeviceRequest>,
    ) -> Result<Response<DeviceDescriptor>, Status> {
        self.client.clone().get_device(request).await
    }

    async fn capture_frame(
        &self,
        request: Request<CaptureFrameRequest>,
    ) -> Result<Response<FrameDescriptor>, Status> {
        self.client.clone().capture_frame(request).await
    }
}

/// Vision-operator catalog and execution proxy.
#[derive(Clone)]
pub struct VisionGatewayProxy {
    client: VisionGatewayClient<Channel>,
}

impl VisionGatewayProxy {
    /// Creates a proxy using a lazy local Device/Vision Host channel.
    pub fn new(channel: Channel) -> Self {
        Self {
            client: VisionGatewayClient::new(channel),
        }
    }
}

#[tonic::async_trait]
impl VisionGateway for VisionGatewayProxy {
    async fn list_operators(
        &self,
        request: Request<ListVisionOperatorsRequest>,
    ) -> Result<Response<ListVisionOperatorsReply>, Status> {
        self.client.clone().list_operators(request).await
    }

    async fn execute_operator(
        &self,
        request: Request<ExecuteVisionOperatorRequest>,
    ) -> Result<Response<VisionOperatorReply>, Status> {
        self.client.clone().execute_operator(request).await
    }
}
