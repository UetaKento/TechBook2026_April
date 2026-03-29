Shader "Kenty/OcclusionToonLit"
{
    Properties
    {
        _MainTex ("メインテクスチャ", 2D) = "white" {}
        _Color ("カラー", Color) = (1, 1, 1, 1)

        [Header(Toon Shading)]
        _ShadeColor ("シェードカラー", Color) = (0.8, 0.8, 1, 1)
        _ShadeToony ("シェード硬さ", Range(0, 1)) = 0.5
        _ShadeShift ("シェード境界シフト", Range(-1, 1)) = 0

        [Header(Rim Light)]
        _RimColor ("リムライトカラー", Color) = (0.5, 0.5, 0.5, 1)
        _RimPower ("リムライト強度", Range(0, 1)) = 0.3
        _RimFresnelPower ("リムライト幅", Range(1, 10)) = 3

        [Header(Depth Occlusion)]
        _EnvironmentDepthBias ("デプスバイアス", Float) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        // Surface Shader 宣言:
        //   surf         … サーフェス関数（テクスチャ・リムライト）
        //   ToonLit      … カスタムライティングモデル（セルシェーディング）
        //   finalcolor   … 最終色にオクルージョンを適用する関数
        //   keepalpha    … Alpha を保持（オクルージョンで透過させるために必須）
        #pragma surface surf ToonLit finalcolor:colorModifier fullforwardshadows keepalpha
        #pragma target 3.5

        // Meta XR SDK のデプスオクルージョン用キーワード
        // EnvironmentDepthManager が HARD_OCCLUSION または SOFT_OCCLUSION を有効化する
        #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

        // Meta XR SDK の BiRP 用オクルージョンインクルード
        // 環境深度テクスチャのサンプリングとオクルージョン計算マクロを提供する
        #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _ShadeColor;
        half _ShadeToony;
        half _ShadeShift;
        fixed4 _RimColor;
        half _RimPower;
        half _RimFresnelPower;
        float _EnvironmentDepthBias;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
            float3 worldPos;
        };

        /// <summary>
        /// カスタムライティングモデル: セルシェーディング（トゥーン調の陰影）
        /// Half-Lambert をベースに、smoothstep で明暗の境界を鋭くすることで
        /// アニメ調の 2 トーンシェーディングを実現する。
        /// </summary>
        half4 LightingToonLit(SurfaceOutput s, half3 lightDir, half atten)
        {
            // Half-Lambert: NdotL を 0〜1 の範囲に変換し、暗部を持ち上げる
            half NdotL = dot(s.Normal, lightDir);
            half halfLambert = NdotL * 0.5 + 0.5;

            // _ShadeShift で明暗境界の位置を調整し、_ShadeToony で遷移の鋭さを制御する
            // _ShadeToony = 1 でパキッとしたセル調、0 で滑らかなグラデーション
            half shadeCenter = 0.5 + _ShadeShift * 0.5;
            half feather = (1.0 - _ShadeToony) * 0.5;
            half shade = saturate((halfLambert - shadeCenter + feather) / max(2.0 * feather, 0.001));

            // ベースカラーとシェードカラーを shade 値で補間する
            half3 litColor = s.Albedo;
            half3 shadeColor = s.Albedo * _ShadeColor.rgb;
            half3 color = lerp(shadeColor, litColor, shade);

            // ライトカラーと減衰を適用する
            color *= _LightColor0.rgb * atten;

            return half4(color, s.Alpha);
        }

        /// <summary>
        /// サーフェス関数: テクスチャサンプリングとリムライトの計算
        /// リムライトはフレネル効果を利用し、輪郭部分を光らせることで
        /// キャラクターのシルエットを際立たせる。
        /// </summary>
        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;

            // リムライト: 視線と法線の内積からフレネル項を計算し、
            // 輪郭に近い部分ほど強く発光させる
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            o.Emission = _RimColor.rgb * pow(rim, _RimFresnelPower) * _RimPower;
        }

        /// <summary>
        /// 最終色修正関数: Meta XR SDK のデプスオクルージョンを適用する
        /// ライティング計算の後に呼ばれ、環境深度と比較して
        /// 現実世界のオブジェクトの背後にあるピクセルを透過させる。
        /// </summary>
        void colorModifier(Input IN, SurfaceOutput o, inout fixed4 color)
        {
            META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY_WORLDPOS(IN.worldPos, color, _EnvironmentDepthBias)
        }
        ENDCG
    }

    FallBack "Diffuse"
}
