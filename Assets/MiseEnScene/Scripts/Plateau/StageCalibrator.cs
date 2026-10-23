using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Calage du plateau en 2 points avec la manette droite :
    ///  1. la pointe de la manette sur le 0 (centre du nez de scène) + gâchette ;
    ///  2. la pointe sur un repère au lointain, sur l'axe central + gâchette.
    /// Le repère obtenu est passé à <see cref="StageFrame.Calibrate"/>.
    /// </summary>
    public class StageCalibrator : MonoBehaviour
    {
        public enum Step { Inactif, Origine, Axe }

        [SerializeField] OVRCameraRig rig;
        [SerializeField] StageFrame stage;
        [Tooltip("Position de la pointe (petite boule jaune) par rapport à la manette droite.")]
        [SerializeField] Vector3 tipOffset = new Vector3(0f, 0f, 0.08f);
        [Tooltip("Ramène l'origine au niveau du sol détecté par le casque.")]
        [SerializeField] bool snapOriginToFloor = true;
        [SerializeField] float minAxisLength = 0.5f;
        [SerializeField] Material markerMaterial;

        public Step CurrentStep { get; private set; } = Step.Inactif;
        public bool IsActive => CurrentStep != Step.Inactif;
        public string Instructions { get; private set; } = "";

        static readonly Color MarkerColor = new Color(1f, 0.85f, 0f);

        Transform _tip;
        Transform _originMarker;
        Vector3 _origin;

        void Awake()
        {
            if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
            if (stage == null) stage = FindFirstObjectByType<StageFrame>();
        }

        void Start()
        {
            // Origine au sol : indispensable pour que y = 0 du plateau soit le vrai sol.
            if (OVRManager.instance != null)
                OVRManager.instance.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;

            _tip = CreateMarker("Pointe de calage", rig.rightControllerAnchor, 0.02f);
            _tip.localPosition = tipOffset;
            _originMarker = CreateMarker("Repère 0", transform, 0.04f);
            SetMarkersVisible(false);
        }

        public void Begin()
        {
            CurrentStep = Step.Origine;
            Instructions = "CALAGE DU PLATEAU (1/2)\n" +
                           "Pose la petite boule jaune de la manette droite sur le 0 " +
                           "(centre du nez de scène, au sol) puis appuie sur la gâchette.\n\nB : annuler";
            SetMarkersVisible(true);
            _originMarker.gameObject.SetActive(false);
        }

        public void Cancel()
        {
            CurrentStep = Step.Inactif;
            Instructions = "";
            SetMarkersVisible(false);
        }

        void Update()
        {
            if (!IsActive) return;

            if (OVRInput.GetDown(OVRInput.RawButton.B))
            {
                Cancel();
                return;
            }
            if (!OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger)) return;

            var point = _tip.position;
            if (CurrentStep == Step.Origine)
            {
                _origin = point;
                _originMarker.position = point;
                _originMarker.gameObject.SetActive(true);
                CurrentStep = Step.Axe;
                Instructions = "CALAGE DU PLATEAU (2/2)\n" +
                               "Pose la boule sur un repère au lointain, sur l'axe central " +
                               "(le plus loin possible, au moins 2 m), puis gâchette.\n\nB : annuler";
                return;
            }

            var axis = Vector3.ProjectOnPlane(point - _origin, Vector3.up);
            if (axis.magnitude < minAxisLength)
            {
                Instructions = "Les deux points sont trop proches.\n" +
                               "Recommence le point du lointain, plus loin du 0.\n\nB : annuler";
                return;
            }

            var origin = _origin;
            if (snapOriginToFloor && rig != null) origin.y = rig.trackingSpace.position.y;
            stage.Calibrate(origin, Quaternion.LookRotation(axis.normalized, Vector3.up));
            Cancel();
        }

        Transform CreateMarker(string markerName, Transform parent, float diameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = markerName;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * diameter;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = markerMaterial != null ? markerMaterial : RenderUtil.FallbackUnlit();
            RenderUtil.SetColor(r, MarkerColor);
            return go.transform;
        }

        void SetMarkersVisible(bool visible)
        {
            if (_tip != null) _tip.gameObject.SetActive(visible);
            if (_originMarker != null) _originMarker.gameObject.SetActive(visible);
        }
    }
}
