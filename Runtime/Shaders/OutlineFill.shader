// Outline fill pass: extrudes vertices along smooth normals baked into TEXCOORD3 (UV4)
// and draws the outline color only where the stencil mask did NOT write (outside the
// original silhouette).
//
// SubShader 1: URP (HLSL, SRP Batcher compatible).
// SubShader 2: Built-in RP (CG). Unity picks the one matching the active pipeline.
Shader "reromanlee/OutlineFill" {
	Properties {
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0
		[IntRange] _StencilRef("Stencil Reference", Range(0, 255)) = 1
		_OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
		_OutlineWidth("Outline Width", Range(0, 10)) = 2
	}

	// ------------------------------------------------------------------ URP
	SubShader {
		Tags {
			"RenderPipeline" = "UniversalPipeline"
			"Queue" = "Transparent+110"
			"RenderType" = "Transparent"
			// Extrusion math relies on per-object transforms; dynamic/static batching
			// pre-transforms vertices and would break it. SRP Batcher and GPU
			// instancing are unaffected by this tag and still work.
			"DisableBatching" = "True"
		}
		Pass {
			Name "Fill"
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Cull Off
			ZTest [_ZTest]
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			ColorMask RGB

			Stencil {
				Ref [_StencilRef]
				Comp NotEqual
			}

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes {
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float3 smoothNormalOS : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings {
				float4 positionCS : SV_POSITION;
				half4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			// All material properties used by the program live in UnityPerMaterial,
			// which is what makes this SubShader SRP Batcher compatible.
			CBUFFER_START(UnityPerMaterial)
				half4 _OutlineColor;
				float _OutlineWidth;
			CBUFFER_END

			Varyings vert(Attributes input) {
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				// Fall back to the regular normal if no smooth normal was baked.
				float3 normalOS = any(input.smoothNormalOS) ? input.smoothNormalOS : input.normalOS;

				float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
				float3 normalVS = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(normalOS)));

				// Perspective: scale by view depth for constant screen-space width.
				// Orthographic (unity_OrthoParams.w == 1): scale by the camera's ortho
				// size instead, so the width stays constant regardless of distance.
				float scale = lerp(-positionVS.z, unity_OrthoParams.y, unity_OrthoParams.w);
				positionVS += normalVS * scale * _OutlineWidth / 1000.0;

				output.positionCS = TransformWViewToHClip(positionVS);
				output.color = _OutlineColor;
				return output;
			}

			half4 frag(Varyings input) : SV_Target {
				return input.color;
			}
			ENDHLSL
		}
	}

	// ------------------------------------------------------------- Built-in RP
	SubShader {
		Tags {
			"Queue" = "Transparent+110"
			"RenderType" = "Transparent"
			"IgnoreProjector" = "True"
			"DisableBatching" = "True"
		}
		Pass {
			Name "Fill"
			Cull Off
			ZTest [_ZTest]
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			ColorMask RGB

			Stencil {
				Ref [_StencilRef]
				Comp NotEqual
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			// Required for the instancing/stereo macros below to actually do anything
			// (enables GPU instancing and single-pass instanced VR).
			#pragma multi_compile_instancing

			#include "UnityCG.cginc"

			struct appdata {
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float3 smoothNormal : TEXCOORD3;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f {
				float4 position : SV_POSITION;
				fixed4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			fixed4 _OutlineColor;
			float _OutlineWidth;

			v2f vert(appdata input) {
				v2f output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 normal = any(input.smoothNormal) ? input.smoothNormal : input.normal;
				float3 viewPosition = UnityObjectToViewPos(input.vertex);
				float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, normal));

				// Perspective: constant screen-space width. Orthographic: constant
				// width via the camera's ortho size (unity_OrthoParams.w is 1 if ortho).
				float scale = lerp(-viewPosition.z, unity_OrthoParams.y, unity_OrthoParams.w);
				output.position = UnityViewToClipPos(viewPosition + viewNormal * scale * _OutlineWidth / 1000.0);
				output.color = _OutlineColor;
				return output;
			}

			fixed4 frag(v2f input) : SV_Target {
				return input.color;
			}
			ENDCG
		}
	}
}
