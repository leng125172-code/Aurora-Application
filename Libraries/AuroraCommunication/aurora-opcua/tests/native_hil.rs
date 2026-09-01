//! Opt-in hardware/server integration test for the native open62541 backend.

#![cfg(feature = "native")]

use aurora_comm_core::{DeviceClient, DeviceDataType, DeviceValue, ReadRequest, WriteRequest};
use aurora_opcua::{OpcUaClient, OpcUaOptions, Open62541Backend};
use std::{env, sync::Arc, time::Duration};

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
#[ignore = "requires AURORA_OPCUA_ENDPOINT/PARENT_NODE/SCALAR_NODE and a writable server"]
async fn native_browse_read_write_and_monitored_item_round_trip() {
    let endpoint = required("AURORA_OPCUA_ENDPOINT");
    let parent = required("AURORA_OPCUA_PARENT_NODE");
    let scalar_node = required("AURORA_OPCUA_SCALAR_NODE");
    let client = OpcUaClient::new(
        OpcUaOptions::anonymous(endpoint),
        Arc::new(Open62541Backend::default()),
    )
    .expect("HIL options must be valid");

    client.connect().await.expect("native session must connect");
    let children = client.browse(&parent).await.expect("Browse must succeed");
    assert!(
        children.iter().any(|node| node.node_id == scalar_node),
        "configured scalar node was not returned below its parent"
    );

    let request = ReadRequest {
        key: "hil.scalar".to_owned(),
        address: scalar_node.clone(),
        data_type: DeviceDataType::Float64,
        count: 1,
        timeout: Duration::from_secs(5),
    };
    let original = client.read(&request).await.expect("Read must succeed");
    let changed = changed_value(&original);
    let mut updates = client.subscribe();
    client
        .monitor(&scalar_node, Duration::from_millis(100))
        .await
        .expect("MonitoredItem creation must succeed");
    client
        .write(&WriteRequest {
            key: "hil.scalar".to_owned(),
            address: scalar_node.clone(),
            value: changed.clone(),
            timeout: Duration::from_secs(5),
        })
        .await
        .expect("Write must succeed");

    let (_, notified) = tokio::time::timeout(Duration::from_secs(10), updates.recv())
        .await
        .expect("MonitoredItem notification timed out")
        .expect("notification channel closed");
    assert_eq!(notified, changed);

    client
        .write(&WriteRequest {
            key: "hil.restore".to_owned(),
            address: scalar_node,
            value: original,
            timeout: Duration::from_secs(5),
        })
        .await
        .expect("original server value must be restored");
    client.disconnect().await.expect("disconnect must succeed");
}

fn required(name: &str) -> String {
    env::var(name).unwrap_or_else(|_| panic!("missing required HIL variable {name}"))
}

fn changed_value(value: &DeviceValue) -> DeviceValue {
    match value {
        DeviceValue::Bool(value) => DeviceValue::Bool(!value),
        DeviceValue::UInt16(value) => DeviceValue::UInt16(value.wrapping_add(1)),
        DeviceValue::Int16(value) => DeviceValue::Int16(value.wrapping_add(1)),
        DeviceValue::UInt32(value) => DeviceValue::UInt32(value.wrapping_add(1)),
        DeviceValue::Int32(value) => DeviceValue::Int32(value.wrapping_add(1)),
        DeviceValue::UInt64(value) => DeviceValue::UInt64(value.wrapping_add(1)),
        DeviceValue::Int64(value) => DeviceValue::Int64(value.wrapping_add(1)),
        DeviceValue::Float32(value) => DeviceValue::Float32(value + 1.0),
        DeviceValue::Float64(value) => DeviceValue::Float64(value + 1.0),
        _ => panic!("HIL scalar node must be a supported numeric or Boolean scalar"),
    }
}
