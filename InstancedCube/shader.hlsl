#define MAX_NUM_INSTANCES 16
struct VertexOut
{
	float4 PosH  : SV_POSITION;
	float4 color : TEXCOORD0;
};

cbuffer buffer:register(b0, space0)
{
	float4x4 modelViewProjectionMatrix[MAX_NUM_INSTANCES];
	
};

VertexOut VSMain(float4 position : WGPULOCATION0, float4 color:WGPULOCATION1, uint instanceIndex: SV_InstanceID)
{
	VertexOut vout;
	vout.PosH = mul(modelViewProjectionMatrix[instanceIndex], position);
	vout.color = color;
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	return pin.color;
}