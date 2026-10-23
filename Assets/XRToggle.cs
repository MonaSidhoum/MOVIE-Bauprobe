using UnityEngine;

public class PassthroughToggle : MonoBehaviour
{
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private GameObject theatreRoot;
    [SerializeField] private Camera centerEyeCamera;
    [SerializeField] private OVRInput.Button toggleButton = OVRInput.Button.One;

    private Renderer[] theatreRenderers;
    private Light[] theatreLights;
    private bool isAR;

    private void Awake()
    {
        // includeInactive: true so it still works if something starts disabled
        theatreRenderers = theatreRoot.GetComponentsInChildren<Renderer>(true);
        theatreLights = theatreRoot.GetComponentsInChildren<Light>(true);
    }

    private void Start() => Apply(true);

    private void Update()
    {
        if (OVRInput.GetDown(toggleButton))
        {
            isAR = !isAR;
            Apply(isAR);
        }
    }

    private void Apply(bool ar)
    {
        foreach (var r in theatreRenderers) r.enabled = !ar;
        foreach (var l in theatreLights) l.enabled = !ar;

        passthroughLayer.enabled = ar;

        centerEyeCamera.clearFlags = ar ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
        centerEyeCamera.backgroundColor = ar ? new Color(0, 0, 0, 0) : Color.black;
    }
}