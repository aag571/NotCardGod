Shader "NotCardGod/UI/EnhancementGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.35
        _GlowRadius ("Glow Radius", Range(0.5, 4)) = 1.5
        _PulseSpeed ("Pulse Speed", Range(0, 8)) = 2.4
        _ScanStrength ("Scan Strength", Range(0, 3)) = 0.8
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

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
            Name "EnhancementGlow"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _MainTex_TexelSize;
            float4 _ClipRect;
            float _GlowStrength;
            float _GlowRadius;
            float _PulseSpeed;
            float _ScanStrength;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy * _GlowRadius;
                fixed4 mainSample = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;

                fixed4 glow = 0;
                glow += tex2D(_MainTex, input.texcoord + float2(texel.x, 0));
                glow += tex2D(_MainTex, input.texcoord - float2(texel.x, 0));
                glow += tex2D(_MainTex, input.texcoord + float2(0, texel.y));
                glow += tex2D(_MainTex, input.texcoord - float2(0, texel.y));
                glow += tex2D(_MainTex, input.texcoord + texel);
                glow += tex2D(_MainTex, input.texcoord - texel);
                glow += tex2D(_MainTex, input.texcoord + float2(texel.x, -texel.y));
                glow += tex2D(_MainTex, input.texcoord + float2(-texel.x, texel.y));
                glow *= 0.125;

                float pulse = 0.72 + 0.28 * sin(_Time.y * _PulseSpeed);
                float scanPosition = frac(_Time.y * 0.28);
                float scan = 1.0 - smoothstep(0.0, 0.16, abs(input.texcoord.y - scanPosition));
                float imageMask = saturate(mainSample.a + glow.a);

                fixed4 color;
                color.rgb = (mainSample.rgb +
                    glow.rgb * (_GlowStrength * pulse) +
                    mainSample.rgb * (scan * _ScanStrength * imageMask)) * input.color.rgb;
                color.a = saturate(mainSample.a + glow.a * 0.35 * pulse) * input.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
