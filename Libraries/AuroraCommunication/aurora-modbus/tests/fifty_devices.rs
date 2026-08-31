//! Concurrent acceptance test using 50 independent simulated PLC endpoints.

use aurora_comm_core::{
    DeviceClient, DeviceDataType, DeviceValue, OperationPriority, ReadRequest, WriteRequest,
};
use aurora_comm_runtime::{ManagedSession, ManagedSessionOptions};
use aurora_modbus::{ModbusTcpClient, ModbusTcpOptions};
use futures::future::try_join_all;
use std::io;
use std::sync::Arc;
use std::time::{Duration, Instant};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};
use tokio::task::JoinHandle;

const DEVICE_COUNT: usize = 50;

struct Simulator {
    port: u16,
    worker: JoinHandle<io::Result<()>>,
}

async fn start_simulator(seed: u16) -> io::Result<Simulator> {
    let listener = TcpListener::bind(("127.0.0.1", 0)).await?;
    let port = listener.local_addr()?.port();
    let worker = tokio::spawn(async move {
        let (stream, _) = listener.accept().await?;
        serve_connection(stream, seed).await
    });
    Ok(Simulator { port, worker })
}

async fn serve_connection(mut stream: TcpStream, seed: u16) -> io::Result<()> {
    loop {
        let mut header = [0_u8; 7];
        match stream.read_exact(&mut header).await {
            Ok(_) => {}
            Err(error) if error.kind() == io::ErrorKind::UnexpectedEof => return Ok(()),
            Err(error) => return Err(error),
        }
        let frame_length = usize::from(u16::from_be_bytes([header[4], header[5]]));
        if frame_length < 2 {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                "invalid MBAP length",
            ));
        }
        let mut pdu = vec![0_u8; frame_length - 1];
        stream.read_exact(&mut pdu).await?;
        let response_pdu = simulate_pdu(&pdu, seed)?;

        let response_length = u16::try_from(response_pdu.len() + 1)
            .map_err(|_| io::Error::new(io::ErrorKind::InvalidData, "response too large"))?;
        header[4..6].copy_from_slice(&response_length.to_be_bytes());
        stream.write_all(&header).await?;
        stream.write_all(&response_pdu).await?;
    }
}

fn simulate_pdu(pdu: &[u8], seed: u16) -> io::Result<Vec<u8>> {
    let function = *pdu
        .first()
        .ok_or_else(|| io::Error::new(io::ErrorKind::InvalidData, "missing function"))?;
    match function {
        3 | 4 => {
            if pdu.len() != 5 {
                return Err(io::Error::new(io::ErrorKind::InvalidData, "invalid read"));
            }
            let offset = u16::from_be_bytes([pdu[1], pdu[2]]);
            let quantity = u16::from_be_bytes([pdu[3], pdu[4]]);
            let byte_count = u8::try_from(quantity * 2)
                .map_err(|_| io::Error::new(io::ErrorKind::InvalidData, "read too large"))?;
            let mut response = vec![function, byte_count];
            for index in 0..quantity {
                response.extend_from_slice(&(seed + offset + index).to_be_bytes());
            }
            Ok(response)
        }
        6 => Ok(pdu.to_vec()),
        16 => {
            if pdu.len() < 6 {
                return Err(io::Error::new(io::ErrorKind::InvalidData, "invalid write"));
            }
            Ok(pdu[..5].to_vec())
        }
        _ => Ok(vec![function | 0x80, 1]),
    }
}

#[tokio::test(flavor = "multi_thread", worker_threads = 8)]
async fn fifty_independent_devices_read_and_write_concurrently() {
    let simulators = try_join_all(
        (0..DEVICE_COUNT)
            .map(|index| start_simulator(u16::try_from(index).expect("device index fits u16"))),
    )
    .await
    .expect("simulators should bind");

    let start = Instant::now();
    let operations = simulators
        .iter()
        .enumerate()
        .map(|(index, simulator)| {
            let port = simulator.port;
            tokio::spawn(async move {
                let mut options = ModbusTcpOptions::new("127.0.0.1", 1);
                options.port = port;
                options.connect_timeout = Duration::from_secs(2);
                let client: Arc<dyn DeviceClient> = Arc::new(ModbusTcpClient::new(options));
                let session = ManagedSession::spawn(client, ManagedSessionOptions::default());

                let read = session
                    .read(
                        ReadRequest {
                            key: format!("device-{index}.register"),
                            address: "10".to_owned(),
                            data_type: DeviceDataType::UInt16,
                            count: 1,
                            timeout: Duration::from_secs(1),
                        },
                        OperationPriority::Normal,
                    )
                    .await?;
                let expected = u16::try_from(index).expect("device index fits u16") + 10;
                assert_eq!(read, DeviceValue::UInt16(expected));

                session
                    .write(
                        WriteRequest {
                            key: format!("device-{index}.command"),
                            address: "20".to_owned(),
                            value: DeviceValue::UInt16(7),
                            timeout: Duration::from_secs(1),
                        },
                        OperationPriority::Control,
                    )
                    .await?;
                session.shutdown();
                Ok::<_, aurora_comm_core::CommError>(())
            })
        })
        .collect::<Vec<_>>();

    for operation in operations {
        operation
            .await
            .expect("device task should not panic")
            .expect("device operation should succeed");
    }

    assert!(
        start.elapsed() < Duration::from_secs(5),
        "50 device smoke test exceeded its five second test budget"
    );
    for simulator in simulators {
        simulator
            .worker
            .await
            .expect("simulator should not panic")
            .expect("simulator should stop cleanly");
    }
}
