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
    [Tooltip("Ignore DB poll results for an item this long after we PATCHed it (avoids stale-read snap-back).")]
    public float localWriteSuppressSeconds = 5.0f;

    [Header("Fusion Network Prefab")]
    public NetworkObject networkedContainerPrefab;

    private readonly Dictionary<string, DynamicNetworkObject> _spawnedObjects = new Dictionary<string, DynamicNetworkObject>();
    private readonly Dictionary<string, float> _lastLocalWrite = new Dictionary<string, float>();
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

        var result = await _runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = "Theater_XR_Room",
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (!result.Ok)
        {
            Debug.LogError($"[Fusion] Failed to start game: {result.ErrorMessage}");
            return;
        }

        Debug.Log("[Fusion] Connected to Shared Mode session.");

        // Only the Master Client seeds the room from Supabase. Late joiners receive the
        // spawned objects (with ModelUrl/SupabaseId already in their first snapshot).
        if (_runner.IsSharedModeMasterClient)
            StartCoroutine(FetchSceneFromSupabase());

        StartPollingLoop();
    }

    private void StartPollingLoop()
    {
        if (_pollingCoroutine != null) StopCoroutine(_pollingCoroutine);
        _pollingCoroutine = StartCoroutine(PollSupabaseForExternalChanges());
    }

    private UnityWebRequest BuildGet(string url)
    {
        var www = UnityWebRequest.Get(url);
        www.SetRequestHeader("apikey", supabaseAnonKey);
        www.SetRequestHeader("Authorization", $"Bearer {supabaseAnonKey}");
        return www;
    }

    private IEnumerator FetchSceneFromSupabase()
    {
        using (UnityWebRequest www = BuildGet($"{supabaseUrl}/rest/v1/scene_objects?select=*"))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Supabase Fetch Error]: {www.error}");
                yield break;
            }

            SupabaseItem[] items = JsonHelper.FromJson<SupabaseItem>(www.downloadHandler.text);

            if (items != null && _runner != null && _runner.IsRunning)
            {
                foreach (var item in items)
                    SpawnDynamicModel(item);
            }
        }
    }

    private IEnumerator PollSupabaseForExternalChanges()
    {
        var wait = new WaitForSeconds(dbPollIntervalSeconds);

        while (true)
        {
            yield return wait;
            if (_runner == null || !_runner.IsRunning) yield break;

            using (UnityWebRequest www = BuildGet($"{supabaseUrl}/rest/v1/scene_objects?select=*"))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success) continue;

                SupabaseItem[] items = JsonHelper.FromJson<SupabaseItem>(www.downloadHandler.text);
                if (items == null) continue;

                foreach (var item in items)
                {
                    if (!_spawnedObjects.TryGetValue(item.id, out DynamicNetworkObject netObj) || netObj == null)
                        continue;
                    if (netObj.IsBeingGrabbed) continue;

                    if (_lastLocalWrite.TryGetValue(item.id, out float t) &&
                        Time.time - t < localWriteSuppressSeconds)
                        continue;

                    netObj.ApplyExternalDatabaseTransform(
                        new Vector3(item.pos_x, item.pos_y, item.pos_z),
                        Quaternion.Euler(item.rot_x, item.rot_y, item.rot_z),
                        new Vector3(item.scale_x, item.scale_y, item.scale_z));
                }
            }
        }
    }

    public void SpawnDynamicModel(SupabaseItem item)
    {
        if (_runner == null || !_runner.IsRunning) return;
        if (_spawnedObjects.ContainsKey(item.id)) return;

        Vector3 position = new Vector3(item.pos_x, item.pos_y, item.pos_z);
        Quaternion rotation = Quaternion.Euler(item.rot_x, item.rot_y, item.rot_z);
        Vector3 scale = new Vector3(item.scale_x, item.scale_y, item.scale_z);

        // onBeforeSpawned runs BEFORE the first state snapshot is sent, so remote and
        // late-joining clients see SupabaseId + ModelUrl the moment the object exists.
        NetworkObject spawned = _runner.Spawn(
            networkedContainerPrefab,
            position,
            rotation,
            null,
            (runner, obj) =>
            {
                obj.transform.localScale = scale; // replicated by NetworkTransform (Sync Scale)

                var dyn = obj.GetComponent<DynamicNetworkObject>();
                dyn.SupabaseId = item.id;
                dyn.ModelUrl = item.model_url;
            });

        var dynamicScript = spawned.GetComponent<DynamicNetworkObject>();
        if (dynamicScript != null)
            _spawnedObjects[item.id] = dynamicScript;
        // Model download starts from DynamicNetworkObject.Spawned()/Render() on every client.
    }

    public void UpdateObjectInSupabase(string itemId, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _lastLocalWrite[itemId] = Time.time;
        StartCoroutine(PatchObjectToSupabase(itemId, position, rotation, scale));
    }

    private IEnumerator PatchObjectToSupabase(string itemId, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        string url = $"{supabaseUrl}/rest/v1/scene_objects?id=eq.{itemId}";
        Vector3 e = rotation.eulerAngles;
        var ci = System.Globalization.CultureInfo.InvariantCulture; // avoid "1,5" decimal commas

        string json = "{" +
            $"\"pos_x\":{position.x.ToString(ci)},\"pos_y\":{position.y.ToString(ci)},\"pos_z\":{position.z.ToString(ci)}," +
            $"\"rot_x\":{e.x.ToString(ci)},\"rot_y\":{e.y.ToString(ci)},\"rot_z\":{e.z.ToString(ci)}," +
            $"\"scale_x\":{scale.x.ToString(ci)},\"scale_y\":{scale.y.ToString(ci)},\"scale_z\":{scale.z.ToString(ci)}" +
            "}";

        using (var www = new UnityWebRequest(url, "PATCH"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("apikey", supabaseAnonKey);
            www.SetRequestHeader("Authorization", $"Bearer {supabaseAnonKey}");
            www.SetRequestHeader("Prefer", "return=minimal");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                _lastLocalWrite[itemId] = Time.time; // restart suppression from write completion
                Debug.Log($"[Supabase Sync] Persisted '{itemId}'.");
            }
            else
            {
                Debug.LogError($"[Supabase Sync Error] '{itemId}': {www.error}");
            }
        }
    }

    public void RegisterExistingNetworkObject(string id, DynamicNetworkObject netObj)
    {
        if (!_spawnedObjects.ContainsKey(id))
            _spawnedObjects[id] = netObj;
    }

    public void UnregisterNetworkObject(string id, DynamicNetworkObject netObj)
    {
        if (_spawnedObjects.TryGetValue(id, out var existing) && existing == netObj)
            _spawnedObjects.Remove(id);
    }
}