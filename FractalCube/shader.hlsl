struct VertexOut
{
	float4 PosH  : SV_POSITION;
	float4 color : TEXCOORD0;
	float2 uv : TEXCOORD1;
};

cbuffer buffer:register(b0, space0)
{
	float4x4 modelViewProjectionMatrix;
	
};
SamplerState mySampler : register(s1, space0);
Texture2D myTexture : register(t2, space0);

VertexOut VSMain(float4 position : WGPULOCATION0, float4 color:WGPULOCATION1, float2 uv : WGPULOCATION2)
{
	VertexOut vout;
	vout.PosH = mul(modelViewProjectionMatrix, position);
	vout.color = color;
	vout.uv = uv;
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	float4 texColor = myTexture.Sample(mySampler, pin.uv * 0.8 + 0.1);
	float f = float(length(texColor.rgb - float3(0.5, 0.5, 0.5)) < 0.01);
	return lerp(texColor, pin.color, f);
}