using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

public class CubeSync : MonoBehaviour
{
    const string DB_URL = "https://bauprobe-immersive-default-rtdb.europe-west1.firebasedatabase.app/";

    [SerializeField] float speed = 3f;
    [SerializeField] float sendInterval = 0.05f;
    [SerializeField] float smoothing = 15f;

    DatabaseReference cubeRef;
    string myId;
    bool ready;
    bool hasRemote;
    Vector3 targetPos;
    Quaternion targetRot;
    float lastSend;

    void Start()
    {
        myId = SystemInfo.deviceUniqueIdentifier;
        targetPos = transform.position;
        targetRot = transform.rotation;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result != DependencyStatus.Available)
            {
                Debug.LogError("Firebase indisponible : " + task.Result);
                return;
            }
            var db = FirebaseDatabase.GetInstance(DB_URL);
            cubeRef = db.RootReference.Child("scene").Child("cube");
            cubeRef.ValueChanged += OnCubeChanged;
            ready = true;
            Debug.Log("CubeSync prêt");
        });
    }

    void OnDestroy()
    {
        if (cubeRef != null) cubeRef.ValueChanged -= OnCubeChanged;
    }

    void Update()
    {
        if (!ready) return;

        if (MoveLocal())
        {
            hasRemote = false;
            if (Time.time - lastSend > sendInterval) Send();
        }
        else if (hasRemote)
        {
            transform.position = Vector3.Lerp(transform.position, targetPos, smoothing * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, smoothing * Time.deltaTime);
        }
    }

    bool MoveLocal()
    {
        var k = Keyboard.current;
        if (k == null) return false;

        Vector3 dir = Vector3.zero;
        if (k.wKey.isPressed || k.upArrowKey.isPressed)    dir.z += 1;
        if (k.sKey.isPressed || k.downArrowKey.isPressed)  dir.z -= 1;
        if (k.dKey.isPressed || k.rightArrowKey.isPressed) dir.x += 1;
        if (k.aKey.isPressed || k.leftArrowKey.isPressed)  dir.x -= 1;

        if (dir == Vector3.zero) return false;
        transform.position += dir.normalized * speed * Time.deltaTime;
        return true;
    }

    void Send()
    {
        lastSend = Time.time;
        Vector3 p = transform.position;
        Vector3 e = transform.rotation.eulerAngles;
        cubeRef.SetValueAsync(new Dictionary<string, object> {
            {"x", p.x}, {"y", p.y}, {"z", p.z},
            {"rx", e.x}, {"ry", e.y}, {"rz", e.z},
            {"by", myId}
        });
    }

    void OnCubeChanged(object sender, ValueChangedEventArgs args)
    {
        if (args.DatabaseError != null)
        {
            Debug.LogError(args.DatabaseError.Message);
            return;
        }
        if (!args.Snapshot.Exists) return;

        var s = args.Snapshot;
        if ((string)s.Child("by").Value == myId) return;

        float F(string k) => System.Convert.ToSingle(s.Child(k).Value);
        targetPos = new Vector3(F("x"), F("y"), F("z"));
        targetRot = Quaternion.Euler(F("rx"), F("ry"), F("rz"));
        hasRemote = true;
    }
}