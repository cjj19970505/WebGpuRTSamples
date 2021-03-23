struct VertexOut
{
	float4 PosH  : SV_POSITION;
	float4 color : TEXCOORD0;
};

static const float2 pos[3] = { float2(0.0f, 0.5f), float2(-0.5f, -0.5f), float2(0.5f, -0.5f) };

VertexOut VSMain(uint vertexId: SV_VertexID)
{
	VertexOut vout;
	vout.PosH = float4(pos[vertexId], 0.0, 1.0);
	vout.color = float4(vertexId % 3 == 0, vertexId % 3 == 1, vertexId % 3 == 2, 1);
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	float4 col = pin.color;
	return col;
}