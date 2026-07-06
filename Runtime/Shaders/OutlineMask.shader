// Outline mask pass: writes the object's silhouette into the stencil buffer without
// drawing any color, so the fill pass can render only OUTSIDE the silhouette.
//
// Multi-object correctness: this shader shares its render queue (Transparent+100) with
// OutlineFill. Both materials sit on the same renderer as [mask, fill], so Unity draws
// each object's mask immediately followed by its own fill (objects sorted back-to-front),
// and the fill's "Fail Zero" op erases the silhouette from the stencil afterwards.
// Result: outlines never clip each other and occlusion is decided purely by depth —
// fully automatic, no sorting layers or per-object stencil references needed.
//
// Both SubShaders use explicit (programmable) passes: fixed-function passes are not
// supported by SRPs and don't work with GPU instancing or single-pass instanced VR.
//
// SubShader 1: URP (HLSL, SRP Batcher compatible).
// SubShader 2: Built-in RP (CG). Unity picks the one matching the active pipeline.
Shader "reromanlee/OutlineMask" {
	Properties {
		// LessEqual (4) by default: only the visible part of the silhouette is masked,
		// matching the fill's depth-correct behavior. Set to Always (8) for X-ray mode.
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
		[IntRange] _StencilRef("Stencil Reference", Range(0, 255)) = 1
	}

	// ------------------------------------------------------------------ URP
	SubShader {
		Tags {
			"RenderPipeline" = "UniversalPipeline"
			// MUST match OutlineFill's queue so mask+fill interleave per object.
			"Queue" = "Transparent+100"
			"RenderType" = "Transparent"
			// Keep every mask an individual draw so the per-object mask->fill order is
			// never disturbed by dynamic batching. SRP Batcher/instancing still work.
			"DisableBatching" = "True"
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
			"DisableBatching" = "True"
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
