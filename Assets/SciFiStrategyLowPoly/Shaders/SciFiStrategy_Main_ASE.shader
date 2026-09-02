// Made with Amplify Shader Editor v1.9.2.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "SciFiStrategy/Main_ASE"
{
	Properties
	{
		_Palette("Palette", 2D) = "white" {}
		[HideInInspector]_ColorMask("ColorMask", 2D) = "white" {}
		_Emmision_Map("Emmision_Map", 2D) = "white" {}
		_Color("Color", Color) = (0,0,0,0)
		[HDR]_EmmisionColor("EmmisionColor", Color) = (0,0,0,0)
		_Metallic("Metallic", Range( 0 , 1)) = 0
		_Smoothness("Smoothness", Range( 0 , 1)) = 0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" "IsEmissive" = "true"  }
		Cull Back
		CGPROGRAM
		#pragma target 3.0
		#pragma surface surf Standard keepalpha addshadow fullforwardshadows 
		struct Input
		{
			float2 uv_texcoord;
		};

		uniform sampler2D _Palette;
		uniform float4 _Palette_ST;
		uniform float4 _Color;
		uniform sampler2D _ColorMask;
		uniform float4 _ColorMask_ST;
		uniform float4 _EmmisionColor;
		uniform sampler2D _Emmision_Map;
		uniform float4 _Emmision_Map_ST;
		uniform float _Metallic;
		uniform float _Smoothness;

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_Palette = i.uv_texcoord * _Palette_ST.xy + _Palette_ST.zw;
			float4 blendOpSrc2 = tex2D( _Palette, uv_Palette );
			float4 blendOpDest2 = _Color;
			float2 uv_ColorMask = i.uv_texcoord * _ColorMask_ST.xy + _ColorMask_ST.zw;
			float4 lerpBlendMode2 = lerp(blendOpDest2,( blendOpSrc2 * blendOpDest2 ),tex2D( _ColorMask, uv_ColorMask ).r);
			o.Albedo = ( saturate( lerpBlendMode2 )).rgb;
			float2 uv_Emmision_Map = i.uv_texcoord * _Emmision_Map_ST.xy + _Emmision_Map_ST.zw;
			float4 blendOpSrc9 = _EmmisionColor;
			float4 blendOpDest9 = tex2D( _Emmision_Map, uv_Emmision_Map );
			o.Emission = ( saturate( ( blendOpSrc9 * blendOpDest9 ) )).rgb;
			o.Metallic = _Metallic;
			o.Smoothness = _Smoothness;
			o.Alpha = 1;
		}

		ENDCG
	}
	Fallback "Diffuse"
	CustomEditor "ASEMaterialInspector"
}
/*ASEBEGIN
Version=19201
Node;AmplifyShaderEditor.RangedFloatNode;5;-447.263,149.3333;Inherit;False;Property;_Metallic;Metallic;5;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;6;-442.0629,232.5335;Inherit;False;Property;_Smoothness;Smoothness;6;0;Create;True;0;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;4;-867.166,259.8335;Inherit;True;Property;_ColorMask;ColorMask;1;1;[HideInInspector];Create;True;0;0;0;False;0;False;-1;None;10d6d7c7ec497bb4f89ebdfa8f00cc2d;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ColorNode;3;-1141.864,-188.4137;Inherit;False;Property;_Color;Color;3;0;Create;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StandardSurfaceOutputNode;0;240.1542,-67.8366;Float;False;True;-1;2;ASEMaterialInspector;0;0;Standard;SciFiStrategy/Main_ASE;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;Back;0;False;;0;False;;False;0;False;;0;False;;False;0;Opaque;0.5;True;True;0;False;Opaque;;Geometry;All;12;all;True;True;True;True;0;False;;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;2;15;10;25;False;0.5;True;0;0;False;;0;False;;0;0;False;;0;False;;0;False;;0;False;;0;False;0;0,0,0,0;VertexOffset;True;False;Cylindrical;False;True;Relative;0;;-1;-1;-1;-1;0;False;0;0;False;;-1;0;False;;0;0;0;False;0.1;False;;0;False;;False;16;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;2;FLOAT3;0,0,0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT3;0,0,0;False;7;FLOAT3;0,0,0;False;8;FLOAT;0;False;9;FLOAT;0;False;10;FLOAT;0;False;13;FLOAT3;0,0,0;False;11;FLOAT3;0,0,0;False;12;FLOAT3;0,0,0;False;14;FLOAT4;0,0,0,0;False;15;FLOAT3;0,0,0;False;0
Node;AmplifyShaderEditor.BlendOpsNode;2;-779.7999,73.85789;Inherit;False;Multiply;True;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.BlendOpsNode;9;-7.580124,367.7435;Inherit;False;Multiply;True;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;1;False;1;COLOR;0
Node;AmplifyShaderEditor.SamplerNode;1;-1213.581,-2.140131;Inherit;True;Property;_Palette;Palette;0;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;7;-442.4935,360.1026;Inherit;True;Property;_Emmision_Map;Emmision_Map;2;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ColorNode;8;-371.3982,556.3279;Inherit;False;Property;_EmmisionColor;EmmisionColor;4;1;[HDR];Create;True;0;0;0;False;0;False;0,0,0,0;0,0,0,0;True;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
WireConnection;0;0;2;0
WireConnection;0;2;9;0
WireConnection;0;3;5;0
WireConnection;0;4;6;0
WireConnection;2;0;1;0
WireConnection;2;1;3;0
WireConnection;2;2;4;1
WireConnection;9;0;8;0
WireConnection;9;1;7;0
ASEEND*/
//CHKSM=2C594EA6F8044852DD8F9D2C21043F92918EDA1C