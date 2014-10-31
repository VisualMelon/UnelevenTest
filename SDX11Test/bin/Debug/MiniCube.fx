struct VS_Input
{
	float4 pos : POSITION;
	float4 nrm : NORMAL;
	float4 col : COLOR;
	float tti : TEXCOORD0;
};

struct VS_Output
{
	float4 pos : SV_POSITION;
	float4 nrm : TEXCOORD1;
	float4 altPos : TEXCOORD0;
	float4 col : COLOR0;
};

struct VS_Input_Tex
{
	float4 pos : POSITION0;
	float4 nrm : NORMAL0;
	float4 col : COLOR0;
	float2 txc : TEXCOORD0;
	float tti : TEXCOORD1;
};

struct VS_Output_Tex
{
	float4 pos : SV_POSITION;
	float lit : TEXCOORD3;
	float4 altPos : TEXCOORD1;
	float4 col : COLOR0;
	float2 txc : TEXCOORD0;
	float4 lmc: TEXCOORD2;
};

struct VS_Input_Over
{
	float4 pos : POSITION0;
	float2 txc : TEXCOORD0;
};

struct VS_Output_Over
{
	float4 pos : SV_POSITION;
	float2 txc : TEXCOORD0;
	float4 altPos : TEXCOORD1;
};

struct PS_Output
{
	float4 col : SV_TARGET;
};

cbuffer eyeBuffer : register(b0)
{
	matrix viewProj;// : packoffset(c0);
	float4 eyePos;
	float4 eyeDir;
	float farDepth;
	float invFarDepth;
};

cbuffer lightBuffer : register(b1)
{
	matrix lightViewProj;
	float4 lightPos;
	float4 lightDir;
	float4 lightAmbient;
	float4 lightColMod;
	float lightCoof;
	float lightDepth;
	float lightDodge;
	float lightType;
};

// lightmap buffer ?

cbuffer transBuffer : register(b2)
{
	matrix transarr[30]; // need to be same as number of segs
};

cbuffer spriteBuffer : register(b2)
{
	float4 spriteLoc[120]; // sprite buffer size must be no more than (this len / size of sprite data)
};

cbuffer overBuffer : register(b3)
{
	float4 overTexData;
};

cbuffer targetBuffer : register(b3)
{
	matrix targetVPMat;
	float4 targetTexData;
};

cbuffer sectionBuffer : register(b4)
{
	float4 colMod;
};

// don't know if this works yet, we will find out
Texture2D tex : register( t0 );
Texture2D tex0 : register( t1 );
Texture2D tex1 : register( t2 );
Texture2D tex2 : register( t3 );
Texture2D tex3 : register( t4 );
Texture2D sideTex : register( t5 );
Texture2D targetTex : register( t5 );
Texture2D lightTex : register( t6 );
Texture2D lightPatternTex : register( t7 );

SamplerState linearWrapSampler : register( s0 );
SamplerState pointWrapSampler : register( s1 );
SamplerState linearBorderSampler : register( s2 );
SamplerState pointBorderSampler : register( s3 );




//
// helper methods
//

float clampPositive(float num)
{
	if (num < 0)
		return 0;
	return num;
}

float4 normaliseXYZ(float4 vec)
{
	float mod = rsqrt(vec.x * vec.x + vec.y * vec.y + vec.z * vec.z);
	return vec * mod;
}

float4 reflect(float4 dir, float4 nrm)
{
	return dir + (nrm * -dot(nrm, dir)) * 2.0;
}

float4 lightTransOrtho(float4 pos)
{
	float4 res = mul(pos, lightViewProj);
	return res;
}

float4 lightTransPersp(float4 pos)
{
	float4 res = mul(pos, lightViewProj);
	res.z = res.z * res.w / lightDepth;
	return res;
}

float4 lightTransPoint(float4 pos)
{
	pos.w = 1;
	return pos;
}

