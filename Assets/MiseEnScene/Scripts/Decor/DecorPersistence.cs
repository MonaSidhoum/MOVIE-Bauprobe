using System;
using System.IO;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Sauvegarde le décor dans un fichier JSON sur le casque
    /// (Application.persistentDataPath/miseenscene.json) et le recharge au lancement.
    /// Les poses sont dans le repère du plateau : après un recalage, le décor revient
    /// au même endroit par rapport aux marques au sol.
    /// </summary>
    public class DecorPersistence : MonoBehaviour
    {
        [SerializeField] DecorManager manager;
        [SerializeField] string fileName = "miseenscene.json";
        [Tooltip("Délai avant d'écrire après la dernière modification (secondes).")]
        [SerializeField] float saveDelay = 0.5f;

        public string FilePath => Path.Combine(Application.persistentDataPath, fileName);

        bool _dirty;
        float _dirtySince;

        void Awake()
        {
            if (manager == null) manager = FindFirstObjectByType<DecorManager>();
        }

        void OnEnable()
        {
            manager.ObjectAdded += OnStateChanged;
            manager.ObjectChanged += OnStateChanged;
            manager.ObjectRemoved += OnRemoved;
        }

        void OnDisable()
        {
            manager.ObjectAdded -= OnStateChanged;
            manager.ObjectChanged -= OnStateChanged;
            manager.ObjectRemoved -= OnRemoved;
        }

        void Start() => Load();

        void Update()
        {
            if (_dirty && Time.unscaledTime - _dirtySince > saveDelay) Save();
        }

        // Le casque met l'application en pause quand on l'enlève : on sauvegarde tout de suite.
        void OnApplicationPause(bool paused)
        {
            if (paused && _dirty) Save();
        }

        void OnApplicationQuit()
        {
            if (_dirty) Save();
        }

        void OnStateChanged(DecorState _) => MarkDirty();
        void OnRemoved(string _) => MarkDirty();

        void MarkDirty()
        {
            _dirty = true;
            _dirtySince = Time.unscaledTime;
        }

        public void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                var data = JsonUtility.FromJson<DecorSaveFile>(File.ReadAllText(FilePath));
                if (data?.objects == null) return;
                foreach (var state in data.objects) manager.ApplyState(state);
                Debug.Log($"[Décor] {data.objects.Count} élément(s) chargé(s) depuis {FilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Décor] Lecture de {FilePath} impossible : {e.Message}");
            }
        }

        public void Save()
        {
            _dirty = false;
            try
            {
                var json = JsonUtility.ToJson(new DecorSaveFile { objects = manager.CaptureAll() }, true);
                // Écriture dans un fichier temporaire puis remplacement, pour ne jamais laisser un fichier à moitié écrit.
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Décor] Sauvegarde impossible : {e.Message}");
            }
        }

        [ContextMenu("Effacer tout le décor sauvegardé")]
        public void ClearSave()
        {
            manager.Clear(notify: false);
            if (File.Exists(FilePath)) File.Delete(FilePath);
            _dirty = false;
        }
    }
}
