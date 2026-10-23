using System.Collections.Generic;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Affiche le repère du plateau : le 0, l'axe cour (rouge) et l'axe lointain (bleu),
    /// plus une grille au mètre activable. Pratique pour vérifier au mètre ruban
    /// que le décor virtuel ne dérive pas par rapport aux marques au sol.
    /// À placer sur le même GameObject que <see cref="StageFrame"/>.
    /// </summary>
    public class StageGizmo : MonoBehaviour
    {
        [SerializeField] Material lineMaterial;
        [SerializeField] float axisLength = 1f;
        [Tooltip("Étendue de la grille en X (jardin → cour), en mètres.")]
        [SerializeField] Vector2 gridX = new Vector2(-10f, 10f);
        [Tooltip("Étendue de la grille en Z (avant-scène → lointain), en mètres.")]
        [SerializeField] Vector2 gridZ = new Vector2(-2f, 16f);
        [SerializeField] float gridStep = 1f;
        [SerializeField] bool showGrid;

        static readonly Color CourColor = new Color(0.95f, 0.25f, 0.25f);
        static readonly Color LointainColor = new Color(0.25f, 0.5f, 1f);
        static readonly Color GridColor = new Color(0.7f, 0.7f, 0.7f);

        GameObject _grid;

        public bool GridVisible
        {
            get => showGrid;
            set
            {
                showGrid = value;
                if (_grid != null) _grid.SetActive(value);
            }
        }

        void Start()
        {
            const float lift = 0.005f; // évite que les lignes se confondent avec le sol
            var cour = RenderUtil.CreateLine("Axe cour (+X)", transform, lineMaterial, CourColor, 0.01f, false);
            cour.positionCount = 2;
            cour.SetPositions(new[] { new Vector3(0, lift, 0), new Vector3(axisLength, lift, 0) });

            var lointain = RenderUtil.CreateLine("Axe lointain (+Z)", transform, lineMaterial, LointainColor, 0.01f, false);
            lointain.positionCount = 2;
            lointain.SetPositions(new[] { new Vector3(0, lift, 0), new Vector3(0, lift, axisLength) });

            _grid = CreateGrid(lift);
            _grid.SetActive(showGrid);
        }

        GameObject CreateGrid(float y)
        {
            var points = new List<Vector3>();
            for (var x = Mathf.Ceil(gridX.x / gridStep) * gridStep; x <= gridX.y + 1e-4f; x += gridStep)
            {
                points.Add(new Vector3(x, y, gridZ.x));
                points.Add(new Vector3(x, y, gridZ.y));
            }
            for (var z = Mathf.Ceil(gridZ.x / gridStep) * gridStep; z <= gridZ.y + 1e-4f; z += gridStep)
            {
                points.Add(new Vector3(gridX.x, y, z));
                points.Add(new Vector3(gridX.y, y, z));
            }

            var indices = new int[points.Count];
            for (var i = 0; i < indices.Length; i++) indices[i] = i;
            var mesh = new Mesh { name = "Grille plateau" };
            mesh.SetVertices(points);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);

            var go = new GameObject("Grille (1 m)");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = lineMaterial != null ? lineMaterial : RenderUtil.FallbackUnlit();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            RenderUtil.SetColor(r, GridColor);
            return go;
        }
    }
}
