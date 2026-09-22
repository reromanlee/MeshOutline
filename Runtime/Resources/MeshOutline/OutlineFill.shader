// Outline fill pass: pushes the outline mesh outward along its smoothed normals and draws
// the outline color only where the mask pass did NOT stamp this outline's stencil
// reference, i.e. only outside the object's own silhouette.
//
// Smoothed normals: the outline mesh is a baked copy of the source mesh whose NORMAL
// channel holds position-averaged normals (all normals sharing a position are averaged),
// so hard edges don't tear open when extruded. Keeping them in NORMAL rather than a UV
// channel means skinning and batching transform them like any other normal.
//
// Width: _OutlineWidth is in pixels at 1080p. The offset is derived from the projection
// matrix, so the outline covers the same share of the screen at any resolution, field of
// view or orthographic size.
//
// Depth: only view-space X/Y are offset; view-space Z (and therefore clip Z and, in
// perspective, clip W) stays exactly that of the source surface. The fill depth-tests
// where the real surface is, so occlusion between outlined objects stays correct at any
// distance and any normal direction.
//
// Ordering: masks render at Transparent+100 and fills at Transparent+110, so every mask is
// stamped before any fill. Each ObjectOutline has its own _StencilRef: a fill skips only its
// own silhouette, draws over other objects where it is nearer, and hides behind them where
// it is farther.
//
// SubShader 1: URP (SRP Batcher compatible). SubShader 2: Built-in RP.
Shader "Hidden/MeshOutline/Fill" {
	Properties {
		// Driven by ObjectOutline.Occlusion: LessEqual (4) = Normal, Always (8) = X-Ray.
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
		// Assigned per outline by ObjectOutline.
		[IntRange] _StencilRef("Stencil Reference", Range(0, 255)) = 1
		[HDR] _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
		_OutlineWidth("Outline Width (px at 1080p)", Float) = 4
	}

	// ------------------------------------------------------------------ URP
	SubShader {
		Tags {
			"RenderPipeline" = "UniversalPipeline"
			"Queue" = "Transparent+110"
			"RenderType" = "Transparent"
			"IgnoreProjector" = "True"
		}
		Pass {
			Name "Fill"
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Cull Off
			ZTest [_ZTest]
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			ColorMask RGB
			// Same bias as the mask pass: keeps the fill's depth test aligned with the mask's
			// stamp and stops the band z-fighting geometry it rests against.
			Offset -1, -1

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
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings {
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			CBUFFER_START(UnityPerMaterial)
				half4 _OutlineColor;
				float _OutlineWidth;
			CBUFFER_END

			Varyings vert(Attributes input) {
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
				float3 normalVS = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS)));

				// A 1080p-high frame spans 2 / P[1][1] view units per unit of depth in
				// perspective (P[1][1] = cot(fov / 2)), and 2 * orthoSize in orthographic
				// (P[1][1] = 1 / orthoSize, no depth factor). abs(): flipped render targets
				// negate P[1][1].
				float viewUnitsPerPixel = 2.0 / (1080.0 * abs(UNITY_MATRIX_P._m11));
				float depth = unity_OrthoParams.w > 0.5 ? 1.0 : -positionVS.z;
				positionVS.xy += normalVS.xy * (_OutlineWidth * viewUnitsPerPixel * depth);

				output.positionCS = TransformWViewToHClip(positionVS);
				return output;
			}

			half4 frag(Varyings input) : SV_Target {
				return _OutlineColor;
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
		}
		Pass {
			Name "Fill"
			Cull Off
			ZTest [_ZTest]
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			ColorMask RGB
			Offset -1, -1

			Stencil {
				Ref [_StencilRef]
				Comp NotEqual
			}

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "UnityCG.cginc"

			struct appdata {
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f {
				float4 position : SV_POSITION;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			// float4 rather than fixed4 so HDR colors aren't clamped.
			float4 _OutlineColor;
			float _OutlineWidth;

			v2f vert(appdata input) {
				v2f output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 positionVS = UnityObjectToViewPos(input.vertex.xyz);
				float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, input.normal));

				// Pixels at 1080p to view units: see the URP pass above.
				float viewUnitsPerPixel = 2.0 / (1080.0 * abs(UNITY_MATRIX_P._m11));
				float depth = unity_OrthoParams.w > 0.5 ? 1.0 : -positionVS.z;
				positionVS.xy += normalVS.xy * (_OutlineWidth * viewUnitsPerPixel * depth);

				output.position = UnityViewToClipPos(positionVS);
				return output;
			}

			float4 frag(v2f input) : SV_Target {
				return _OutlineColor;
			}
			ENDCG
		}
	}
}