float4 lightTrans(float4 pos)
{
	if (lightType == 0)
		return lightTransOrtho(pos);
	else if (lightType == 1)
		return lightTransPersp(pos);
	else if (lightType == 2)
		return lightTransPoint(pos);
	return (float4)0;
}

float4 lightTransOrthoVP(float4 pos)
{
	float4 res = mul(pos, viewProj);
	return res;
}

float4 lightTransPerspVP(float4 pos)
{
	float4 res = mul(pos, viewProj);
	res.z = res.z * res.w / lightDepth;
	return res;
}

float4 lightTransPointVP(float4 pos)
{
	pos.w = 1;
	return pos;
}

float4 lightTransVP(float4 pos)
{
	if (lightType == 0)
		return lightTransOrthoVP(pos);
	else if (lightType == 1)
		return lightTransPerspVP(pos);
	else if (lightType == 2)
		return lightTransPointVP(pos);
	return (float4)0;
}

float4 lightUnTransOrtho(float4 pos)
{
	float4 res = pos;
	return res;
}

float4 lightUnTransPersp(float4 pos)
{
	float4 res = pos;
	res.x = res.x / res.w;
	res.y = res.y / res.w;
	res.z = res.z / res.w;
	return res;
}

float4 lightUnTransPoint(float4 pos)
{
	return pos;
}

float4 lightUnTrans(float4 pos)
{
	if (lightType == 0)
		return lightUnTransOrtho(pos);
	else if (lightType == 1)
		return lightUnTransPersp(pos);
	else if (lightType == 2)
		return lightUnTransPoint(pos);
	return (float4)0;
}

float lightLitnessOrtho(float4 pos, float4 nrm)
{
	float res = -dot(nrm, lightDir);
	return res;
}

float lightLitnessPersp(float4 pos, float4 nrm)
{
	float4 plDir = pos - lightPos;
	plDir = normaliseXYZ(plDir);

	float res = -dot(nrm, plDir);
	return res;
}

float lightLitnessPoint(float4 pos, float4 nrm)
{
	float4 plDir = pos - lightPos;
	plDir = normaliseXYZ(plDir);

	float res = -dot(nrm, plDir);
	return res;
}

// reflectiveness (not quite specular)
/*float lightLitnessPointRef(float4 pos, float4 nrm)
{
	float4 plDir = pos - lightPos;
	plDir = normaliseXYZ(plDir);
	float4 eDir = pos - eyePos; // eyeDir just doesn't cut it
	eDir = normaliseXYZ(eDir);

	plDir.w = 0; // need a zero w for the dot
	//float res = -dot(nrm, plDir); // dullness
	res += -dot(plDir, reflect(eDir, nrm)); // reflectivness
	return res;
}*/

float lightLitness(float4 pos, float4 nrm)
{
	if (lightType == 0)
		return lightLitnessOrtho(pos, nrm);
	else if (lightType == 1)
		return lightLitnessPersp(pos, nrm);
	else if (lightType == 2)
		return lightLitnessPoint(pos, nrm);
	return 0;
}

float4 calcLightModOrtho(float4 lmc)
{
	lmc = lightUnTransOrtho(lmc);

	float4 lightMod = 0.0;

	float2 lightCoords;
	lightCoords.x = lmc.x;
	lightCoords.y = lmc.y;

	float targDist = lmc.z;

	float4 lightCol = lightTex.Sample(linearBorderSampler, lightCoords);
	float lightDist = lightCol.x;

	if (lightDodge + lightDist > targDist)
	{
		lightMod = lightPatternTex.Sample(linearBorderSampler, lightCoords) * lightColMod;
		return lightMod;
	}
	lightMod = 0;
	return lightMod;
}
// these two (calcLightModOrtho and calcLightModPersp) are the SAME ATM
float4 calcLightModPersp(float4 lmc)
{
	lmc = lightUnTransPersp(lmc);

	float4 lightMod = 0.0;

	float2 lightCoords;
	lightCoords.x = lmc.x;
	lightCoords.y = lmc.y;

	float targDist = lmc.z;

	float4 lightCol = lightTex.Sample(linearBorderSampler, lightCoords);
	float lightDist = lightCol.x;

	if (lightDodge + lightDist > targDist)
	{
		lightMod = lightPatternTex.Sample(linearBorderSampler, lightCoords) * lightColMod;
		lightMod *= (1 - targDist * targDist);
		return lightMod;
	}
	lightMod = 0;
	return lightMod;
}

