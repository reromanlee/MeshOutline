// Outline mask pass: writes the object's silhouette into the stencil buffer without
// drawing any color, so the fill pass can render only OUTSIDE the silhouette.
//
// Both SubShaders use explicit (programmable) passes: fixed-function passes are not
// supported by SRPs and don't work with GPU instancing or single-pass instanced VR.
//
// SubShader 1: URP (HLSL, SRP Batcher compatible).
// SubShader 2: Built-in RP (CG). Unity picks the one matching the active pipeline.
Shader "reromanlee/OutlineMask" {
	Properties {
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0
		[IntRange] _StencilRef("Stencil Reference", Range(0, 255)) = 1
	}

	// ------------------------------------------------------------------ URP
	SubShader {
		Tags {
			"RenderPipeline" = "UniversalPipeline"
			"Queue" = "Transparent+100"
			"RenderType" = "Transparent"
		}
		Pass {
			Name "Mask"
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Cull Off
			ZTest [_ZTest]
			ZWrite Off
			ColorMask 0

			Stencil {
				Ref [_StencilRef]
				Pass Replace
			}

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes {
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings {
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			// No material properties are read by the program (_ZTest/_StencilRef are
			// render state only), so an empty UnityPerMaterial keeps SRP Batcher happy.
			CBUFFER_START(UnityPerMaterial)
			CBUFFER_END

			Varyings vert(Attributes input) {
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half4 frag(Varyings input) : SV_Target {
				return 0; // ColorMask 0: nothing is written anyway.
			}
			ENDHLSL
		}
	}

	// ------------------------------------------------------------- Built-in RP
	SubShader {
		Tags {
			"Queue" = "Transparent+100"
			"RenderType" = "Transparent"
			"IgnoreProjector" = "True"
		}
		Pass {
			Name "Mask"
			Cull Off
			ZTest [_ZTest]
			ZWrite Off
			ColorMask 0

			Stencil {
				Ref [_StencilRef]
				Pass Replace
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "UnityCG.cginc"

			struct appdata {
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f {
				float4 position : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata input) {
				v2f output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.position = UnityObjectToClipPos(input.vertex);
				return output;
			}

			fixed4 frag(v2f input) : SV_Target {
				return 0; // ColorMask 0: nothing is written anyway.
			}
			ENDCG
		}
	}
}
