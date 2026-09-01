//! Golden compatibility vectors for every Modbus stream framing mode.

use aurora_modbus::{decode_ascii_frame, decode_rtu_frame, encode_ascii_frame, encode_rtu_frame};
use serde::Deserialize;

#[derive(Deserialize)]
struct GoldenFile {
    vectors: Vec<GoldenVector>,
}

#[derive(Deserialize)]
struct GoldenVector {
    name: String,
    unit: u8,
    pdu_hex: String,
    rtu_hex: String,
    ascii: String,
}

fn hex(value: &str) -> Vec<u8> {
    value
        .as_bytes()
        .as_chunks::<2>()
        .0
        .iter()
        .map(|pair| u8::from_str_radix(std::str::from_utf8(pair).unwrap(), 16).unwrap())
        .collect()
}

#[test]
fn committed_golden_frames_encode_and_decode_exactly() {
    let golden: GoldenFile =
        serde_json::from_str(include_str!("../../test-data/golden/modbus-frames.json")).unwrap();
    for vector in golden.vectors {
        let pdu = hex(&vector.pdu_hex);
        let rtu = hex(&vector.rtu_hex);
        assert_eq!(encode_rtu_frame(vector.unit, &pdu), rtu, "{}", vector.name);
        assert_eq!(decode_rtu_frame(&rtu).unwrap(), (vector.unit, pdu.clone()));
        assert_eq!(
            encode_ascii_frame(vector.unit, &pdu),
            vector.ascii.as_bytes()
        );
        assert_eq!(
            decode_ascii_frame(vector.ascii.as_bytes()).unwrap(),
            (vector.unit, pdu)
        );
    }
}
