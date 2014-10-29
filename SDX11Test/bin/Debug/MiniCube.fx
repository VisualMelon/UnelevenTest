struct VS_Input
{
	float4 pos : POSITION;
	float4 nrm : NORMAL;
	float4 col : COLOR;
	float tti : TEXCOORD;
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
	float4 pos : POSITION0;
	float lit : TEXCOORD3;
	float4 altPos : TEXCOORD1;
	float4 col : COLOR0;
	float2 txc : TEXCOORD0;
	float4 lmc: TEXCOORD2;
};

struct PS_Output
{
	float4 col : SV_TARGET;
};

cbuffer eyeBuffer : register(cb0)
{
	matrix viewProj;// : packoffset(c0);
	float4 eyePos;
	float4 eyeDir;
	float farDepth;
	float invFarDepth;
};

cbuffer lightBuffer : register(cb1)
{
	matrix lightViewProj;
	float4 lightPos;
	float4 lightDir;
	float4 lightAmbient;
	float lightCoof;
	float lightDepth;
	float lightDodge;
	float lightType;
};

// lightmap buffer ?

cbuffer transBuffer : register(cb2)
{
	matrix transarr[30]; // need to be same as number of segs
};

cbuffer spriteBuffer : register(cb2)
{
	float4 spriteLoc[120]; // sprite buffer size must be no more than (this len / size of sprite data)
};

cbuffer overBuffer : register(cb3)
{
	float4 overTexData;
};

cbuffer targetBuffer : register(cb3)
{
	matrix targetVPMat;
	float4 targetTexData;
};

cbuffer sectionBuffer : register(cb4)
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

SamplerState linearSampler
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Wrap;
    AddressV = Wrap;
};


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

PS_Output PShade2(VS_Output inp)
{
	PS_Output outp = (PS_Output)0;
	outp.col = inp.col * 0.5;// * colMod;

	return outp;
}





VS_Output_Tex VShade_Tex(VS_Input_Tex inp)
{
	VS_Output_Tex outp = (VS_Output_Tex)0;
	//inp.pos = mul(inp.pos, transarr[inp.tti]);
	//inp.nrm = mul(inp.nrm, transarr[inp.tti]);
	outp.pos = mul(inp.pos, viewProj);
	outp.altPos = outp.pos;
	outp.altPos.z = outp.altPos.z * outp.altPos.w * invFarDepth;
	outp.col = inp.col;
	outp.txc = inp.txc;

	return outp;
}

PS_Output PShade_Tex_Alpha(VS_Output_Tex inp)
{
	PS_Output outp = (PS_Output)0;
	/*outp.col = inp.col * tex.Sample(linearSampler, inp.txc);

	clip(outp.col.w - 0.5);

	outp.col = outp.col * colMod;
	float alphaPreserve = outp.col.w;

	outp.col = outp.col;

	outp.col *= alphaPreserve;
	outp.col.w = alphaPreserve;*/
	outp.col.w = 1;
	outp.col.g = 1;

	return outp;
}