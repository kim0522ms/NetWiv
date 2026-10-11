use wasm_bindgen::prelude::*;

#[wasm_bindgen]
extern "C" {
    #[wasm_bindgen(js_namespace = window, js_name = netwivGreet)]
    async fn call_greet(name: &str) -> JsValue;
}

pub async fn greet(name: &str) -> String {
    let value = call_greet(name).await;
    js_sys::Reflect::get(&value, &JsValue::from_str("message"))
        .ok()
        .and_then(|message| message.as_string())
        .unwrap_or_else(|| "No message returned by the C# command".to_owned())
}
