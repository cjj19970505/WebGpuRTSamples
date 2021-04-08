struct VertexOut
{
	float4 PosH  : SV_POSITION;
	float2 uv : TEXCOORD0;
};


VertexOut VSMain(float3 position : WGPULOCATION0, float2 uv:WGPULOCATION1, uint instanceIndex: SV_InstanceID)
{
	VertexOut vout;
	vout.PosH = float4(position, 1);
	vout.uv = uv;
	return vout;
}

SamplerState mySampler : register(s0, space0);
Texture2D myTexture : register(t1, space0);

float4 PSMain(VertexOut pin) : SV_Target
{
	return myTexture.Sample(mySampler, pin.uv);
}