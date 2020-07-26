/* NV12-to-RGBA Effect
 * Written by Ethan "flibitijibibo" Lee
 * http://www.flibitijibibo.com/
 *
 * This effect is based on the YUV-to-RGBA GLSL shader found in SDL.
 * Thus, it also released under the zlib license:
 * http://libsdl.org/license.php
 */

sampler samp0 : register(s0);
sampler samp1 : register(s1);

void VS(inout float2 tex : TEXCOORD0,
	inout float4 pos : SV_Position)
{
	pos.w = 1.0;
}

float4 PS(float2 tex : TEXCOORD0) : SV_Target0
{
	const float3 offset = float3(-0.0625, -0.5, -0.5);

	/* More info about colorspace conversion:
	 * http://www.equasys.de/colorconversion.html
	 * http://www.equasys.de/colorformat.html
	 */
	const float3 Rcoeff = float3(1.164,  0.000,  1.793);
	const float3 Gcoeff = float3(1.164, -0.213, -0.533);
	const float3 Bcoeff = float3(1.164,  2.112,  0.000);

	float3 yuv;
	yuv.x = tex2D(samp0, tex).w;
	yuv.yz = tex2D(samp1, tex).rg;
	yuv += offset;

	float4 rgba;
	rgba.x = dot(yuv, Rcoeff);
	rgba.y = dot(yuv, Gcoeff);
	rgba.z = dot(yuv, Bcoeff);
	rgba.w = 1.0;
	return rgba;
}

Technique T
{
	Pass P
	{
		VertexShader = compile vs_3_0 VS();
		PixelShader = compile ps_3_0 PS();
	}
}