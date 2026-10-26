cmake_minimum_required(VERSION 3.24)
project(FizzyImageViewerCuda LANGUAGES CXX CUDA)
get_filename_component(REPOSITORY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
find_package(CUDAToolkit 11.8 REQUIRED)
if(NOT FV_CUDA_MAJOR MATCHES "^(11|12|13)$" OR NOT CUDAToolkit_VERSION_MAJOR EQUAL FV_CUDA_MAJOR)
  message(FATAL_ERROR "Select FV_CUDA_MAJOR=11,12,13 matching the CUDA compiler and Toolkit")
endif()
add_library(Fizzy.ImageViewer.Cuda.Native SHARED display.cu queries.cu)
target_compile_definitions(Fizzy.ImageViewer.Cuda.Native PRIVATE FV_CUDA_EXPORTS)
target_link_libraries(Fizzy.ImageViewer.Cuda.Native PRIVATE CUDA::cudart_static CUDA::cuda_driver d3d9)
target_compile_options(Fizzy.ImageViewer.Cuda.Native PRIVATE $<$<COMPILE_LANGUAGE:CUDA>:-lineinfo;--fmad=false;-Xcompiler=/utf-8>)
set_target_properties(Fizzy.ImageViewer.Cuda.Native PROPERTIES
  CXX_STANDARD 17 CUDA_STANDARD 17 CUDA_RUNTIME_LIBRARY Static MSVC_RUNTIME_LIBRARY MultiThreaded
  RUNTIME_OUTPUT_DIRECTORY "${REPOSITORY_ROOT}/build/$<CONFIG>/native/cuda${FV_CUDA_MAJOR}"
  ARCHIVE_OUTPUT_DIRECTORY "${REPOSITORY_ROOT}/build/$<CONFIG>/native/cuda${FV_CUDA_MAJOR}")
option(FIZZY_IMAGEVIEWER_CUDA_TESTS "Build hardware tests; execution is explicit" OFF)
if(FIZZY_IMAGEVIEWER_CUDA_TESTS)
  enable_testing()
  add_executable(Fizzy.ImageViewer.Cuda.Tests native_tests.cpp)
  target_link_libraries(Fizzy.ImageViewer.Cuda.Tests PRIVATE Fizzy.ImageViewer.Cuda.Native CUDA::cudart_static CUDA::cuda_driver d3d9)
  set_target_properties(Fizzy.ImageViewer.Cuda.Tests PROPERTIES CXX_STANDARD 17 MSVC_RUNTIME_LIBRARY MultiThreaded
    RUNTIME_OUTPUT_DIRECTORY "${REPOSITORY_ROOT}/build/$<CONFIG>/tests/Cuda${FV_CUDA_MAJOR}")
  add_custom_command(TARGET Fizzy.ImageViewer.Cuda.Tests POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different "$<TARGET_FILE:Fizzy.ImageViewer.Cuda.Native>" "$<TARGET_FILE_DIR:Fizzy.ImageViewer.Cuda.Tests>")
  add_test(NAME cuda_images COMMAND Fizzy.ImageViewer.Cuda.Tests)
  set_tests_properties(cuda_images PROPERTIES LABELS hardware TIMEOUT 120)
endif()
