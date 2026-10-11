use quote::ToTokens;
use serde_json::{json, Value};
use std::{
    env, fs,
    path::{Path, PathBuf},
};
use syn::{spanned::Spanned, visit::Visit};

#[derive(Default)]
struct Inventory {
    path: String,
    modules: Vec<String>,
    conditions: Vec<String>,
    tests: Vec<Value>,
    macros: Vec<Value>,
}

fn attributes(attributes: &[syn::Attribute]) -> Vec<String> {
    attributes
        .iter()
        .map(|a| a.meta.to_token_stream().to_string())
        .collect()
}

impl<'ast> Visit<'ast> for Inventory {
    fn visit_item_mod(&mut self, item: &'ast syn::ItemMod) {
        let count = self.conditions.len();
        self.conditions.extend(attributes(&item.attrs));
        self.modules.push(item.ident.to_string());
        syn::visit::visit_item_mod(self, item);
        self.modules.pop();
        self.conditions.truncate(count);
    }

    fn visit_item_fn(&mut self, item: &'ast syn::ItemFn) {
        let test_attributes: Vec<_> = item
            .attrs
            .iter()
            .filter(|a| {
                a.path().segments.last().is_some_and(|s| {
                    matches!(
                        s.ident.to_string().as_str(),
                        "test" | "rstest" | "test_case" | "quickcheck"
                    )
                })
            })
            .collect();
        if !test_attributes.is_empty() {
            let name = self
                .modules
                .iter()
                .map(String::as_str)
                .chain(std::iter::once(item.sig.ident.to_string().as_str()))
                .collect::<Vec<_>>()
                .join("::");
            self.tests.push(json!({
                "file": self.path,
                "name": name,
                "line": item.span().start().line,
                "endLine": item.span().end().line,
                "moduleAttributes": self.conditions,
                "attributes": attributes(&item.attrs),
                "classification": "unreviewed",
                "netwivEvidence": [],
            }));
        }
        // Also find nested test modules; preserve function context for unique ids.
        self.modules.push(item.sig.ident.to_string());
        syn::visit::visit_item_fn(self, item);
        self.modules.pop();
    }

    fn visit_item_macro(&mut self, item: &'ast syn::ItemMacro) {
        let name = item.mac.path.to_token_stream().to_string();
        let mut property_count = 0;
        if name == "proptest" {
            let tokens: Vec<_> = item.mac.tokens.clone().into_iter().collect();
            for (index, pair) in tokens.windows(2).enumerate() {
                if matches!(&pair[0], proc_macro2::TokenTree::Ident(value) if value == "fn") {
                    if let proc_macro2::TokenTree::Ident(function) = &pair[1] {
                        let body = tokens[index + 2..].iter().find(|token| matches!(token,
                            proc_macro2::TokenTree::Group(group) if group.delimiter() == proc_macro2::Delimiter::Brace));
                        let name = self
                            .modules
                            .iter()
                            .map(String::as_str)
                            .chain(std::iter::once(function.to_string().as_str()))
                            .collect::<Vec<_>>()
                            .join("::");
                        self.tests.push(json!({
                            "file": self.path, "name": name, "line": pair[0].span().start().line,
                            "endLine": body.map(|value| value.span().end().line),
                            "moduleAttributes": self.conditions, "attributes": ["proptest"],
                            "generator": "proptest", "classification": "unreviewed", "netwivEvidence": [],
                        }));
                        property_count += 1;
                    }
                }
            }
        }
        if name.contains("test") || self.modules.iter().any(|part| part.contains("test")) {
            self.macros.push(
                json!({"file": self.path, "name": name, "line": item.span().start().line,
                "enumeratedProperties": property_count,
                "review": "macro-generated tests require expansion or manual accounting"}),
            );
        }
        syn::visit::visit_item_macro(self, item);
    }
}

fn files(root: &Path, output: &mut Vec<PathBuf>) -> std::io::Result<()> {
    for entry in fs::read_dir(root)? {
        let entry = entry?;
        let kind = entry.file_type()?;
        if kind.is_dir() {
            files(&entry.path(), output)?;
        } else if kind.is_file() && entry.path().extension().is_some_and(|s| s == "rs") {
            output.push(entry.path());
        }
    }
    Ok(())
}

