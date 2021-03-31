struct VertexOut
{
	float4 position  : SV_POSITION;
	float4 color : TEXCOORD0;
};

cbuffer Time:register(b0, space0)
{
	float time;
};

cbuffer Uniforms:register(b0, space1)
{
	float scale;
	float offsetX;
	float offsetY;
	float scalar;
	float scalarOffset;
};


VertexOut VSMain(float4 position : WGPULOCATION0, float4 color:WGPULOCATION1)
{
	float fade = fmod(scalarOffset + time * scalar / 10.0, 1.0);
	if (fade < 0.5)
	{
		fade = fade * 2.0;
	}
	else
	{
		fade = (1.0 - fade) * 2.0;
	}
	VertexOut vout;
	float xpos = position.x * scale;
	float ypos = position.y * scale;
	float angle = 3.14159 * 2.0 * fade;
	float xrot = xpos * cos(angle) - ypos * sin(angle);
	float yrot = xpos * sin(angle) + ypos * cos(angle);
	xpos = xrot + offsetX;
	ypos = yrot + offsetY;
	vout.color = float4(fade, 1.0 - fade, 0.0, 1.0) + color;
	vout.position = float4(xpos, ypos, 0, 1);
	//vout.position = float4(position.xy * time, 0, 1);
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	return pin.color;
}