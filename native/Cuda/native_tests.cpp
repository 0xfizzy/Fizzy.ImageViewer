#include "imageviewer_cuda.h"
#include <cuda.h>
#include <cuda_runtime.h>
#include <d3d9.h>
#include <wrl/client.h>
#include <vector>
#include <iostream>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
void check(bool ok,const char* text){if(!ok)throw std::runtime_error(text);}
void ck(cudaError_t e){check(e==cudaSuccess,cudaGetErrorString(e));}
int main(){
 try{
  check(cuInit(0)==CUDA_SUCCESS,"cuInit");CUdevice device;check(cuDeviceGet(&device,0)==CUDA_SUCCESS,"device");
  CUcontext context;check(cuDevicePrimaryCtxRetain(&context,device)==CUDA_SUCCESS,"context");check(cuCtxSetCurrent(context)==CUDA_SUCCESS,"set context");
  cudaStream_t stream;ck(cudaStreamCreate(&stream));
  check(fv_abi_version()==1,"ABI");
  for(int format=0;format<6;format++){
   const int w=257,h=257,ch=format==0?1:format<3?3:4,pitch=w*ch+7;
   std::vector<unsigned char> src(pitch*h,199);int alphas[]={0,1,127,254,255};
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)for(int c=0;c<ch;c++){
    int alpha=alphas[x%5],v=(x+y+c*31)%256;
    src[y*pitch+x*ch+c]=(unsigned char)(c==3?alpha:format==4?(v*alpha+127)/255:v);
   }
   void *input,*scratch,*out,*host,*coords,*deviceCoords;ck(cudaMalloc(&input,pitch*h));ck(cudaMalloc(&scratch,(size_t)w*h*4));
   ck(cudaMalloc(&out,(size_t)w*h*4));ck(cudaMallocHost(&host,(size_t)w*h*4));ck(cudaMallocHost(&coords,16));ck(cudaMalloc(&deviceCoords,16));
   ck(cudaMemcpyAsync(input,src.data(),src.size(),cudaMemcpyHostToDevice,stream));ck(cudaStreamSynchronize(stream));
   void *owner=nullptr,*surface=nullptr;
   check(fv_display_create(context,0,h,&owner,&surface)!=0&&!owner&&!surface,"invalid surface");
   check(!fv_display_create(context,w,h,&owner,&surface),fv_error());
   check(!fv_display_write(owner,stream,input,pitch,format,format==4?nullptr:scratch),fv_error());ck(cudaStreamSynchronize(stream));
   auto target=(IDirect3DSurface9*)surface;ComPtr<IDirect3DDevice9> d3d;check(SUCCEEDED(target->GetDevice(&d3d)),"GetDevice");
   ComPtr<IDirect3DSurface9> staging;check(SUCCEEDED(d3d->CreateOffscreenPlainSurface(w,h,D3DFMT_A8R8G8B8,D3DPOOL_SYSTEMMEM,&staging,nullptr)),"staging");
   check(SUCCEEDED(d3d->GetRenderTargetData(target,staging.Get())),"readback");D3DLOCKED_RECT locked{};check(SUCCEEDED(staging->LockRect(&locked,nullptr,D3DLOCK_READONLY)),"lock");
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    auto p=&src[y*pitch+x*ch];auto q=(unsigned char*)locked.pBits+y*locked.Pitch+x*4;
    int a=ch==4?p[3]:255;bool rgb=format==2||format==5;int b=p[ch==1?0:rgb?2:0],g=p[ch==1?0:1],r=p[ch==1?0:rgb?0:2];
    if(format==3||format==5){b=(b*a+127)/255;g=(g*a+127)/255;r=(r*a+127)/255;}
    check(q[0]==b&&q[1]==g&&q[2]==r&&q[3]==a,"surface conversion");
   }
   staging->UnlockRect();check(!fv_display_release(owner),fv_error());
   auto xy=(int*)coords;xy[0]=0;xy[1]=0;xy[2]=w-1;xy[3]=h-1;
   check(!fv_query(context,stream,input,pitch,w,h,ch,0,coords,2,0,0,0,0,deviceCoords,nullptr,out,host),fv_error());ck(cudaStreamSynchronize(stream));
   auto bytes=(unsigned char*)host;for(int c=0;c<ch;c++){check(bytes[c]==src[c],"gather start");check(bytes[ch+c]==src[(h-1)*pitch+(w-1)*ch+c],"gather end");}
   for(int size:{1,16,17,257}){
    check(!fv_query(context,stream,input,pitch,w,h,ch,1,nullptr,0,0,0,size,size,nullptr,scratch,out,host),fv_error());ck(cudaStreamSynchronize(stream));
    auto stats=(uint64_t*)host;for(int c=0;c<ch;c++){uint64_t sum=0,mn=255,mx=0;for(int y=0;y<size;y++)for(int x=0;x<size;x++){auto v=src[y*pitch+x*ch+c];sum+=v;mn=mn<v?mn:v;mx=mx>v?mx:v;}check(stats[c*4]==size*size&&stats[c*4+1]==mn&&stats[c*4+2]==mx&&stats[c*4+3]==sum,"statistics");}
   }
   check(!fv_query(context,stream,input,pitch,w,h,ch,2,nullptr,0,2,3,4,5,nullptr,nullptr,nullptr,host),fv_error());ck(cudaStreamSynchronize(stream));
   for(int y=0;y<5;y++)for(int x=0;x<4*ch;x++)check(bytes[y*4*ch+x]==src[(y+3)*pitch+2*ch+x],"region");
   xy[0]=w;check(fv_query(context,stream,input,pitch,w,h,ch,0,coords,2,0,0,0,0,deviceCoords,nullptr,out,host)!=0,"invalid point");
   ck(cudaFree(input));ck(cudaFree(scratch));ck(cudaFree(out));ck(cudaFreeHost(host));ck(cudaFreeHost(coords));ck(cudaFree(deviceCoords));
  }
  check(fv_display_live_surfaces()==0,"surface leak");ck(cudaStreamDestroy(stream));check(cuDevicePrimaryCtxRelease(device)==CUDA_SUCCESS,"release context");
  std::cout<<"PASS: six formats, alpha, padded pitch, gather, ROI boundaries, statistics and cleanup";return 0;
 }catch(const std::exception& e){std::cerr<<e.what();return 1;}
}
