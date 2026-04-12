// MaskOverlay.shader
// Transparent UV-sampled overlay shader used to project the mask preview texture
// onto the 3D mesh surface in the scene view.
//
// The overlay texture encodes selection state and hand-painted areas:
//   - Selected / painted pixels  : mask color with configurable alpha
//   - Unselected pixels          : fully transparent (alpha = 0)
//
// The ZTest property is set at runtime via Material.SetInt("_ZTest", ...)
// to match the "Overlay On Top" setting in MaskSettings.
Shader "Hidden/UVMaskMaker/MaskOverlay"
{
    Properties
    {
        _MainTex ("Overlay Texture", 2D) = "black" {}
        // ZTest is driven at runtime; default 4 = LessEqual
        [HideInInspector] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent+10"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "MASK_OVERLAY"

            // Screen-space compositing: standard alpha blend
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Lighting Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   2.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // Vertices are supplied in world space via GL.Vertex() with
                // GL.MultMatrix(Matrix4x4.identity).  UnityObjectToClipPos uses
                // UNITY_MATRIX_MVP which equals P*V*M; because GL sets M=identity
                // this correctly transforms world-space → clip-space.
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample the overlay texture; fully-transparent pixels are discarded
                // so unselected mesh areas remain unchanged in the scene view.
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(col.a - 0.004);   // discard nearly-transparent pixels
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
