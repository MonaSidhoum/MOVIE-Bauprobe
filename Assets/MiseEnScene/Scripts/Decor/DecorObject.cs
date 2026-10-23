using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Un élément de décor posé sur le plateau. Le GameObject racine porte la pose
    /// (position / rotation / échelle dans le repère du plateau) ; le modèle 3D est un enfant.
    /// Créé uniquement par <see cref="DecorManager"/>.
    /// </summary>
    public class DecorObject : MonoBehaviour
    {
        static readonly Color SelectionColor = new Color(1f, 0.8f, 0.1f);

        public string Id { get; private set; }
        public string CatalogId { get; private set; }
        public string DisplayName { get; private set; }
        public bool HasTint { get; private set; }
        public Color Tint { get; private set; } = Color.white;
        public bool IsSelected { get; private set; }

        /// <summary>Boîte englobante du modèle, dans l'espace local (non mis à l'échelle) de la racine.</summary>
        public Bounds LocalBounds { get; private set; }

        /// <summary>Dimensions réelles en mètres (largeur X, hauteur Y, profondeur Z).</summary>
        public Vector3 Size => Vector3.Scale(LocalBounds.size, transform.localScale);

        Renderer[] _renderers;
        LineRenderer _selectionBox;

        internal void Init(string id, string catalogId, string displayName, Material lineMaterial)
        {
            Id = id;
            CatalogId = catalogId;
            DisplayName = displayName;
            _renderers = GetComponentsInChildren<Renderer>(true);

            // Les colliders et rigidbodies éventuels du prefab sont neutralisés :
            // un seul BoxCollider sur la racine sert à viser l'objet avec le rayon.
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            LocalBounds = ComputeLocalBounds(transform);
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = LocalBounds.center;
            box.size = Vector3.Max(LocalBounds.size, Vector3.one * 0.02f);

            _selectionBox = RenderUtil.CreateLine("Selection", transform, lineMaterial, SelectionColor, 0.01f, false);
            _selectionBox.positionCount = 16;
            _selectionBox.SetPositions(BoxPath(LocalBounds));
            _selectionBox.enabled = false;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (_selectionBox != null) _selectionBox.enabled = selected;
        }

        public void SetTint(bool hasTint, Color tint)
        {
            HasTint = hasTint;
            Tint = tint;
            foreach (var r in _renderers)
            {
                if (r == null || r == _selectionBox) continue;
                if (hasTint) RenderUtil.SetColor(r, tint);
                else RenderUtil.ClearColor(r);
            }
        }

        public DecorState CaptureState() => new DecorState
        {
            id = Id,
            catalogId = CatalogId,
            position = transform.localPosition,
            rotation = transform.localRotation,
            scale = transform.localScale,
            hasTint = HasTint,
            tint = Tint,
        };

        public void ApplyState(DecorState state)
        {
            transform.localPosition = state.position;
            transform.localRotation = state.rotation;
            transform.localScale = state.scale;
            if (state.hasTint != HasTint || state.tint != Tint) SetTint(state.hasTint, state.tint);
        }

        static Bounds ComputeLocalBounds(Transform root)
        {
            var toRoot = root.worldToLocalMatrix;
            var bounds = new Bounds();
            var any = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var m = toRoot * filter.transform.localToWorldMatrix;
                var mb = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any ? bounds : new Bounds(Vector3.zero, Vector3.one * 0.2f);
        }

        // Parcours qui dessine les 12 arêtes d'une boîte avec une seule ligne (certaines repassées).
        static Vector3[] BoxPath(Bounds b)
        {
            Vector3 P(int x, int y, int z) => new Vector3(
                x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z);
            return new[]
            {
                P(0,0,0), P(1,0,0), P(1,0,1), P(0,0,1), P(0,0,0),
                P(0,1,0), P(1,1,0), P(1,0,0), P(1,1,0),
                P(1,1,1), P(1,0,1), P(1,1,1),
                P(0,1,1), P(0,0,1), P(0,1,1), P(0,1,0),
            };
        }
    }
}
