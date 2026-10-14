using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Fusion;
using GLTFast;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class DynamicNetworkObject : NetworkBehaviour
{
    // Replicated state. Set via onBeforeSpawned so it is part of the FIRST snapshot.
    // Signed Supabase URLs can be long, so use _512 (NetworkString truncates silently).
    [Networked] public NetworkString<_64> SupabaseId { get; set; }
    [Networked] public NetworkString<_512> ModelUrl { get; set; }

    public bool IsBeingGrabbed { get; private set; }

    private bool _isLoaded;
    private bool _isLoading;
    private float _nextRetryTime;
    private bool _registered;

    private HybridSceneManager _sceneManager;
    private Transform _modelRoot;

    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastScale;

    public override void Spawned()
    {
        _sceneManager = FindFirstObjectByType<HybridSceneManager>();
        TryBeginLoad();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_registered && _sceneManager != null)
            _sceneManager.UnregisterNetworkObject(SupabaseId.ToString(), this);
    }

    // Safety net: if state arrived after Spawned() (or a download failed), keep trying.
    public override void Render()
    {
        if (!_isLoaded && !_isLoading && Time.time >= _nextRetryTime)
            TryBeginLoad();
    }

    private void TryBeginLoad()
    {
        if (_isLoaded || _isLoading) return;

        string url = ModelUrl.ToString();
        if (string.IsNullOrEmpty(url)) return;

        string id = SupabaseId.ToString();
        if (!_registered && !string.IsNullOrEmpty(id) && _sceneManager != null)
        {
            _sceneManager.RegisterExistingNetworkObject(id, this);
            _registered = true;
        }

        _isLoading = true;
        StartCoroutine(DownloadAndInstantiateGLTF(url));
    }

    private IEnumerator DownloadAndInstantiateGLTF(string url)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Supabase Download Error] {www.error}");
                Fail();
                yield break;
            }

            var gltf = new GltfImport();
            var loadTask = gltf.LoadGltfBinary(www.downloadHandler.data, new System.Uri(url));
            while (!loadTask.IsCompleted) yield return null;

            if (loadTask.IsFaulted || !loadTask.Result)
            {
                Debug.LogError($"[GLTF] Failed to parse '{SupabaseId}'.");
                Fail();
                yield break;
            }

            // Model lives under a child so the ROOT pose belongs solely to NetworkTransform.
            _modelRoot = new GameObject("Model").transform;
            _modelRoot.SetParent(transform, false);

            var instTask = gltf.InstantiateMainSceneAsync(_modelRoot);
            while (!instTask.IsCompleted) yield return null;

            if (instTask.IsFaulted || !instTask.Result)
            {
                Fail();
                yield break;
            }

            _isLoaded = true;
            _isLoading = false;

            FixMaterialsAndShaders();
            SetupXRColliders();

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
            _lastScale = transform.localScale;

            Debug.Log($"[Supabase] Asset '{SupabaseId}' loaded.");
        }
    }

    private void Fail()
    {
        _isLoading = false;
        _nextRetryTime = Time.time + 3f;
        if (_modelRoot != null) Destroy(_modelRoot.gameObject);
    }

    private void FixMaterialsAndShaders()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.enabled = true;
            foreach (Material m in r.sharedMaterials)
            {
                if (m != null && (m.shader == null || m.shader.name == "Hidden/InternalErrorShader"))
                {
                    var fallback = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (fallback != null) m.shader = fallback;
                }
            }
        }
    }

    private void SetupXRColliders()
    {
        foreach (var mr in GetComponentsInChildren<MeshRenderer>())
        {
            if (mr.GetComponent<Collider>() == null)
                mr.gameObject.AddComponent<BoxCollider>();
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        XRGrabInteractable grab = GetComponent<XRGrabInteractable>();
        if (grab == null) grab = gameObject.AddComponent<XRGrabInteractable>();

        grab.useDynamicAttach = true;
        grab.throwOnDetach = false;

        grab.selectEntered.RemoveAllListeners();
        grab.selectEntered.AddListener((SelectEnterEventArgs args) =>
        {
            IsBeingGrabbed = true;
            RequestNetworkOwnership();
        });

        grab.selectExited.RemoveAllListeners();
        grab.selectExited.AddListener((SelectExitEventArgs args) =>
        {
            IsBeingGrabbed = false;
            OnXRTransformEnded();
        });
    }

    public void RequestNetworkOwnership()
    {
        // Requires "Allow State Authority Override" on the NetworkObject.
        if (!Object.HasStateAuthority)
            Object.RequestStateAuthority();
    }

    public void OnXRTransformEnded()
    {
        if (!HasStateAuthority) return;

        if (transform.position != _lastPosition ||
            transform.rotation != _lastRotation ||
            transform.localScale != _lastScale)
        {
            _lastPosition = transform.position;
            _lastRotation = transform.rotation;
            _lastScale = transform.localScale;

            string id = SupabaseId.ToString();
            if (_sceneManager != null && !string.IsNullOrEmpty(id))
                _sceneManager.UpdateObjectInSupabase(id, transform.position, transform.rotation, transform.localScale);
        }
    }

    // Called by the poll loop. Only the state authority may write the root transform;
    // NetworkTransform then replicates it to everyone else.
    public void ApplyExternalDatabaseTransform(Vector3 newPos, Quaternion newRot, Vector3 newScale)
    {
        if (!HasStateAuthority || IsBeingGrabbed) return;

        if (Vector3.Distance(transform.position, newPos) > 0.01f ||
            Quaternion.Angle(transform.rotation, newRot) > 0.1f ||
            Vector3.Distance(transform.localScale, newScale) > 0.01f)
        {
            transform.SetPositionAndRotation(newPos, newRot);
            transform.localScale = newScale;

            _lastPosition = newPos;
            _lastRotation = newRot;
            _lastScale = newScale;
        }
    }
}