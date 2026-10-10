#pragma once
#include <cstdint>
#ifdef FV_CUDA_EXPORTS
#define FV_API extern "C" __declspec(dllexport)
#else
#define FV_API extern "C" __declspec(dllimport)
#endif
// ABI 1. All pointers are standard CUDA addresses/handles. The caller owns
// scratch, execution, completion proof and context lifetime. No call creates a stream.
FV_API int fv_abi_version();
FV_API const char* fv_error();
FV_API int fv_error_domain();
FV_API int fv_error_code();
// Create before registering the owner with the execution scope. No GPU work yet.
FV_API int fv_display_create(void* context, int width, int height, void** owner, void** surface);
// Register owner before calling. format: Gray8=0, Bgr24=1, Rgb24=2, Bgra32=3, Pbgra32=4, Rgba32=5.
// scratch: width*height*4 device bytes, except Pbgra32 needs none. Wait before publication.
FV_API int fv_display_write(void* owner, void* stream, const void* input, int64_t pitch, int format, void* scratch);
// Call once after proven completion and final display reference. Failure leaves the owner alive.
FV_API int fv_display_release(void* owner);
FV_API int64_t fv_display_live_surfaces();
// kind: gather=0, stats=1, region=2. Stats: count/min/max/sum uint64 per source channel.
// Gather coordinates: count*8 pinned host bytes and equal deviceCoordinates storage.
// output: count*channels device bytes for gather, channels*32 for stats; not used for region.
// stats scratch: min(256,ceil(regionPixels/256))*channels*32 device bytes.
// host: gather count*channels, stats channels*32, region regionWidth*regionHeight*channels.
// All storage stays registered through completion, including exceptional return.
FV_API int fv_query(void* context, void* stream, const void* input, int64_t pitch,
    int width, int height, int channels, int kind, const void* coordinates, int count,
    int x, int y, int regionWidth, int regionHeight, void* deviceCoordinates, void* scratch, void* output, void* host);
