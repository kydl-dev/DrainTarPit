// DrainTarPit pool water (T-022, user 2026-10-20: own shader, no game or Asset Store textures).
// Built-in render pipeline (Valheim), forward. Everything is procedural: waves, caustics and foam are computed here, there are no textures.
//  - the scene behind the water comes from a shared GrabPass ("_DTPWaterGrab"), bent by the waves (refraction)
//  - water thickness from the camera depth texture: clear and light in the shallows, coloured and opaque in the deep
//  - light patterns (caustics) on the pool floor, strongest in shallow water
//  - sky / reflection probe reflection with Fresnel, sharp sun glints on the ripples, foam where the water is very thin
//  - optional glow (_Emission) for the Ashlands-style red water
// The mesh alpha (vertex colour a, set by the game / PoolMesh) fades the water out at its edge.
Shader "DTP/PoolWater"
{
    Properties
    {
        _ShallowColor ("Shallow colour", Color) = (0.55, 0.95, 0.88, 1)
        _DeepColor ("Deep colour", Color) = (0.03, 0.42, 0.52, 1)
        _DepthRange ("Depth of the deep colour (m)", Float) = 3
        _Clarity ("See-through depth (m)", Float) = 4
        _Refraction ("Refraction", Range(0, 0.2)) = 0.035
        _WaveScale ("Wave size (m)", Float) = 1.4
        _WaveSpeed ("Wave speed", Float) = 0.35
        _WaveStrength ("Wave strength", Range(0, 2)) = 0.55
        _ReflectionStrength ("Reflection", Range(0, 1)) = 0.55
        _GlintPower ("Sun glint sharpness", Range(16, 4096)) = 900
        _GlintStrength ("Sun glint strength", Range(0, 30)) = 8
        _CausticScale ("Caustic size (m)", Float) = 1.1
        _CausticStrength ("Caustic strength", Range(0, 3)) = 0.9
        _CausticDepth ("Caustic fade depth (m)", Float) = 3
        _FoamColor ("Edge foam colour", Color) = (1, 1, 1, 1)
        _FoamWidth ("Edge foam width (m)", Float) = 0.12
        _Emission ("Glow colour", Color) = (0, 0, 0, 1)
        _EmissionPulse ("Glow pulse", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }

        GrabPass { "_DTPWaterGrab" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _DTPWaterGrab;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

            float4 _ShallowColor, _DeepColor, _FoamColor, _Emission;
            float _DepthRange, _Clarity, _Refraction, _WaveScale, _WaveSpeed, _WaveStrength, _ReflectionStrength;
            float _GlintPower, _GlintStrength, _CausticScale, _CausticStrength, _CausticDepth, _FoamWidth, _EmissionPulse;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float4 color : COLOR;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.screenPos = ComputeScreenPos(o.pos);
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // a few directional waves: their slope (analytic), cheap and smooth
            float2 WaveSlope(float2 p, float t)
            {
                float2 slope = 0;
                const float2 dirs[8] = { float2(1, 0.2), float2(-0.4, 1), float2(0.7, -0.7), float2(-1, -0.3), float2(0.25, 0.95), float2(0.9, 0.6), float2(-0.6, -0.8), float2(0.1, -1) };
                const float freqs[8] = { 1.0, 1.6, 2.5, 3.7, 5.1, 7.3, 9.8, 13.1 };
                const float amps[8]  = { 0.22, 0.2, 0.17, 0.14, 0.11, 0.08, 0.06, 0.04 };
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float2 d = normalize(dirs[k]);
                    float f = freqs[k] / _WaveScale;
                    float ph = dot(d, p) * f + t * (1.0 + k * 0.37) + sin(dot(p, d.yx) * f * 0.5 + k) * 0.6;
                    slope += d * (cos(ph) * amps[k] * f);
                }
                return slope;
            }

            // caustic light pattern on the floor (iterated sine warp, procedural)
            float Caustic(float2 p, float t)
            {
                float2 q = p / _CausticScale;
                float2 w = q;
                float c = 1.0;
                const float inten = 0.005;
                [unroll] for (int n = 0; n < 4; n++)
                {
                    float tt = t * (1.0 - (3.5 / (n + 1)));
                    w = q + float2(cos(tt - w.x) + sin(tt + w.y), sin(tt - w.y) + cos(tt + w.x));
                    c += 1.0 / length(float2(q.x / (sin(w.x + tt) / inten), q.y / (cos(w.y + tt) / inten)));
                }
                c /= 4.0;
                c = 1.17 - pow(c, 1.4);
                return pow(abs(c), 8.0);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                float2 slope = WaveSlope(i.worldPos.xz, t) * _WaveStrength;
                float3 N = normalize(float3(-slope.x, 1, -slope.y));

                // thickness of the water along the view ray (camera depth texture)
                float surfDepth = i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                float thick = max(0, sceneDepth - surfDepth);

                // refraction: bend the grabbed scene, but never pull in things that are in front of the water
                float2 offs = N.xz * _Refraction * saturate(thick);
                float4 gp = i.grabPos; gp.xy += offs * gp.w;
                float4 sp = i.screenPos; sp.xy += offs * sp.w;
                float sceneDepth2 = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(sp)));
                if (sceneDepth2 < surfDepth) { gp = i.grabPos; sceneDepth2 = sceneDepth; }
                float thick2 = max(0, sceneDepth2 - surfDepth);
                float3 behind = tex2Dproj(_DTPWaterGrab, UNITY_PROJ_COORD(gp)).rgb;

                // floor point under the water (for caustics and the vertical depth)
                float3 floorPos = _WorldSpaceCameraPos + (i.worldPos - _WorldSpaceCameraPos) * (sceneDepth2 / max(surfDepth, 1e-4));
                float depth = max(0, i.worldPos.y - floorPos.y);

                float caus = Caustic(floorPos.xz, _Time.y * 0.6) * _CausticStrength * exp(-depth / max(_CausticDepth, 0.01));
                behind *= 1.0 + caus * _LightColor0.rgb;

                // absorption: shallow = clear, deep = coloured
                float trans = exp(-thick2 / max(_Clarity, 0.01));
                float3 waterCol = lerp(_DeepColor.rgb, _ShallowColor.rgb, exp(-depth / max(_DepthRange, 0.01)));
                float3 col = behind * lerp(waterCol, 1, 0.35) * trans + waterCol * (1 - trans) * (0.35 + 0.65 * _LightColor0.rgb);

                // reflection of the sky / reflection probe, stronger at grazing angles
                float3 R = reflect(-V, N);
                R.y = abs(R.y);                                  // a ripple facing away never reflects the ground under the horizon (dark blotches)
                float3 refl = DecodeHDR(UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, R), unity_SpecCube0_HDR);
                float fres = 0.02 + 0.98 * pow(1 - saturate(dot(N, V)), 5);
                col = lerp(col, refl, saturate(fres * _ReflectionStrength * 1.6));

                // sun glints
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 H = normalize(L + V);
                col += pow(saturate(dot(N, H)), _GlintPower) * _GlintStrength * _LightColor0.rgb;

                // foam where the water is very thin
                float foam = saturate(1 - thick / max(_FoamWidth, 0.001));
                foam *= 0.6 + 0.4 * sin(dot(i.worldPos.xz, float2(7.1, 5.3)) + _Time.y * 2.0);
                col = lerp(col, _FoamColor.rgb * (0.5 + 0.5 * _LightColor0.rgb), saturate(foam) * _FoamColor.a);

                // glow (red Ashlands-style water)
                col += _Emission.rgb * (1 + _EmissionPulse * sin(_Time.y * 1.3 + dot(i.worldPos.xz, float2(0.3, 0.2))));

                UNITY_APPLY_FOG(i.fogCoord, col);

                // fade out at the edge of the water mesh (vertex alpha), towards the unbent scene
                float3 plain = tex2Dproj(_DTPWaterGrab, UNITY_PROJ_COORD(i.grabPos)).rgb;
                return fixed4(lerp(plain, col, saturate(i.color.a)), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
