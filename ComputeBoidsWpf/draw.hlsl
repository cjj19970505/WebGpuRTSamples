struct VertexOut
{
	float4 position  : SV_POSITION;
};

VertexOut VSMain( float2 particle_pos : WGPULOCATION0, float2 particle_vel : WGPULOCATION1, float2 position: WGPULOCATION2)
{
	VertexOut vout;
	float angle = -atan2(particle_vel.x, particle_vel.y);
	float2 pos = float2(
		position.x * cos(angle) - position.y * sin(angle),
		position.x * sin(angle) + position.y * cos(angle)
	);
	vout.position = float4(pos + particle_pos, 0, 1);
	return vout;
}

float4 PSMain(VertexOut pin) : SV_Target
{
	return float4(1,1,1,1);
}