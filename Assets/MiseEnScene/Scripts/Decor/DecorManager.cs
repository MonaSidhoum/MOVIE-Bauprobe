using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Crée, modifie et supprime les éléments de décor sous le repère du plateau.
    ///
    /// Point d'entrée pour la sauvegarde et le futur multijoueur :
    ///  - les événements ObjectAdded / ObjectChanged / ObjectRemoved signalent les modifications
    ///    faites LOCALEMENT par le metteur en scène (à envoyer au réseau / à sauvegarder) ;
    ///  - ApplyState / Remove(id, notify: false) appliquent un état venu d'ailleurs
    ///    (fichier, réseau) SANS relancer ces événements, ce qui évite les boucles.
    /// </summary>
    public class DecorManager : MonoBehaviour
    {
        [SerializeField] DecorCatalog catalog;
        [SerializeField] StageFrame stage;
        [Tooltip("Matériau des primitives (URP/Lit).")]
        [SerializeField] Material defaultMaterial;
        [Tooltip("Matériau des contours de sélection (URP/Unlit).")]
        [SerializeField] Material lineMaterial;

        public event Action<DecorState> ObjectAdded;
        public event Action<DecorState> ObjectChanged;
        public event Action<string> ObjectRemoved;

        public DecorCatalog Catalog => catalog;
        public StageFrame Stage => stage;
        public IReadOnlyCollection<DecorObject> Objects => _objects.Values;

        readonly Dictionary<string, DecorObject> _objects = new Dictionary<string, DecorObject>();

        void Awake()
        {
            if (catalog == null) catalog = Resources.Load<DecorCatalog>("DecorCatalog");
            if (catalog == null)
            {
                Debug.LogError("[Décor] Aucun catalogue : lance le menu Bauprobe > Mettre à jour le catalogue de décors.");
                catalog = ScriptableObject.CreateInstance<DecorCatalog>();
            }
            if (stage == null) stage = FindFirstObjectByType<StageFrame>();
        }

        // ---------- Actions locales (déclenchent les événements) ----------

        public DecorObject Create(string catalogId, Vector3 stagePosition, Quaternion stageRotation)
        {
            var state = new DecorState
            {
                id = NewId(),
                catalogId = catalogId,
                position = stagePosition,
                rotation = stageRotation,
            };
            var obj = Spawn(state);
            ObjectAdded?.Invoke(obj.CaptureState());
            return obj;
        }

        public DecorObject Duplicate(DecorObject source, Vector3 stageOffset)
        {
            var state = source.CaptureState();
            state.id = NewId();
            state.position += stageOffset;
            var obj = Spawn(state);
            ObjectAdded?.Invoke(obj.CaptureState());
            return obj;
        }

        /// <summary>À appeler après avoir modifié la pose ou la couleur d'un objet.</summary>
        public void NotifyChanged(DecorObject obj)
        {
            if (obj != null) ObjectChanged?.Invoke(obj.CaptureState());
        }

        public void Delete(DecorObject obj)
        {
            if (obj != null) Remove(obj.Id, notify: true);
        }

        // ---------- Application d'états venus d'ailleurs (pas d'événement) ----------

        /// <summary>Crée l'objet s'il n'existe pas, sinon le met à jour.</summary>
        public DecorObject ApplyState(DecorState state)
        {
            if (string.IsNullOrEmpty(state.id)) return null;
            if (_objects.TryGetValue(state.id, out var existing) && existing != null)
            {
                existing.ApplyState(state);
                return existing;
            }
            return Spawn(state);
        }

        public void Remove(string id, bool notify)
        {
            if (!_objects.TryGetValue(id, out var obj)) return;
            _objects.Remove(id);
            if (obj != null) Destroy(obj.gameObject);
            if (notify) ObjectRemoved?.Invoke(id);
        }

        public void Clear(bool notify)
        {
            foreach (var id in new List<string>(_objects.Keys)) Remove(id, notify);
        }

        public DecorObject Get(string id) => _objects.TryGetValue(id, out var obj) ? obj : null;

        public List<DecorState> CaptureAll()
        {
            var list = new List<DecorState>(_objects.Count);
            foreach (var obj in _objects.Values)
                if (obj != null) list.Add(obj.CaptureState());
            return list;
        }

        // ---------- Construction ----------

        DecorObject Spawn(DecorState state)
        {
            var entry = catalog.Find(state.catalogId);
            if (entry == null)
                Debug.LogWarning($"[Décor] '{state.catalogId}' absent du catalogue : remplacé par un cube.");

            var root = new GameObject($"Décor - {entry?.displayName ?? state.catalogId}");
            root.transform.SetParent(stage != null ? stage.DecorRoot : transform, false);

            BuildModel(entry, root.transform);

            var obj = root.AddComponent<DecorObject>();
            obj.Init(state.id, state.catalogId, entry?.displayName ?? state.catalogId, lineMaterial);
            obj.ApplyState(state);
            _objects[state.id] = obj;
            return obj;
        }

        void BuildModel(DecorCatalog.Entry entry, Transform root)
        {
            if (entry != null && entry.prefab != null)
            {
                // Le pivot d'origine du prefab est conservé (utile pour les décors en plusieurs parties).
                Instantiate(entry.prefab, root, false);
                return;
            }

            var model = GameObject.CreatePrimitive(entry?.primitive ?? PrimitiveType.Cube);
            model.name = "Modèle";
            Destroy(model.GetComponent<Collider>());
            model.transform.SetParent(root, false);
            model.transform.localScale = entry?.primitiveScale ?? Vector3.one;
            // La base de la primitive est posée au sol (y = 0 de l'objet).
            var extentsY = model.GetComponent<MeshFilter>().sharedMesh.bounds.extents.y;
            model.transform.localPosition = new Vector3(0f, extentsY * model.transform.localScale.y, 0f);
            if (defaultMaterial != null) model.GetComponent<Renderer>().sharedMaterial = defaultMaterial;
        }

        static string NewId() => Guid.NewGuid().ToString("N");
    }
}
