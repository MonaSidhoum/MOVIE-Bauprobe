using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// État d'un élément de décor, exprimé dans le repère du plateau
    /// (+X vers cour, +Y vers le haut, +Z vers le lointain, en mètres).
    /// C'est l'unité d'échange pour la sauvegarde, et pour le réseau plus tard.
    /// </summary>
    [Serializable]
    public class DecorState
    {
        public string id;
        public string catalogId;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public bool hasTint;
        public Color tint = Color.white;

        public DecorState Clone() => (DecorState)MemberwiseClone();
    }

    /// <summary>Contenu du fichier de sauvegarde.</summary>
    [Serializable]
    public class DecorSaveFile
    {
        public int version = 1;
        public List<DecorState> objects = new List<DecorState>();
    }
}
