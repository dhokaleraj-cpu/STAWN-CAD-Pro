cmake_minimum_required(VERSION 3.20)
project(StawnCAD VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

find_package(OpenCASCADE REQUIRED)
find_package(Eigen3 REQUIRED NO_MODULE)
find_package(nlohmann_json REQUIRED)

set(STAWN_OCCT_LIBS
    TKernel TKMath TKG2d TKG3d TKGeomBase TKBRep TKGeomAlgo TKTopAlgo TKPrim TKBO TKFillet
    TKShHealing TKMesh TKHLR TKXSBase)
# OpenCascade 7.8 merged the STEP and STL translators into the TKDE* toolkits.
if(OpenCASCADE_VERSION VERSION_GREATER_EQUAL 7.8)
    list(APPEND STAWN_OCCT_LIBS TKDE TKDESTEP TKDESTL)
else()
    list(APPEND STAWN_OCCT_LIBS TKSTEP TKSTL)
endif()

add_library(stawn_core
    core/src/Sketch.cpp
    core/src/Document.cpp
    core/src/Serialization.cpp
    core/src/Mesh.cpp
    core/src/Samples.cpp
    core/src/Drawing.cpp
    core/src/Commands.cpp
    core/src/Migration.cpp
    core/capi/stawn_c.cpp)
target_include_directories(stawn_core PUBLIC core/include core/capi ${OpenCASCADE_INCLUDE_DIR})
target_link_libraries(stawn_core PUBLIC ${STAWN_OCCT_LIBS} Eigen3::Eigen nlohmann_json::nlohmann_json)
if(NOT MSVC)
    target_compile_options(stawn_core PRIVATE -Wall -Wextra -Wno-unused-parameter -Wno-missing-field-initializers -Wno-deprecated-declarations)
endif()

# Shared library for scripting hosts such as the Python MCP server (loads it with ctypes).
add_library(stawn SHARED core/capi/stawn_c.cpp)
target_link_libraries(stawn PRIVATE stawn_core)
set_target_properties(stawn PROPERTIES OUTPUT_NAME stawn)

add_executable(stawn-cli tools/stawn_cli.cpp)
target_link_libraries(stawn-cli PRIVATE stawn_core)

include(CTest)
if(BUILD_TESTING)
    find_package(doctest REQUIRED)
    add_executable(stawn_tests
        core/tests/main.cpp
        core/tests/sketch_tests.cpp
        core/tests/document_tests.cpp
        core/tests/drawing_tests.cpp
        core/tests/capi_tests.cpp
        core/tests/command_tests.cpp
        core/tests/migration_tests.cpp)
    target_link_libraries(stawn_tests PRIVATE stawn_core doctest::doctest)
    add_test(NAME stawn_tests COMMAND stawn_tests)
endif()
