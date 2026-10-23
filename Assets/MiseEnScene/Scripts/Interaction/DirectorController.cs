using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Commandes du metteur en scène avec les manettes Quest.
    ///
    /// Manette droite (rayon) :
    ///   gâchette        sélectionner l'objet visé (dans le vide : désélectionner)
    ///   grip maintenu   déplacer l'objet visé en le faisant glisser à hauteur constante
    ///   stick gauche/droite  tourner     stick haut/bas  agrandir / réduire
    ///   clic stick      redresser la rotation au multiple de 15° le plus proche
    ///   A               couleur suivante        B  axe d'agrandissement suivant
    /// Manette gauche :
    ///   stick gauche/droite  choisir le décor à ajouter   stick haut/bas  monter / descendre
    ///   X               ajouter le décor choisi là où vise le rayon
    ///   gâchette        dupliquer la sélection  Y maintenu  supprimer la sélection
    ///   clic stick      afficher la grille      Menu        caler le plateau
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class DirectorController : MonoBehaviour
    {
        public enum ScaleAxis { Uniforme, Largeur, Hauteur, Profondeur }

        [Header("Références (trouvées automatiquement si vides)")]
        [SerializeField] OVRCameraRig rig;
        [SerializeField] DecorManager decor;
        [SerializeField] StageFrame stage;
        [SerializeField] StageCalibrator calibrator;
        [SerializeField] StageGizmo gizmo;

        [Header("Rayon")]
        [SerializeField] Material rayMaterial;
        [SerializeField] float maxRayDistance = 30f;
        [SerializeField] LayerMask decorLayers = ~0;

        [Header("Réglages")]
        [SerializeField] float rotateSpeed = 90f;      // degrés par seconde
        [SerializeField] float scaleSpeed = 1f;        // doublements par seconde, stick à fond
        [SerializeField] float liftSpeed = 0.5f;       // mètres par seconde
        [SerializeField] float snapAngle = 15f;
        [SerializeField] float deleteHoldSeconds = 0.8f;
        [SerializeField] float deadZone = 0.2f;
        [SerializeField] float minScale = 0.05f;
        [SerializeField] float maxScale = 50f;
        [SerializeField] float notifyInterval = 0.25f;

        static readonly Color RayIdle = new Color(1f, 1f, 1f);
        static readonly Color RayHover = new Color(0.3f, 0.9f, 1f);

        public DecorObject Selected { get; private set; }
        public int CatalogIndex { get; private set; }
        public ScaleAxis CurrentScaleAxis { get; private set; }
        public bool IsGrabbing { get; private set; }
        public float DeleteProgress => Selected != null ? Mathf.Clamp01(_deleteTimer / deleteHoldSeconds) : 0f;

        LineRenderer _ray;
        Plane _grabPlane;
        Vector3 _grabOffset;
        float _deleteTimer;
        bool _dirty;
        float _lastNotify;

        void Awake()
        {
            if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
            if (decor == null) decor = FindFirstObjectByType<DecorManager>();
            if (stage == null) stage = FindFirstObjectByType<StageFrame>();
            if (calibrator == null) calibrator = FindFirstObjectByType<StageCalibrator>();
            if (gizmo == null) gizmo = FindFirstObjectByType<StageGizmo>();

            _ray = RenderUtil.CreateLine("Rayon", transform, rayMaterial, RayIdle, 0.004f, true);
            _ray.positionCount = 2;
        }

        void Update()
        {
            if (Selected == null) Selected = null; // objet supprimé ailleurs (réseau, etc.)

            if (calibrator != null && calibrator.IsActive)
            {
                _ray.enabled = false;
                EndGrab();
                return;
            }
            if (OVRInput.GetDown(OVRInput.RawButton.Start) && calibrator != null)
            {
                Select(null);
                calibrator.Begin();
                return;
            }

            var hand = rig.rightControllerAnchor;
            var ray = new Ray(hand.position, hand.forward);
            var hovered = RaycastDecor(ray, out var hit);
            DrawRay(ray, hovered != null ? hit.point : ray.GetPoint(3f), hovered != null);

            HandleSelectionAndGrab(ray, hovered, hit);
            HandleCatalogAndCreation(ray);
            if (Selected != null) HandleManipulation();

            if (OVRInput.GetDown(OVRInput.RawButton.LThumbstick) && gizmo != null)
                gizmo.GridVisible = !gizmo.GridVisible;

            FlushChanges(force: false);
        }

        // ---------- Sélection et déplacement ----------

        void HandleSelectionAndGrab(Ray ray, DecorObject hovered, RaycastHit hit)
        {
            if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger)) Select(hovered);

            if (OVRInput.GetDown(OVRInput.RawButton.RHandTrigger) && hovered != null)
            {
                Select(hovered);
                IsGrabbing = true;
                // On glisse l'objet sur un plan horizontal (du plateau) passant par le point visé.
                _grabPlane = new Plane(stage.transform.up, hit.point);
                _grabOffset = Selected.transform.position - hit.point;
            }

            if (!IsGrabbing) return;
            if (!OVRInput.Get(OVRInput.RawButton.RHandTrigger) || Selected == null)
            {
                EndGrab();
                return;
            }
            if (_grabPlane.Raycast(ray, out var enter) && enter < maxRayDistance)
            {
                Selected.transform.position = ray.GetPoint(enter) + _grabOffset;
                _dirty = true;
            }
        }

        void EndGrab()
        {
            if (!IsGrabbing) return;
            IsGrabbing = false;
            FlushChanges(force: true);
        }

        void Select(DecorObject obj)
        {
            if (obj == Selected) return;
            FlushChanges(force: true);
            if (Selected != null) Selected.SetSelected(false);
            Selected = obj;
            if (Selected != null) Selected.SetSelected(true);
            _deleteTimer = 0f;
        }

        // ---------- Catalogue, ajout, duplication, suppression ----------

        void HandleCatalogAndCreation(Ray ray)
        {
            var count = decor.Catalog.Count;
            if (count > 0)
            {
                if (OVRInput.GetDown(OVRInput.RawButton.LThumbstickRight)) CatalogIndex = (CatalogIndex + 1) % count;
                if (OVRInput.GetDown(OVRInput.RawButton.LThumbstickLeft)) CatalogIndex = (CatalogIndex + count - 1) % count;
                CatalogIndex = Mathf.Clamp(CatalogIndex, 0, count - 1);

                if (OVRInput.GetDown(OVRInput.RawButton.X))
                {
                    var entry = decor.Catalog.entries[CatalogIndex];
                    var stagePos = stage.transform.InverseTransformPoint(PlacementPoint(ray));
                    stagePos.y = 0f;
                    Select(decor.Create(entry.id, stagePos, Quaternion.identity));
                }
            }

            if (Selected == null) return;

            if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger))
            {
                var offset = new Vector3(Mathf.Max(0.3f, Selected.Size.x + 0.1f), 0f, 0f);
                Select(decor.Duplicate(Selected, offset));
            }

            if (OVRInput.Get(OVRInput.RawButton.Y))
            {
                _deleteTimer += Time.deltaTime;
                if (_deleteTimer >= deleteHoldSeconds)
                {
                    var toDelete = Selected;
                    Select(null);
                    decor.Delete(toDelete);
                }
            }
            else _deleteTimer = 0f;
        }

        /// <summary>Point du sol du plateau visé par le rayon, sinon 2 m devant le metteur en scène.</summary>
        Vector3 PlacementPoint(Ray ray)
        {
            var floor = new Plane(stage.transform.up, stage.transform.position);
            if (floor.Raycast(ray, out var enter) && enter < 15f) return ray.GetPoint(enter);
            var head = rig.centerEyeAnchor;
            var forward = Vector3.ProjectOnPlane(head.forward, stage.transform.up).normalized;
            return head.position + forward * 2f;
        }

        // ---------- Rotation, échelle, hauteur, couleur ----------

        void HandleManipulation()
        {
            var t = Selected.transform;
            var up = stage.transform.up;
            var right = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
            var left = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);

            if (Mathf.Abs(right.x) > deadZone)
            {
                t.RotateAround(t.position, up, right.x * rotateSpeed * Time.deltaTime);
                _dirty = true;
            }

            if (Mathf.Abs(right.y) > deadZone)
            {
                var factor = Mathf.Pow(2f, right.y * scaleSpeed * Time.deltaTime);
                var s = t.localScale;
                switch (CurrentScaleAxis)
                {
                    case ScaleAxis.Uniforme: s *= factor; break;
                    case ScaleAxis.Largeur: s.x *= factor; break;
                    case ScaleAxis.Hauteur: s.y *= factor; break;
                    case ScaleAxis.Profondeur: s.z *= factor; break;
                }
                t.localScale = new Vector3(
                    Mathf.Clamp(s.x, minScale, maxScale),
                    Mathf.Clamp(s.y, minScale, maxScale),
                    Mathf.Clamp(s.z, minScale, maxScale));
                _dirty = true;
            }

            if (Mathf.Abs(left.y) > deadZone)
            {
                t.position += up * (left.y * liftSpeed * Time.deltaTime);
                _dirty = true;
            }

            if (OVRInput.GetDown(OVRInput.RawButton.RThumbstick))
            {
                var yaw = t.localEulerAngles.y;
                t.localRotation = Quaternion.Euler(0f, Mathf.Round(yaw / snapAngle) * snapAngle, 0f);
                _dirty = true;
            }

            if (OVRInput.GetDown(OVRInput.RawButton.A))
            {
                ColorPalette.ApplyNext(Selected);
                _dirty = true;
            }

            if (OVRInput.GetDown(OVRInput.RawButton.B))
                CurrentScaleAxis = (ScaleAxis)(((int)CurrentScaleAxis + 1) % 4);
        }

        // ---------- Utilitaires ----------

        /// <summary>Prévient le DecorManager (sauvegarde, réseau) au plus toutes les notifyInterval secondes.</summary>
        void FlushChanges(bool force)
        {
            if (!_dirty || Selected == null) return;
            if (!force && Time.unscaledTime - _lastNotify < notifyInterval) return;
            _dirty = false;
            _lastNotify = Time.unscaledTime;
            decor.NotifyChanged(Selected);
        }

        DecorObject RaycastDecor(Ray ray, out RaycastHit hit)
        {
            if (Physics.Raycast(ray, out hit, maxRayDistance, decorLayers, QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<DecorObject>();
            return null;
        }

        void DrawRay(Ray ray, Vector3 end, bool hovering)
        {
            _ray.enabled = true;
            _ray.SetPosition(0, ray.origin);
            _ray.SetPosition(1, end);
            RenderUtil.SetColor(_ray, hovering ? RayHover : RayIdle);
        }
    }
}
