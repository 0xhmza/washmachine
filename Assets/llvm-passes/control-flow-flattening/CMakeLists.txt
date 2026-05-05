cmake_minimum_required(VERSION 3.20)
project(ControlFlowFlatteningPass LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)

# Point this at the installed LLVM 22 CMake package.
# Default expects LLVM installed to C:\Program Files\LLVM
set(LLVM_DIR "C:/Program Files/LLVM/lib/cmake/llvm" CACHE PATH "LLVM CMake directory")

find_package(LLVM REQUIRED CONFIG)
message(STATUS "Found LLVM ${{LLVM_PACKAGE_VERSION}}: ${{LLVM_DIR}}")

include_directories(${{LLVM_INCLUDE_DIRS}})
separate_arguments(LLVM_DEFINITIONS_LIST NATIVE_COMMAND ${{LLVM_DEFINITIONS}})
add_definitions(${{LLVM_DEFINITIONS_LIST}})

add_library(pass MODULE pass.cpp)

target_link_libraries(pass PRIVATE LLVM)

set_target_properties(pass PROPERTIES
    OUTPUT_NAME pass
    PREFIX ""
    SUFFIX ".dll"
)

install(TARGETS pass DESTINATION .)
