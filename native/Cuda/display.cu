#include "operation.h"
#include <d3d9.h>
#include <cuda_d3d9_interop.h>
#include <wrl/client.h>
#include <atomic>
#include <map>
#include <mutex>
using Microsoft::WRL::ComPtr;
thread_local std::string fv_message;
thread_local int fv_domain=0;
thread_local int fv_code=0;
namespace {
void hr(HRESULT r) { if(FAILED(r)) throw NativeError(3,(int)r,"D3D9 operation failed"); }
struct Device {
    ComPtr<IDirect3D9Ex> api;
    ComPtr<IDirect3DDevice9Ex> device;
    explicit Device(int ordinal) {
        hr(Direct3DCreate9Ex(D3D_SDK_VERSION,&api));
        UINT adapter=UINT_MAX;
        for(UINT i=0;i<api->GetAdapterCount();++i) {
            D3DADAPTER_IDENTIFIER9 id{}; hr(api->GetAdapterIdentifier(i,0,&id));
            int candidate=-1;
            if(cudaD3D9GetDevice(&candidate,id.DeviceName)==cudaSuccess && candidate==ordinal) { adapter=i;break; }
        }
        if(adapter==UINT_MAX) throw std::runtime_error("No D3D9 adapter matches CUDA device");
        D3DPRESENT_PARAMETERS pp{}; pp.Windowed=TRUE;pp.SwapEffect=D3DSWAPEFFECT_DISCARD;
        pp.hDeviceWindow=GetDesktopWindow();pp.BackBufferFormat=D3DFMT_UNKNOWN;pp.BackBufferWidth=1;pp.BackBufferHeight=1;
        hr(api->CreateDeviceEx(adapter,D3DDEVTYPE_HAL,pp.hDeviceWindow,
            D3DCREATE_MULTITHREADED|D3DCREATE_FPU_PRESERVE|D3DCREATE_HARDWARE_VERTEXPROCESSING,&pp,nullptr,&device));
    }
};
std::mutex devicesGate;
std::map<CUcontext,std::weak_ptr<Device>> devices;
std::shared_ptr<Device> getDevice(CUcontext context) {
    std::lock_guard<std::mutex> lock(devicesGate);
    auto device=devices[context].lock();
    if(!device) { CUdevice ordinal;driver(cuCtxGetDevice(&ordinal));device=std::make_shared<Device>(ordinal);devices[context]=device; }
    return device;
}
std::atomic<int64_t> live{0};
struct Surface {
    std::shared_ptr<Device> device;
    ComPtr<IDirect3DTexture9> texture;
    ComPtr<IDirect3DSurface9> surface;
    cudaGraphicsResource* registration=nullptr;
    CUcontext context=nullptr;
    int width,height;
    bool mapped=false;
    Surface(int w,int h):width(w),height(h){++live;}
    ~Surface(){--live;}
};
__global__ void convert(const unsigned char* input,int64_t pitch,uchar4* output,int w,int h,int format) {
    int x=blockIdx.x*blockDim.x+threadIdx.x,y=blockIdx.y*blockDim.y+threadIdx.y;
    if(x>=w||y>=h)return;
    int channels=format==0?1:format<3?3:4;
    auto p=input+y*pitch+x*channels;
    if(format==0) { output[y*w+x]=make_uchar4(p[0],p[0],p[0],255);return; }
    bool rgb=format==2||format==5;
    int b=p[rgb?2:0],g=p[1],r=p[rgb?0:2],a=channels==4?p[3]:255;
    if(format==3||format==5){b=(b*a+127)/255;g=(g*a+127)/255;r=(r*a+127)/255;}
    output[y*w+x]=make_uchar4((unsigned char)b,(unsigned char)g,(unsigned char)r,(unsigned char)a);
}
}
int fv_abi_version(){return 1;}
const char* fv_error(){return fv_message.c_str();}
int fv_error_domain(){return fv_domain;}
int fv_error_code(){return fv_code;}
int64_t fv_display_live_surfaces(){return live.load();}
int fv_display_create(void* context,int w,int h,void** owner,void** surface) {
    API_BEGIN
    if(!owner||!surface)throw std::invalid_argument("null surface output");
    *owner=nullptr;*surface=nullptr;
    if(w<=0||h<=0)throw std::invalid_argument("invalid dimensions");
    ContextScope scope(context);
    auto result=std::make_unique<Surface>(w,h);
    result->context=(CUcontext)context;result->device=getDevice(result->context);
    hr(result->device->device->CreateTexture(w,h,1,D3DUSAGE_RENDERTARGET,D3DFMT_A8R8G8B8,D3DPOOL_DEFAULT,&result->texture,nullptr));
    hr(result->texture->GetSurfaceLevel(0,&result->surface));
    scope.restore();
    *surface=result->surface.Get();*owner=result.release();
    API_END
}
int fv_display_write(void* owner,void* stream,const void* input,int64_t pitch,int format,void* scratch) {
    API_BEGIN
    if(!owner||!input||format<0||format>5)throw std::invalid_argument("invalid display input");
    auto s=static_cast<Surface*>(owner);int channels=format==0?1:format<3?3:4;
    if(pitch<(int64_t)s->width*channels||(format!=4&&!scratch))throw std::invalid_argument("invalid display storage");
    ContextScope scope(s->context);auto queue=(cudaStream_t)stream;
    ck(cudaGraphicsD3D9RegisterResource(&s->registration,s->texture.Get(),cudaGraphicsRegisterFlagsNone));
    ck(cudaGraphicsMapResources(1,&s->registration,queue));s->mapped=true;
    cudaArray_t array;ck(cudaGraphicsSubResourceGetMappedArray(&array,s->registration,0,0));
    auto pixels=input;size_t rowPitch=(size_t)pitch;
    if(format!=4) {
        convert<<<dim3((s->width+15)/16,(s->height+15)/16),dim3(16,16),0,queue>>>((const unsigned char*)input,pitch,(uchar4*)scratch,s->width,s->height,format);
        ck(cudaGetLastError());pixels=scratch;rowPitch=(size_t)s->width*4;
    }
    ck(cudaMemcpy2DToArrayAsync(array,0,0,pixels,rowPitch,(size_t)s->width*4,s->height,cudaMemcpyDeviceToDevice,queue));
    ck(cudaGraphicsUnmapResources(1,&s->registration,queue));s->mapped=false;
    scope.restore();
    API_END
}
int fv_display_release(void* owner) {
    API_BEGIN
    if(!owner)return 0;
    auto s=static_cast<Surface*>(owner);ContextScope scope(s->context);
    // Only exceptional mapped resources reach this branch, after bridge drain proof.
    if(s->mapped) { ck(cudaGraphicsUnmapResources(1,&s->registration,0));s->mapped=false;ck(cudaStreamSynchronize(0)); }
    if(s->registration) { ck(cudaGraphicsUnregisterResource(s->registration));s->registration=nullptr; }
    scope.restore();delete s;
    API_END
}
