#define TILE_DIM 256
#define BATCH_0 4
#define BATCH_1 4
SamplerState samp : register(s0, space0);
cbuffer params : register(b1, space0)
{
	uint uFilterDim;
	uint uBlockDim;
};
Texture2D inputTex : register(t1, space1);
RWTexture2D<float4> outputTex : register(u2, space1);
cbuffer uniforms : register(b3, space1)
{
	uint uFlip;
};
// following statements are copied from http://austin-eng.com/webgpu-samples/samples/imageBlur?wgsl=0
// This shader blurs the input texture in one diection, depending on whether
// |uFlip| is 0 or 1.
// It does so by running ${tileDim / batch[0]} threads per workgroup to load ${tileDim}
// texels into ${batch[1]} rows of shared memory. Each thread loads a
// ${batch[0]} x ${batch[1]} block of texels to take advantage of the texture sampling
// hardware.
// Then, each thread computes the blur result by averaging the adjacent texel values
// in shared memory.
// Because we're operating on a subset of the texture, we cannot compute all of the
// results since not all of the neighbors are available in shared memory.
// Specifically, with ${tileDim} x ${tileDim} tiles, we can only compute and write out
// square blocks of size ${tileDim} - (filterSize - 1). We compute the number of blocks
// needed and dispatch that amount.
groupshared float3 tile[BATCH_1][TILE_DIM];
[numthreads(TILE_DIM / BATCH_0, 1, 1)]
void main( uint3 DTid : SV_DispatchThreadID, uint3 Gid: SV_GroupID, uint3 GTid: SV_GroupThreadID)
{
	int filterOffset = int(uFilterDim - 1) / 2;
	uint2 dims;
	inputTex.GetDimensions(dims.x, dims.y);
	int2 baseIndex = int2(Gid.xy * uint2(uBlockDim, BATCH_1) + GTid.xy * uint2(BATCH_0, 1)) - int2(filterOffset, 0);
	[unroll]
	for (uint r = 0; r < BATCH_1; ++r)
	{
		for (uint c = 0; c < BATCH_0; ++c)
		{
			int2 loadIndex = baseIndex + int2(c, r);
			if (uFlip != 0)
			{
				loadIndex = loadIndex.yx;
			}
			float4 sampled = inputTex.SampleLevel(samp, (float2(loadIndex)+float2(0.25, 0.25)) / float2(dims), 0);
			tile[r][BATCH_0 * GTid.x + c] = sampled.rgb;
		}
	}
	GroupMemoryBarrierWithGroupSync();
	[unroll]
	for (uint r = 0; r < BATCH_1; ++r)
	{
		for (uint c = 0; c < BATCH_0; ++c)
		{
			int2 writeIndex = baseIndex + int2(c, r);
			if (uFlip != 0)
			{
				writeIndex = writeIndex.yx;
			}
			uint center = BATCH_0 * GTid.x + c;
			if (center >= filterOffset && center < TILE_DIM - filterOffset && all(writeIndex < dims))
			{
				float3 acc = float3(0, 0, 0);
				for (uint f = 0; f < uFilterDim; ++f)
				{
					uint i = center + f - filterOffset;
					acc += (1.0 / float(uFilterDim)) * tile[r][i];
				}
				outputTex[uint2(writeIndex)] = float4(acc, 1.0);
			}
			else
			{
				//outputTex[uint2(writeIndex)] = float4(baseIndex / float2(512, 512), 0.0, 1.0);
			}

		}
	}


}