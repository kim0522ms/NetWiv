// Comparison-only executable. Never shipped in the NetWiv product or CLI.
use std::io::{self, BufRead};
use urlpattern::{UrlPattern, UrlPatternInit, UrlPatternMatchInput};

fn main() {
    for line in io::stdin().lock().lines() {
        let mut row: serde_json::Value = serde_json::from_str(&line.unwrap()).unwrap();
        let compiled = UrlPatternInit::parse_constructor_string::<regex::Regex>(
            row["pattern"].as_str().unwrap(), None,
        ).and_then(|mut init| {
            // tauri-utils 2.9.3 acl::RemoteUrlPattern::from_str overrides.
            if init.search.as_ref().is_none_or(|s| s.is_empty()) { init.search = Some("*".into()); }
            if init.hash.as_ref().is_none_or(|s| s.is_empty()) { init.hash = Some("*".into()); }
            if init.pathname.as_ref().is_none_or(|s| s.is_empty() || s == "/") { init.pathname = Some("*".into()); }
            <UrlPattern>::parse(init, Default::default())
        });
        row["valid"] = compiled.is_ok().into();
        if let Ok(pattern) = compiled {
            if std::env::var_os("NETWIV_URLPATTERN_DEBUG").is_some() { eprintln!("{pattern:#?}"); }
            row["canonicalUrls"] = row["urls"].as_array().unwrap().iter().map(|url| {
                url.as_str().unwrap().parse::<url::Url>().ok().map(|url| serde_json::Value::String(url.to_string())).unwrap_or(serde_json::Value::Null)
            }).collect::<Vec<_>>().into();
            row["matches"] = row["urls"].as_array().unwrap().iter().map(|url| {
                url.as_str().unwrap().parse().ok().is_some_and(|url| {
                    pattern.test(UrlPatternMatchInput::Url(url)).unwrap_or(false)
                })
            }).collect::<Vec<_>>().into();
        }
        println!("{}", row);
    }
}
