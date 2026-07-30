Shader "NotCardGod/UI/ShieldCapsule"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0.25, 0.85, 1.0, 1.0)
        _Break ("Break", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float _Break;
            float4 _ClipRect;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.texcoord;
                output.color = input.color * _Color;
                output.worldPosition = input.vertex;
                return output;
            }

            // Signed distance to the rounded capsule that clips the hexagon field.
            float CapsuleDistance(float2 p)
            {
                float2 q = float2(abs(p.x) - 0.62, p.y);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.48;
            }

            // Returns the distance from the current point to the nearest edge of a
            // regular hexagon in a staggered, pointy-top hexagonal tiling.
            float HexEdge(float2 p)
            {
                const float2 step = float2(1.0, 1.7320508);
                float2 a = p - step * floor(p / step) - float2(0.5, 0.8660254);
                float2 b = p - step * floor((p - float2(0.5, 0.8660254)) / step)
                    - float2(0.0, 0.8660254);
                float2 cell = dot(a, a) < dot(b, b) ? a : b;
                float2 ac = abs(cell);
                return max(ac.x * 0.8660254 + ac.y * 0.5, ac.y) - 0.43;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float capsuleDistance = CapsuleDistance(p);
                float capsule = 1.0 - smoothstep(-0.012, 0.012, capsuleDistance);
                float outline = 1.0 - smoothstep(-0.018, 0.012, abs(capsuleDistance));

                // Pointy-top hexagons with a visible gap between every cell.
                float2 honeyUv = p * float2(10.0, 8.0);
                float hexDistance = HexEdge(honeyUv);
                float hexLine = 1.0 - smoothstep(-0.035, 0.018, abs(hexDistance));
                float hexFill = 1.0 - smoothstep(0.0, 0.035, hexDistance);

                // A narrow central fracture opens outward during the break animation.
                float breakWidth = lerp(0.0, 0.72, _Break);
                float centerCrack = smoothstep(breakWidth + 0.035, breakWidth, abs(p.x));
                float crackJitter = 0.5 + 0.5 * sin(p.y * 43.0 + p.x * 17.0);
                float crack = centerCrack * lerp(0.55, 1.0, crackJitter);
                float alpha = capsule * (0.16 * hexFill + 0.92 * hexLine + 0.9 * outline);
                alpha *= 1.0 - crack;

                fixed4 mainSample = tex2D(_MainTex, input.uv) + _TextureSampleAdd;
                fixed4 color = input.color;
                color.rgb *= lerp(0.7, 1.0, hexLine);
                color.a *= mainSample.a * saturate(alpha);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
