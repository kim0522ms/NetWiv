// Maintainer oracle for the exact dpi crate in the pinned Tauri Cargo.lock.
// Redirect stdout as UTF-8 to tests/NetWiv.Core.Tests/fixtures/dpi-0.1.2.json.
fn main() {
    let values = [0.0f64, -0.0, 0.5, -0.5, 1.5, -1.5, 2.5, -2.5,
        0.5f64.next_down(), (-0.5f64).next_up(), 1.5f64.next_down(), (-1.5f64).next_up(),
        i32::MIN as f64, i32::MAX as f64, u32::MAX as f64,
        i32::MIN as f64 - 1.0, u32::MAX as f64 + 1.0,
        f64::MAX, f64::MIN, f64::INFINITY, f64::NEG_INFINITY, f64::NAN,
        f64::MIN_POSITIVE, f64::from_bits(1)];
    let scales = [0.5f64, 1.0, 1.5, 2.0, f64::MAX, f64::MIN_POSITIVE];
    println!("{{\"crate\":\"dpi 0.1.2\",\"conversions\":[");
    let mut first = true;
    for value in values {
        for scale in scales {
            let position = dpi::LogicalPosition::new(value, value).to_physical::<i32>(scale);
            let size = dpi::LogicalSize::new(value, value).to_physical::<u32>(scale);
            if !first { println!(","); }
            first = false;
            print!("{{\"valueBits\":\"{:016x}\",\"scaleBits\":\"{:016x}\",\"position\":{},\"size\":{}}}",
                value.to_bits(), scale.to_bits(), position.x, size.width);
        }
    }
    println!("],\"scaleValidation\":[");
    for (index, scale) in values.into_iter().enumerate() {
        if index != 0 { println!(","); }
        print!("{{\"bits\":\"{:016x}\",\"valid\":{}}}", scale.to_bits(), dpi::validate_scale_factor(scale));
    }
    println!("]}}");
}
