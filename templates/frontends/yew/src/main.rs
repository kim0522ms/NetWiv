mod netwiv;

use wasm_bindgen_futures::spawn_local;
use web_sys::HtmlInputElement;
use yew::prelude::*;

#[function_component(App)]
fn app() -> Html {
    let name = use_state(|| "World".to_owned());
    let message = use_state(String::new);
    let oninput = {
        let name = name.clone();
        Callback::from(move |event: InputEvent| {
            let input: HtmlInputElement = event.target_unchecked_into();
            name.set(input.value());
        })
    };
    let onsubmit = {
        let name = name.clone();
        let message = message.clone();
        Callback::from(move |event: SubmitEvent| {
            event.prevent_default();
            let name = (*name).clone();
            let message = message.clone();
            spawn_local(async move { message.set(netwiv::greet(&name).await) });
        })
    };
    html! { <main><h1>{ "NetWiv + Yew" }</h1><form {onsubmit}><input aria-label="Name" value={(*name).clone()} {oninput}/><button>{ "Greet" }</button></form><p>{ &*message }</p></main> }
}

fn main() { yew::Renderer::<App>::new().render(); }
