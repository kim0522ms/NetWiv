mod netwiv;

use dioxus::prelude::*;

#[allow(non_snake_case)]
fn App() -> Element {
    let mut name = use_signal(|| "World".to_owned());
    let mut message = use_signal(String::new);
    rsx! {
        main {
            h1 { "NetWiv + Dioxus" }
            form {
                onsubmit: move |event| async move {
                    event.prevent_default();
                    let requested = name.read().clone();
                    message.set(netwiv::greet(&requested).await);
                },
                input { aria_label: "Name", value: "{name}", oninput: move |event| name.set(event.value()) }
                button { r#type: "submit", "Greet" }
            }
            p { "{message}" }
        }
    }
}

fn main() { dioxus::launch(App); }
