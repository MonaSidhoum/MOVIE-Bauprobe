using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>Petits utilitaires de rendu partagés (URP).</summary>
    public static class RenderUtil
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static MaterialPropertyBlock _block;

        /// <summary>Teinte un renderer sans dupliquer ses matériaux (URP : _BaseColor, autres : _Color).</summary>
        public static void SetColor(Renderer renderer, Color color)
        {
            _block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        public static void ClearColor(Renderer renderer) => renderer.SetPropertyBlock(null);

        /// <summary>
        /// Matériau de secours si aucun n'a été assigné dans l'inspecteur.
        /// Attention : dans un build, le shader n'est inclus que s'il est utilisé par un asset ;
        /// c'est pour ça que le menu « Bauprobe » crée et assigne de vrais matériaux.
        /// </summary>
        public static Material FallbackUnlit()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            return new Material(shader);
        }

        public static LineRenderer CreateLine(string name, Transform parent, Material material, Color color, float width, bool worldSpace)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material != null ? material : FallbackUnlit();
            line.useWorldSpace = worldSpace;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = line.endColor = color;
            SetColor(line, color);
            return line;
        }
    }
}
