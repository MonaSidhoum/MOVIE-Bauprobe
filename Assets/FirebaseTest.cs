using UnityEngine;
using Firebase;
using Firebase.Database;
using Firebase.Extensions;

public class FirebaseTest : MonoBehaviour
{
    void Start()
    {
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                var db = FirebaseDatabase.GetInstance(
                    "https://bauprobe-immersive-default-rtdb.europe-west1.firebasedatabase.app/");
                db.RootReference.Child("test").SetValueAsync("Bonjour depuis Unity")
                    .ContinueWithOnMainThread(t =>
                    {
                        if (t.IsFaulted) Debug.LogError("Echec : " + t.Exception);
                        else Debug.Log("Ecriture reussie");
                    });
            }
            else
            {
                Debug.LogError("Firebase indisponible : " + task.Result);
            }
        });
    }
}