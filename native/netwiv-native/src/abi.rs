#![allow(clippy::missing_safety_doc)] // Safety requirements are centralized in the installed C header.

use std::{
    cell::RefCell,
    ffi::c_void,
    panic::{AssertUnwindSafe, catch_unwind},
    ptr, slice,
    sync::Arc,
};

use crate::{
    ABI_VERSION,
    model::RunConfig,
    runtime::{RuntimeHandle, STATE_RUNNING, Shared, UserEvent, run},
};

pub type NetWivCallback = unsafe extern "C" fn(
    user: *mut c_void,
    kind: u32,
    request_id: u64,
    json: *const u8,
    json_len: usize,
    binary: *const u8,
    binary_len: usize,
) -> i32;

#[repr(C)]
pub struct NetWivBuffer {
    pub data: *mut u8,
    pub len: usize,
}

thread_local! {
    static LAST_ERROR: RefCell<Vec<u8>> = const { RefCell::new(Vec::new()) };
}

fn set_error(error: impl Into<String>) {
    let mut bytes = error.into().into_bytes();
    bytes.retain(|byte| *byte != 0);
    LAST_ERROR.with(|last| *last.borrow_mut() = bytes);
}

fn panic_message(payload: Box<dyn std::any::Any + Send>) -> String {
    if let Some(message) = payload.downcast_ref::<&str>() {
        (*message).to_owned()
    } else if let Some(message) = payload.downcast_ref::<String>() {
        message.clone()
    } else {
        "native Rust panic".into()
    }
}

fn status(action: impl FnOnce() -> Result<(), String>) -> i32 {
    match catch_unwind(AssertUnwindSafe(action)) {
        Ok(Ok(())) => 0,
        Ok(Err(error)) => {
            set_error(error);
            1
        }
        Err(panic) => {
            set_error(format!("NetWiv native panic: {}", panic_message(panic)));
            2
        }
    }
}

unsafe fn runtime_arc(runtime: *mut RuntimeHandle) -> Result<Arc<Shared>, String> {
    if runtime.is_null() {
        return Err("runtime handle is null".into());
    }
    // SAFETY: the ABI contract forbids destroy racing any exported runtime call.
    Ok(unsafe { (*runtime).shared.clone() })
}

unsafe fn copy_bytes(data: *const u8, len: usize, name: &str) -> Result<Vec<u8>, String> {
    if len == 0 {
        return Ok(Vec::new());
    }
    if data.is_null() {
        return Err(format!("{name} is null with a non-zero length"));
    }
    // SAFETY: callers promise each input span is readable for this call; it is copied immediately.
    Ok(unsafe { slice::from_raw_parts(data, len) }.to_vec())
}

unsafe fn copy_utf8(data: *const u8, len: usize, name: &str) -> Result<String, String> {
    String::from_utf8(unsafe { copy_bytes(data, len, name)? })
        .map_err(|_| format!("{name} is not valid UTF-8"))
}

#[unsafe(no_mangle)]
pub extern "C" fn netwiv_abi_version() -> u32 {
    ABI_VERSION
}