fn render_plugin_template(source: &str, license: bool) -> Result<String, handlebars::RenderError> {
    let mut renderer = handlebars::Handlebars::new();
    renderer.set_strict_mode(true);
    renderer.register_escape_fn(handlebars::no_escape);
    renderer.render_template(source, &json!({
        "license_header": if license { "// Inventory license fixture" } else { "" },
        "plugin_name": "inventory-plugin", "plugin_name_snake_case": "inventory_plugin",
        "plugin_name_pascal_case": "InventoryPlugin", "android_package_id": "dev.netwiv.inventory"
    }))
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let root = PathBuf::from(
        env::args()
            .nth(1)
            .ok_or("usage: netwiv-upstream-test-inventory <upstream-source-root>")?,
    );
    let mut paths = Vec::new();
    files(&root, &mut paths)?;
    paths.sort();
    let mut inventory = Inventory::default();
    let mut failures = Vec::new();
    let mut templates = Vec::new();
    for path in &paths {
        let source = fs::read_to_string(path)?;
        inventory.path = path
            .strip_prefix(&root)?
            .to_string_lossy()
            .replace('\\', "/");
        if inventory
            .path
            .starts_with("crates/tauri-cli/templates/plugin/")
            && source.contains("{{")
        {
            for license in [false, true] {
                let profile = if license {
                    "with-license"
                } else {
                    "without-license"
                };
                let rendered = match render_plugin_template(&source, license) {
                    Ok(value) => value,
                    Err(error) => {
                        failures.push(json!({"file": inventory.path, "profile": profile, "error": error.to_string(), "stage": "handlebars"}));
                        continue;
                    }
                };
                match syn::parse_file(&rendered) {
                    Ok(file) => {
                        let mut local = Inventory { path: inventory.path.clone(), ..Inventory::default() };
                        local.visit_file(&file);
                        templates.push(json!({"file": inventory.path, "profile": profile, "status": "parsed", "tests": local.tests.len(), "renderer": "handlebars 6.3.0", "lineOrigin": "rendered template"}));
                        for mut test in local.tests {
                            if let Some(existing) = inventory.tests.iter_mut().find(|entry| entry["file"] == test["file"] && entry["name"] == test["name"]) {
                                existing["templateProfiles"].as_array_mut().unwrap().push(json!(profile));
                            } else {
                                test["templateProfiles"] = json!([profile]);
                                test["lineOrigin"] = json!("rendered template");
                                inventory.tests.push(test);
                            }
                        }
                        inventory.macros.extend(local.macros);
                    }
                    Err(error) => failures.push(json!({"file": inventory.path, "profile": profile, "error": error.to_string(), "line": error.span().start().line, "stage": "rendered Rust"})),
                }
            }
            continue;
        }
        match syn::parse_file(&source) {
            Ok(file) => inventory.visit_file(&file),
            Err(error) => failures.push(json!({"file": inventory.path, "error": error.to_string(), "line": error.span().start().line})),
        }
    }
    println!(
        "{}",
        serde_json::to_string_pretty(&json!({
            "formatVersion": 1, "rustFiles": paths.len(), "tests": inventory.tests,
            "macroReview": inventory.macros, "parseErrors": failures,
            "templateParsing": templates,
            "coverageStatus": "inventory only; no test is counted as ported or passed",
        }))?
    );
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn inventories_syntax_and_conditions_without_comment_false_positives() {
        let file = syn::parse_file(
            r##"
            // #[test] fn imaginary() {}
            const NOTE: &str = "#[test] fn fake() {}";
            #[cfg(test)] mod tests {
                #[test] fn simple() {}
                #[tokio::test] async fn asynchronous() {}
                #[test_case(1)] #[test_case(2)] fn parameterized(value: i32) {}
                proptest! { fn generated(value: u32) { assert!(value == value); } }
                #[quickcheck] fn quick_generated(value: String) -> bool { !value.is_empty() }
            }
        "##,
        )
        .unwrap();
        let mut inventory = Inventory::default();
        inventory.visit_file(&file);
        assert_eq!(inventory.tests.len(), 5);
        assert_eq!(inventory.tests[0]["name"], "tests::simple");
        assert_eq!(inventory.tests[0]["moduleAttributes"][0], "cfg (test)");
        assert_eq!(
            inventory.tests[2]["attributes"].as_array().unwrap().len(),
            2
        );
        assert_eq!(inventory.macros.len(), 1);
        assert_eq!(inventory.tests[3]["name"], "tests::generated");
        assert_eq!(inventory.tests[3]["generator"], "proptest");
        assert_eq!(inventory.tests[4]["name"], "tests::quick_generated");
    }

    #[test]
    fn renders_both_template_branches_and_keeps_real_tests() {
        let source = "{{#if license_header}}#[test] fn with_header() {}{{else}}#[test] fn without_header() {}{{/if}} fn {{ plugin_name_snake_case }}() {}";
        for (license, expected) in [(true, "with_header"), (false, "without_header")] {
            let rendered = render_plugin_template(source, license).unwrap();
            let mut inventory = Inventory::default();
            inventory.visit_file(&syn::parse_file(&rendered).unwrap());
            assert_eq!(inventory.tests.len(), 1);
            assert_eq!(inventory.tests[0]["name"], expected);
        }
    }

    #[test]
    fn unknown_template_inputs_and_invalid_rendered_rust_are_errors() {
        assert!(render_plugin_template("fn {{ future_parameter }}() {}", false).is_err());
        assert!(syn::parse_file(
            &render_plugin_template("fn broken({{ plugin_name }}", false).unwrap()
        )
        .is_err());
    }
}
