using UnityEngine;

namespace MyGame
{
    /// <summary>没有美术资源前，用纯色材质区分物体。</summary>
    public static class Visuals
    {
        static readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, c);
            _block.SetColor(ColorId, c);
            r.SetPropertyBlock(_block);
        }
    }
}
