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
    [HideInInspector] public string supabaseId;
    public bool IsBeingGrabbed { get; private set; } = false;

    private bool _isLoaded = false;
    private HybridSceneManager _sceneManager;

    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private Vector3 _lastScale;

    public override void Spawned()
    {
        _sceneManager = FindFirstObjectByType<HybridSceneManager>();
        _lastPosition = transform.position;
        _lastRotation = transform.rotation;
        _lastScale = transform.localScale;
    }

    public override void FixedUpdateNetwork()
    {
        // If we hold state authority and were being grabbed, check for positional changes to sync to Supabase
        if (HasStateAuthority && IsBeingGrabbed)
        {
            // Transform updates are automatically synced across the network by Fusion's NetworkTransform
        }
    }

    public void InitializeModel(string itemId, string directUrl, Vector3 targetScale, Vector3 targetRotation)
    {
        supabaseId = itemId;
        if (_isLoaded) return;
        StartCoroutine(DownloadAndInstantiateGLTF(directUrl, targetScale, targetRotation));
    }

    private IEnumerator DownloadAndInstantiateGLTF(string url, Vector3 targetScale, Vector3 targetRotation)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Supabase Download Error]: {www.error}");
                yield break;
            }

            byte[] gltfData = www.downloadHandler.data;
            GltfImport gltf = new GltfImport();
            var loadTask = gltf.LoadGltfBinary(gltfData, new System.Uri(url));

            while (!loadTask.IsCompleted) yield return null;

            if (loadTask.Result)
            {
                var instantiateTask = gltf.InstantiateMainSceneAsync(transform);
                while (!instantiateTask.IsCompleted) yield return null;

                _isLoaded = true;

                transform.localScale = targetScale;
                transform.localRotation = Quaternion.Euler(targetRotation);

                FixMaterialsAndShaders();
                SetupXRColliders();

                _lastPosition = transform.position;
                _lastRotation = transform.rotation;
                _lastScale = transform.localScale;

                Debug.Log($"[Supabase] Asset '{supabaseId}' initialized successfully via Fusion.");
            }
        }
    }

    private void FixMaterialsAndShaders()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            r.enabled = true;
            foreach (Material m in r.materials)
            {
                if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader")
                {
                    m.shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                }
            }
        }
    }

    private void SetupXRColliders()
    {
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (var renderer in renderers)
        {
            if (renderer.GetComponent<Collider>() == null)
            {
                renderer.gameObject.AddComponent<BoxCollider>();
            }
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.isKinematic = true;
        rb.useGravity = false;

        XRGrabInteractable grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable == null)
        {
            grabInteractable = gameObject.AddComponent<XRGrabInteractable>();
        }

        grabInteractable.useDynamicAttach = true;
        grabInteractable.throwOnDetach = false;

        grabInteractable.selectEntered.RemoveAllListeners();
        grabInteractable.selectEntered.AddListener((SelectEnterEventArgs args) => 
        {
            IsBeingGrabbed = true;
            RequestNetworkOwnership();
        });

        grabInteractable.selectExited.RemoveAllListeners();
        grabInteractable.selectExited.AddListener((SelectExitEventArgs args) => 
        {
            IsBeingGrabbed = false;
            OnXRTransformEnded();
        });
    }

    public void RequestNetworkOwnership()
    {
        // In Fusion Shared Mode, request State Authority to move objects
        if (!Object.HasStateAuthority)
        {
            Object.RequestStateAuthority();
        }
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

            if (_sceneManager != null && !string.IsNullOrEmpty(supabaseId))
            {
                _sceneManager.UpdateObjectInSupabase(
                    supabaseId, 
                    transform.position, 
                    transform.rotation, 
                    transform.localScale
                );
            }
        }
    }

    public void ApplyExternalDatabaseTransform(Vector3 newPos, Quaternion newRot, Vector3 newScale)
    {
        if (Vector3.Distance(transform.position, newPos) > 0.01f ||
            Quaternion.Angle(transform.rotation, newRot) > 0.1f ||
            Vector3.Distance(transform.localScale, newScale) > 0.01f)
        {
            transform.position = newPos;
            transform.rotation = newRot;
            transform.localScale = newScale;

            _lastPosition = newPos;
            _lastRotation = newRot;
            _lastScale = newScale;

            Debug.Log($"[Supabase Direct Update] Updated '{supabaseId}' transform from database.");
        }
    }
}