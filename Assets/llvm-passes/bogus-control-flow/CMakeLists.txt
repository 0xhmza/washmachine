cmake_minimum_required(VERSION 3.20)
project(BogusControlFlowPass LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

# Point this at the installed LLVM 22 CMake package.
# Default expects LLVM installed to C:\Program Files\LLVM
set(LLVM_DIR "C:/Program Files/LLVM/lib/cmake/llvm" CACHE PATH "LLVM CMake directory")

find_package(LLVM REQUIRED CONFIG)
message(STATUS "Found LLVM ${LLVM_PACKAGE_VERSION}: ${LLVM_DIR}")

include_directories(${LLVM_INCLUDE_DIRS})
separate_arguments(LLVM_DEFINITIONS_LIST NATIVE_COMMAND ${LLVM_DEFINITIONS})
add_definitions(${LLVM_DEFINITIONS_LIST})

add_library(pass MODULE pass.cpp)

# The plugin only needs LLVM symbols that clang.exe already exports — but the
# Windows linker still has to resolve the C++ names. The official Windows
# release ships static component libs (LLVMCore.lib, …) rather than a combined
# LLVM.lib, so we resolve through llvm_map_components_to_libnames on every
# platform.
target_include_directories(pass PRIVATE ${LLVM_INCLUDE_DIRS})
llvm_map_components_to_libnames(llvm_libs
    Core Support IRReader Analysis Passes TransformUtils
    BitReader BitWriter MC Object Target)
target_link_libraries(pass PRIVATE ${llvm_libs})

# LLVM_ATTRIBUTE_WEAK is empty on MSVC, so llvmGetPassPluginInfo would not be
# exported. The /EXPORT flag publishes it so clang's plugin loader can find it.
if(MSVC)
    target_link_options(pass PRIVATE "/EXPORT:llvmGetPassPluginInfo")
endif()

# LLVM is compiled without RTTI by default; matching avoids ABI mismatches.
if(NOT LLVM_ENABLE_RTTI)
    if(MSVC)
        target_compile_options(pass PRIVATE /GR-)
    else()
        target_compile_options(pass PRIVATE -fno-rtti)
    endif()
endif()

set_target_properties(pass PROPERTIES
    OUTPUT_NAME pass
    PREFIX ""
    SUFFIX ".dll"
)

install(TARGETS pass DESTINATION .)