float4 calcLightModPoint(float4 lmc)
{
	// lmc is infact orignal pos in disguise
	lmc = lightUnTransPoint(lmc);

	float4 lightMod = 0.0;

	float x = lmc.x - lightPos.x;
	float y = lmc.y - lightPos.y;
	float z = lmc.z - lightPos.z;

	float targDist = (x * x + y * y + z * z);

	targDist = 1.0 - targDist / (lightDepth * lightDepth);
	targDist = max(targDist, 0.0); // clamp down

	lightMod = lightColMod * targDist;
	return lightMod;
}

float4 calcLightMod_Switch(float4 lmc)
{
	if (lightType == 0)
		return calcLightModOrtho(lmc);
	else if (lightType == 1)
		return calcLightModPersp(lmc);
	else if (lightType == 2)
		return calcLightModPoint(lmc);
	return 0;
}

float4 calcDynModOrtho(float4 lmc)
{
	lmc = lightUnTransOrtho(lmc);

	float4 lightMod = 0.0;

	float2 lightCoords;
	lightCoords.x = lmc.x;
	lightCoords.y = lmc.y;

	lightMod = lightPatternTex.Sample(linearBorderSampler, lightCoords) * lightColMod;

	return lightMod;
}

float4 calcDynModPersp(float4 lmc)
{
	lmc = lightUnTransPersp(lmc);

	float4 lightMod = 0.0;

	float2 lightCoords;
	lightCoords.x = lmc.x;
	lightCoords.y = lmc.y;

	lightMod = lightPatternTex.Sample(linearBorderSampler, lightCoords) * lightColMod;

	return lightMod;
}

float4 calcDynMod(float4 lmc)
{
	if (lightType == 0)
		return calcDynModOrtho(lmc);
	else if (lightType == 1)
		return calcDynModPersp(lmc);
	return 0;
}




//
// shaders
//

// dull shaders

VS_Output VShade(VS_Input inp)
{
	VS_Output outp = (VS_Output)0;
	/*if (inp.tti >= 0)
	{
		outp.pos = mul(mul(inp.pos, transarr[inp.tti]), viewProj);
	}
	else
	{
		outp.pos = mul(inp.pos, viewProj);
	}*/
	outp.pos = mul(inp.pos, viewProj);
	//outp.pos = mul(viewProj, inp.pos);
	outp.col = inp.col;
	return outp;
}

PS_Output PShade(VS_Output inp)
{
	PS_Output outp = (PS_Output)0;
	outp.col = inp.col;// * colMod;

	return outp;
}

VS_Output VShade2(VS_Input inp)
{
	VS_Output outp = (VS_Output)0;
	if (inp.tti >= 0)
	{
		outp.pos = mul(mul(inp.pos, transarr[inp.tti]), viewProj);
	}
	else
	{
		outp.pos = mul(inp.pos, viewProj);
	}
	outp.col = inp.col;
	return outp;
}

PS_Output PShade2(VS_Output inp)
{
	PS_Output outp = (PS_Output)0;
	outp.col = inp.col * 0.5;// * colMod;

	return outp;
}

VS_Output_Tex VShade3(VS_Input_Tex inp)
{
	VS_Output_Tex outp = (VS_Output_Tex)0;
	if (inp.tti >= 0)
	{
		outp.pos = mul(mul(inp.pos, transarr[inp.tti]), viewProj);
	}
	else
	{
		outp.pos = mul(inp.pos, viewProj);
	}
	outp.col = inp.col;
	return outp;
}

