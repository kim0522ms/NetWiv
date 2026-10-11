use std::{collections::HashMap, str::FromStr};

use global_hotkey::{GlobalHotKeyEvent, GlobalHotKeyManager, hotkey::HotKey};
use serde_json::{Value, json};

pub(crate) struct GlobalShortcuts {
    manager: Option<GlobalHotKeyManager>,
    registered: HashMap<u32, (HotKey, String)>,
}

impl GlobalShortcuts {
    pub(crate) fn new() -> Self {
        Self { manager: None, registered: HashMap::new() }
    }

    pub(crate) fn invoke(&mut self, command: &str, args: &Value) -> Result<Value, String> {
        match command {
            "plugin:global-shortcut|register" => {
                let shortcuts = read_shortcuts(args)?;
                let mut parsed = Vec::with_capacity(shortcuts.len());
                for name in shortcuts {
                    parsed.push((HotKey::from_str(name).map_err(|e| e.to_string())?, name.to_owned()));
                }
                if self.manager.is_none() {
                    self.manager = Some(GlobalHotKeyManager::new().map_err(|e| e.to_string())?);
                }
                let manager = self.manager.as_ref().expect("manager initialized");
                let mut added = Vec::new();
                for (hotkey, name) in parsed {
                    if let Err(error) = manager.register(hotkey) {
                        for id in &added {
                            if let Some((registered, _)) = self.registered.remove(id) {
                                let _ = manager.unregister(registered);
                            }
                        }
                        return Err(error.to_string());
                    }
                    let id = hotkey.id();
                    self.registered.insert(id, (hotkey, name.clone()));
                    added.push(id);
                }
                Ok(Value::Array(added.into_iter().map(|id| json!(id)).collect()))
            }
            "plugin:global-shortcut|unregister" => {
                let shortcuts = read_shortcuts(args)?;
                let mut removed = Vec::with_capacity(shortcuts.len());
                for name in shortcuts {
                    let hotkey = HotKey::from_str(name).map_err(|e| e.to_string())?;
                    if let Some(manager) = &self.manager {
                        manager.unregister(hotkey).map_err(|e| e.to_string())?;
                    }
                    self.registered.remove(&hotkey.id());
                    removed.push(hotkey.id());
                }
                Ok(Value::Array(removed.into_iter().map(|id| json!(id)).collect()))
            }
            "plugin:global-shortcut|unregister_all" => {
                if let Some(manager) = &self.manager {
                    let keys: Vec<_> = self.registered.values().map(|(hotkey, _)| *hotkey).collect();
                    manager.unregister_all(&keys).map_err(|e| e.to_string())?;
                }
                self.registered.clear();
                Ok(Value::Null)
            }
            "plugin:global-shortcut|is_registered" => {
                let name = args.get("shortcut").and_then(Value::as_str).ok_or("shortcut must be a string")?;
                let hotkey = HotKey::from_str(name).map_err(|e| e.to_string())?;
                Ok(json!(self.registered.contains_key(&hotkey.id())))
            }
            _ => Err(format!("unknown global shortcut command: {command}")),
        }
    }

    pub(crate) fn drain(&self, mut emit: impl FnMut(Value)) {
        while let Ok(event) = GlobalHotKeyEvent::receiver().try_recv() {
            if let Some((_, shortcut)) = self.registered.get(&event.id) {
                let state = match event.state {
                    global_hotkey::HotKeyState::Pressed => "Pressed",
                    global_hotkey::HotKeyState::Released => "Released",
                };
                emit(json!({"shortcut":shortcut,"id":event.id,"state":state}));
            }
        }
    }
}

fn read_shortcuts(args: &Value) -> Result<Vec<&str>, String> {
    args.get("shortcuts").and_then(Value::as_array).ok_or("shortcuts must be an array")?
        .iter().map(|value| value.as_str().ok_or_else(|| "shortcut must be a string".to_owned())).collect()
}

#[cfg(test)]
mod tests {
    #[test]
    fn rejects_bad_arguments_before_creating_manager() {
        let mut shortcuts = super::GlobalShortcuts::new();
        assert!(shortcuts.invoke("plugin:global-shortcut|register", &serde_json::json!({"shortcuts":[5]})).is_err());
        assert!(shortcuts.manager.is_none());
        assert!(shortcuts.invoke("plugin:global-shortcut|register", &serde_json::json!({"shortcuts":["not+a+key"]})).is_err());
        assert!(shortcuts.manager.is_none());
    }
}
