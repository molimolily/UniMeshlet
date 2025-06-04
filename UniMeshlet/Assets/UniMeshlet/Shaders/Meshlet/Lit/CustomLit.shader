Shader "Meshlet/CustomLit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SpecReflect ("Specular Reflectance", Range(0, 1)) = 0.0
        _SpecColor ("Specular Color", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "LightMode"="UniversalForward"
        }
        LOD 100
        CUll Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float3 posWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 vertex : SV_POSITION;
                half3 vertexSH : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            float4x4 _ObjectToWorld;
            float4x4 _WorldToObject;

            float4 _Color;
            float _SpecReflect;
            float4 _SpecColor;
            float _Smoothness;

            float3 CustomTransformObjectToWorldDir(float3 dirOS, bool doNormalize = true)
            {
                #ifndef SHADER_STAGE_RAY_TRACING
                float3 dirWS = mul((float3x3)_ObjectToWorld, dirOS);
                #else
                float3 dirWS = mul((float3x3)_ObjectToWorld, dirOS);
                #endif
                if (doNormalize)
                    return SafeNormalize(dirWS);

                return dirWS;
            }

            float3 CustomTransformObjectToWorldNormal(float3 normalOS, bool doNormalize = true)
            {
            #ifdef UNITY_ASSUME_UNIFORM_SCALING
                return CustomTransformObjectToWorldDir(normalOS, doNormalize);
            #else
                // Normal need to be multiply by inverse transpose
                float3 normalWS = mul(normalOS, (float3x3)_WorldToObject);
                if (doNormalize)
                    return SafeNormalize(normalWS);

                return normalWS;
            #endif
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posWS = mul(_ObjectToWorld, v.vertex).xyz;
                o.vertex = TransformWorldToHClip(o.posWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normalWS = CustomTransformObjectToWorldNormal(v.normal);
                o.vertexSH = SampleSH(o.normalWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                albedo *= _Color;

                float3 normal = normalize(i.normalWS);

                Light mainLight = GetMainLight();

                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.posWS);

                // Calculate lighting
                half3 ambient = i.vertexSH * albedo.rgb;
                half3 diffuse = LightingLambert(mainLight.color, mainLight.direction, normal) * albedo.rgb / PI;
                float smoothness = exp2(10 * _Smoothness + 1);
                half3 specular = LightingSpecular(mainLight.color, mainLight.direction, normal, viewDir, half4(_SpecColor.rgb, 1.0), smoothness) * (smoothness + 8) / (8 * PI);
                half3 mainLightColor = (1.0 - _SpecReflect) * diffuse + _SpecReflect * specular;

                half3 finalColor = ambient + mainLightColor;

                return half4(finalColor, albedo.a);
            }
            ENDHLSL
        }
    }
}
