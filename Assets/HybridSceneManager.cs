using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Fusion;

[System.Serializable]
public class SupabaseItem
{
    public string id;
    public string model_url;
    
    public float pos_x;
    public float pos_y;
    public float pos_z;

    public float scale_x = 1.0f;
    public float scale_y = 1.0f;
    public float scale_z = 1.0f;

    public float rot_x = 0.0f;
    public float rot_y = 0.0f;
    public float rot_z = 0.0f;
}

public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        string newJson = "{\"Items\":" + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
        return wrapper != null ? wrapper.Items : new T[0];
    }

    [System.Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}

public class HybridSceneManager : MonoBehaviour
{
    [Header("Supabase Configuration")]
    public string supabaseUrl = "https://eunkmyopqbsqdvvqhzex.supabase.co";
    public string supabaseAnonKey = "YOUR_SUPABASE_ANON_KEY";
    
    [Header("Sync Configuration")]
    public float dbPollIntervalSeconds = 2.0f;

    [Header("Fusion Network Prefab")]
    public NetworkObject networkedContainerPrefab; // Must be registered in Fusion Network Project Registry

    private Dictionary<string, DynamicNetworkObject> _spawnedObjects = new Dictionary<string, DynamicNetworkObject>();
    private Coroutine _pollingCoroutine;
    private NetworkRunner _runner;

    private async void Start()
    {
        _runner = FindFirstObjectByType<NetworkRunner>();
        if (_runner == null)
        {
            Debug.LogError("[Fusion] No NetworkRunner found in scene!");
            return;
        }

        // Automatically start or wait for Fusion session startup
        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = "Theater_XR_Room",
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok)
        {
            Debug.Log("[Fusion] Successfully connected to Shared Mode session.");
            StartCoroutine(FetchSceneFromSupabase());
            StartPollingLoop();
        }
        else
        {
            Debug.LogError($"[Fusion] Failed to start game: {result.ErrorMessage}");
        }
    }

    private void StartPollingLoop()
    {
        if (_pollingCoroutine != null)
        {
            StopCoroutine(_pollingCoroutine);
        }
        _pollingCoroutine = StartCoroutine(PollSupabaseForExternalChanges());
    }

    private IEnumerator FetchSceneFromSupabase()
    {
        string url = $"{supabaseUrl}/rest/v1/scene_objects?select=*";

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("apikey", supabaseAnonKey);
            www.SetRequestHeader("Authorization", $"Bearer {supabaseAnonKey}");

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Supabase Fetch Error]: {www.error}");
                yield break;
            }

            string jsonResult = www.downloadHandler.text;
            SupabaseItem[] items = JsonHelper.FromJson<SupabaseItem>(jsonResult);

            if (items != null && items.Length > 0 && _runner != null && _runner.IsServer || _runner.IsRunning)
            {
                foreach (var item in items)
                {
                    if (!_spawnedObjects.ContainsKey(item.id))
                    {
                        SpawnDynamicModel(item);
                    }
                }
            }
        }
    }

    private IEnumerator PollSupabaseForExternalChanges()
    {
        WaitForSeconds wait = new WaitForSeconds(dbPollIntervalSeconds);

        while (true)
        {
            yield return wait;

            if (_runner == null || !_runner.IsRunning) yield break;

            string url = $"{supabaseUrl}/rest/v1/scene_objects?select=*";

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                www.SetRequestHeader("apikey", supabaseAnonKey);
                www.SetRequestHeader("Authorization", $"Bearer {supabaseAnonKey}");

                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    string jsonResult = www.downloadHandler.text;
                    SupabaseItem[] items = JsonHelper.FromJson<SupabaseItem>(jsonResult);

                    if (items != null && items.Length > 0)
                    {
                        foreach (var item in items)
                        {
                            if (_spawnedObjects.TryGetValue(item.id, out DynamicNetworkObject netObj))
                            {
                                if (!netObj.IsBeingGrabbed)
                                {
                                    Vector3 dbPos = new Vector3(item.pos_x, item.pos_y, item.pos_z);
                                    Quaternion dbRot = Quaternion.Euler(item.rot_x, item.rot_y, item.rot_z);
                                    Vector3 dbScale = new Vector3(item.scale_x, item.scale_y, item.scale_z);

                                    netObj.ApplyExternalDatabaseTransform(dbPos, dbRot, dbScale);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    public void SpawnDynamicModel(SupabaseItem item)
    {
        if (_runner == null || !_runner.IsRunning) return;

        Vector3 position = new Vector3(item.pos_x, item.pos_y, item.pos_z);
        Quaternion rotation = Quaternion.Euler(item.rot_x, item.rot_y, item.rot_z);
        Vector3 scale = new Vector3(item.scale_x, item.scale_y, item.scale_z);

        // Fusion 2 network instantiation
        NetworkObject spawnedNetworkObject = _runner.Spawn(
            networkedContainerPrefab, 
            position, 
            rotation, 
            inputAuthority: null
        );

        DynamicNetworkObject dynamicScript = spawnedNetworkObject.GetComponent<DynamicNetworkObject>();
        if (dynamicScript != null)
        {
            dynamicScript.supabaseId = item.id;
            _spawnedObjects[item.id] = dynamicScript;
            
            // Initialize and download GLTF across all clients
            dynamicScript.InitializeModel(item.id, item.model_url, scale, rotation.eulerAngles);
        }
    }

    public void UpdateObjectInSupabase(string itemId, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        StartCoroutine(PatchObjectToSupabase(itemId, position, rotation, scale));
    }

    private IEnumerator PatchObjectToSupabase(string itemId, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        string url = $"{supabaseUrl}/rest/v1/scene_objects?id=eq.{itemId}";
        Vector3 euler = rotation.eulerAngles;

        string jsonPayload = $@"{{
            ""pos_x"": {position.x},
            ""pos_y"": {position.y},
            ""pos_z"": {position.z},
            ""rot_x"": {euler.x},
            ""rot_y"": {euler.y},
            ""rot_z"": {euler.z},
            ""scale_x"": {scale.x},
            ""scale_y"": {scale.y},
            ""scale_z"": {scale.z}
        }}";

        using (UnityWebRequest www = new UnityWebRequest(url, "PATCH"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();

            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("apikey", supabaseAnonKey);
            www.SetRequestHeader("Authorization", $"Bearer {supabaseAnonKey}");
            www.SetRequestHeader("Prefer", "return=minimal");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log($"[Supabase Sync] Persisted item '{itemId}' to database.");
            }
            else
            {
                Debug.LogError($"[Supabase Sync Error] Failed to update '{itemId}': {www.error}");
            }
        }
    }
}