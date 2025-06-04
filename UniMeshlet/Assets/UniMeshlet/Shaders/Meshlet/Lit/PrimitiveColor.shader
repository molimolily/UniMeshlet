Shader "Meshlet/PrimitiveColor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
            #pragma  target 4.5
            
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

            uint _TableStride;
            ByteAddressBuffer _MeshletIndexTable;
            
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

            inline  float3 HSVtoRGB(float h, float s, float v)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(float3(h, h, h) + K.xyz) * 6.0 - K.www);
                return v * lerp(K.xxx, saturate(p - K.xxx), s);
            }
            
            inline  float3 IDToHSVColor(uint id, float saturation = 1.0, float value = 1.0)
            {
                float hue = frac(id * 0.61803398875);
                return HSVtoRGB(hue, saturation, value);
            }

            inline  half3 PrimitiveIDToColor(uint id)
            {
                return IDToHSVColor(id);
            }

            inline uint ReadMeshletID(uint primitiveID)
            {
                uint byteOffset = primitiveID * _TableStride;   // 三角形 n の先頭バイト
                uint aligned    = byteOffset & ~3u;             // 4 B アライン境界
                uint pack       = _MeshletIndexTable.Load(aligned);

                uint shift = (byteOffset & 3u) * 8u;            // 0 / 8 / 16 / 24

                if (_TableStride == 1)   return (pack >> shift) & 0xFFu;      // 8 bit
                if (_TableStride == 2)   return (pack >> shift) & 0xFFFFu;    // 16 bit
                return pack;                                                  // 32 bit
                
            }

            half4 frag(Varyings i, uint primitiveID : SV_PrimitiveID) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);

                uint meshletID = ReadMeshletID(primitiveID);
                albedo.rgb *= PrimitiveIDToColor(meshletID);

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