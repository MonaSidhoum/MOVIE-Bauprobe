using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Liste des éléments de décor que le metteur en scène peut ajouter.
    /// L'asset est dans Assets/MiseEnScene/Resources/DecorCatalog.asset,
    /// créé et mis à jour par le menu « Bauprobe ».
    /// </summary>
    [CreateAssetMenu(menuName = "Bauprobe/Catalogue de décors", fileName = "DecorCatalog")]
    public class DecorCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Identifiant stable, utilisé dans les sauvegardes. Ne pas le changer une fois utilisé.")]
            public string id;
            public string displayName;
            [Tooltip("Prefab du décor. Si vide, la primitive ci-dessous est utilisée.")]
            public GameObject prefab;
            public PrimitiveType primitive = PrimitiveType.Cube;
            [Tooltip("Échelle de la primitive (un cube de 1 = 1 m ; un cylindre de 1 = 2 m de haut).")]
            public Vector3 primitiveScale = Vector3.one;
        }

        public List<Entry> entries = new List<Entry>();

        public int Count => entries.Count;

        public Entry Find(string id) => entries.Find(e => e.id == id);
    }
}
