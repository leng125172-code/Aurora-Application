# Vendored OPC UA native dependencies

These release artifacts are committed so production and HIL builds do not
download native code at build time.

| Component | Version | Upstream artifact | SHA-256 |
| --- | --- | --- | --- |
| open62541 | 1.5.4 | `open62541.c` | `9768d3f019ff67be0f9a815d0acd09a9e1906b0dcbb373512343aa41feecd87f` |
| open62541 | 1.5.4 | `open62541.h` | `b6edbc3b2d213927750af9774adc13f3bb9bbfa52199aa46f9e57cf657186afe` |
| mbedTLS | 3.6.7 | `mbedtls-3.6.7.tar.bz2` | `a7e8bcbec0e6f761b4af24f25677626b35f762f68eef79c08677a363212d11f6` |

Sources:

- <https://github.com/open62541/open62541/releases/tag/v1.5.4>
- <https://github.com/Mbed-TLS/mbedtls/releases/tag/mbedtls-3.6.7>

open62541 is MPL-2.0. The release files retain their upstream notices. mbedTLS
is Apache-2.0 OR GPL-2.0-or-later; Aurora uses it under Apache-2.0. The official
archive contains the complete license texts. `aurora-opcua-sys` verifies every
artifact before compilation and applies only an out-of-tree generated-header
architecture selection so the same snapshot builds on Windows, Linux and macOS.
