mod netwiv;

use sycamore::futures::spawn_local_scoped;
use sycamore::prelude::*;
use sycamore::web::events::SubmitEvent;

#[component]
fn App() -> View {
    let name = create_signal("World".to_owned());
    let message = create_signal(String::new());
    let greet = move |event: SubmitEvent| {
        event.prevent_default();
        let requested = name.get_clone();
        spawn_local_scoped(async move { message.set(netwiv::greet(&requested).await) });
    };
    view! {
        main {
            h1 { "NetWiv + Sycamore" }
            form(on:submit=greet) {
                input(bind:value=name, aria-label="Name")
                button(r#type="submit") { "Greet" }
            }
            p { (message) }
        }
    }
}

fn main() { sycamore::render(App); }
