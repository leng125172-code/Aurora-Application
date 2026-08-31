//! Multi-register byte and word ordering.

use serde::{Deserialize, Serialize};

/// Ordering applied to one multi-byte scalar read from protocol registers.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum DataLayout {
    /// Bytes and words retain network order: ABCD.
    #[default]
    Abcd,
    /// Bytes within every 16-bit register are swapped: BADC.
    Badc,
    /// Register words are reversed while bytes stay ordered: CDAB.
    Cdab,
    /// All bytes are reversed: DCBA.
    Dcba,
}

impl DataLayout {
    /// Converts one scalar between protocol order and canonical big-endian order.
    /// Applying the operation twice restores the original bytes.
    pub fn reorder(self, value: &mut [u8]) {
        match self {
            Self::Abcd => {}
            Self::Badc => {
                for word in value.chunks_exact_mut(2) {
                    word.swap(0, 1);
                }
            }
            Self::Cdab => {
                let words = value.len() / 2;
                for index in 0..words / 2 {
                    let opposite = words - index - 1;
                    value.swap(index * 2, opposite * 2);
                    value.swap(index * 2 + 1, opposite * 2 + 1);
                }
            }
            Self::Dcba => value.reverse(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::DataLayout;

    #[test]
    fn layouts_match_hsl_names() {
        for (layout, expected) in [
            (DataLayout::Abcd, [1, 2, 3, 4]),
            (DataLayout::Badc, [2, 1, 4, 3]),
            (DataLayout::Cdab, [3, 4, 1, 2]),
            (DataLayout::Dcba, [4, 3, 2, 1]),
        ] {
            let mut value = [1, 2, 3, 4];
            layout.reorder(&mut value);
            assert_eq!(value, expected);
            layout.reorder(&mut value);
            assert_eq!(value, [1, 2, 3, 4]);
        }
    }
}
