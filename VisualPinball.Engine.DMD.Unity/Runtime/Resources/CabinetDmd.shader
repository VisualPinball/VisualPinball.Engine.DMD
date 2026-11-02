// Visual Pinball Engine
// Copyright (C) 2026 freezy and VPE Team
//
// SPDX-License-Identifier: MIT

Shader "VisualPinball/DMD/Cabinet"
{
	Properties { _MainTex("Processed DMD", 2D) = "black" {} }
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		CGINCLUDE
		#include "UnityCG.cginc"
		sampler2D _MainTex, _DotGlow, _Back;
		float4 _FrameSize, _Direction, _Unlit;
		float _DotSize, _Rounding, _Edge, _Brightness, _Gamma, _Glow, _BackGlow;
		float3 Cell(float2 uv, float2 neighbor)
		{
			float2 center = floor(uv * _FrameSize.xy) + neighbor;
			float2 distanceFromCenter = 2 * (frac(uv * _FrameSize.xy) - neighbor);
			float radius = _Rounding * _DotSize;
			float2 corner = abs(distanceFromCenter) - _DotSize + radius;
			float distance = length(max(corner, 0)) + min(max(corner.x, corner.y), 0) - radius;
			float coverage = 1 - smoothstep(-_Edge, _Edge, distance);
			return (tex2D(_MainTex, center * _FrameSize.zw).rgb + _Unlit.rgb) * coverage;
		}
		float4 Dots(v2f_img input) : SV_Target
		{
			// Processed RGB buffers are top-down; Unity uploads their first row at UV y=0.
			float2 uv = float2(input.uv.x, 1 - input.uv.y);
			float3 color = 0;
			if (_DotSize > .5) {
				[unroll] for (int x = -1; x <= 1; x++) [unroll] for (int y = -1; y <= 1; y++) color = max(color, Cell(uv, float2(x + .5, y + .5)));
			} else color = Cell(uv, float2(.5, .5));
			color += tex2D(_DotGlow, uv).rgb * _Glow + tex2D(_Back, uv).rgb * _BackGlow;
			return float4(pow(max(0, color * _Brightness), 1 / max(.1, _Gamma)), 1);
		}
		float4 SmallBlur(v2f_img input) : SV_Target
		{
			return float4(tex2D(_MainTex, input.uv).rgb * .5 + (tex2D(_MainTex, input.uv + _Direction.xy).rgb + tex2D(_MainTex, input.uv - _Direction.xy).rgb) * .25, 1);
		}
		float4 LargeBlur(v2f_img input) : SV_Target
		{
			float2 nearOffset = _Direction.xy * 1.3846153846, farOffset = _Direction.xy * 3.2307692308;
			return float4(tex2D(_MainTex, input.uv).rgb * .2270270270 + (tex2D(_MainTex, input.uv + nearOffset).rgb + tex2D(_MainTex, input.uv - nearOffset).rgb) * .3162162162 + (tex2D(_MainTex, input.uv + farOffset).rgb + tex2D(_MainTex, input.uv - farOffset).rgb) * .0702702703, 1);
		}
		ENDCG
		Pass { CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_img
			#pragma fragment Dots
		ENDCG }
		Pass { CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment SmallBlur
		ENDCG }
		Pass { CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment LargeBlur
		ENDCG }
	}
}
