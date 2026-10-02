cmake_minimum_required(VERSION 3.24)
project(RoamClockAdapter LANGUAGES CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "The adapter requires Windows x64")
endif()
add_library(RoamClockAdapter SHARED adapter.cpp exports.def)
set_target_properties(RoamClockAdapter PROPERTIES OUTPUT_NAME "RoamClock.Native"
  CXX_STANDARD 20 CXX_STANDARD_REQUIRED YES
  MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/bin")
target_compile_definitions(RoamClockAdapter PRIVATE UNICODE _UNICODE NOMINMAX
  WINVER=0x0A00 _WIN32_WINNT=0x0A00)
target_compile_options(RoamClockAdapter PRIVATE /utf-8 /W4 /EHsc /Zi)
target_link_options(RoamClockAdapter PRIVATE /DEBUG:FULL /INCREMENTAL:NO /OPT:REF /OPT:ICF /MAP)
target_link_libraries(RoamClockAdapter PRIVATE ole32 oleaut32 runtimeobject windowsapp bcrypt)
