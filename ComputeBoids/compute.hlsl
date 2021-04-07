#define NUM_PARTICLES 1500u

struct Particle
{
	float2 pos;
	float2 vel;
};

struct SimParams
{
	float deltaT;
	float rule1Distance;
	float rule2Distance;
	float rule3Distance;
	float rule1Scale;
	float rule2Scale;
	float rule3Scale;
};

cbuffer simParamBuffer:register(b0, space0)
{
	SimParams params;
};
ByteAddressBuffer particlesSrcBuffer : register(t1, space0);
RWByteAddressBuffer particlesDstBuffer : register(u2, space0);
Particle GetParticle(uint index)
{
	const uint size = 4 * 4;
	uint offset = size * index;
	Particle particle;
	particle.pos = asfloat(particlesSrcBuffer.Load2(offset));
	particle.vel = asfloat(particlesSrcBuffer.Load2(offset + 4 * 2));
	return particle;
}

void StoreParticle(uint index, Particle value)
{
	const uint size = 4 * 4;
	uint offset = size * index;
	particlesDstBuffer.Store2(offset, asuint(value.pos));
	particlesDstBuffer.Store2(offset + 4 * 2, asuint(value.vel));
}

[numthreads(64, 1, 1)]
void main( uint3 DTid : SV_DispatchThreadID )
{
	uint index = DTid.x;
	if (index >= NUM_PARTICLES)
	{
		return;
	}
	Particle particle = GetParticle(index);
	float2 vPos = particle.pos;
	float2 vVel = particle.vel;
	float2 cMass = float2(0, 0);
	float2 cVel = float2(0, 0);
	float2 colVel = float2(0, 0);
	int cMassCount = 0;
	int cVelCount = 0;
	float2 pos;
	float2 vel;
	[loop]
	for (uint i = 0; i < NUM_PARTICLES; ++i)
	{
		if (i == index)
		{
			continue;
		}
		particle = GetParticle(index);
		pos = particle.pos;
		vel = particle.vel;
		if (distance(pos, vPos) < params.rule1Distance)
		{
			cMass += pos;
			++cMassCount;
		}
		if (distance(pos, vPos) < params.rule2Distance)
		{
			colVel  -= (pos - vPos);
		}
		if (distance(pos, vPos) < params.rule3Distance)
		{
			cVel += vel;
			++cVelCount;
		}
	}
	if (cMassCount > 0)
	{
		cMass = cMass * (1.0 / float(cMassCount)) - vPos;
	}
	if (cVelCount > 0)
	{
		cVel = cVel * (1.0 / float(cVelCount));
	}
	vVel = vVel + (cMass * params.rule1Scale) + (colVel * params.rule2Scale) + (cVel * params.rule3Scale);
	vVel = normalize(vVel) * clamp(length(vVel), 0.0, 0.1);
	vPos = vPos + (vVel * params.deltaT);
	if (vPos.x < -1.0) 
	{
		vPos.x = 1.0;
	}
	if (vPos.x > 1.0) 
	{
		vPos.x = -1.0;
	}
	if (vPos.y < -1.0) 
	{
		vPos.y = 1.0;
	}
	if (vPos.y > 1.0) 
	{
		vPos.y = -1.0;
	}
	particle.pos = vPos;
	particle.vel = vVel;
	StoreParticle(index, particle);
}