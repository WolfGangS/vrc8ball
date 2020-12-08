Shader "harry_t/balls"
{
	Properties
	{
		_Color("Main Color", Color) = (1,1,1,1)
	}

	SubShader
	{
		Tags { "Queue" = "Transparent" }
		
		ZWrite Off

		Pass
		{
			Blend One One

			CGPROGRAM

			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float4 normal : NORMAL;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float4 opos : TEXCOORD0;
				float4 normal : NORMAL;
			};


			float4 _Color;
			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.opos = v.vertex;
				o.normal = v.normal;
				return o;
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				float3 viewDir = ObjSpaceViewDir ( i.opos );
				float fresnelValue = 1 - saturate ( dot ( i.normal, viewDir ) * 0.1 );

				return fixed4( _Color * fresnelValue );
			}
			ENDCG
		}
	}
}
