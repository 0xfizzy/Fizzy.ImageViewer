#include "operation.h"
namespace {
struct Stat{uint64_t count,minimum,maximum,sum;};
__global__ void gather(const unsigned char* input,int64_t pitch,int channels,const int2* points,int count,unsigned char* out){
    int i=blockIdx.x*blockDim.x+threadIdx.x;if(i>=count)return;
    auto p=points[i];for(int c=0;c<channels;c++)out[i*channels+c]=input[p.y*pitch+p.x*channels+c];
}
__global__ void reduce(const unsigned char* src,int64_t pitch,int channels,int x,int y,int w,int h,Stat* partial){
    __shared__ Stat values[256];int c=blockIdx.y,t=threadIdx.x;Stat s{0,255,0,0};
    for(int64_t i=(int64_t)blockIdx.x*blockDim.x+t;i<(int64_t)w*h;i+=(int64_t)gridDim.x*blockDim.x){
        unsigned v=src[(y+i/w)*pitch+(x+i%w)*channels+c];s.count++;s.sum+=v;s.minimum=s.minimum<v?s.minimum:v;s.maximum=s.maximum>v?s.maximum:v;
    }
    values[t]=s;__syncthreads();
    for(int step=128;step;step/=2){if(t<step){auto& a=values[t];auto b=values[t+step];a.count+=b.count;a.sum+=b.sum;a.minimum=a.minimum<b.minimum?a.minimum:b.minimum;a.maximum=a.maximum>b.maximum?a.maximum:b.maximum;}__syncthreads();}
    if(t==0)partial[c*gridDim.x+blockIdx.x]=values[0];
}
__global__ void finish(Stat* partial,int blocks,Stat* out){
    int c=threadIdx.x;Stat s{0,255,0,0};for(int b=0;b<blocks;b++){auto v=partial[c*blocks+b];s.count+=v.count;s.sum+=v.sum;s.minimum=s.minimum<v.minimum?s.minimum:v.minimum;s.maximum=s.maximum>v.maximum?s.maximum:v.maximum;}out[c]=s;
}
}
int fv_query(void* context,void* stream,const void* input,int64_t pitch,int width,int height,int channels,int kind,
    const void* coordinates,int count,int x,int y,int rw,int rh,void* deviceCoordinates,void* scratch,void* output,void* host){
    API_BEGIN
    if(!input||!host||width<=0||height<=0||(channels!=1&&channels!=3&&channels!=4)||pitch<(int64_t)width*channels||kind<0||kind>2)
        throw std::invalid_argument("invalid query layout");
    ContextScope scope(context);auto queue=(cudaStream_t)stream;auto src=(const unsigned char*)input;
    if(kind==0){
        if(!coordinates||!deviceCoordinates||!output||count<=0||count>65536)throw std::invalid_argument("invalid gather");
        auto xy=(const int*)coordinates;
        for(int i=0;i<count;i++)if(xy[i*2]<0||xy[i*2]>=width||xy[i*2+1]<0||xy[i*2+1]>=height)throw std::invalid_argument("point outside image");
        ck(cudaMemcpyAsync(deviceCoordinates,coordinates,(size_t)count*8,cudaMemcpyHostToDevice,queue));
        gather<<<(count+255)/256,256,0,queue>>>(src,pitch,channels,(const int2*)deviceCoordinates,count,(unsigned char*)output);
        ck(cudaGetLastError());ck(cudaMemcpyAsync(host,output,(size_t)count*channels,cudaMemcpyDeviceToHost,queue));
    }else{
        if(x<0||y<0||rw<=0||rh<=0||(int64_t)x+rw>width||(int64_t)y+rh>height)throw std::invalid_argument("region outside image");
        if(kind==1){
            if(!scratch||!output)throw std::invalid_argument("missing reduction storage");
            const int blocks=(int)std::min<int64_t>(256,((int64_t)rw*rh+255)/256);
            reduce<<<dim3(blocks,channels),256,0,queue>>>(src,pitch,channels,x,y,rw,rh,(Stat*)scratch);
            ck(cudaGetLastError());finish<<<1,channels,0,queue>>>((Stat*)scratch,blocks,(Stat*)output);
            ck(cudaGetLastError());ck(cudaMemcpyAsync(host,output,channels*sizeof(Stat),cudaMemcpyDeviceToHost,queue));
        }else ck(cudaMemcpy2DAsync(host,(size_t)rw*channels,src+y*pitch+(int64_t)x*channels,(size_t)pitch,(size_t)rw*channels,rh,cudaMemcpyDeviceToHost,queue));
    }
    scope.restore();
    API_END
}
