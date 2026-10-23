using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Repère du plateau, comme sur un plan de scène :
    /// origine au 0 (centre du nez de scène, au sol), +X vers cour, +Z vers le lointain, +Y vers le haut.
    ///
    /// Tout le décor est enfant de <see cref="DecorRoot"/> et s'exprime donc dans ce repère.
    /// Le repère est tenu en place par une ancre spatiale Meta sauvegardée sur le casque :
    /// si on enlève et remet le casque, ou si on relance l'appli, le plateau (et donc le décor)
    /// reste au même endroit de la salle réelle.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class StageFrame : MonoBehaviour
    {
        public enum Status { NonCale, Recherche, Cale, AncrePerdue }

        const string AnchorUuidKey = "Bauprobe.MiseEnScene.StageAnchorUuid";

        [SerializeField] Transform decorRoot;

        public Transform DecorRoot => decorRoot;
        public Status CurrentStatus { get; private set; } = Status.NonCale;
        public string StatusText { get; private set; } = "";
        public event Action<Status> StatusChanged;

        OVRSpatialAnchor _anchor;
        int _calibrationVersion;

        void Awake()
        {
            if (decorRoot == null)
            {
                decorRoot = new GameObject("Décor").transform;
                decorRoot.SetParent(transform, false);
            }
        }

        async void Start() => await LoadSavedAnchorAsync();

        void LateUpdate()
        {
            if (_anchor == null || !_anchor.Created) return;

            if (_anchor.IsTracked)
            {
                transform.SetPositionAndRotation(_anchor.transform.position, _anchor.transform.rotation);
                if (CurrentStatus == Status.AncrePerdue) SetStatus(Status.Cale, "Ancre du plateau retrouvée.");
            }
            else if (CurrentStatus == Status.Cale)
            {
                SetStatus(Status.AncrePerdue, "Ancre momentanément perdue : regarde autour de toi, plateau éclairé.");
            }
        }

        /// <summary>
        /// Place le repère du plateau (appelé par <see cref="StageCalibrator"/>), puis crée
        /// et sauvegarde une nouvelle ancre à cet endroit. L'ancienne ancre est effacée.
        /// Le décor garde ses coordonnées plateau : il suit le nouveau calage.
        /// </summary>
        public async void Calibrate(Vector3 origin, Quaternion rotation)
        {
            var version = ++_calibrationVersion;
            ReleaseAnchor();
            transform.SetPositionAndRotation(origin, rotation);
            SetStatus(Status.Cale, "Plateau calé. Création de l'ancre…");

            var go = new GameObject("Ancre plateau");
            go.transform.SetPositionAndRotation(origin, rotation);
            var anchor = go.AddComponent<OVRSpatialAnchor>();
            while (anchor != null && anchor.PendingCreation) await Task.Yield();

            if (version != _calibrationVersion)
            {
                if (anchor != null) Destroy(go);
                return;
            }
            if (anchor == null || !anchor.Created)
            {
                if (go != null) Destroy(go);
                SetStatus(Status.Cale, "Plateau calé, mais ancre impossible à créer : le calage ne survivra pas à un retrait du casque.");
                return;
            }

            _anchor = anchor;
            var result = await anchor.SaveAnchorAsync();
            if (version != _calibrationVersion) return;
            if (result.Success)
            {
                PlayerPrefs.SetString(AnchorUuidKey, anchor.Uuid.ToString());
                PlayerPrefs.Save();
                SetStatus(Status.Cale, "Plateau calé, ancre sauvegardée.");
            }
            else
            {
                SetStatus(Status.Cale, $"Plateau calé, mais ancre non sauvegardée ({result.Status}) : il faudra recaler au prochain lancement.");
            }
        }

        async Task LoadSavedAnchorAsync()
        {
            if (!Guid.TryParse(PlayerPrefs.GetString(AnchorUuidKey, ""), out var uuid))
            {
                SetStatus(Status.NonCale, "Plateau non calé : bouton Menu (manette gauche) pour le caler.");
                return;
            }

            var version = _calibrationVersion;
            SetStatus(Status.Recherche, "Recherche de l'ancre du plateau…");
            try
            {
                var unbound = new List<OVRSpatialAnchor.UnboundAnchor>();
                var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[] { uuid }, unbound);
                if (version != _calibrationVersion) return; // recalé entre-temps
                if (!result.Success || result.Value == null || result.Value.Count == 0)
                {
                    SetStatus(Status.NonCale, "Ancre du plateau introuvable : recale le plateau (bouton Menu, manette gauche).");
                    return;
                }

                var unboundAnchor = result.Value[0];
                var localized = unboundAnchor.Localized || await unboundAnchor.LocalizeAsync();
                if (version != _calibrationVersion) return;
                if (!localized)
                {
                    SetStatus(Status.NonCale, "Ancre trouvée mais pas localisée (salle trop sombre ou trop changée ?) : recale le plateau.");
                    return;
                }

                var go = new GameObject("Ancre plateau");
                if (unboundAnchor.TryGetPose(out var pose)) go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                var anchor = go.AddComponent<OVRSpatialAnchor>();
                unboundAnchor.BindTo(anchor);
                _anchor = anchor;
                transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
                SetStatus(Status.Cale, "Plateau retrouvé grâce à l'ancre sauvegardée.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (version == _calibrationVersion)
                    SetStatus(Status.NonCale, "Erreur au chargement de l'ancre : recale le plateau.");
            }
        }

        void ReleaseAnchor()
        {
            PlayerPrefs.DeleteKey(AnchorUuidKey);
            if (_anchor == null) return;
            EraseAndDestroy(_anchor);
            _anchor = null;
        }

        static async void EraseAndDestroy(OVRSpatialAnchor anchor)
        {
            try { await anchor.EraseAnchorAsync(); }
            catch (Exception e) { Debug.LogWarning($"[Plateau] Effacement de l'ancre impossible : {e.Message}"); }
            if (anchor != null) Destroy(anchor.gameObject);
        }

        void SetStatus(Status status, string text)
        {
            CurrentStatus = status;
            StatusText = text;
            // Pendant la recherche de l'ancre, le décor est caché pour ne pas l'afficher au mauvais endroit.
            decorRoot.gameObject.SetActive(status != Status.Recherche);
            Debug.Log($"[Plateau] {text}");
            StatusChanged?.Invoke(status);
        }
    }
}
