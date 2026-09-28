using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Renderer.Effects
{
    internal class BasicUOEffect : Effect
    {
        public BasicUOEffect(GraphicsDevice graphicsDevice) : base(graphicsDevice, Resources.GetUOShader().ToArray())
        {
            MatrixTransform = Parameters["MatrixTransform"];
            WorldMatrix = Parameters["WorldMatrix"];
            Viewport = Parameters["Viewport"];
            Brighlight = Parameters["Brightlight"];
            CircleOfTransparencyRadius = Parameters["CircleOfTransparencyRadius"];
            CircleProgress = Parameters["CircleProgress"];
            CircleHalfStroke = Parameters["CircleHalfStroke"];
            CircleFeather = Parameters["CircleFeather"];

            CurrentTechnique = Techniques["HueTechnique"];
            Pass = CurrentTechnique.Passes[0];
        }

        public EffectParameter MatrixTransform { get; }
        public EffectParameter WorldMatrix { get; }
        public EffectParameter Viewport { get; }
        public EffectParameter Brighlight { get; }
        public EffectParameter CircleOfTransparencyRadius { get; }
        public EffectParameter CircleProgress { get; }
        public EffectParameter CircleHalfStroke { get; }
        public EffectParameter CircleFeather { get; }
        public EffectPass Pass { get; }
    }
}
