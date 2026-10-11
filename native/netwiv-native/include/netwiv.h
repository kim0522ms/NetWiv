#ifndef NETWIV_H
#define NETWIV_H
#include <stddef.h>
#include <stdint.h>
#define NETWIV_ABI_VERSION 2u
#ifdef __cplusplus
extern "C" {
#endif
typedef struct netwiv_runtime netwiv_runtime;
typedef struct { uint8_t *data; size_t len; } netwiv_buffer;
/* All byte spans are borrowed for the duration of a call. Callback returns 1
   only to prevent the default action for tauri://close-requested. */
typedef int32_t (*netwiv_callback)(void *user, uint32_t kind, uint64_t request_id,
  const uint8_t *json, size_t json_len, const uint8_t *binary, size_t binary_len);
uint32_t netwiv_abi_version(void);
netwiv_runtime *netwiv_runtime_create(netwiv_callback callback, void *user);
int32_t netwiv_runtime_run(netwiv_runtime *runtime, const uint8_t *config, size_t config_len);
int32_t netwiv_runtime_submit(netwiv_runtime *runtime, uint64_t request_id,
  const uint8_t *json, size_t json_len, const uint8_t *binary, size_t binary_len);
int32_t netwiv_runtime_dispatch(netwiv_runtime *runtime, uint64_t request_id);
int32_t netwiv_runtime_eval(netwiv_runtime *runtime, const uint8_t *label, size_t label_len,
  uint64_t document_id, const uint8_t *script, size_t script_len);
int32_t netwiv_runtime_respond_asset(netwiv_runtime *runtime, uint64_t request_id,
  const uint8_t *json, size_t json_len, const uint8_t *body, size_t body_len);
int32_t netwiv_runtime_exit(netwiv_runtime *runtime, int32_t exit_code);
void netwiv_runtime_destroy(netwiv_runtime *runtime);
netwiv_buffer netwiv_last_error(void);
void netwiv_buffer_free(netwiv_buffer buffer);
#ifdef __cplusplus
}
#endif
#endif
