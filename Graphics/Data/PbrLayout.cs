using Veldrid;

namespace Whisperleaf.Graphics.Data;

public static class PbrLayout
{
    public static ResourceLayout MaterialLayout;
    public static ResourceLayout MaterialParamsLayout;

    public static void Initialize(GraphicsDevice gd)
    {
        var factory = gd.ResourceFactory;
        MaterialLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("BaseColorTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("NormalTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("MetallicTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("RoughnessTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("OcclusionTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("EmissiveTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment)
            ));

        MaterialParamsLayout = factory.CreateResourceLayout(
            new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("MaterialParams", ResourceKind.UniformBuffer, ShaderStages.Fragment)
            ));
    }

}