/*
 * JNI bindings for org.moonsharp.hosting.NativeBridge. Java passes UTF-8 byte arrays, not Strings:
 * JNI's own string functions use "modified UTF-8", which encodes characters outside the BMP differently
 * from the UTF-8 that .NET reads.
 *
 * Arrays are copied with GetByteArrayRegion rather than pinned with GetPrimitiveArrayCritical: a Lua
 * call can run for a long time, and holding a critical region would stop the Java GC meanwhile.
 */
#include "moonsharp_native.h"

#include <jni.h>
#include <stdlib.h>

#define STACK_BUFFER 4096

typedef struct {
    uint8_t *data;
    int32_t length; /* -1 for null */
    uint8_t stack[STACK_BUFFER];
} bytes;

/* Returns 0 and leaves an OutOfMemoryError pending when the copy cannot be allocated. */
static int copy_in(JNIEnv *env, jbyteArray array, bytes *out)
{
    out->data = NULL;
    out->length = -1;
    if (array == NULL)
        return 1;

    jsize n = (*env)->GetArrayLength(env, array);
    out->data = n <= STACK_BUFFER ? out->stack : malloc((size_t)n);
    if (out->data == NULL) {
        jclass oom = (*env)->FindClass(env, "java/lang/OutOfMemoryError");
        if (oom != NULL)
            (*env)->ThrowNew(env, oom, "MoonSharp argument buffer");
        return 0;
    }

    (*env)->GetByteArrayRegion(env, array, 0, n, (jbyte *)out->data);
    out->length = n;
    return 1;
}

static void release(bytes *b)
{
    if (b->data != NULL && b->data != b->stack)
        free(b->data);
}

static jstring open_result(JNIEnv *env, const char *error)
{
    return error == NULL ? NULL : (*env)->NewStringUTF(env, error);
}

JNIEXPORT jstring JNICALL Java_org_moonsharp_hosting_NativeBridge_openCoreClr(JNIEnv *env, jclass cls,
                                                                              jstring config, jstring assembly,
                                                                              jstring dotnetRoot)
{
    (void)cls;
    const char *c = (*env)->GetStringUTFChars(env, config, NULL);
    const char *a = (*env)->GetStringUTFChars(env, assembly, NULL);
    const char *r = dotnetRoot != NULL ? (*env)->GetStringUTFChars(env, dotnetRoot, NULL) : NULL;
    const char *error = (c == NULL || a == NULL) ? "out of memory" : moonsharp_open_coreclr(c, a, r);
    if (c != NULL)
        (*env)->ReleaseStringUTFChars(env, config, c);
    if (a != NULL)
        (*env)->ReleaseStringUTFChars(env, assembly, a);
    if (r != NULL)
        (*env)->ReleaseStringUTFChars(env, dotnetRoot, r);
    return open_result(env, error);
}

JNIEXPORT jstring JNICALL Java_org_moonsharp_hosting_NativeBridge_openAot(JNIEnv *env, jclass cls, jstring library)
{
    (void)cls;
    const char *l = (*env)->GetStringUTFChars(env, library, NULL);
    const char *error = l == NULL ? "out of memory" : moonsharp_open_aot(l);
    if (l != NULL)
        (*env)->ReleaseStringUTFChars(env, library, l);
    return open_result(env, error);
}

JNIEXPORT jlong JNICALL Java_org_moonsharp_hosting_NativeBridge_contextCreate(JNIEnv *env, jclass cls,
                                                                              jint options)
{
    (void)env;
    (void)cls;
    return moonsharp_context_create(options);
}

JNIEXPORT jboolean JNICALL Java_org_moonsharp_hosting_NativeBridge_contextDispose(JNIEnv *env, jclass cls,
                                                                                 jlong context)
{
    (void)env;
    (void)cls;
    return moonsharp_context_dispose(context) != 0 ? JNI_TRUE : JNI_FALSE;
}

JNIEXPORT jbyteArray JNICALL Java_org_moonsharp_hosting_NativeBridge_call(JNIEnv *env, jclass cls, jlong context,
                                                                          jint op, jbyteArray a, jbyteArray b,
                                                                          jbyteArray c)
{
    (void)cls;
    bytes ba, bb, bc;
    jbyteArray out = NULL;
    ba.data = bb.data = bc.data = NULL;

    if (copy_in(env, a, &ba) && copy_in(env, b, &bb) && copy_in(env, c, &bc)) {
        int32_t length = 0;
        uint8_t *result = moonsharp_call(context, op, ba.data, ba.length, bb.data, bb.length, bc.data, bc.length,
                                         &length);
        if (result == NULL) {
            jclass oom = (*env)->FindClass(env, "java/lang/OutOfMemoryError");
            if (oom != NULL)
                (*env)->ThrowNew(env, oom, "MoonSharp could not allocate the result");
        } else {
            out = (*env)->NewByteArray(env, length);
            if (out != NULL)
                (*env)->SetByteArrayRegion(env, out, 0, length, (const jbyte *)result);
            moonsharp_free(result);
        }
    }

    release(&ba);
    release(&bb);
    release(&bc);
    return out;
}