#[unsafe(no_mangle)]
pub extern "C" fn netwiv_runtime_create(
    callback: Option<NetWivCallback>,
    user: *mut c_void,
) -> *mut RuntimeHandle {
    match catch_unwind(AssertUnwindSafe(|| {
        let callback = callback.ok_or_else(|| "callback is null".to_string())?;
        Ok::<_, String>(Box::into_raw(Box::new(RuntimeHandle {
            shared: Arc::new(Shared::new(callback, user)),
        })))
    })) {
        Ok(Ok(runtime)) => runtime,
        Ok(Err(error)) => {
            set_error(error);
            ptr::null_mut()
        }
        Err(panic) => {
            set_error(format!("NetWiv native panic: {}", panic_message(panic)));
            ptr::null_mut()
        }
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_run(
    runtime: *mut RuntimeHandle,
    config: *const u8,
    config_len: usize,
) -> i32 {
    status(|| {
        let shared = unsafe { runtime_arc(runtime)? };
        let bytes = unsafe { copy_bytes(config, config_len, "run config")? };
        let config: RunConfig =
            serde_json::from_slice(&bytes).map_err(|e| format!("invalid run config: {e}"))?;
        run(shared, config)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_submit(
    runtime: *mut RuntimeHandle,
    request_id: u64,
    json: *const u8,
    json_len: usize,
    binary: *const u8,
    binary_len: usize,
) -> i32 {
    status(|| {
        if request_id == 0 {
            return Err("submit request id cannot be zero".into());
        }
        let shared = unsafe { runtime_arc(runtime)? };
        let json = unsafe { copy_utf8(json, json_len, "submit JSON")? };
        let binary = unsafe { copy_bytes(binary, binary_len, "submit binary")? };
        shared.enqueue(UserEvent::Submit {
            request_id,
            json,
            binary,
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_cancel(
    runtime: *mut RuntimeHandle,
    request_id: u64,
) -> i32 {
    status(|| {
        if request_id == 0 {
            return Err("cancel request id cannot be zero".into());
        }
        unsafe { runtime_arc(runtime)? }.enqueue(UserEvent::CancelRequest { request_id })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_dispatch(
    runtime: *mut RuntimeHandle,
    request_id: u64,
) -> i32 {
    status(|| unsafe { runtime_arc(runtime)? }.enqueue(UserEvent::Dispatch { request_id }))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_eval(
    runtime: *mut RuntimeHandle,
    label: *const u8,
    label_len: usize,
    document_id: u64,
    script: *const u8,
    script_len: usize,
) -> i32 {
    status(|| {
        let shared = unsafe { runtime_arc(runtime)? };
        let label = unsafe { copy_utf8(label, label_len, "webview label")? };
        let script = unsafe { copy_utf8(script, script_len, "script")? };
        shared.enqueue(UserEvent::Eval {
            label,
            document_id,
            script,
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_respond_asset(
    runtime: *mut RuntimeHandle,
    request_id: u64,
    json: *const u8,
    json_len: usize,
    body: *const u8,
    body_len: usize,
) -> i32 {
    status(|| {
        let shared = unsafe { runtime_arc(runtime)? };
        let json = unsafe { copy_utf8(json, json_len, "asset response JSON")? };
        let body = unsafe { copy_bytes(body, body_len, "asset response body")? };
        shared.respond_asset(request_id, &json, body)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_exit(runtime: *mut RuntimeHandle, exit_code: i32) -> i32 {
    status(|| unsafe { runtime_arc(runtime)? }.enqueue(UserEvent::Exit { exit_code }))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_runtime_destroy(runtime: *mut RuntimeHandle) {
    let _ = catch_unwind(AssertUnwindSafe(|| {
        if !runtime.is_null() {
            // SAFETY: ownership of the pointer is transferred back exactly once by the ABI contract.
            let handle = unsafe { Box::from_raw(runtime) };
            if handle
                .shared
                .state
                .load(std::sync::atomic::Ordering::Acquire)
                == STATE_RUNNING
            {
                set_error("destroy called while runtime is running");
                std::mem::forget(handle);
            }
        }
    }));
}

#[unsafe(no_mangle)]
pub extern "C" fn netwiv_last_error() -> NetWivBuffer {
    let bytes = LAST_ERROR.with(|last| std::mem::take(&mut *last.borrow_mut()));
    if bytes.is_empty() {
        return NetWivBuffer {
            data: ptr::null_mut(),
            len: 0,
        };
    }
    let mut boxed = bytes.into_boxed_slice();
    let result = NetWivBuffer {
        data: boxed.as_mut_ptr(),
        len: boxed.len(),
    };
    std::mem::forget(boxed);
    result
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn netwiv_buffer_free(buffer: NetWivBuffer) {
    if !buffer.data.is_null() {
        // SAFETY: buffers returned by netwiv_last_error have capacity exactly equal to len.
        drop(unsafe { Vec::from_raw_parts(buffer.data, buffer.len, buffer.len) });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static CALLBACKS: AtomicUsize = AtomicUsize::new(0);
    unsafe extern "C" fn callback(
        _: *mut c_void,
        _: u32,
        _: u64,
        _: *const u8,
        _: usize,
        _: *const u8,
        _: usize,
    ) -> i32 {
        CALLBACKS.fetch_add(1, Ordering::Relaxed);
        0
    }

    #[test]
    fn abi_version_and_handle_lifetime() {
        assert_eq!(netwiv_abi_version(), 3);
        let handle = netwiv_runtime_create(Some(callback), ptr::null_mut());
        assert!(!handle.is_null());
        unsafe { netwiv_runtime_destroy(handle) };
    }

    #[test]
    fn invalid_spans_return_owned_error() {
        let handle = netwiv_runtime_create(Some(callback), ptr::null_mut());
        let status = unsafe { netwiv_runtime_run(handle, ptr::null(), 4) };
        assert_ne!(status, 0);
        let error = netwiv_last_error();
        assert!(!error.data.is_null());
        assert!(error.len > 0);
        unsafe {
            netwiv_buffer_free(error);
            netwiv_runtime_destroy(handle)
        };
    }

    #[test]
    fn calls_before_run_are_rejected() {
        let handle = netwiv_runtime_create(Some(callback), ptr::null_mut());
        assert_ne!(unsafe { netwiv_runtime_dispatch(handle, 7) }, 0);
        assert_ne!(unsafe { netwiv_runtime_cancel(handle, 7) }, 0);
        unsafe { netwiv_runtime_destroy(handle) };
    }

    #[test]
    fn cancellation_rejects_null_handle_and_zero_request_id() {
        assert_ne!(unsafe { netwiv_runtime_cancel(ptr::null_mut(), 7) }, 0);
        let handle = netwiv_runtime_create(Some(callback), ptr::null_mut());
        assert_ne!(unsafe { netwiv_runtime_cancel(handle, 0) }, 0);
        unsafe { netwiv_runtime_destroy(handle) };
    }
}