PS_Output PShade3(VS_Output_Tex inp)
{
	PS_Output outp = (PS_Output)0;
	outp.col = inp.col * 0.5;// * colMod;

	return outp;
}




// tex shaders

VS_Output_Tex VShade_Tex(VS_Input_Tex inp)
{
	VS_Output_Tex outp = (VS_Output_Tex)0;
	inp.pos = mul(inp.pos, transarr[inp.tti]);
	inp.nrm = mul(inp.nrm, transarr[inp.tti]);
	outp.pos = mul(inp.pos, viewProj);
	outp.altPos = outp.pos;
	outp.altPos.z = outp.altPos.z * outp.altPos.w * invFarDepth;
	outp.col = inp.col;
	outp.txc = inp.txc;

	return outp;
}

// VShade_Tex_Lit
#define STD_MCR_VShade_Tex_Lit(lightName) \
VS_Output_Tex VShade_Tex_Lit##lightName##(VS_Input_Tex inp) \
{ \
	VS_Output_Tex outp = (VS_Output_Tex)0; \
	inp.pos = mul(inp.pos, transarr[inp.tti]); \
	inp.nrm = mul(inp.nrm, transarr[inp.tti]); \
	outp.pos = mul(inp.pos, viewProj); \
	outp.altPos = outp.pos; \
	outp.altPos.z = outp.altPos.z * outp.altPos.w * invFarDepth; \
	outp.col = inp.col; \
	outp.txc = inp.txc; \
 \
	outp.lit = lightLitness##lightName##(inp.pos, inp.nrm); \
	outp.lmc = lightTrans##lightName##(inp.pos); \
 \
	return outp; \
}

STD_MCR_VShade_Tex_Lit(Ortho)
STD_MCR_VShade_Tex_Lit(Persp)
STD_MCR_VShade_Tex_Lit(Point)

PS_Output PShade_Tex_Alpha(VS_Output_Tex inp)
{
	PS_Output outp = (PS_Output)0;
	outp.col = inp.col * tex.Sample(pointWrapSampler, inp.txc);

	clip(outp.col.w - 0.5);

	outp.col = outp.col * colMod;
	float alphaPreserve = outp.col.w;

	outp.col = outp.col * (1.0 - lightCoof);

	outp.col *= alphaPreserve;
	outp.col.w = alphaPreserve;

	return outp;
}

// PShade_Tex_Alpha_Lit
#define STD_MCR_PShade_Tex_Alpha_Lit(lightName) \
PS_Output PShade_Tex_Alpha_Lit##lightName##(VS_Output_Tex inp) \
{ \
	PS_Output outp = (PS_Output)0; \
	outp.col = inp.col * tex.Sample(pointWrapSampler, inp.txc); \
 \
	clip(outp.col.w - 0.5); \
 \
	float4 lightMod = calcLightMod##lightName##(inp.lmc); \
 \
	outp.col = outp.col * colMod; \
	float alphaPreserve = outp.col.w; \
 \
	outp.col = outp.col * (lightMod * inp.lit + lightAmbient) * lightCoof; \
 \
	outp.col *= alphaPreserve; \
	outp.col.w = 0; \
 \
	return outp; \
}

STD_MCR_PShade_Tex_Alpha_Lit(Ortho)
STD_MCR_PShade_Tex_Alpha_Lit(Persp)
STD_MCR_PShade_Tex_Alpha_Lit(Point)




// side/over shaders

VS_Output_Over VShade_Over(VS_Input_Over inp)
{
	VS_Output_Over outp = (VS_Output_Over)0;
	outp.pos = mul(inp.pos, viewProj);
	outp.altPos = outp.pos;
	outp.txc = inp.txc;
	return outp;
}

PS_Output PShade_Over(VS_Output_Over inp)
{
	PS_Output outp = (PS_Output)0;

	outp.col = tex.Sample(linearWrapSampler, inp.txc);

	return outp;
}







