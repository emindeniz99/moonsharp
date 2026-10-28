/*
 * Loads MoonSharp into the current process and forwards calls to its managed entry points
 * (src/Hosting/MoonSharp.Hosting.Native/Exports.cs). See moonsharp_native.h.
 *
 * CoreCLR path, after https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting:
 * nethost finds hostfxr, hostfxr starts the runtime from a runtimeconfig.json, and
 * load_assembly_and_get_function_pointer returns C-callable pointers to [UnmanagedCallersOnly] methods.
 * NativeAOT path: dlopen the shared library and dlsym the same entry points by their EntryPoint names.
 */
#include "moonsharp_native.h"

#include <dlfcn.h>
#include <pthread.h>
#include <signal.h>
#include <stdio.h>
#include <string.h>

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

typedef int64_t (*context_create_fn)(int32_t, int64_t, int32_t);
typedef int32_t (*context_dispose_fn)(int64_t);
typedef uint8_t *(*call_fn)(int64_t, int32_t, const uint8_t *, int32_t, const uint8_t *, int32_t,
                            const uint8_t *, int32_t, int32_t *);
typedef void (*free_fn)(uint8_t *);

enum backend { BACKEND_NONE, BACKEND_CORECLR, BACKEND_AOT };

static pthread_mutex_t open_lock = PTHREAD_MUTEX_INITIALIZER;
static enum backend opened = BACKEND_NONE;
static context_create_fn api_context_create;
static context_dispose_fn api_context_dispose;
static call_fn api_call;
static free_fn api_free;

/* Written only under open_lock, returned to the caller as the error message. */
static char last_error[2048];

static const char *fail(const char *what, const char *detail, int rc)
{
    snprintf(last_error, sizeof last_error, "%.200s%s%.1500s (0x%08x)", what, detail ? ": " : "", detail ? detail : "",
             (unsigned)rc);
    return last_error;
}

/* hostfxr reports details (missing framework, bad runtimeconfig) through this callback. */
static char hostfxr_message[1536];

static void HOSTFXR_CALLTYPE on_hostfxr_error(const char_t *message)
{
    snprintf(hostfxr_message, sizeof hostfxr_message, "%s", message);
}

static const char *already_open(enum backend wanted)
{
    if (opened == wanted)
        return NULL;
    return opened == BACKEND_CORECLR ? "MoonSharp is already loaded through CoreCLR in this process"
                                     : "MoonSharp is already loaded as NativeAOT in this process";
}

static const char *open_coreclr_locked(const char *runtime_config_path, const char *assembly_path,
                                       const char *dotnet_root)
{
    char_t hostfxr_path[4096];
    size_t size = sizeof hostfxr_path / sizeof(char_t);
    struct get_hostfxr_parameters find = {sizeof find, assembly_path, dotnet_root};
    int rc = get_hostfxr_path(hostfxr_path, &size, &find);
    if (rc != 0)
        return fail("cannot find hostfxr; install the .NET 8 runtime or set DOTNET_ROOT", NULL, rc);

    void *lib = dlopen(hostfxr_path, RTLD_NOW | RTLD_LOCAL);
    if (lib == NULL)
        return fail("cannot load hostfxr", dlerror(), 0);

    hostfxr_set_error_writer_fn set_error_writer = (hostfxr_set_error_writer_fn)dlsym(lib, "hostfxr_set_error_writer");
    hostfxr_initialize_for_runtime_config_fn init =
        (hostfxr_initialize_for_runtime_config_fn)dlsym(lib, "hostfxr_initialize_for_runtime_config");
    hostfxr_get_runtime_delegate_fn get_delegate =
        (hostfxr_get_runtime_delegate_fn)dlsym(lib, "hostfxr_get_runtime_delegate");
    hostfxr_close_fn close = (hostfxr_close_fn)dlsym(lib, "hostfxr_close");
    if (set_error_writer == NULL || init == NULL || get_delegate == NULL || close == NULL)
        return fail("hostfxr is missing an export", hostfxr_path, 0);

    hostfxr_message[0] = 0;
    set_error_writer(on_hostfxr_error);

    /*
     * CoreCLR installs a SIGTERM handler that shuts the process down through .NET (exit code 143) and
     * does not chain to the previous handler, so a JVM would exit without running its shutdown hooks.
     * Keep the host's handler. CoreCLR's other handlers (SIGSEGV, SIGINT, SIGQUIT...) chain to the
     * previous ones for signals that are not its own, and stay.
     */
    struct sigaction host_sigterm;
    sigaction(SIGTERM, NULL, &host_sigterm);

    struct hostfxr_initialize_parameters params = {sizeof params, NULL, dotnet_root};
    hostfxr_handle context = NULL;
    rc = init(runtime_config_path, dotnet_root != NULL ? &params : NULL, &context);
    /* 0 = started, 1 = runtime already running, 2 = running with different properties. */
    if (rc < 0 || rc > 2 || context == NULL) {
        if (context != NULL)
            close(context);
        set_error_writer(NULL);
        return fail("hostfxr_initialize_for_runtime_config failed", hostfxr_message, rc);
    }

    load_assembly_and_get_function_pointer_fn load = NULL;
    rc = get_delegate(context, hdt_load_assembly_and_get_function_pointer, (void **)&load);
    close(context);
    if (rc != 0 || load == NULL) {
        set_error_writer(NULL);
        return fail("hostfxr_get_runtime_delegate failed", hostfxr_message, rc);
    }

    static const char *type = "MoonSharp.Hosting.Native.Exports, MoonSharp.Hosting.Native";
    struct {
        const char *method;
        void **target;
    } entries[] = {
        {"ContextCreate", (void **)&api_context_create},
        {"ContextDispose", (void **)&api_context_dispose},
        {"Call", (void **)&api_call},
        {"Free", (void **)&api_free},
    };

    for (size_t i = 0; i < sizeof entries / sizeof entries[0]; i++) {
        rc = load(assembly_path, type, entries[i].method, UNMANAGEDCALLERSONLY_METHOD, NULL, entries[i].target);
        if (rc != 0 || *entries[i].target == NULL) {
            set_error_writer(NULL);
            return fail("cannot bind managed entry point", entries[i].method, rc);
        }
    }

    sigaction(SIGTERM, &host_sigterm, NULL);
    set_error_writer(NULL);
    return NULL;
}

