// Outline fill pass: extrudes vertices in the view-space XY plane (perpendicular to
// the view direction) using smooth normals baked into TEXCOORD3 (UV4), and draws the
// outline color only where the stencil mask did NOT write (outside the original
// silhouette).
//
// Depth correctness: the extrusion deliberately leaves view-space Z untouched, so the
// projection matrix (which derives clip Z, and clip W in perspective, purely from view
// Z) places the extruded geometry at EXACTLY the same depth as the real surface it
// came from — regardless of the object's distance or which way its normal faces. This
// matters because the screen-space width scales with distance (to stay visually
// constant), so a naive extrusion along the full 3D normal can push a far object's
// fill geometry to an arbitrary, incorrect depth; extruding in view-space XY only
// makes that impossible by construction.
//
// Multi-object correctness: the mask renders at Transparent+100 and the fill at
// Transparent+110, so ALL masks are guaranteed to render before ANY fill — a
// deterministic order that no pipeline sorting can break. Each Outline component
// automatically assigns its object a unique _StencilRef (on internal material
// instances), and the masks use ZTest LEqual so each object only stamps the pixels
// where it is actually visible. A fill therefore skips only its OWN silhouette
// (Comp NotEqual its own ref), draws over other objects' visible bodies when it is
// nearer (depth passes), and is hidden behind them when it is farther (depth fails).
// Per-pixel correct, fully automatic — no sorting layers, no manual priorities.
//
// SubShader 1: URP (HLSL, SRP Batcher compatible).
// SubShader 2: Built-in RP (CG). Unity picks the one matching the active pipeline.
Shader "reromanlee/OutlineFill" {
	Properties {
		// LessEqual (4) by default: outlines are occluded by scene geometry like any
		// normal object. Set to Always (8) for an X-ray outline visible through walls.
		[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
		// Assigned automatically per object by the Outline component; the value on the
		// shared material asset is only a fallback and never needs manual editing.
		[IntRange] _StencilRef("Stencil Reference", Range(0, 255)) = 1
		_OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
		_OutlineWidth("Outline Width", Range(0, 10)) = 2
	}

	// ------------------------------------------------------------------ URP
	SubShader {
		Tags {
			"RenderPipeline" = "UniversalPipeline"
			// One queue step after OutlineMask: all masks render before all fills.
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
			// Same bias as the mask pass: keeps the fill's depth test aligned with the
			// mask's stamp, and stops the band z-fighting geometry it rests against
			// (e.g. an outlined character standing on the ground).
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

				// Extrude ONLY in the view-space XY plane (perpendicular to the view
				// direction) and leave view-space Z exactly as the source surface's.
				// The projection matrix derives clip Z (and, in perspective, clip W)
				// purely from view-space Z, so this guarantees the fill's depth is
				// IDENTICAL to the real surface it came from — no matter the object's
				// distance or which way its normal faces. That's what makes occlusion
				// between outlined objects correct regardless of scale: a far object's
				// fill can never end up depth-testing as if it were nearer than a
				// close object, because its depth never actually changes.
				positionVS.xy += normalVS.xy * scale * _OutlineWidth / 1000.0;

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
			// Depth-precision guard — see the URP pass above.
			Offset -1, -1

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

				// Extrude ONLY in the view-space XY plane, leaving view-space Z exactly
				// as the source surface's — see the URP pass above for why this is what
				// makes cross-object depth ordering correct at any distance/normal angle.
				viewPosition.xy += viewNormal.xy * scale * _OutlineWidth / 1000.0;

				output.position = UnityViewToClipPos(viewPosition);
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
