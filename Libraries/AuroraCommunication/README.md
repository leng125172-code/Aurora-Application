# AuroraCommunication

AuroraCommunication is the managed, cross-platform industrial communication
runtime used by Aurora 2.0. It is intentionally independent from the Aurora
business domain, persistence, gRPC, HMI, and web hosting layers.

The first production slice contains:

- shared device types and stable error contracts;
- Tokio-based TCP, connected UDP, and serial transports;
- one serialized session actor per device with priority command lanes;
- reconnect-aware polling subscriptions;
- Modbus TCP/UDP, RTU/ASCII serial, and RTU/ASCII-over-TCP clients compatible
  with the address forms used by the existing C# implementation;
- committed protocol golden vectors that keep CI independent of the C# source;
- Siemens ISO-on-TCP/S7comm with model defaults for S7-1200/300/400/1500,
  S7-200 SMART, and S7-200 endpoints;
- Mitsubishi MC protocol 3E binary/ASCII over TCP or UDP (4E/A1E/FX are
  intentionally separate future protocols);
- OPC UA backend contract with browse/read/write/subscription simulator tests,
  plus a feature-gated native boundary pinned to open62541 1.5.4 and mbedTLS
  3.6.7. Native secure sessions, browse continuation and monitored items are
  implemented but remain HIL-gated preview features.

The implementation is derived from internally licensed protocol behavior and
must not be redistributed outside the authorized organization.
