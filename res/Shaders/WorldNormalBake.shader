Shader "Hidden/Dennoko/UVTools/WorldNormalBake"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ _NORMALMAP

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float4 color : COLOR; // Pre-calculated curvature in R
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD3;
                float3 worldTangent : TEXCOORD4;
                float3 worldBinormal : TEXCOORD5;
                float curvature : TEXCOORD6;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _BumpMap;
            float _Cutoff;

            v2f vert (appdata v)
            {
                v2f o;
                // Map UV to Clip Space [-1, 1]
                // UV (0,0) -> (-1, -1), UV (1,1) -> (1, 1)
                // Need to handle platform specific Y flip? Generally Unity handles it for RT?
                // For manual projection:
                float2 uvClip = v.uv * 2.0 - 1.0;
                
                // Unity V starts at bottom, but sometimes RenderTextures are flipped.
                // We assume standard UV layout.
                #if UNITY_UV_STARTS_AT_TOP
                // uvClip.y = -uvClip.y; // If rendering to texture, this might differ
                #endif

                o.pos = float4(uvClip.x, uvClip.y, 0, 1);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                // World Basis construction
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                float3 worldBinormal = cross(worldNormal, worldTangent) * v.tangent.w;

                o.worldNormal = worldNormal;
                o.worldTangent = worldTangent;
                o.worldBinormal = worldBinormal;
                o.curvature = v.color.r;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Simple alpha test if needed (usually MainTex alpha)
                /*
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - _Cutoff);
                */

                float3 worldNormal = normalize(i.worldNormal);

                // Apply Normal Map
                // Note: We need check if normal map is assigned.
                // Since we can't easily detect "no texture" in shader without variants or property check,
                // we assume _BumpMap is valid or "bump" (neutral) if not set.
                float3 tangentNormal = UnpackNormal(tex2D(_BumpMap, i.uv));
                
                // Transform tangent normal to world
                float3 n = normalize(
                    tangentNormal.x * i.worldTangent +
                    tangentNormal.y * i.worldBinormal +
                    tangentNormal.z * worldNormal
                );

                // Encode normal [-1, 1] -> [0, 1]
                float3 packedNormal = n * 0.5 + 0.5;

                // Output: RGB = Normal, A = Vertex Curvature
                return float4(packedNormal, i.curvature);
            }
            ENDCG
        }
    }
}
