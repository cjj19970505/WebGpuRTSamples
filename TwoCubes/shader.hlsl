struct VertexOut
{
	float4 PosH  : SV_POSITION;
	float4 color : TEXCOORD0;
};

cbuffer buffer:register(b0, space0)
{
	float4x4 modelViewProjectionMatrix;
	
};

VertexOut VSMain(float4 position : WGPULOCATION0, float4 color:WGPULOCATION1)
{
	VertexOut vout;
	vout.PosH = mul(modelViewProjectionMatrix, position);
	vout.color = color;
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	return pin.color;
}