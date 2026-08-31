# AuroraCommunication

AuroraCommunication is the managed, cross-platform industrial communication
runtime used by Aurora 2.0. It is intentionally independent from the Aurora
business domain, persistence, gRPC, HMI, and web hosting layers.

The first production slice contains:

- shared device types and stable error contracts;
- Tokio-based TCP transport;
- one serialized session actor per device with priority command lanes;
- reconnect-aware polling subscriptions;
- a Modbus TCP client compatible with the address forms used by the existing
  C# AuroraCommunication implementation.

The implementation is derived from internally licensed protocol behavior and
must not be redistributed outside the authorized organization.
