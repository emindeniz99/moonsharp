/*
 * C API for running MoonSharp inside the calling process (see docs/hosting.md, "Java: in process").
 *
 * The library loads MoonSharp once per process, in one of two ways:
 *   moonsharp_open_coreclr  starts CoreCLR through hostfxr and loads MoonSharp.Hosting.Native.dll;
 *   moonsharp_open_aot      loads the NativeAOT build, libMoonSharp.Hosting.Native.so.
 * After that every function behaves the same. Strings are UTF-8 with an explicit length; pass -1 for a
 * missing string. Results are JSON envelopes (docs/hosting.md, "The contract") that the caller frees
 * with moonsharp_free.
 *
 * A context is one Lua state. Calls on one context are serialized by a lock inside the library;
 * calls on different contexts run in parallel.
 */
#ifndef MOONSHARP_NATIVE_H
#define MOONSHARP_NATIVE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#ifndef MOONSHARP_API
#define MOONSHARP_API __attribute__((visibility("default")))
#endif

enum moonsharp_op {
    MOONSHARP_OP_ECHO = 0,     /* a -> a, for measuring the boundary */
    MOONSHARP_OP_LOAD = 1,     /* module, source */
    MOONSHARP_OP_INVOKE = 2,   /* module, function, argsJson */
    MOONSHARP_OP_RUN = 3,      /* chunk, source, inputJson */
    MOONSHARP_OP_VALIDATE = 4, /* chunk, source */
    MOONSHARP_OP_UNLOAD = 5,   /* module */
    MOONSHARP_OP_VERSION = 6
};

/*
 * Starts CoreCLR and loads the managed entry points from assembly_path. runtime_config_path is the
 * assembly's .runtimeconfig.json. dotnet_root may be NULL: hostfxr is then found through DOTNET_ROOT,
 * the default install locations or /etc/dotnet. Returns NULL on success, or a static error message.
 */
MOONSHARP_API const char *moonsharp_open_coreclr(const char *runtime_config_path, const char *assembly_path, const char *dotnet_root);

/* Loads a NativeAOT build of the managed entry points. Returns NULL on success, or a static error message. */
MOONSHARP_API const char *moonsharp_open_aot(const char *library_path);

/* Options for moonsharp_context_create, combined with |. */
enum moonsharp_context_option {
    MOONSHARP_CONTEXT_INT64 = 1 /* the int64 and uint64 modules (docs/int64.md) */
};

/*
 * Creates a context with the given options (enum moonsharp_context_option, 0 for none). Returns its id,
 * or 0 when the library is not open or the context could not be created.
 */
MOONSHARP_API int64_t moonsharp_context_create(int32_t options);

/* Drops a context. Returns 1 if it existed. */
MOONSHARP_API int32_t moonsharp_context_dispose(int64_t context);

/* Runs an operation (enum moonsharp_op). Returns a NUL-terminated UTF-8 envelope, or NULL when out of memory. */
MOONSHARP_API uint8_t *moonsharp_call(int64_t context, int32_t op,
                        const uint8_t *a, int32_t a_length,
                        const uint8_t *b, int32_t b_length,
                        const uint8_t *c, int32_t c_length,
                        int32_t *out_length);

MOONSHARP_API void moonsharp_free(uint8_t *result);

#ifdef __cplusplus
}
#endif

#endif
