using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>Couleurs proposées pour teinter le décor (l'entrée 0 = couleur d'origine).</summary>
    public static class ColorPalette
    {
        public struct Swatch
        {
            public string name;
            public Color color;
            public bool isOriginal;
        }

        public static readonly Swatch[] Swatches =
        {
            new Swatch { name = "d'origine", color = Color.white, isOriginal = true },
            new Swatch { name = "blanc", color = new Color(0.95f, 0.95f, 0.95f) },
            new Swatch { name = "noir", color = new Color(0.08f, 0.08f, 0.08f) },
            new Swatch { name = "gris", color = new Color(0.5f, 0.5f, 0.5f) },
            new Swatch { name = "bois", color = new Color(0.55f, 0.38f, 0.22f) },
            new Swatch { name = "rouge", color = new Color(0.75f, 0.1f, 0.1f) },
            new Swatch { name = "bleu", color = new Color(0.15f, 0.3f, 0.75f) },
            new Swatch { name = "vert", color = new Color(0.2f, 0.55f, 0.25f) },
            new Swatch { name = "jaune", color = new Color(0.95f, 0.8f, 0.2f) },
        };

        /// <summary>Indice de la couleur de la palette la plus proche de la teinte de l'objet.</summary>
        public static int IndexOf(DecorObject obj)
        {
            if (!obj.HasTint) return 0;
            var best = 1;
            var bestDistance = float.MaxValue;
            for (var i = 1; i < Swatches.Length; i++)
            {
                var c = Swatches[i].color;
                var d = new Vector3(c.r - obj.Tint.r, c.g - obj.Tint.g, c.b - obj.Tint.b).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        public static string NameOf(DecorObject obj) => Swatches[IndexOf(obj)].name;

        public static void ApplyNext(DecorObject obj)
        {
            var next = Swatches[(IndexOf(obj) + 1) % Swatches.Length];
            obj.SetTint(!next.isOriginal, next.color);
        }
    }
}
