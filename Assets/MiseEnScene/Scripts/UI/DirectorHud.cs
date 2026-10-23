using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

namespace Bauprobe.MiseEnScene
{
    /// <summary>
    /// Petit panneau au-dessus de la manette gauche : état du calage, décor choisi,
    /// infos sur la sélection et rappel des commandes.
    /// </summary>
    public class DirectorHud : MonoBehaviour
    {
        [SerializeField] OVRCameraRig rig;
        [SerializeField] DirectorController controller;
        [SerializeField] StageFrame stage;
        [SerializeField] StageCalibrator calibrator;
        [SerializeField] DecorManager decor;
        [SerializeField] Material backgroundMaterial;

        [SerializeField] Vector2 panelSize = new Vector2(0.24f, 0.20f);
        [SerializeField] Vector3 offsetFromHand = new Vector3(0f, 0.16f, 0.02f);
        [SerializeField] bool showHelp = true;
        [SerializeField] float refreshInterval = 0.1f;

        Transform _panel;
        TextMeshPro _text;
        float _nextRefresh;
        readonly StringBuilder _sb = new StringBuilder(512);

        void Awake()
        {
            if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
            if (controller == null) controller = FindFirstObjectByType<DirectorController>();
            if (stage == null) stage = FindFirstObjectByType<StageFrame>();
            if (calibrator == null) calibrator = FindFirstObjectByType<StageCalibrator>();
            if (decor == null) decor = FindFirstObjectByType<DecorManager>();
            BuildPanel();
        }

        void BuildPanel()
        {
            _panel = new GameObject("HUD metteur en scène").transform;
            _panel.SetParent(transform, false);

            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Fond";
            Destroy(background.GetComponent<Collider>());
            background.transform.SetParent(_panel, false);
            background.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            background.transform.localScale = new Vector3(panelSize.x + 0.02f, panelSize.y + 0.02f, 1f);
            var r = background.GetComponent<Renderer>();
            r.sharedMaterial = backgroundMaterial != null ? backgroundMaterial : RenderUtil.FallbackUnlit();
            RenderUtil.SetColor(r, new Color(0.08f, 0.08f, 0.1f));

            var textGo = new GameObject("Texte");
            textGo.transform.SetParent(_panel, false);
            _text = textGo.AddComponent<TextMeshPro>();
            _text.rectTransform.sizeDelta = panelSize;
            _text.enableAutoSizing = true;
            _text.fontSizeMin = 0.05f;
            _text.fontSizeMax = 0.6f;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.color = Color.white;
            _text.richText = true;
        }

        void LateUpdate()
        {
            var hand = rig.leftControllerAnchor;
            var head = rig.centerEyeAnchor;
            _panel.position = hand.position + Vector3.up * offsetFromHand.y + hand.forward * offsetFromHand.z;
            var toPanel = _panel.position - head.position;
            if (toPanel.sqrMagnitude > 1e-6f) _panel.rotation = Quaternion.LookRotation(toPanel, Vector3.up);

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshInterval;
            _text.text = BuildText();
        }

        string BuildText()
        {
            _sb.Clear();

            if (calibrator != null && calibrator.IsActive)
            {
                _sb.Append("<b>").Append(calibrator.Instructions).Append("</b>");
                return _sb.ToString();
            }

            _sb.Append("<b>Plateau :</b> ").Append(stage.StatusText).Append('\n');

            if (decor.Catalog.Count > 0)
            {
                var entry = decor.Catalog.entries[controller.CatalogIndex];
                _sb.Append("<b>À ajouter (X) :</b> < ").Append(entry.displayName)
                   .Append(" >  (").Append(controller.CatalogIndex + 1).Append('/').Append(decor.Catalog.Count).Append(")\n");
            }

            var sel = controller.Selected;
            if (sel != null)
            {
                var p = sel.transform.localPosition;
                var size = sel.Size;
                _sb.Append("\n<b>").Append(sel.DisplayName).Append("</b>\n");
                _sb.Append("Taille : ").Append(F(size.x)).Append(" x ").Append(F(size.y)).Append(" x ").Append(F(size.z)).Append(" m\n");
                _sb.Append("Position : ").Append(F(Mathf.Abs(p.x))).Append(p.x >= 0 ? " m cour" : " m jardin")
                   .Append(", ").Append(F(p.z)).Append(" m vers le lointain");
                if (Mathf.Abs(p.y) > 0.005f) _sb.Append(", hauteur ").Append(F(p.y)).Append(" m");
                _sb.Append('\n');
                _sb.Append("Rotation : ").Append(Mathf.RoundToInt(sel.transform.localEulerAngles.y)).Append("°   ")
                   .Append("Couleur : ").Append(ColorPalette.NameOf(sel)).Append('\n');
                _sb.Append("Agrandir : ").Append(controller.CurrentScaleAxis.ToString().ToLowerInvariant()).Append('\n');
                if (controller.DeleteProgress > 0f)
                    _sb.Append("<color=#ff6060>Suppression… ").Append(Mathf.RoundToInt(controller.DeleteProgress * 100)).Append("%</color>\n");
            }
            else
            {
                _sb.Append("\nAucune sélection.\n");
            }

            if (showHelp)
            {
                _sb.Append("\n<size=70%><color=#bbbbbb>")
                   .Append("DROITE : gâchette sélectionner, grip déplacer, stick tourner / agrandir, ")
                   .Append("A couleur, B axe d'agrandissement, clic stick redresser\n")
                   .Append("GAUCHE : stick choisir / monter-descendre, X ajouter, gâchette dupliquer, ")
                   .Append("Y maintenu supprimer, clic stick grille, Menu caler le plateau")
                   .Append("</color></size>");
            }
            return _sb.ToString();
        }

        // Virgule décimale à la française, sans dépendre des cultures (parfois absentes sur Android).
        static string F(float meters) => meters.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
    }
}