const char *moonsharp_open_coreclr(const char *runtime_config_path, const char *assembly_path, const char *dotnet_root)
{
    pthread_mutex_lock(&open_lock);
    const char *error;

    if (opened != BACKEND_NONE) {
        error = already_open(BACKEND_CORECLR);
    } else {
        error = open_coreclr_locked(runtime_config_path, assembly_path, dotnet_root);
        if (error == NULL)
            opened = BACKEND_CORECLR;
    }

    pthread_mutex_unlock(&open_lock);
    return error;
}

const char *moonsharp_open_aot(const char *library_path)
{
    pthread_mutex_lock(&open_lock);
    const char *error = NULL;

    if (opened != BACKEND_NONE) {
        error = already_open(BACKEND_AOT);
    } else {
        void *lib = dlopen(library_path, RTLD_NOW | RTLD_LOCAL);
        if (lib == NULL) {
            error = fail("cannot load the NativeAOT library", dlerror(), 0);
        } else {
            api_context_create = (context_create_fn)dlsym(lib, "moonsharp_managed_context_create");
            api_context_dispose = (context_dispose_fn)dlsym(lib, "moonsharp_managed_context_dispose");
            api_call = (call_fn)dlsym(lib, "moonsharp_managed_call");
            api_free = (free_fn)dlsym(lib, "moonsharp_managed_free");
            if (api_context_create == NULL || api_context_dispose == NULL || api_call == NULL || api_free == NULL)
                error = fail("the NativeAOT library is missing an entry point", library_path, 0);
            else
                opened = BACKEND_AOT;
        }
    }

    pthread_mutex_unlock(&open_lock);
    return error;
}

/*
 * The entry points are written once, under open_lock. Callers must order their first call after a
 * successful open (the Java side does: NativeBridge opens inside a synchronized method), so these plain
 * reads see the pointers. Before an open they are NULL and the functions fail softly.
 */
int64_t moonsharp_context_create(int32_t options, int64_t instruction_limit, int32_t time_limit_ms)
{
    return api_context_create != NULL ? api_context_create(options, instruction_limit, time_limit_ms) : 0;
}

int32_t moonsharp_context_dispose(int64_t context)
{
    return api_context_dispose != NULL ? api_context_dispose(context) : 0;
}

uint8_t *moonsharp_call(int64_t context, int32_t op, const uint8_t *a, int32_t a_length, const uint8_t *b,
                        int32_t b_length, const uint8_t *c, int32_t c_length, int32_t *out_length)
{
    if (api_call == NULL) {
        *out_length = -1;
        return NULL;
    }
    return api_call(context, op, a, a_length, b, b_length, c, c_length, out_length);
}

void moonsharp_free(uint8_t *result)
{
    if (api_free != NULL && result != NULL)
        api_free(result);
}
