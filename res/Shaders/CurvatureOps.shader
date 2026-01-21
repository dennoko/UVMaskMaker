Shader "Hidden/Dennoko/UVTools/CurvatureOps"
{
    Properties
    {
        _MainTex ("Input Texture (World Normal)", 2D) = "grey" {}
        _Strength ("Strength", Float) = 1.0
        _Mode ("Mode (0:Convex, 1:Concave)", Int) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Pass 0: Calculate Curvature from Normal Map & Combine with Vertex Curvature
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Strength;
            int _Mode; // 0=Convex(Ridge/Edge), 1=Concave(Cavity/Valley)

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample center
                float4 dataCenter = tex2D(_MainTex, i.uv);
                float3 nCenter = dataCenter.rgb * 2.0 - 1.0;
                float vCurve = dataCenter.a; // Vertex Curvature (0..1), assuming 0.5 is neutral? Or 0 is flat, 1 is sharp?
                // For vertex curvature approach: 
                // We'll assume vCurve is "Convexity" magnitude (0 to 1). 
                // If we want both convex/concave, we might store signed value packed in 0..1 (0.5=flat).
                // Let's assume input vCurve is strictly "Edge Strength" for now (unsigned).

                // Calculate pixel-based curvature using fwidth or neighbors
                // fwidth gives rate of change.
                float3 ddxN = ddx(nCenter);
                float3 ddyN = ddy(nCenter);
                
                // Magnitude of change
                float delta = length(abs(ddxN) + abs(ddyN)); 
                // This is simple edge detection.
                
                // For more accurate Convex vs Concave, we need neighbor sampling.
                // 3x3 Convolution?
                // Let's stick to simple delta (magnitude of change) for "details" first.
                // Normal map details usually mean high frequency changes.
                
                float pixelCurve = delta * _Strength;

                // Combine
                // Vertex curvature handles large scale hard edges.
                // Pixel curvature handles normal map details.
                float totalCurve = saturate(vCurve + pixelCurve);

                return float4(totalCurve, totalCurve, totalCurve, 1.0);
            }
            ENDCG
        }

        // Pass 1: Dilation (Grow White)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            fixed4 frag (v2f i) : SV_Target
            {
                float c = tex2D(_MainTex, i.uv).r;
                if (c > 0.01) return c; // Already white-ish

                // Check neighbors
                float maxVal = 0;
                for(int y=-1; y<=1; y++)
                {
                    for(int x=-1; x<=1; x++)
                    {
                        float val = tex2D(_MainTex, i.uv + float2(x, y) * _MainTex_TexelSize.xy).r;
                        maxVal = max(maxVal, val);
                    }
                }
                return maxVal;
            }
            ENDCG
        }
    }
}
