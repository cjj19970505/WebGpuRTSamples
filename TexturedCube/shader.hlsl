struct VertexOut
{
	float4 PosH  : SV_POSITION;
	float2 uv : TEXCOORD0;
};

cbuffer buffer:register(b0, space0)
{
	float4x4 modelViewProjectionMatrix;
	
};
SamplerState mySampler : register(s1, space0);
Texture2D myTexture : register(t2, space0);

VertexOut VSMain(float4 position : WGPULOCATION0, float2 uv:WGPULOCATION1)
{
	VertexOut vout;
	vout.PosH = mul(modelViewProjectionMatrix, position);
	vout.uv = uv;
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	return myTexture.Sample(mySampler, pin.uv);
}