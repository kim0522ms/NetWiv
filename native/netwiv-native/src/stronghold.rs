// The command and procedure contract follows tauri-plugin-stronghold (MIT OR Apache-2.0).
use std::{collections::HashMap, path::{Path, PathBuf}, time::Duration};

use crypto::keys::bip39;
use iota_stronghold::{
    Client, KeyProvider, Location, SnapshotPath, Stronghold,
    procedures::{
        BIP39Generate, BIP39Recover, Curve, Ed25519Sign, KeyType, MnemonicLanguage,
        PublicKey, Slip10Derive, Slip10DeriveInput, Slip10Generate, StrongholdProcedure,
    },
};
use serde::Deserialize;
use serde_json::{Value, json};
use zeroize::{Zeroize, Zeroizing};

struct Snapshot {
    inner: Stronghold,
    path: SnapshotPath,
    key: KeyProvider,
}

impl Snapshot {
    fn save(&self) -> Result<(), String> {
        self.inner.commit_with_keyprovider(&self.path, &self.key).map_err(|e| e.to_string())
    }
}

pub(crate) struct Strongholds(HashMap<PathBuf, Snapshot>);

impl Strongholds {
    pub(crate) fn new() -> Self { Self(HashMap::new()) }

    pub(crate) fn invoke(&mut self, command: &str, args: &Value) -> Result<Value, String> {
        let path = PathBuf::from(required_string(args, "snapshotPath")?);
        match command {
            "plugin:stronghold|initialize" => {
                let mut password = required_string(args, "password")?.to_owned();
                let salt_path = args.get("saltPath").and_then(Value::as_str)
                    .map(PathBuf::from).unwrap_or_else(|| PathBuf::from(format!("{}.salt", path.display())));
                let key = derive_key(&password, &salt_path);
                password.zeroize();
                let key = key?;
                let provider = KeyProvider::try_from(Zeroizing::new(key)).map_err(|e| e.to_string())?;
                let snapshot_path = SnapshotPath::from_path(&path);
                let inner = Stronghold::default();
                if snapshot_path.exists() {
                    inner.load_snapshot(&provider, &snapshot_path).map_err(|e| e.to_string())?;
                }
                self.0.insert(path, Snapshot { inner, path: snapshot_path, key: provider });
                Ok(Value::Null)
            }
            "plugin:stronghold|destroy" => {
                if let Some(snapshot) = self.0.get(&path) { snapshot.save()?; }
                self.0.remove(&path);
                Ok(Value::Null)
            }
            "plugin:stronghold|save" => {
                if let Some(snapshot) = self.0.get(&path) { snapshot.save()?; }
                Ok(Value::Null)
            }
            _ => {
                let snapshot = self.0.get(&path).ok_or("stronghold not initialized")?;
                invoke_client(&snapshot.inner, command, args)
            }
        }
    }
}

