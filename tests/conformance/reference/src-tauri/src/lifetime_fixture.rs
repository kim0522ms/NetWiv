use tauri::{ipc::Channel, AppHandle, Listener};

pub async fn stream_numbers(app: AppHandle, channel: Channel<u32>, resume_event: Option<String>) -> Result<(), String> {
    if let Some(event) = resume_event {
        let (sender, receiver) = std::sync::mpsc::channel();
        let listener = app.listen(event, move |_| { let _ = sender.send(()); });
        if let Err(error) = channel.send(0) {
            app.unlisten(listener);
            return Err(error.to_string());
        }
        let resumed = tauri::async_runtime::spawn_blocking(move ||
            receiver.recv_timeout(std::time::Duration::from_secs(10))).await;
        app.unlisten(listener);
        resumed.map_err(|error| error.to_string())?.map_err(|error| error.to_string())?;
    } else {
        channel.send(0).map_err(|error| error.to_string())?;
    }
    channel.send(1).map_err(|error| error.to_string())?;
    channel.send(2).map_err(|error| error.to_string())?;
    Ok(())
}
