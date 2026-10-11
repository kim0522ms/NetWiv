// Pinned Tauri command arguments deserialize from an already parsed JSON Value.
use serde::{de::DeserializeOwned, Serialize};
use serde_json::{json, Value};
use std::io::Write;

fn check<T: DeserializeOwned + Serialize>(value: &Value) -> Value {
    match T::deserialize(value) {
        Ok(value) => json!({"ok": serde_json::to_string(&value).unwrap()}),
        Err(error) => json!({"error": error.to_string()}),
    }
}

fn main() {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.first().map(String::as_str) == Some("--format-oracle") {
        assert_eq!(args.len(), 2);
        let mut output = std::io::BufWriter::new(std::fs::File::create(&args[1]).unwrap());
        let mut bits = 0xa57c_91e3_4b26_d80fu64;
        for _ in 0..100_000 {
            bits ^= bits << 13;
            bits ^= bits >> 7;
            bits ^= bits << 17;
            let Some(number) = serde_json::Number::from_f64(f64::from_bits(bits)) else { continue };
            let result = check::<u8>(&Value::Number(number));
            writeln!(output, "{bits:016x}\t{}", result["error"].as_str().unwrap()).unwrap();
        }
        return;
    }
    assert!(args.is_empty());
    let inputs = ["null", "true", "{}", "[]", "\"number\"", "0", "-0", "0.0",
        "1", "1.0", "1e0", "-1", "0.5", "-0.5", "255", "256", "-129", "128",
        "65535", "65536", "-32769", "32768", "4294967295", "4294967296",
        "-2147483649", "2147483648", "9223372036854775807", "9223372036854775808",
        "-9223372036854775808", "-9223372036854775809", "18446744073709551615",
        "18446744073709551616", "1e-7", "1e-6", "1e15", "1e16", "1e20", "1e21",
        "100000000000000000000", "1.2345678901234567", "1.23456789012345678",
        "2.2250738585072014e-308", "5e-324", "1e-400", "3.4028235e38", "1e40"];
    let vectors: Vec<_> = inputs.into_iter().map(|input| {
        let value: Value = serde_json::from_str(input).unwrap();
        json!({"input":input, "results": {
            "byte":check::<u8>(&value), "sbyte":check::<i8>(&value),
            "ushort":check::<u16>(&value), "short":check::<i16>(&value),
            "uint":check::<u32>(&value), "int":check::<i32>(&value),
            "ulong":check::<u64>(&value), "long":check::<i64>(&value),
            "float":check::<f32>(&value), "double":check::<f64>(&value)
        }})
    }).collect();
    println!("{}", serde_json::to_string_pretty(&json!({"serde":"1.0.229", "serdeJson":"1.0.151", "vectors":vectors})).unwrap());
}
