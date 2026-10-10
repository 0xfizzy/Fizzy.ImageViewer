#pragma once
#include "imageviewer_cuda.h"
#include <cuda.h>
#include <cuda_runtime.h>
#include <algorithm>
#include <memory>
#include <stdexcept>
#include <string>
extern thread_local std::string fv_message;
extern thread_local int fv_domain;
extern thread_local int fv_code;
struct NativeError : std::runtime_error {
    int domain, code;
    NativeError(int domain, int code, const char* message) : std::runtime_error(message), domain(domain), code(code) {}
};
inline void ck(cudaError_t e) { if(e!=cudaSuccess) throw NativeError(1,(int)e,cudaGetErrorString(e)); }
inline void driver(CUresult e) {
    if(e!=CUDA_SUCCESS) { const char* text=nullptr; cuGetErrorString(e,&text); throw NativeError(2,(int)e,text?text:"CUDA driver error"); }
}
struct ContextScope {
    CUcontext previous=nullptr;
    explicit ContextScope(void* context) {
        if(!context) throw std::invalid_argument("null CUDA context");
        driver(cuCtxGetCurrent(&previous)); driver(cuCtxSetCurrent((CUcontext)context));
    }
    ~ContextScope() { cuCtxSetCurrent(previous); }
    void restore() { driver(cuCtxSetCurrent(previous)); }
};
inline int failure(int domain,int code,const char* message) noexcept {
    fv_domain=domain;fv_code=code;try { fv_message=message; } catch(...) { fv_message.clear(); } return 1;
}
#define API_BEGIN try {
#define API_END return 0; } catch(const NativeError& e) { return failure(e.domain,e.code,e.what()); } catch(const std::exception& e) { return failure(0,0,e.what()); } catch(...) { return failure(0,0,"Unknown native failure"); }
