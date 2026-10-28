# libmoonsharp_jni.so: loads MoonSharp into the current process (CoreCLR through hostfxr, or a NativeAOT
# build) and exposes it to C (moonsharp_native.h) and to Java (NativeBridge, through JNI).
#
#   cmake -S src/Hosting/native -B src/Hosting/native/build -DCMAKE_BUILD_TYPE=Release
#   cmake --build src/Hosting/native/build
#
# nethost is linked statically, so the .so needs nothing at run time besides libc and, for the CoreCLR
# path, an installed .NET 8 runtime.
cmake_minimum_required(VERSION 3.18)
project(moonsharp_native C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Only jni.h is needed; the library does not link against libjvm.
if(NOT JNI_INCLUDE_DIRS)
    if(DEFINED ENV{JAVA_HOME})
        set(_java_home "$ENV{JAVA_HOME}")
    else()
        find_program(_java java REQUIRED)
        get_filename_component(_java "${_java}" REALPATH)
        get_filename_component(_java_home "${_java}/../.." ABSOLUTE)
    endif()
    if(NOT EXISTS "${_java_home}/include/jni.h")
        message(FATAL_ERROR "jni.h not found under ${_java_home}/include; set JAVA_HOME to a JDK")
    endif()
    set(JNI_INCLUDE_DIRS "${_java_home}/include" "${_java_home}/include/linux")
endif()

# nethost.h, hostfxr.h, coreclr_delegates.h and libnethost.a ship in the app host pack of every .NET SDK
# (packs/Microsoft.NETCore.App.Host.<rid>/<version>/runtimes/<rid>/native) and in the
# Microsoft.NETCore.DotNetAppHost NuGet package. Any version can find any installed runtime.
if(NOT NETHOST_DIR)
    if(DEFINED ENV{DOTNET_ROOT})
        set(_dotnet_root "$ENV{DOTNET_ROOT}")
    else()
        find_program(_dotnet dotnet REQUIRED)
        get_filename_component(_dotnet "${_dotnet}" REALPATH)
        get_filename_component(_dotnet_root "${_dotnet}" DIRECTORY)
    endif()
    file(GLOB _nethost_headers "${_dotnet_root}/packs/Microsoft.NETCore.App.Host.linux-*/*/runtimes/*/native/nethost.h")
    list(SORT _nethost_headers COMPARE NATURAL)
    list(POP_BACK _nethost_headers _nethost_header)
    if(NOT _nethost_header)
        message(FATAL_ERROR "nethost.h not found under ${_dotnet_root}/packs; set NETHOST_DIR")
    endif()
    get_filename_component(NETHOST_DIR "${_nethost_header}" DIRECTORY)
endif()
message(STATUS "nethost: ${NETHOST_DIR}")

add_library(moonsharp_jni SHARED moonsharp_native.c moonsharp_jni.c)
target_include_directories(moonsharp_jni PRIVATE ${JNI_INCLUDE_DIRS} "${NETHOST_DIR}")
# libnethost.a is C++: link its runtime explicitly rather than rely on the JVM having loaded it.
target_link_libraries(moonsharp_jni PRIVATE "${NETHOST_DIR}/libnethost.a" stdc++ ${CMAKE_DL_LIBS} pthread)
# --exclude-libs keeps nethost's own symbols out of the export table.
target_link_options(moonsharp_jni PRIVATE -Wl,--no-undefined -Wl,--exclude-libs,ALL $<$<CONFIG:Release>:-s>)
target_compile_options(moonsharp_jni PRIVATE -Wall -Wextra -fvisibility=hidden)
# Only the C API (MOONSHARP_API) and the JNI functions (JNIEXPORT) are exported.
