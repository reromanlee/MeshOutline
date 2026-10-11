// A minimal lit shader with a URP and a Built-in SubShader, so the sample looks the same in
// either pipeline (the default materials would render pink in one of them).
Shader "MeshOutline Samples/Simple Lit" {
	Properties {
		_Color("Color", Color) = (0.8, 0.8, 0.8, 1)
	}

	// ------------------------------------------------------------------ URP
	SubShader {
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		CBUFFER_START(UnityPerMaterial)
			half4 _Color;
		CBUFFER_END
		ENDHLSL

		Pass {
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes {
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings {
				float4 positionCS : SV_POSITION;
				float3 normalWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input) {
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);
				return output;
			}

			half4 frag(Varyings input) : SV_Target {
				float3 normal = normalize(input.normalWS);
				Light light = GetMainLight();
				half3 lighting = light.color * saturate(dot(normal, light.direction)) + SampleSH(normal);
				return half4(_Color.rgb * lighting, 1);
			}
			ENDHLSL
		}

		// Depth for the depth prepass (depth priming, SSAO, Forward+).
		Pass {
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }
			ColorMask 0

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			struct Attributes {
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings {
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input) {
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half4 frag(Varyings input) : SV_Target {
				return 0;
			}
			ENDHLSL
		}
	}

	// ------------------------------------------------------------- Built-in RP
	SubShader {
		Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
		Pass {
			Tags { "LightMode" = "ForwardBase" }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "UnityCG.cginc"
			#include "UnityLightingCommon.cginc"

			fixed4 _Color;

			struct appdata {
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f {
				float4 position : SV_POSITION;
				float3 normalWS : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata input) {
				v2f output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.position = UnityObjectToClipPos(input.vertex);
				output.normalWS = UnityObjectToWorldNormal(input.normal);
				return output;
			}

			fixed4 frag(v2f input) : SV_Target {
				float3 normal = normalize(input.normalWS);
				float3 lighting = _LightColor0.rgb * saturate(dot(normal, _WorldSpaceLightPos0.xyz)) + ShadeSH9(float4(normal, 1));
				return fixed4(_Color.rgb * lighting, 1);
			}
			ENDCG
		}
	}
}
