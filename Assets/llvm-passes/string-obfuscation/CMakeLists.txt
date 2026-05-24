cmake_minimum_required(VERSION 3.20)
project(StringObfuscationPass LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# LLVM_DIR can be passed via -DLLVM_DIR=<path>; the build-all.ps1 script always
# passes it explicitly, so the fallback is only for manual cmake invocations.
if(NOT DEFINED LLVM_DIR)
    foreach(_candidate
            "${CMAKE_CURRENT_SOURCE_DIR}/../../Tools/msys64/mingw64/lib/cmake/llvm"
            "C:/msys64/mingw64/lib/cmake/llvm"
            "C:/Program Files/LLVM/lib/cmake/llvm")
        if(EXISTS "${_candidate}/LLVMConfig.cmake")
            set(LLVM_DIR "${_candidate}" CACHE PATH "LLVM CMake directory")
            break()
        endif()
    endforeach()
endif()

find_package(LLVM REQUIRED CONFIG)
message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION} at ${LLVM_DIR}")

add_library(pass MODULE pass.cpp)

target_include_directories(pass PRIVATE ${LLVM_INCLUDE_DIRS})

# LLVM_DEFINITIONS is a space-separated string on some CMake/LLVM combos.
separate_arguments(LLVM_DEFS_LIST NATIVE_COMMAND "${LLVM_DEFINITIONS}")
target_compile_definitions(pass PRIVATE ${LLVM_DEFS_LIST})

llvm_map_components_to_libnames(llvm_libs
    Core Support IRReader Analysis Passes TransformUtils
    BitReader BitWriter MC Object Target)
target_link_libraries(pass PRIVATE ${llvm_libs})

# Newer LLVM (≥17) moved PassPlugin.h from Passes/ to Plugins/;
# older LLVM used Passes/.  Accept either.
if(EXISTS "${LLVM_INCLUDE_DIRS}/llvm/Plugins/PassPlugin.h")
    target_compile_definitions(pass PRIVATE LLVM_PASSPLUGIN_HEADER_PLUGINS)
endif()

if(NOT LLVM_ENABLE_RTTI)
    target_compile_options(pass PRIVATE $<IF:$<CXX_COMPILER_ID:MSVC>,/GR-,-fno-rtti>)
endif()

if(CMAKE_CXX_COMPILER_ID STREQUAL "GNU")
    set(_msys_lib "${LLVM_INSTALL_PREFIX}/lib")

    if(TARGET LLVMSupport)
        get_target_property(_ill LLVMSupport INTERFACE_LINK_LIBRARIES)
        if(_ill)
            list(TRANSFORM _ill REPLACE "^zstd::libzstd_shared$" "zstd::libzstd_static")
            list(REMOVE_ITEM _ill "pthread")
            set_property(TARGET LLVMSupport PROPERTY INTERFACE_LINK_LIBRARIES "${_ill}")
        endif()
    endif()

    # Force-include all libwinpthread.a objects so clock_gettime64 is defined
    # directly in pass.dll before libstdc++.a's chrono.o is linked.
    if(EXISTS "${_msys_lib}/libwinpthread.a")
        target_link_libraries(pass PRIVATE
            "-Wl,--whole-archive,${_msys_lib}/libwinpthread.a,--no-whole-archive")
    endif()

    if(TARGET ZLIB::ZLIB AND EXISTS "${_msys_lib}/libz.a")
        set_property(TARGET ZLIB::ZLIB PROPERTY IMPORTED_IMPLIB           "")
        set_property(TARGET ZLIB::ZLIB PROPERTY IMPORTED_IMPLIB_RELEASE   "")
        set_property(TARGET ZLIB::ZLIB PROPERTY IMPORTED_LOCATION         "${_msys_lib}/libz.a")
        set_property(TARGET ZLIB::ZLIB PROPERTY IMPORTED_LOCATION_RELEASE "${_msys_lib}/libz.a")
    endif()

    target_link_options(pass PRIVATE -static-libgcc -static-libstdc++ -Wl,--as-needed)
endif()

set_target_properties(pass PROPERTIES OUTPUT_NAME pass PREFIX "" SUFFIX ".dll")
