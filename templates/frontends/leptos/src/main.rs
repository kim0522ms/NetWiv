mod netwiv;

use leptos::{ev::SubmitEvent, prelude::*, task::spawn_local};

#[component]
fn App() -> impl IntoView {
    let (name, set_name) = signal("World".to_owned());
    let (message, set_message) = signal(String::new());
    let greet = move |event: SubmitEvent| {
        event.prevent_default();
        let name = name.get_untracked();
        spawn_local(async move { set_message.set(netwiv::greet(&name).await) });
    };
    view! {
        <main><h1>"NetWiv + Leptos"</h1>
            <form on:submit=greet>
                <input aria-label="Name" prop:value=name on:input=move |event| set_name.set(event_target_value(&event)) />
                <button>"Greet"</button>
            </form><p>{move || message.get()}</p>
        </main>
    }
}

fn main() { mount_to_body(|| view! { <App/> }); }
