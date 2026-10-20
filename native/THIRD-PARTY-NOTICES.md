# Native third-party notices

`netwiv-native` is licensed under Apache-2.0. The dependencies below retain their
respective licenses. This inventory covers every registry package resolved in the pinned
`native/Cargo.lock`, including target-specific and development dependencies.
Verbatim license and NOTICE candidates are stored by SHA-256 under
`native/third-party-licenses/texts`; `manifest.json` maps each package and original filename
to those bytes and explicitly marks packages for which no candidate was found.

Cargo.lock SHA-256: `a2d64d9bd2226eb7aaa21095ade9d43b9904a76479cfc6d446c3ed9c961001f7`

Regenerate this file with `node native/generate-third-party-notices.mjs` after changing
the native dependency graph.

| Package | Version | License | Source | Collected files |
| --- | --- | --- | --- | ---: |
| adler2 | 2.0.1 | 0BSD OR MIT OR Apache-2.0 | https://github.com/oyvindln/adler2 | 3 |
| adler32 | 1.2.0 | Zlib | https://github.com/remram44/adler32-rs | 1 |
| aead | 0.5.2 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits | 2 |
| aes | 0.8.4 | MIT OR Apache-2.0 | https://github.com/RustCrypto/block-ciphers | 2 |
| aes-gcm | 0.10.3 | Apache-2.0 OR MIT | https://github.com/RustCrypto/AEADs | 2 |
| aho-corasick | 1.1.5 | Unlicense OR MIT | https://github.com/BurntSushi/aho-corasick | 3 |
| allocator-api2 | 0.2.21 | MIT OR Apache-2.0 | https://github.com/zakarumych/allocator-api2 | 2 |
| android_system_properties | 0.1.6 | MIT OR Apache-2.0 | https://github.com/nical/android_system_properties | 2 |
| anyhow | 1.0.104 | MIT OR Apache-2.0 | https://github.com/dtolnay/anyhow | 2 |
| arboard | 3.6.1 | MIT OR Apache-2.0 | https://github.com/1Password/arboard | 2 |
| arrayvec | 0.7.8 | MIT OR Apache-2.0 | https://github.com/bluss/arrayvec | 2 |
| async-broadcast | 0.7.2 | MIT OR Apache-2.0 | https://github.com/smol-rs/async-broadcast | 2 |
| async-channel | 2.5.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-channel | 2 |
| async-executor | 1.14.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-executor | 2 |
| async-fs | 2.2.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-fs | 2 |
| async-io | 2.6.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-io | 2 |
| async-lock | 3.4.2 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-lock | 2 |
| async-process | 2.5.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-process | 2 |
| async-recursion | 1.1.1 | MIT OR Apache-2.0 | https://github.com/dcchut/async-recursion | 2 |
| async-signal | 0.2.14 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-signal | 2 |
| async-task | 4.7.1 | Apache-2.0 OR MIT | https://github.com/smol-rs/async-task | 2 |
| async-trait | 0.1.92 | MIT OR Apache-2.0 | https://github.com/dtolnay/async-trait | 2 |
| atk | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 2 |
| atk-sys | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 1 |
| atomic-waker | 1.1.2 | Apache-2.0 OR MIT | https://github.com/smol-rs/atomic-waker | 3 |
| autocfg | 1.5.1 | Apache-2.0 OR MIT | https://github.com/cuviper/autocfg | 2 |
| base16ct | 0.2.0 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats/tree/master/base16ct | 2 |
| base64 | 0.13.1 | MIT/Apache-2.0 | https://github.com/marshallpierce/rust-base64 | 2 |
| base64 | 0.21.7 | MIT OR Apache-2.0 | https://github.com/marshallpierce/rust-base64 | 2 |
| base64 | 0.22.1 | MIT OR Apache-2.0 | https://github.com/marshallpierce/rust-base64 | 2 |
| base64 | 0.23.1 | MIT OR Apache-2.0 | https://github.com/marshallpierce/rust-base64 | 2 |
| base64ct | 1.8.3 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats | 2 |
| bincode | 1.3.3 | MIT | https://github.com/servo/bincode | 1 |
| bit-set | 0.8.0 | Apache-2.0 OR MIT | https://github.com/contain-rs/bit-set | 2 |
| bit-vec | 0.8.0 | Apache-2.0 OR MIT | https://github.com/contain-rs/bit-vec | 2 |
| bitflags | 1.3.2 | MIT/Apache-2.0 | https://github.com/bitflags/bitflags | 2 |
| bitflags | 2.13.1 | MIT OR Apache-2.0 | https://github.com/bitflags/bitflags | 2 |
| blake2 | 0.10.6 | MIT OR Apache-2.0 | https://github.com/RustCrypto/hashes | 2 |
| blake2b_simd | 1.0.5 | MIT | https://github.com/oconnor663/blake2_simd | 1 |
| block-buffer | 0.10.4 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils | 2 |
| block-padding | 0.3.3 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils | 2 |
| block2 | 0.6.2 | MIT | https://github.com/madsmtm/objc2 | 1 |
| blocking | 1.7.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/blocking | 2 |
| bs58 | 0.5.1 | MIT/Apache-2.0 | https://github.com/Nullus157/bs58-rs | 2 |
| bumpalo | 3.20.3 | MIT OR Apache-2.0 | https://github.com/fitzgen/bumpalo | 2 |
| bytemuck | 1.25.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/Lokathor/bytemuck | 3 |
| byteorder | 1.5.0 | Unlicense OR MIT | https://github.com/BurntSushi/byteorder | 3 |
| byteorder-lite | 0.1.0 | Unlicense OR MIT | https://github.com/image-rs/byteorder-lite | 2 |
| bytes | 1.12.1 | MIT | https://github.com/tokio-rs/bytes | 1 |
| cairo-rs | 0.18.5 | MIT | https://github.com/gtk-rs/gtk-rs-core | 2 |
| cairo-sys-rs | 0.18.2 | MIT | https://github.com/gtk-rs/gtk-rs-core | 1 |
| cc | 1.4.5 | MIT OR Apache-2.0 | https://github.com/rust-lang/cc-rs | 2 |
| cesu8 | 1.1.0 | Apache-2.0/MIT | https://github.com/emk/cesu8-rs | 1 |
| cfb | 0.7.3 | MIT | https://github.com/mdsteele/rust-cfb | 1 |
| cfg_aliases | 0.2.2 | MIT | https://github.com/katharostech/cfg_aliases | 1 |
| cfg-expr | 0.15.8 | MIT OR Apache-2.0 | https://github.com/EmbarkStudios/cfg-expr | 2 |
| cfg-if | 1.0.4 | MIT OR Apache-2.0 | https://github.com/rust-lang/cfg-if | 2 |
| chacha20 | 0.9.1 | Apache-2.0 OR MIT | https://github.com/RustCrypto/stream-ciphers | 2 |
| chacha20poly1305 | 0.10.1 | Apache-2.0 OR MIT | https://github.com/RustCrypto/AEADs/tree/master/chacha20poly1305 | 2 |
| chrono | 0.4.45 | MIT OR Apache-2.0 | https://github.com/chronotope/chrono | 1 |
| cipher | 0.4.4 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits | 2 |
| clipboard-win | 5.4.1 | BSL-1.0 | https://github.com/DoumanAsh/clipboard-win | 1 |
| combine | 4.6.8 | MIT | https://github.com/Marwes/combine | 1 |
| concurrent-queue | 2.5.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/concurrent-queue | 2 |
| const-oid | 0.9.6 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats/tree/master/const-oid | 2 |
| constant_time_eq | 0.1.5 | CC0-1.0 | https://github.com/cesarb/constant_time_eq | 1 |
| constant_time_eq | 0.3.1 | CC0-1.0 OR MIT-0 OR Apache-2.0 | https://github.com/cesarb/constant_time_eq | 3 |
| constant_time_eq | 0.4.2 | CC0-1.0 OR MIT-0 OR Apache-2.0 | https://github.com/cesarb/constant_time_eq | 3 |
| cookie | 0.18.2 | MIT OR Apache-2.0 | https://github.com/SergioBenitez/cookie-rs | 2 |
| core-foundation | 0.10.1 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs | 2 |
| core-foundation-sys | 0.8.7 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs | 2 |
| core-graphics | 0.25.0 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs | 3 |
| core-graphics-types | 0.2.0 | MIT OR Apache-2.0 | https://github.com/servo/core-foundation-rs | 2 |
| cpufeatures | 0.2.17 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils | 2 |
| crc32fast | 1.5.1 | MIT OR Apache-2.0 | https://github.com/srijs/rust-crc32fast | 2 |
| crossbeam-channel | 0.5.17 | MIT OR Apache-2.0 | https://github.com/crossbeam-rs/crossbeam | 3 |
| crossbeam-utils | 0.8.23 | MIT OR Apache-2.0 | https://github.com/crossbeam-rs/crossbeam | 2 |
| crunchy | 0.2.4 | MIT | https://github.com/eira-fransham/crunchy | 1 |
| crypto-bigint | 0.5.5 | Apache-2.0 OR MIT | https://github.com/RustCrypto/crypto-bigint | 2 |
| crypto-common | 0.1.7 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits | 2 |
| cssparser | 0.36.0 | MPL-2.0 | https://github.com/servo/rust-cssparser | 1 |
| cssparser-macros | 0.6.1 | MPL-2.0 | https://github.com/servo/rust-cssparser | 1 |
| ctor | 0.8.0 | Apache-2.0 OR MIT | https://github.com/mmastrac/rust-ctor | 2 |
| ctor-proc-macro | 0.0.7 | Apache-2.0 OR MIT | https://github.com/mmastrac/rust-ctor | 2 |
| ctr | 0.9.2 | MIT OR Apache-2.0 | https://github.com/RustCrypto/block-modes | 2 |
| curve25519-dalek | 4.1.3 | BSD-3-Clause | https://github.com/dalek-cryptography/curve25519-dalek/tree/main/curve25519-dalek | 1 |
| curve25519-dalek-derive | 0.1.1 | MIT/Apache-2.0 | https://github.com/dalek-cryptography/curve25519-dalek | 2 |
| darling | 0.23.0 | MIT | https://github.com/TedDriggs/darling | 1 |
| darling_core | 0.23.0 | MIT | https://github.com/TedDriggs/darling | 1 |
| darling_macro | 0.23.0 | MIT | https://github.com/TedDriggs/darling | 1 |
| dary_heap | 0.3.9 | MIT OR Apache-2.0 | https://github.com/hanmertens/dary_heap | 2 |
| dbus | 0.9.12 | Apache-2.0/MIT | https://github.com/diwic/dbus-rs | 2 |
| defmt | 1.1.1 | MIT OR Apache-2.0 | https://github.com/knurling-rs/defmt | 2 |
| defmt-macros | 1.1.1 | MIT OR Apache-2.0 | https://github.com/knurling-rs/defmt | 2 |
| defmt-parser | 1.0.0 | MIT OR Apache-2.0 | https://github.com/knurling-rs/defmt | 2 |
| der | 0.7.10 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats/tree/master/der | 2 |
| deranged | 0.5.8 | MIT OR Apache-2.0 | https://github.com/jhpratt/deranged | 2 |
| derive_more | 2.1.1 | MIT | https://github.com/JelteF/derive_more | 1 |
| derive_more-impl | 2.1.1 | MIT | https://github.com/JelteF/derive_more | 1 |
| digest | 0.10.7 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits | 2 |
| dirs | 4.0.0 | MIT OR Apache-2.0 | https://github.com/soc/dirs-rs | 2 |
| dirs | 6.0.0 | MIT OR Apache-2.0 | https://github.com/soc/dirs-rs | 2 |
| dirs | 7.0.0 | MIT OR Apache-2.0 | https://codeberg.org/dirs/dirs-rs | 2 |
| dirs-next | 2.0.0 | MIT OR Apache-2.0 | https://github.com/xdg-rs/dirs | 2 |
| dirs-sys | 0.3.7 | MIT OR Apache-2.0 | https://github.com/dirs-dev/dirs-sys-rs | 2 |
| dirs-sys | 0.5.0 | MIT OR Apache-2.0 | https://github.com/dirs-dev/dirs-sys-rs | 2 |
| dirs-sys-next | 0.1.2 | MIT OR Apache-2.0 | https://github.com/xdg-rs/dirs/tree/master/dirs-sys | 2 |
| dispatch2 | 0.3.1 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| displaydoc | 0.2.7 | MIT OR Apache-2.0 | https://github.com/yaahc/displaydoc | 2 |
| dlopen2 | 0.8.2 | MIT | https://github.com/OpenByteDev/dlopen2 | 1 |
| dlopen2_derive | 0.4.3 | MIT | https://github.com/OpenByteDev/dlopen2 | 1 |
| dom_query | 0.27.0 | MIT | https://github.com/niklak/dom_query | 1 |
| downcast-rs | 1.2.1 | MIT/Apache-2.0 | https://github.com/marcianx/downcast-rs | 2 |
| dpi | 0.1.2 | Apache-2.0 AND MIT | https://github.com/rust-windowing/winit | 2 |
| dtoa | 1.0.11 | MIT OR Apache-2.0 | https://github.com/dtolnay/dtoa | 2 |
| dtoa-short | 0.3.5 | MPL-2.0 | https://github.com/upsuper/dtoa-short | 1 |
| dtor | 0.3.0 | Apache-2.0 OR MIT | https://github.com/mmastrac/rust-ctor | 2 |
| dtor-proc-macro | 0.0.6 | Apache-2.0 OR MIT | https://github.com/mmastrac/rust-ctor | 2 |
| dunce | 1.0.5 | CC0-1.0 OR MIT-0 OR Apache-2.0 | https://gitlab.com/kornelski/dunce | 1 |
| dyn-clone | 1.0.20 | MIT OR Apache-2.0 | https://github.com/dtolnay/dyn-clone | 2 |
| ecdsa | 0.16.9 | Apache-2.0 OR MIT | https://github.com/RustCrypto/signatures/tree/master/ecdsa | 2 |
| ed25519 | 2.2.3 | Apache-2.0 OR MIT | https://github.com/RustCrypto/signatures/tree/master/ed25519 | 2 |
| ed25519-zebra | 4.2.0 | MIT OR Apache-2.0 | https://github.com/ZcashFoundation/ed25519-zebra | 2 |
| elliptic-curve | 0.13.8 | Apache-2.0 OR MIT | https://github.com/RustCrypto/traits/tree/master/elliptic-curve | 2 |
| embed-resource | 3.0.11 | MIT | https://github.com/nabijaczleweli/rust-embed-resource | 1 |
| endi | 1.1.1 | MIT | https://github.com/zeenix/endi | 1 |
| enumflags2 | 0.7.12 | MIT OR Apache-2.0 | https://github.com/meithecatte/enumflags2 | 2 |
| enumflags2_derive | 0.7.12 | MIT OR Apache-2.0 | https://github.com/meithecatte/enumflags2 | 2 |
| equivalent | 1.0.2 | Apache-2.0 OR MIT | https://github.com/indexmap-rs/equivalent | 2 |
| erased-serde | 0.4.10 | MIT OR Apache-2.0 | https://github.com/dtolnay/erased-serde | 2 |
| errno | 0.3.14 | MIT OR Apache-2.0 | https://github.com/lambda-fairy/rust-errno | 2 |
| error-code | 3.4.0 | BSL-1.0 | https://github.com/DoumanAsh/error-code | 1 |
| event-listener | 5.4.2 | Apache-2.0 OR MIT | https://github.com/smol-rs/event-listener | 2 |
| event-listener-strategy | 0.5.4 | Apache-2.0 OR MIT | https://github.com/smol-rs/event-listener-strategy | 2 |
| fastrand | 2.5.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/fastrand | 2 |
| fax | 0.2.7 | MIT | https://github.com/pdf-rs/fax | 1 |
| fdeflate | 0.3.7 | MIT OR Apache-2.0 | https://github.com/image-rs/fdeflate | 2 |
| ff | 0.13.1 | MIT/Apache-2.0 | https://github.com/zkcrypto/ff | 2 |
| fiat-crypto | 0.2.9 | MIT OR Apache-2.0 OR BSD-1-Clause | https://github.com/mit-plv/fiat-crypto | 4 |
| field-offset | 0.3.6 | MIT OR Apache-2.0 | https://github.com/Diggsey/rust-field-offset | 2 |
| filetime | 0.2.29 | MIT/Apache-2.0 | https://github.com/alexcrichton/filetime | 2 |
| find-msvc-tools | 0.1.12 | MIT OR Apache-2.0 | https://github.com/rust-lang/cc-rs | 2 |
| fixedbitset | 0.5.7 | MIT OR Apache-2.0 | https://github.com/petgraph/fixedbitset | 2 |
| flate2 | 1.1.10 | MIT OR Apache-2.0 | https://github.com/rust-lang/flate2-rs | 2 |
| fnv | 1.0.7 | Apache-2.0 / MIT | https://github.com/servo/rust-fnv | 2 |
| foldhash | 0.1.5 | Zlib | https://github.com/orlp/foldhash | 1 |
| foldhash | 0.2.0 | Zlib | https://github.com/orlp/foldhash | 1 |
| foreign-types | 0.5.0 | MIT/Apache-2.0 | https://github.com/sfackler/foreign-types | 2 |
| foreign-types-macros | 0.2.4 | MIT/Apache-2.0 | https://github.com/sfackler/foreign-types | 2 |
| foreign-types-shared | 0.3.1 | MIT/Apache-2.0 | https://github.com/sfackler/foreign-types | 2 |
| form_urlencoded | 1.2.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-url | 2 |
| futures | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-channel | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-core | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-executor | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-io | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-lite | 2.6.1 | Apache-2.0 OR MIT | https://github.com/smol-rs/futures-lite | 3 |
| futures-macro | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-sink | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-task | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| futures-util | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/futures-rs | 2 |
| gdk | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 2 |
| gdk-pixbuf | 0.18.5 | MIT | https://github.com/gtk-rs/gtk-rs-core | 2 |
| gdk-pixbuf-sys | 0.18.0 | MIT | https://github.com/gtk-rs/gtk-rs-core | 1 |
| gdk-sys | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 1 |
| gdkwayland-sys | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 1 |
| gdkx11 | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 2 |
| gdkx11-sys | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 1 |
| generic-array | 0.14.7 | MIT | https://github.com/fizyk20/generic-array.git | 1 |
| gethostname | 1.1.0 | Apache-2.0 | https://codeberg.org/swsnr/gethostname.rs.git | 1 |
| getrandom | 0.2.17 | MIT OR Apache-2.0 | https://github.com/rust-random/getrandom | 2 |
| getrandom | 0.3.4 | MIT OR Apache-2.0 | https://github.com/rust-random/getrandom | 2 |
| getrandom | 0.4.3 | MIT OR Apache-2.0 | https://github.com/rust-random/getrandom | 2 |
| ghash | 0.5.1 | Apache-2.0 OR MIT | https://github.com/RustCrypto/universal-hashes | 2 |
| gio | 0.18.4 | MIT | https://github.com/gtk-rs/gtk-rs-core | 2 |
| gio-sys | 0.18.1 | MIT | https://github.com/gtk-rs/gtk-rs-core | 1 |
| glib | 0.18.5 | MIT | https://github.com/gtk-rs/gtk-rs-core | 2 |
| glib-macros | 0.18.5 | MIT | https://github.com/gtk-rs/gtk-rs-core | 2 |
| glib-sys | 0.18.1 | MIT | https://github.com/gtk-rs/gtk-rs-core | 1 |
| glob | 0.3.4 | MIT OR Apache-2.0 | https://github.com/rust-lang/glob | 2 |
| global-hotkey | 0.8.0 | Apache-2.0 OR MIT | https://github.com/tauri-apps/global-hotkey | 3 |
| gobject-sys | 0.18.0 | MIT | https://github.com/gtk-rs/gtk-rs-core | 1 |
| group | 0.13.0 | MIT/Apache-2.0 | https://github.com/zkcrypto/group | 3 |
| gtk | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 2 |
| gtk-sys | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 1 |
| gtk3-macros | 0.18.2 | MIT | https://github.com/gtk-rs/gtk3-rs | 2 |
| half | 2.7.1 | MIT OR Apache-2.0 | https://github.com/VoidStarKat/half-rs | 2 |
| hashbrown | 0.12.3 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown | 2 |
| hashbrown | 0.15.5 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown | 2 |
| hashbrown | 0.16.1 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown | 2 |
| hashbrown | 0.17.1 | MIT OR Apache-2.0 | https://github.com/rust-lang/hashbrown | 2 |
| heck | 0.4.1 | MIT OR Apache-2.0 | https://github.com/withoutboats/heck | 2 |
| heck | 0.5.0 | MIT OR Apache-2.0 | https://github.com/withoutboats/heck | 2 |
| hermit-abi | 0.5.3 | MIT OR Apache-2.0 | https://github.com/hermit-os/hermit-rs | 2 |
| hex | 0.4.3 | MIT OR Apache-2.0 | https://github.com/KokaKiwi/rust-hex | 2 |
| hkdf | 0.12.4 | MIT OR Apache-2.0 | https://github.com/RustCrypto/KDFs/ | 2 |
| hmac | 0.12.1 | MIT OR Apache-2.0 | https://github.com/RustCrypto/MACs | 2 |
| html5ever | 0.38.0 | MIT OR Apache-2.0 | https://github.com/servo/html5ever | 2 |
| http | 1.3.1 | MIT OR Apache-2.0 | https://github.com/hyperium/http | 2 |
| httparse | 1.10.1 | MIT OR Apache-2.0 | https://github.com/seanmonstar/httparse | 2 |
| iana-time-zone | 0.1.65 | MIT OR Apache-2.0 | https://github.com/strawlab/iana-time-zone | 2 |
| iana-time-zone-haiku | 0.1.2 | MIT OR Apache-2.0 | https://github.com/strawlab/iana-time-zone | 2 |
| icu_collections | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| icu_locale_core | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| icu_normalizer | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| icu_normalizer_data | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| icu_properties | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| icu_properties_data | 2.3.0 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| icu_provider | 2.3.1 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| ident_case | 1.0.1 | MIT/Apache-2.0 | https://github.com/TedDriggs/ident_case | 1 |
| idna | 1.1.0 | MIT OR Apache-2.0 | https://github.com/servo/rust-url/ | 2 |
| idna_adapter | 1.2.2 | Apache-2.0 OR MIT | https://github.com/hsivonen/idna_adapter | 2 |
| image | 0.25.10 | MIT OR Apache-2.0 | https://github.com/image-rs/image | 2 |
| indexmap | 1.9.3 | Apache-2.0 OR MIT | https://github.com/bluss/indexmap | 2 |
| indexmap | 2.14.2 | Apache-2.0 OR MIT | https://github.com/indexmap-rs/indexmap | 2 |
| infer | 0.19.0 | MIT | https://github.com/bojand/infer | 1 |
| inout | 0.1.4 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils | 2 |
| iota_stronghold | 2.1.0 | Apache-2.0 | https://github.com/iotaledger/stronghold.rs | 1 |
| iota-crypto | 0.23.2 | Apache-2.0 | https://github.com/iotaledger/crypto.rs | 1 |
| iterator-sorted | 0.1.0 | Apache-2.0 | https://github.com/iotaledger/common-rs | 1 |
| itoa | 1.0.18 | MIT OR Apache-2.0 | https://github.com/dtolnay/itoa | 2 |
| javascriptcore-rs | 1.1.2 | MIT | https://github.com/tauri-apps/javascriptcore-rs | 1 |
| javascriptcore-rs-sys | 1.1.1 | MIT | https://github.com/tauri-apps/javascriptcore-rs | 1 |
| jiff | 0.2.35 | Unlicense OR MIT | https://github.com/BurntSushi/jiff | 3 |
| jiff-core | 0.1.0 | Unlicense OR MIT | https://github.com/BurntSushi/jiff | 3 |
| jiff-static | 0.2.35 | Unlicense OR MIT | https://github.com/BurntSushi/jiff | 3 |
| jiff-tzdb | 0.1.8 | Unlicense OR MIT | https://github.com/BurntSushi/jiff | 3 |
| jiff-tzdb-platform | 0.1.3 | Unlicense OR MIT | https://github.com/BurntSushi/jiff | 3 |
| jni | 0.21.1 | MIT/Apache-2.0 | https://github.com/jni-rs/jni-rs | 2 |
| jni-sys | 0.3.1 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-sys | 2 |
| jni-sys | 0.4.1 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-sys | 2 |
| jni-sys-macros | 0.4.1 | MIT OR Apache-2.0 | https://github.com/jni-rs/jni-sys | 2 |
| js-sys | 0.3.105 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/js-sys | 2 |
| json-patch | 3.0.1 | MIT/Apache-2.0 | https://github.com/idubrov/json-patch | 2 |
| jsonptr | 0.6.3 | MIT OR Apache-2.0 | https://github.com/chanced/jsonptr | 2 |
| k256 | 0.13.4 | Apache-2.0 OR MIT | https://github.com/RustCrypto/elliptic-curves/tree/master/k256 | 2 |
| keyboard-types | 0.7.0 | MIT OR Apache-2.0 | https://github.com/pyfisch/keyboard-types | 2 |
| keyboard-types | 0.8.3 | MIT OR Apache-2.0 | https://github.com/rust-windowing/keyboard-types | 2 |
| libappindicator | 0.9.0 | Apache-2.0 OR MIT | https://crates.io/crates/libappindicator/0.9.0 | 2 |
| libappindicator-sys | 0.9.0 | Apache-2.0 OR MIT | https://crates.io/crates/libappindicator-sys/0.9.0 | 2 |
| libc | 0.2.189 | MIT OR Apache-2.0 | https://github.com/rust-lang/libc | 2 |
| libdbus-sys | 0.2.7 | Apache-2.0/MIT | https://github.com/diwic/dbus-rs | 2 |
| libflate | 2.3.2 | MIT | https://github.com/sile/libflate | 1 |
| libflate_lz77 | 2.3.0 | MIT | https://github.com/sile/libflate | 1 |
| libloading | 0.7.4 | ISC | https://github.com/nagisa/rust_libloading/ | 1 |
| libredox | 0.1.23 | MIT | https://gitlab.redox-os.org/redox-os/libredox.git | 1 |
| libsodium-sys-stable | 1.24.0 | MIT OR Apache-2.0 | https://github.com/jedisct1/libsodium-sys-stable | 3 |
| linux-raw-sys | 0.12.1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | https://github.com/sunfishcode/linux-raw-sys | 4 |
| litemap | 0.8.3 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| lock_api | 0.4.14 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot | 2 |
| log | 0.4.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/log | 2 |
| mac-notification-sys | 0.6.15 | MIT/Apache-2.0 | https://github.com/h4llow3En/mac-notification-sys | 1 |
| markup5ever | 0.38.0 | MIT OR Apache-2.0 | https://github.com/servo/html5ever | 2 |
| memchr | 2.8.3 | Unlicense OR MIT | https://github.com/BurntSushi/memchr | 3 |
| memoffset | 0.6.5 | MIT | https://github.com/Gilnaa/memoffset | 1 |
| memoffset | 0.9.1 | MIT | https://github.com/Gilnaa/memoffset | 1 |
| minisign-verify | 0.2.5 | MIT | https://github.com/jedisct1/rust-minisign-verify | 1 |
| miniz_oxide | 0.8.9 | MIT OR Zlib OR Apache-2.0 | https://github.com/Frommi/miniz_oxide/tree/master/miniz_oxide | 4 |
| miniz_oxide | 0.9.1 | MIT OR Zlib OR Apache-2.0 | https://github.com/Frommi/miniz_oxide/tree/master/miniz_oxide | 4 |
| moxcms | 0.8.1 | BSD-3-Clause OR Apache-2.0 | https://github.com/awxkee/moxcms.git | 2 |
| muda | 0.20.0 | Apache-2.0 OR MIT | https://github.com/tauri-apps/muda | 3 |
| ndk | 0.9.0 | MIT OR Apache-2.0 | https://github.com/rust-mobile/ndk | 2 |
| ndk-context | 0.1.1 | MIT OR Apache-2.0 | https://github.com/rust-windowing/android-ndk-rs | 2 |
| ndk-sys | 0.6.0+11769913 | MIT OR Apache-2.0 | https://github.com/rust-mobile/ndk | 2 |
| new_debug_unreachable | 1.0.6 | MIT | https://github.com/mbrubeck/rust-debug-unreachable | 1 |
| nix | 0.24.3 | MIT | https://github.com/nix-rust/nix | 1 |
| nix | 0.29.0 | MIT | https://github.com/nix-rust/nix | 1 |
| no_std_io2 | 0.9.4 | Apache-2.0 OR MIT | https://github.com/wcampbell0x2a/no-std-io2 | 2 |
| nom | 8.0.0 | MIT | https://github.com/rust-bakery/nom | 1 |
| notify-rust | 4.11.0 | MIT/Apache-2.0 | https://github.com/hoodie/notify-rust | 2 |
| num_enum | 0.7.6 | BSD-3-Clause OR MIT OR Apache-2.0 | https://github.com/illicitonion/num_enum | 3 |
| num_enum_derive | 0.7.6 | BSD-3-Clause OR MIT OR Apache-2.0 | https://github.com/illicitonion/num_enum | 3 |
| num-conv | 0.2.2 | MIT OR Apache-2.0 | https://github.com/jhpratt/num-conv | 2 |
| num-traits | 0.2.19 | MIT OR Apache-2.0 | https://github.com/rust-num/num-traits | 2 |
| objc2 | 0.6.4 | MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-app-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-cloud-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-core-data | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-core-foundation | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-core-graphics | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-core-image | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-core-location | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-core-text | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-encode | 4.1.0 | MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-exception-helper | 0.1.1 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-foundation | 0.3.2 | MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-io-surface | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-quartz-core | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-ui-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-user-notifications | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| objc2-web-kit | 0.3.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/madsmtm/objc2 | 1 |
| once_cell | 1.21.4 | MIT OR Apache-2.0 | https://github.com/matklad/once_cell | 2 |
| opaque-debug | 0.3.1 | MIT OR Apache-2.0 | https://github.com/RustCrypto/utils | 2 |
| option-ext | 0.2.0 | MPL-2.0 | https://github.com/soc/option-ext.git | 1 |
| ordered-stream | 0.2.0 | MIT OR Apache-2.0 | https://github.com/danieldg/ordered-stream | 2 |
| os_pipe | 1.2.3 | MIT | https://github.com/oconnor663/os_pipe.rs | 1 |
| pango | 0.18.3 | MIT | https://github.com/gtk-rs/gtk-rs-core | 2 |
| pango-sys | 0.18.0 | MIT | https://github.com/gtk-rs/gtk-rs-core | 1 |
| parking | 2.2.1 | Apache-2.0 OR MIT | https://github.com/smol-rs/parking | 3 |
| parking_lot | 0.12.5 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot | 2 |
| parking_lot_core | 0.9.12 | MIT OR Apache-2.0 | https://github.com/Amanieu/parking_lot | 2 |
| paste | 1.0.15 | MIT OR Apache-2.0 | https://github.com/dtolnay/paste | 2 |
| pbkdf2 | 0.12.2 | MIT OR Apache-2.0 | https://github.com/RustCrypto/password-hashes/tree/master/pbkdf2 | 2 |
| percent-encoding | 2.3.2 | MIT OR Apache-2.0 | https://github.com/servo/rust-url/ | 2 |
| petgraph | 0.8.3 | MIT OR Apache-2.0 | https://github.com/petgraph/petgraph | 2 |
| phf | 0.13.1 | MIT | https://github.com/rust-phf/rust-phf | 1 |
| phf_codegen | 0.13.1 | MIT | https://github.com/rust-phf/rust-phf | 1 |
| phf_generator | 0.13.1 | MIT | https://github.com/rust-phf/rust-phf | 1 |
| phf_macros | 0.13.1 | MIT | https://github.com/rust-phf/rust-phf | 1 |
| phf_shared | 0.13.1 | MIT | https://github.com/rust-phf/rust-phf | 1 |
| pin-project-lite | 0.2.17 | Apache-2.0 OR MIT | https://github.com/taiki-e/pin-project-lite | 2 |
| piper | 0.2.5 | MIT OR Apache-2.0 | https://github.com/smol-rs/piper | 2 |
| pkcs8 | 0.10.2 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats/tree/master/pkcs8 | 2 |
| pkg-config | 0.3.34 | MIT OR Apache-2.0 | https://github.com/rust-lang/pkg-config-rs | 2 |
| plist | 1.10.1 | MIT | https://github.com/ebarnard/rust-plist/ | 1 |
| png | 0.17.16 | MIT OR Apache-2.0 | https://github.com/image-rs/image-png | 2 |
| png | 0.18.1 | MIT OR Apache-2.0 | https://github.com/image-rs/image-png | 2 |
| polling | 3.11.0 | Apache-2.0 OR MIT | https://github.com/smol-rs/polling | 2 |
| poly1305 | 0.8.0 | Apache-2.0 OR MIT | https://github.com/RustCrypto/universal-hashes | 2 |
| polyval | 0.6.2 | Apache-2.0 OR MIT | https://github.com/RustCrypto/universal-hashes | 2 |
| portable-atomic | 1.15.0 | Apache-2.0 OR MIT | https://github.com/taiki-e/portable-atomic | 2 |
| portable-atomic-util | 0.2.8 | Apache-2.0 OR MIT | https://github.com/taiki-e/portable-atomic-util | 2 |
| potential_utf | 0.1.6 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| powerfmt | 0.2.0 | MIT OR Apache-2.0 | https://github.com/jhpratt/powerfmt | 2 |
| ppv-lite86 | 0.2.21 | MIT OR Apache-2.0 | https://github.com/cryptocorrosion/cryptocorrosion | 2 |
| precomputed-hash | 0.1.1 | MIT | https://github.com/emilio/precomputed-hash | 1 |
| proc-macro-crate | 1.3.1 | MIT OR Apache-2.0 | https://github.com/bkchr/proc-macro-crate | 2 |
| proc-macro-crate | 2.0.2 | MIT OR Apache-2.0 | https://github.com/bkchr/proc-macro-crate | 2 |
| proc-macro-crate | 3.5.0 | MIT OR Apache-2.0 | https://github.com/bkchr/proc-macro-crate | 2 |
| proc-macro-error | 1.0.4 | MIT OR Apache-2.0 | https://gitlab.com/CreepySkeleton/proc-macro-error | 2 |
| proc-macro-error-attr | 1.0.4 | MIT OR Apache-2.0 | https://gitlab.com/CreepySkeleton/proc-macro-error | 2 |
| proc-macro2 | 1.0.107 | MIT OR Apache-2.0 | https://github.com/dtolnay/proc-macro2 | 2 |
| pxfm | 0.1.30 | BSD-3-Clause OR Apache-2.0 | https://github.com/awxkee/pxfm | 2 |
| quick-error | 2.0.1 | MIT/Apache-2.0 | http://github.com/tailhook/quick-error | 2 |
| quick-xml | 0.31.0 | MIT | https://github.com/tafia/quick-xml | 1 |
| quick-xml | 0.41.0 | MIT | https://github.com/tafia/quick-xml | 1 |
| quick-xml | 0.42.0 | MIT | https://github.com/tafia/quick-xml | 1 |
| quote | 1.0.47 | MIT OR Apache-2.0 | https://github.com/dtolnay/quote | 2 |
| r-efi | 5.3.0 | MIT OR Apache-2.0 OR LGPL-2.1-or-later | https://github.com/r-efi/r-efi | 1 |
| r-efi | 6.0.0 | MIT OR Apache-2.0 OR LGPL-2.1-or-later | https://github.com/r-efi/r-efi | 1 |
| rand | 0.8.8 | MIT OR Apache-2.0 | https://github.com/rust-random/rand | 3 |
| rand_chacha | 0.3.1 | MIT OR Apache-2.0 | https://github.com/rust-random/rand | 3 |
| rand_core | 0.6.4 | MIT OR Apache-2.0 | https://github.com/rust-random/rand | 3 |
| raw-window-handle | 0.6.2 | MIT OR Apache-2.0 OR Zlib | https://github.com/rust-windowing/raw-window-handle | 3 |
| redox_syscall | 0.5.18 | MIT | https://gitlab.redox-os.org/redox-os/syscall | 1 |
| redox_users | 0.4.6 | MIT | https://gitlab.redox-os.org/redox-os/users | 1 |
| redox_users | 0.5.2 | MIT | https://gitlab.redox-os.org/redox-os/users | 1 |
| ref-cast | 1.0.27 | MIT OR Apache-2.0 | https://github.com/dtolnay/ref-cast | 2 |
| ref-cast-impl | 1.0.27 | MIT OR Apache-2.0 | https://github.com/dtolnay/ref-cast | 2 |
| regex | 1.13.1 | MIT OR Apache-2.0 | https://github.com/rust-lang/regex | 2 |
| regex-automata | 0.4.18 | MIT OR Apache-2.0 | https://github.com/rust-lang/regex | 2 |
| regex-syntax | 0.8.11 | MIT OR Apache-2.0 | https://github.com/rust-lang/regex | 2 |
| rfc6979 | 0.4.0 | Apache-2.0 OR MIT | https://github.com/RustCrypto/signatures/tree/master/rfc6979 | 2 |
| rfd | 0.16.0 | MIT | https://github.com/PolyMeilex/rfd | 1 |
| rle-decode-fast | 1.0.3 | MIT OR Apache-2.0 | https://github.com/WanzenBug/rle-decode-helper | 2 |
| rust-argon2 | 1.0.0 | MIT/Apache-2.0 | https://github.com/sru-systems/rust-argon2 | 2 |
| rust-argon2 | 2.1.0 | MIT/Apache-2.0 | https://github.com/sru-systems/rust-argon2 | 2 |
| rustc_version | 0.4.1 | MIT OR Apache-2.0 | https://github.com/djc/rustc-version-rs | 2 |
| rustc-hash | 2.1.3 | Apache-2.0 OR MIT | https://github.com/rust-lang/rustc-hash | 2 |
| rustix | 1.1.5 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | https://github.com/bytecodealliance/rustix | 4 |
| rustversion | 1.0.23 | MIT OR Apache-2.0 | https://github.com/dtolnay/rustversion | 2 |
| salsa20 | 0.10.2 | MIT OR Apache-2.0 | https://github.com/RustCrypto/stream-ciphers | 2 |
| same-file | 1.0.6 | Unlicense/MIT | https://github.com/BurntSushi/same-file | 3 |
| scc | 2.4.0 | Apache-2.0 | https://github.com/wvwwvwwv/scalable-concurrent-containers/ | 1 |
| schemars | 0.9.0 | MIT | https://github.com/GREsau/schemars | 1 |
| schemars | 1.2.2 | MIT | https://github.com/GREsau/schemars | 1 |
| scopeguard | 1.2.0 | MIT OR Apache-2.0 | https://github.com/bluss/scopeguard | 2 |
| scrypt | 0.11.0 | MIT OR Apache-2.0 | https://github.com/RustCrypto/password-hashes/tree/master/scrypt | 2 |
| sdd | 3.0.10 | Apache-2.0 | https://github.com/wvwwvwwv/scalable-delayed-dealloc/ | 1 |
| sec1 | 0.7.3 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats/tree/master/sec1 | 2 |
| selectors | 0.36.1 | MPL-2.0 | https://github.com/servo/stylo | 1 |
| semver | 1.0.28 | MIT OR Apache-2.0 | https://github.com/dtolnay/semver | 2 |
| serde | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde | 2 |
| serde_core | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde | 2 |
| serde_derive | 1.0.229 | MIT OR Apache-2.0 | https://github.com/serde-rs/serde | 2 |
| serde_json | 1.0.151 | MIT OR Apache-2.0 | https://github.com/serde-rs/json | 2 |
| serde_repr | 0.1.21 | MIT OR Apache-2.0 | https://github.com/dtolnay/serde-repr | 2 |
| serde_spanned | 0.6.9 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| serde_spanned | 1.1.1 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| serde_with | 3.22.0 | MIT OR Apache-2.0 | https://github.com/jonasbb/serde_with/ | 2 |
| serde_with_macros | 3.22.0 | MIT OR Apache-2.0 | https://github.com/jonasbb/serde_with/ | 2 |
| serde-untagged | 0.1.9 | MIT OR Apache-2.0 | https://github.com/dtolnay/serde-untagged | 2 |
| serial_test | 3.2.0 | MIT | https://github.com/palfrey/serial_test/ | 1 |
| serial_test_derive | 3.2.0 | MIT | https://github.com/palfrey/serial_test/ | 1 |
| servo_arc | 0.4.3 | MIT OR Apache-2.0 | https://github.com/servo/stylo | 2 |
| sha1 | 0.10.7 | MIT OR Apache-2.0 | https://github.com/RustCrypto/hashes | 2 |
| sha2 | 0.10.9 | MIT OR Apache-2.0 | https://github.com/RustCrypto/hashes | 2 |
| shlex | 2.0.1 | MIT OR Apache-2.0 | https://github.com/comex/rust-shlex | 2 |
| signal-hook-registry | 1.4.8 | MIT OR Apache-2.0 | https://github.com/vorner/signal-hook | 2 |
| signature | 2.2.0 | Apache-2.0 OR MIT | https://github.com/RustCrypto/traits/tree/master/signature | 2 |
| simd-adler32 | 0.3.10 | MIT | https://github.com/mcountryman/simd-adler32 | 1 |
| siphasher | 1.0.3 | MIT/Apache-2.0 | https://github.com/jedisct1/rust-siphash | 1 |
| slab | 0.4.12 | MIT | https://github.com/tokio-rs/slab | 1 |
| smallvec | 1.16.0 | MIT OR Apache-2.0 | https://github.com/servo/rust-smallvec | 2 |
| soup3 | 0.5.0 | MIT | https://gitlab.gnome.org/World/Rust/soup3-rs | 1 |
| soup3-sys | 0.5.0 | MIT | https://gitlab.gnome.org/World/Rust/soup3-rs | 1 |
| spki | 0.7.3 | Apache-2.0 OR MIT | https://github.com/RustCrypto/formats/tree/master/spki | 2 |
| stable_deref_trait | 1.2.1 | MIT OR Apache-2.0 | https://github.com/storyyeller/stable_deref_trait | 2 |
| static_assertions | 1.1.0 | MIT OR Apache-2.0 | https://github.com/nvzqz/static-assertions-rs | 2 |
| string_cache | 0.9.0 | MIT OR Apache-2.0 | https://github.com/servo/string-cache | 2 |
| string_cache_codegen | 0.6.1 | MIT OR Apache-2.0 | https://github.com/servo/string-cache | 2 |
| stronghold_engine | 2.0.1 | Apache-2.0 | https://github.com/iotaledger/stronghold.rs | 1 |
| stronghold-derive | 1.0.0 | Apache-2.0 | https://github.com/iotaledger/stronghold.rs | 1 |
| stronghold-runtime | 2.0.1 | Apache-2.0 | https://github.com/iotaledger/stronghold.rs | 1 |
| stronghold-utils | 1.0.0 | Apache-2.0 | https://github.com/iotaledger/stronghold.rs | 1 |
| strsim | 0.11.1 | MIT | https://github.com/rapidfuzz/strsim-rs | 1 |
| subtle | 2.6.1 | BSD-3-Clause | https://github.com/dalek-cryptography/subtle | 1 |
| syn | 1.0.109 | MIT OR Apache-2.0 | https://github.com/dtolnay/syn | 2 |
| syn | 2.0.119 | MIT OR Apache-2.0 | https://github.com/dtolnay/syn | 2 |
| syn | 3.0.5 | MIT OR Apache-2.0 | https://github.com/dtolnay/syn | 2 |
| synstructure | 0.13.2 | MIT | https://github.com/mystor/synstructure | 1 |
| system-deps | 6.2.2 | MIT OR Apache-2.0 | https://github.com/gdesmott/system-deps | 2 |
| tao | 0.36.0 | Apache-2.0 | https://github.com/tauri-apps/tao | 2 |
| tao-macros | 0.1.4 | MIT OR Apache-2.0 | https://github.com/tauri-apps/tao | 3 |
| tar | 0.4.46 | MIT OR Apache-2.0 | https://github.com/composefs/tar-rs | 2 |
| target-lexicon | 0.12.16 | Apache-2.0 WITH LLVM-exception | https://github.com/bytecodealliance/target-lexicon | 1 |
| tauri-utils | 2.9.3 | Apache-2.0 OR MIT | https://github.com/tauri-apps/tauri | 2 |
| tauri-winrt-notification | 0.2.1 | MIT OR Apache-2.0 | https://github.com/tauri-apps/winrt-notification | 3 |
| tempfile | 3.27.0 | MIT OR Apache-2.0 | https://github.com/Stebalien/tempfile | 2 |
| tendril | 0.5.1 | MIT OR Apache-2.0 | https://github.com/servo/html5ever | 2 |
| thiserror | 1.0.69 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror | 2 |
| thiserror | 2.0.20 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror | 2 |
| thiserror-impl | 1.0.69 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror | 2 |
| thiserror-impl | 2.0.20 | MIT OR Apache-2.0 | https://github.com/dtolnay/thiserror | 2 |
| tiff | 0.11.3 | MIT | https://github.com/image-rs/image-tiff | 1 |
| time | 0.3.55 | MIT OR Apache-2.0 | https://github.com/time-rs/time | 2 |
| time-core | 0.1.9 | MIT OR Apache-2.0 | https://github.com/time-rs/time | 2 |
| time-macros | 0.2.32 | MIT OR Apache-2.0 | https://github.com/time-rs/time | 2 |
| tiny-keccak | 2.0.2 | CC0-1.0 | https://crates.io/crates/tiny-keccak/2.0.2 | 1 |
| tinystr | 0.8.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| tinyvec | 1.13.2 | Zlib OR Apache-2.0 OR MIT | https://github.com/Lokathor/tinyvec | 3 |
| tinyvec_macros | 0.1.1 | MIT OR Apache-2.0 OR Zlib | https://github.com/Soveu/tinyvec_macros | 3 |
| toml | 0.8.2 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml | 1.1.5+spec-1.1.0 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_datetime | 0.6.3 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_datetime | 1.1.1+spec-1.1.0 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_edit | 0.19.15 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_edit | 0.20.2 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_edit | 0.25.13+spec-1.1.0 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_parser | 1.1.3+spec-1.1.0 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| toml_writer | 1.1.2+spec-1.1.0 | MIT OR Apache-2.0 | https://github.com/toml-rs/toml | 2 |
| tracing | 0.1.44 | MIT | https://github.com/tokio-rs/tracing | 1 |
| tracing-attributes | 0.1.31 | MIT | https://github.com/tokio-rs/tracing | 1 |
| tracing-core | 0.1.36 | MIT | https://github.com/tokio-rs/tracing | 1 |
| tray-icon | 0.25.1 | MIT OR Apache-2.0 | https://github.com/tauri-apps/tray-icon | 3 |
| tree_magic_mini | 3.2.2 | MIT | https://github.com/mbrubeck/tree_magic/ | 1 |
| typed-path | 0.12.3 | MIT OR Apache-2.0 | https://github.com/chipsenkbeil/typed-path | 2 |
| typeid | 1.0.3 | MIT OR Apache-2.0 | https://github.com/dtolnay/typeid | 2 |
| typenum | 1.20.1 | MIT OR Apache-2.0 | https://github.com/paholg/typenum | 3 |
| uds_windows | 1.2.1 | MIT | https://github.com/haraldh/rust_uds_windows | 1 |
| unic-char-property | 0.9.0 | MIT/Apache-2.0 | https://github.com/open-i18n/rust-unic/ | 3 |
| unic-char-range | 0.9.0 | MIT/Apache-2.0 | https://github.com/open-i18n/rust-unic/ | 3 |
| unic-common | 0.9.0 | MIT/Apache-2.0 | https://github.com/open-i18n/rust-unic/ | 3 |
| unic-ucd-ident | 0.9.0 | MIT/Apache-2.0 | https://github.com/open-i18n/rust-unic/ | 3 |
| unic-ucd-version | 0.9.0 | MIT/Apache-2.0 | https://github.com/open-i18n/rust-unic/ | 3 |
| unicode-ident | 1.0.24 | (MIT OR Apache-2.0) AND Unicode-3.0 | https://github.com/dtolnay/unicode-ident | 3 |
| unicode-normalization | 0.1.25 | MIT OR Apache-2.0 | https://github.com/unicode-rs/unicode-normalization | 3 |
| unicode-segmentation | 1.13.3 | MIT OR Apache-2.0 | https://github.com/unicode-rs/unicode-segmentation | 3 |
| universal-hash | 0.5.1 | MIT OR Apache-2.0 | https://github.com/RustCrypto/traits | 2 |
| ureq | 3.4.2 | MIT OR Apache-2.0 | https://github.com/algesten/ureq | 2 |
| ureq-proto | 0.6.4 | MIT OR Apache-2.0 | https://github.com/algesten/ureq-proto | 2 |
| url | 2.5.8 | MIT OR Apache-2.0 | https://github.com/servo/rust-url | 2 |
| urlpattern | 0.3.0 | MIT | https://github.com/denoland/rust-urlpattern | 1 |
| utf8_iter | 1.0.4 | Apache-2.0 OR MIT | https://github.com/hsivonen/utf8_iter | 3 |
| utf8-zero | 0.8.1 | MIT OR Apache-2.0 | https://github.com/algesten/utf8-zero | 2 |
| uuid | 1.26.0 | Apache-2.0 OR MIT | https://github.com/uuid-rs/uuid | 2 |
| vcpkg | 0.2.15 | MIT/Apache-2.0 | https://github.com/mcgoo/vcpkg-rs | 2 |
| version_check | 0.9.5 | MIT/Apache-2.0 | https://github.com/SergioBenitez/version_check | 2 |
| version-compare | 0.2.1 | MIT | https://gitlab.com/timvisee/version-compare | 1 |
| vswhom | 0.1.0 | MIT | https://github.com/nabijaczleweli/vswhom.rs | 1 |
| vswhom-sys | 0.1.3 | MIT | https://github.com/nabijaczleweli/vswhom-sys.rs | 1 |
| walkdir | 2.5.0 | Unlicense/MIT | https://github.com/BurntSushi/walkdir | 3 |
| wasi | 0.11.1+wasi-snapshot-preview1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | https://github.com/bytecodealliance/wasi | 3 |
| wasip2 | 1.0.4+wasi-0.2.12 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | https://github.com/bytecodealliance/wasi-rs | 3 |
| wasm-bindgen | 0.2.128 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen | 2 |
| wasm-bindgen-futures | 0.4.78 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/futures | 2 |
| wasm-bindgen-macro | 0.2.128 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/macro | 2 |
| wasm-bindgen-macro-support | 0.2.128 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/main/crates/macro-support | 2 |
| wasm-bindgen-shared | 0.2.128 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/shared | 2 |
| wayland-backend | 0.3.17 | MIT | https://github.com/smithay/wayland-rs | 1 |
| wayland-client | 0.31.15 | MIT | https://github.com/smithay/wayland-rs | 1 |
| wayland-protocols | 0.32.13 | MIT | https://github.com/smithay/wayland-rs | 1 |
| wayland-protocols-wlr | 0.3.12 | MIT | https://github.com/smithay/wayland-rs | 1 |
| wayland-scanner | 0.31.11 | MIT | https://github.com/smithay/wayland-rs | 1 |
| wayland-sys | 0.31.11 | MIT | https://github.com/smithay/wayland-rs | 1 |
| web_atoms | 0.2.6 | MIT OR Apache-2.0 | https://github.com/servo/html5ever | 2 |
| web-sys | 0.3.105 | MIT OR Apache-2.0 | https://github.com/wasm-bindgen/wasm-bindgen/tree/master/crates/web-sys | 2 |
| webkit2gtk | 2.0.2 | MIT | https://github.com/tauri-apps/webkit2gtk-rs | 1 |
| webkit2gtk-sys | 2.0.2 | MIT | https://github.com/tauri-apps/webkit2gtk-rs | 1 |
| webview2-com | 0.38.2 | MIT | https://github.com/wravery/webview2-rs | 1 |
| webview2-com-macros | 0.8.1 | MIT | https://github.com/wravery/webview2-rs | 1 |
| webview2-com-sys | 0.38.2 | MIT | https://github.com/wravery/webview2-rs | 1 |
| weezl | 0.1.12 | MIT OR Apache-2.0 | https://github.com/image-rs/weezl | 2 |
| winapi | 0.3.9 | MIT/Apache-2.0 | https://github.com/retep998/winapi-rs | 2 |
| winapi-i686-pc-windows-gnu | 0.4.0 | MIT/Apache-2.0 | https://github.com/retep998/winapi-rs | 2 |
| winapi-util | 0.1.11 | Unlicense OR MIT | https://github.com/BurntSushi/winapi-util | 3 |
| winapi-x86_64-pc-windows-gnu | 0.4.0 | MIT/Apache-2.0 | https://github.com/retep998/winapi-rs | 2 |
| window-vibrancy | 0.6.0 | Apache-2.0 OR MIT | https://github.com/tauri-apps/tauri-plugin-vibrancy | 3 |
| windows | 0.36.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows | 0.56.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows | 0.61.3 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_gnullvm | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_gnullvm | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_gnullvm | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_msvc | 0.36.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_msvc | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_msvc | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_aarch64_msvc | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_gnu | 0.36.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_gnu | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_gnu | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_gnu | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_gnullvm | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_gnullvm | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_msvc | 0.36.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_msvc | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_msvc | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_i686_msvc | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnu | 0.36.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnu | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnu | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnu | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnullvm | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnullvm | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_gnullvm | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_msvc | 0.36.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_msvc | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_msvc | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows_x86_64_msvc | 0.53.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-collections | 0.2.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-core | 0.56.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-core | 0.61.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-future | 0.2.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-implement | 0.56.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-implement | 0.60.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-interface | 0.56.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-interface | 0.59.3 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-link | 0.1.3 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-link | 0.2.1 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-numerics | 0.2.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-result | 0.1.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-result | 0.3.4 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-strings | 0.4.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-sys | 0.45.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-sys | 0.52.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-sys | 0.59.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-sys | 0.60.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-sys | 0.61.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-targets | 0.42.2 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-targets | 0.52.6 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-targets | 0.53.5 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-threading | 0.1.0 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| windows-version | 0.1.7 | MIT OR Apache-2.0 | https://github.com/microsoft/windows-rs | 2 |
| winnow | 0.5.40 | MIT | https://github.com/winnow-rs/winnow | 1 |
| winnow | 1.0.4 | MIT | https://github.com/winnow-rs/winnow | 1 |
| winreg | 0.55.0 | MIT | https://github.com/gentoo90/winreg-rs | 1 |
| wit-bindgen | 0.57.1 | Apache-2.0 WITH LLVM-exception OR Apache-2.0 OR MIT | https://github.com/bytecodealliance/wit-bindgen | 3 |
| wl-clipboard-rs | 0.9.3 | MIT/Apache-2.0 | https://github.com/YaLTeR/wl-clipboard-rs | 2 |
| writeable | 0.6.4 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| wry | 0.55.1 | Apache-2.0 OR MIT | https://github.com/tauri-apps/wry | 3 |
| x11 | 2.21.0 | MIT | https://github.com/AltF02/x11-rs.git | 1 |
| x11-dl | 2.21.0 | MIT | https://github.com/AltF02/x11-rs.git | 1 |
| x11rb | 0.13.2 | MIT OR Apache-2.0 | https://github.com/psychon/x11rb | 2 |
| x11rb-protocol | 0.13.2 | MIT OR Apache-2.0 | https://github.com/psychon/x11rb | 2 |
| x25519-dalek | 2.0.1 | BSD-3-Clause | https://github.com/dalek-cryptography/curve25519-dalek/tree/main/x25519-dalek | 1 |
| xattr | 1.6.1 | MIT OR Apache-2.0 | https://github.com/Stebalien/xattr | 2 |
| xdg-home | 1.3.0 | MIT | https://github.com/zeenix/xdg-home | 1 |
| xkeysym | 0.2.1 | MIT OR Apache-2.0 OR Zlib | https://github.com/notgull/xkeysym | 3 |
| yoke | 0.8.3 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| yoke-derive | 0.8.2 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| zbus | 4.4.0 | MIT | https://github.com/dbus2/zbus/ | 1 |
| zbus_macros | 4.4.0 | MIT | https://github.com/dbus2/zbus/ | 1 |
| zbus_names | 3.0.0 | MIT | https://github.com/dbus2/zbus/ | 1 |
| zerocopy | 0.8.59 | BSD-2-Clause OR Apache-2.0 OR MIT | https://github.com/google/zerocopy | 3 |
| zerocopy-derive | 0.8.59 | BSD-2-Clause OR Apache-2.0 OR MIT | https://github.com/google/zerocopy | 3 |
| zerofrom | 0.1.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| zerofrom-derive | 0.1.7 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| zeroize | 1.8.2 | Apache-2.0 OR MIT | https://github.com/RustCrypto/utils | 2 |
| zeroize_derive | 1.5.0 | Apache-2.0 OR MIT | https://github.com/RustCrypto/utils | 2 |
| zerotrie | 0.2.5 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| zerovec | 0.11.8 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| zerovec-derive | 0.11.6 | Unicode-3.0 | https://github.com/unicode-org/icu4x | 1 |
| zip | 8.6.0 | MIT | https://github.com/zip-rs/zip2 | 1 |
| zlib-rs | 0.6.7 | Zlib | https://github.com/trifectatechfoundation/zlib-rs | 1 |
| zmij | 1.0.23 | MIT | https://github.com/dtolnay/zmij | 1 |
| zopfli | 0.8.3 | Apache-2.0 | https://github.com/zopfli-rs/zopfli | 1 |
| zune-core | 0.5.3 | MIT OR Apache-2.0 OR Zlib | https://github.com/etemesi254/zune-image | 3 |
| zune-jpeg | 0.5.15 | MIT OR Apache-2.0 OR Zlib | https://github.com/etemesi254/zune-image/tree/dev/crates/zune-jpeg | 3 |
| zvariant | 4.2.0 | MIT | https://github.com/dbus2/zbus/ | 1 |
| zvariant_derive | 4.2.0 | MIT | https://github.com/dbus2/zbus/ | 1 |
| zvariant_utils | 2.1.0 | MIT | https://github.com/dbus2/zbus/ | 1 |
