Texture2D SourceTexture : register(t0);
SamplerState SourceSampler : register(s0);
Texture2D LightningTexture : register(t1);
SamplerState LightningSampler : register(s1);

cbuffer Constants : register(b0)
{
    float amount : packoffset(c0.x);
    float pad0 : packoffset(c0.y);
    float pad1 : packoffset(c0.z);
    float pad2 : packoffset(c0.w);
};

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1
) : SV_TARGET
{
    float4 source = SourceTexture.SampleLevel(SourceSampler, uv0.xy, 0);
    if (amount <= 0.0)
        return source;

    float4 lightning = LightningTexture.SampleLevel(LightningSampler, uv1.xy, 0);
    lightning.rgb = min(lightning.rgb, lightning.a.xxx);
    lightning *= amount;
    return source + lightning - source * lightning;
}
