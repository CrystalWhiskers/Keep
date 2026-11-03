// Unlit colour with a fixed fake light so boxes read as three tones, like the flat vector mockup.
Shader "Strategy/Flat"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Ambient ("Ambient", Range(0,1)) = 0.58
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Ambient;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 col : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = UnityObjectToWorldNormal(v.normal);
                float3 l = normalize(float3(0.3, 1.0, -0.5));
                float shade = _Ambient + (1.0 - _Ambient) * saturate(dot(n, l)) / 0.8642;
                o.col = v.color * _Color;
                o.col.rgb *= min(shade, 1.0);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.col; }
            ENDCG
        }
    }
}