fn derive_key(password: &str, salt_path: &Path) -> Result<Vec<u8>, String> {
    let salt = if salt_path.exists() {
        let value = std::fs::read(salt_path).map_err(|e| e.to_string())?;
        if value.len() != 32 { return Err("stronghold salt must have 32 bytes".into()); }
        value
    } else {
        let mut value = vec![0u8; 32];
        getrandom::fill(&mut value).map_err(|e| e.to_string())?;
        if let Some(parent) = salt_path.parent() {
            std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        std::fs::write(salt_path, &value).map_err(|e| e.to_string())?;
        value
    };
    argon2::hash_raw(password.as_bytes(), &salt, &Default::default()).map_err(|e| e.to_string())
}

fn invoke_client(stronghold: &Stronghold, command: &str, args: &Value) -> Result<Value, String> {
    let client_name: Bytes = field(args, "client")?;
    if command == "plugin:stronghold|create_client" {
        stronghold.create_client(client_name).map_err(|e| e.to_string())?;
        return Ok(Value::Null);
    }
    if command == "plugin:stronghold|load_client" {
        stronghold.load_client(client_name).map_err(|e| e.to_string())?;
        return Ok(Value::Null);
    }
    let client: Client = stronghold.get_client(client_name).map_err(|e| e.to_string())?;
    match command {
        "plugin:stronghold|get_store_record" => {
            let key = required_string(args, "key")?;
            let value = client.store().get(key.as_bytes()).map_err(|e| e.to_string())?;
            Ok(json!(value))
        }
        "plugin:stronghold|save_store_record" => {
            let key = required_string(args, "key")?;
            let value: Vec<u8> = field(args, "value")?;
            let lifetime: Option<Duration> = optional_field(args, "lifetime")?;
            Ok(json!(client.store().insert(key.as_bytes().to_vec(), value, lifetime).map_err(|e| e.to_string())?))
        }
        "plugin:stronghold|remove_store_record" => {
            let key = required_string(args, "key")?;
            Ok(json!(client.store().delete(key.as_bytes()).map_err(|e| e.to_string())?))
        }
        "plugin:stronghold|save_secret" => {
            let vault: Bytes = field(args, "vault")?;
            let record: Bytes = field(args, "recordPath")?;
            let secret: Vec<u8> = field(args, "secret")?;
            client.vault(vault.as_ref()).write_secret(Location::generic(vault, record), Zeroizing::new(secret))
                .map_err(|e| e.to_string())?;
            Ok(Value::Null)
        }
        "plugin:stronghold|remove_secret" => {
            let vault: Bytes = field(args, "vault")?;
            let record: Bytes = field(args, "recordPath")?;
            client.vault(vault.as_ref()).delete_secret(record).map_err(|e| e.to_string())?;
            Ok(Value::Null)
        }
        "plugin:stronghold|execute_procedure" => {
            let procedure: ProcedureDto = field(args, "procedure")?;
            let output: Vec<u8> = client.execute_procedure(StrongholdProcedure::try_from(procedure)?)
                .map_err(|e| e.to_string())?.into();
            Ok(json!(output))
        }
        _ => Err(format!("unknown stronghold command: {command}")),
    }
}

#[derive(Deserialize)]
#[serde(untagged)]
enum Bytes { Text(String), Raw(Vec<u8>) }
impl AsRef<[u8]> for Bytes {
    fn as_ref(&self) -> &[u8] {
        match self { Self::Text(value) => value.as_bytes(), Self::Raw(value) => value }
    }
}
impl From<Bytes> for Vec<u8> {
    fn from(value: Bytes) -> Self {
        match value { Bytes::Text(value) => value.into_bytes(), Bytes::Raw(value) => value }
    }
}

#[derive(Deserialize)]
#[serde(tag = "type", content = "payload")]
enum LocationDto {
    Generic { vault: Bytes, record: Bytes },
    Counter { vault: Bytes, counter: usize },
}
impl From<LocationDto> for Location {
    fn from(value: LocationDto) -> Self {
        match value {
            LocationDto::Generic { vault, record } => Self::generic(vault, record),
            LocationDto::Counter { vault, counter } => Self::counter(vault, counter),
        }
    }
}

#[derive(Deserialize)]
#[serde(tag = "type", content = "payload")]
enum DeriveInput { Seed(LocationDto), Key(LocationDto) }
impl From<DeriveInput> for Slip10DeriveInput {
    fn from(value: DeriveInput) -> Self {
        match value { DeriveInput::Seed(location) => Self::Seed(location.into()), DeriveInput::Key(location) => Self::Key(location.into()) }
    }
}

#[derive(Deserialize)]
#[serde(tag = "type", content = "payload")]
enum ProcedureDto {
    SLIP10Generate { output: LocationDto, #[serde(rename = "sizeBytes")] size_bytes: Option<usize> },
    SLIP10Derive { chain: Vec<u32>, input: DeriveInput, output: LocationDto },
    BIP39Recover { mnemonic: String, passphrase: Option<String>, output: LocationDto },
    BIP39Generate { passphrase: Option<String>, output: LocationDto },
    PublicKey { #[serde(rename = "type")] ty: String, #[serde(rename = "privateKey")] private_key: LocationDto },
    Ed25519Sign { #[serde(rename = "privateKey")] private_key: LocationDto, msg: String },
}
impl TryFrom<ProcedureDto> for StrongholdProcedure {
    type Error = String;
    fn try_from(value: ProcedureDto) -> Result<Self, Self::Error> {
        Ok(match value {
            ProcedureDto::SLIP10Generate { output, size_bytes } =>
                Self::Slip10Generate(Slip10Generate { output: output.into(), size_bytes }),
            ProcedureDto::SLIP10Derive { chain, input, output } =>
                Self::Slip10Derive(Slip10Derive { curve: Curve::Ed25519, chain, input: input.into(), output: output.into() }),
            ProcedureDto::BIP39Recover { mnemonic, passphrase, output } =>
                Self::BIP39Recover(BIP39Recover {
                    mnemonic: bip39::Mnemonic::from(mnemonic),
                    passphrase: bip39::Passphrase::from(passphrase.unwrap_or_default()),
                    output: output.into(),
                }),
            ProcedureDto::BIP39Generate { passphrase, output } =>
                Self::BIP39Generate(BIP39Generate {
                    passphrase: bip39::Passphrase::from(passphrase.unwrap_or_default()),
                    output: output.into(), language: MnemonicLanguage::English,
                }),
            ProcedureDto::PublicKey { ty, private_key } =>
                Self::PublicKey(PublicKey {
                    ty: match ty.to_ascii_lowercase().as_str() {
                        "ed25519" => KeyType::Ed25519,
                        "x25519" => KeyType::X25519,
                        _ => return Err("unknown stronghold key type".into()),
                    },
                    private_key: private_key.into(),
                }),
            ProcedureDto::Ed25519Sign { private_key, msg } =>
                Self::Ed25519Sign(Ed25519Sign { private_key: private_key.into(), msg: msg.into_bytes() }),
        })
    }
}

fn required_string<'a>(args: &'a Value, key: &str) -> Result<&'a str, String> {
    args.get(key).and_then(Value::as_str).ok_or_else(|| format!("{key} must be a string"))
}
fn field<T: serde::de::DeserializeOwned>(args: &Value, key: &str) -> Result<T, String> {
    serde_json::from_value(args.get(key).ok_or_else(|| format!("{key} is required"))?.clone())
        .map_err(|e| e.to_string())
}
fn optional_field<T: serde::de::DeserializeOwned>(args: &Value, key: &str) -> Result<Option<T>, String> {
    args.get(key).filter(|value| !value.is_null()).map(|value| serde_json::from_value(value.clone()).map_err(|e| e.to_string())).transpose()
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    #[test]
    fn missing_snapshot_is_rejected() {
        let mut strongholds = super::Strongholds::new();
        assert!(strongholds.invoke("plugin:stronghold|create_client",
            &serde_json::json!({"snapshotPath":"missing","client":"app"})).is_err());
    }

    #[test]
    fn encrypted_store_roundtrips_and_rejects_wrong_password() {
        engine::snapshot::try_set_encrypt_work_factor(0).unwrap();
        let directory = std::env::temp_dir().join(format!(
            "netwiv-stronghold-{}-{}", std::process::id(),
            std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos()
        ));
        std::fs::create_dir_all(&directory).unwrap();
        let snapshot = directory.join("data.hold");
        let salt = directory.join("salt.bin");
        let base = json!({"snapshotPath":snapshot,"saltPath":salt,"password":"correct"});
        let client = json!({"snapshotPath":snapshot,"client":"app"});
        let record = json!({"snapshotPath":snapshot,"client":"app","key":"access","value":[1,2,255]});
        let mut vault = super::Strongholds::new();
        vault.invoke("plugin:stronghold|initialize", &base).unwrap();
        vault.invoke("plugin:stronghold|create_client", &client).unwrap();
        vault.invoke("plugin:stronghold|save_store_record", &record).unwrap();
        vault.invoke("plugin:stronghold|save", &base).unwrap();
        vault.invoke("plugin:stronghold|destroy", &base).unwrap();
        assert!(snapshot.is_file());
        let wrong = json!({"snapshotPath":snapshot,"saltPath":salt,"password":"wrong"});
        assert!(vault.invoke("plugin:stronghold|initialize", &wrong).is_err());
        vault.invoke("plugin:stronghold|initialize", &base).unwrap();
        vault.invoke("plugin:stronghold|load_client", &client).unwrap();
        let value = vault.invoke("plugin:stronghold|get_store_record", &record).unwrap();
        assert_eq!(value, json!([1,2,255]));
        let seed = json!({"type":"Generic","payload":{"vault":"keys","record":"seed"}});
        let private = json!({"type":"Generic","payload":{"vault":"keys","record":"private"}});
        let seed_command = json!({"snapshotPath":snapshot,"client":"app","procedure":{
            "type":"SLIP10Generate","payload":{"output":seed,"sizeBytes":32}
        }});
        vault.invoke("plugin:stronghold|execute_procedure", &seed_command).unwrap();
        let derive_command = json!({"snapshotPath":snapshot,"client":"app","procedure":{
            "type":"SLIP10Derive","payload":{"chain":[2147483648u32],
                "input":{"type":"Seed","payload":seed},"output":private}
        }});
        vault.invoke("plugin:stronghold|execute_procedure", &derive_command).unwrap();
        let public_command = json!({"snapshotPath":snapshot,"client":"app","procedure":{
            "type":"PublicKey","payload":{"type":"Ed25519","privateKey":private}
        }});
        let public = vault.invoke("plugin:stronghold|execute_procedure", &public_command).unwrap();
        assert_eq!(public.as_array().unwrap().len(), 32);
        let sign_command = json!({"snapshotPath":snapshot,"client":"app","procedure":{
            "type":"Ed25519Sign","payload":{"privateKey":private,"msg":"hello"}
        }});
        let signature = vault.invoke("plugin:stronghold|execute_procedure", &sign_command).unwrap();
        assert_eq!(signature.as_array().unwrap().len(), 64);
        vault.invoke("plugin:stronghold|destroy", &base).unwrap();
        std::fs::remove_dir_all(directory).unwrap();
    }
}
