// Painted fantasy sky behind the floating board: a dusk gradient with a glowing horizon, drifting cloud banks,
// twinkling stars and rising embers. If a picture is supplied (_UseTex = 1) it is shown instead, softly tinted.
Shader "Strategy/Backdrop"
{
    Properties
    {
        _MainTex ("Art (optional)", 2D) = "black" {}
        _UseTex ("Use art", Float) = 0
        _Top ("Top", Color) = (0.05, 0.06, 0.14, 1)
        _Mid ("Middle", Color) = (0.13, 0.11, 0.25, 1)
        _Glow ("Horizon glow", Color) = (0.55, 0.3, 0.38, 1)
        _Bottom ("Bottom", Color) = (0.04, 0.07, 0.09, 1)
        _Aspect ("Aspect", Float) = 1.78
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Opaque" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _UseTex, _Aspect;
            fixed4 _Top, _Mid, _Glow, _Bottom;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }

            float fbm (float2 p)
            {
                float v = 0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                if (_UseTex > 0.5)
                {
                    fixed4 art = tex2D(_MainTex, uv);
                    return fixed4(art.rgb * 0.8, 1);
                }
                float t = _Time.y;
                float y = uv.y;
                float3 col = y > 0.42 ? lerp(_Mid.rgb, _Top.rgb, smoothstep(0.42, 1.0, y)) : lerp(_Bottom.rgb, _Mid.rgb, smoothstep(0.0, 0.42, y));
                // Warm band of light at the horizon.
                col += _Glow.rgb * exp(-pow((y - 0.4) * 6.0, 2.0)) * 0.55;

                float2 p = float2(uv.x * _Aspect, y);
                // Two cloud layers drifting at different speeds.
                float c1 = fbm(p * float2(2.2, 5.0) + float2(t * 0.012, 0));
                float c2 = fbm(p * float2(3.5, 8.0) + float2(-t * 0.02, 3.7));
                float band = exp(-pow((y - 0.35) * 3.2, 2.0));
                col = lerp(col, _Glow.rgb * 0.7 + _Mid.rgb * 0.5, smoothstep(0.5, 0.85, c1) * band * 0.65);
                col = lerp(col, _Bottom.rgb * 1.6, smoothstep(0.55, 0.9, c2) * (1.0 - y) * 0.5);

                // Stars, twinkling, only in the upper sky.
                float2 cell = floor(p * 60.0);
                float h = hash(cell);
                float2 fpos = frac(p * 60.0) - 0.5;
                float star = step(0.985, h) * smoothstep(0.18, 0.0, length(fpos)) * (0.6 + 0.4 * sin(t * 2.0 + h * 50.0));
                col += star * smoothstep(0.5, 0.9, y) * 0.9;

                // Embers rising through the lower half.
                float2 ep = float2(p.x * 9.0, y * 6.0 - t * 0.15);
                float2 ec = floor(ep);
                float eh = hash(ec + 17.0);
                float2 ef = frac(ep) - float2(0.5 + 0.3 * sin(t + eh * 20.0), 0.5);
                float ember = step(0.93, eh) * smoothstep(0.12, 0.0, length(ef)) * (1.0 - smoothstep(0.2, 0.6, y));
                col += ember * float3(1.0, 0.55, 0.25) * 0.8;

                // Vignette so the board stays the brightest thing on screen.
                float2 d = uv - 0.5;
                col *= 1.0 - dot(d, d) * 0.9;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
