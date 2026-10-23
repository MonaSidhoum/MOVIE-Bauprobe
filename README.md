# MOVIE Bauprobe : Mise en scène (Meta Quest 3)

Application **standalone Meta Quest 3** (Unity 6) pour le metteur en scène. Il voit le plateau réel en
passthrough, y ajoute des éléments de décor virtuels, les place, les tourne, les agrandit et les colore.
Le décor reste au même endroit de la salle quand on enlève et remet le casque, et d'une séance à l'autre.

Ce dépôt est un fork de [gouajwi/MOVIE-Bauprobe](https://github.com/gouajwi/MOVIE-Bauprobe) **sans la partie
multijoueur**. Il est organisé pour qu'on puisse la rebrancher facilement (voir [Ajouter le multijoueur](#ajouter-le-multijoueur)).

## Prérequis

- Unity **6000.6.3f1**, avec le module Android Build Support (OpenJDK, SDK et NDK)
- [Git LFS](https://git-lfs.com) (`git lfs install` **avant** de cloner : les scènes, prefabs et modèles 3D sont en LFS)
- Un Meta Quest 3 en mode développeur

## Premier lancement

1. Ouvrir le projet dans Unity. Les packages sont résolus automatiquement : Meta XR Core SDK 207, OpenXR et URP.
2. Menu **Bauprobe > 1. Créer la scène Mise en scène**. Cela :
   - crée `Assets/MiseEnScene/Scenes/MiseEnScene.unity` (OVRCameraRig + passthrough + logique de l'appli) ;
   - crée le catalogue `Assets/MiseEnScene/Resources/DecorCatalog.asset` (primitives et prefabs de `Assets/Prefab/ObjetsImportes`) ;
   - met cette scène comme seule scène du build.
3. Commiter la scène, le catalogue et les matériaux générés.
4. *File > Build Profiles > Meta Quest*, puis **Build And Run** avec le casque branché en USB.

Relancer le menu **Bauprobe > 1** régénère la scène à l'identique si elle a été cassée.
Pour ajouter un décor : déposer un prefab dans `Assets/Prefab/ObjetsImportes`, puis lancer
**Bauprobe > 2. Mettre à jour le catalogue de décors** (ou éditer l'asset `DecorCatalog` à la main).

## Utilisation dans le casque

### 1. Caler le plateau (une fois par lieu)

Avant la séance, coller deux croix au gaffeur : le **0** au centre du nez de scène, et un repère **sur l'axe
central, au lointain** (le plus loin possible).

Bouton **Menu** de la manette gauche, puis :
1. poser la petite boule jaune de la manette droite sur le 0 et appuyer sur la **gâchette droite** ;
2. poser la boule sur le repère du lointain et appuyer sur la **gâchette droite**.

Le repère du plateau est alors : **+X vers cour**, **+Z vers le lointain**, **+Y vers le haut**, en mètres
depuis le 0. Les axes s'affichent (rouge = cour, bleu = lointain). Une ancre spatiale Meta est créée et
sauvegardée sur le casque : au prochain lancement, le plateau est retrouvé tout seul.

En cas de dérive, il suffit de **recaler** : le décor est enregistré en coordonnées plateau, donc il se remet
à sa place par rapport aux marques au sol.

### 2. Commandes

| Manette droite (rayon) | |
|---|---|
| Gâchette | Sélectionner l'objet visé (dans le vide : désélectionner) |
| Grip maintenu | Déplacer l'objet visé (il glisse à hauteur constante) |
| Stick gauche / droite | Tourner |
| Stick haut / bas | Agrandir / réduire |
| Clic stick | Redresser la rotation (multiple de 15°) |
| A | Couleur suivante (d'origine, blanc, noir, gris, bois, rouge, bleu, vert, jaune) |
| B | Axe d'agrandissement : uniforme, largeur, hauteur, profondeur |

| Manette gauche | |
|---|---|
| Stick gauche / droite | Choisir le décor à ajouter |
| X | Ajouter le décor choisi là où vise le rayon droit |
| Stick haut / bas | Monter / descendre la sélection (décor suspendu) |
| Gâchette | Dupliquer la sélection |
| Y maintenu | Supprimer la sélection |
| Clic stick | Afficher / cacher la grille au mètre |
| Menu | Caler le plateau |

Le panneau au-dessus de la manette gauche affiche l'état du calage, le décor choisi et les infos de la
sélection : taille en mètres, position cour/jardin et lointain, rotation, couleur.

### Persistance

- **Retirer et remettre le casque** : le repère plateau suit l'ancre spatiale, donc le décor ne bouge pas.
- **Relancer l'appli** : l'ancre est rechargée (`PlayerPrefs`) et le décor est relu depuis
  `Application.persistentDataPath/miseenscene.json`. La sauvegarde est automatique, 0,5 s après chaque
  modification et dès que le casque se met en pause.

## Architecture

```
Assets/MiseEnScene/
├── Scripts/
│   ├── Plateau/
│   │   ├── StageFrame.cs        Repère du plateau + ancre spatiale (création, sauvegarde, rechargement)
│   │   ├── StageCalibrator.cs   Calage en 2 points avec la manette
│   │   └── StageGizmo.cs        Affichage du 0, des axes et de la grille
│   ├── Decor/
│   │   ├── DecorState.cs        État sérialisable d'un décor (id, catalogue, pose plateau, couleur)
│   │   ├── DecorCatalog.cs      Liste des décors disponibles (ScriptableObject)
│   │   ├── DecorObject.cs       Composant d'un décor posé (sélection, teinte, état)
│   │   ├── DecorManager.cs      Création / modification / suppression + événements
│   │   └── DecorPersistence.cs  Sauvegarde JSON locale
│   ├── Interaction/
│   │   ├── DirectorController.cs  Commandes manettes (OVRInput)
│   │   └── ColorPalette.cs
│   ├── UI/DirectorHud.cs        Panneau d'infos sur la manette gauche
│   └── RenderUtil.cs
└── Editor/MiseEnSceneSetup.cs   Menu « Bauprobe » (génération de la scène et du catalogue)

Assets/Prefab/
├── ObjetsImportes/   Décors du projet (ajoutés au catalogue)
├── Modeles/          Modèles sources de ces décors (.obj, .glb)
├── Theatre/          Maquette 3D du Châtelet (non utilisée par la scène pour l'instant)
├── Lights/, Materials/
```

Hiérarchie de la scène générée :

```
OVRCameraRig        (OVRManager passthrough activé + OVRPassthroughLayer en underlay)
Lumière
Plateau             StageFrame + StageGizmo : sa pose = celle de l'ancre spatiale
└── Décor           tous les DecorObject, en coordonnées plateau
MiseEnScene         DecorManager, DecorPersistence, StageCalibrator, DirectorController, DirectorHud
Ancre plateau       (créé à l'exécution) OVRSpatialAnchor
```

Choix techniques :
- **Une seule pile XR : Meta XR Core SDK** (OVRCameraRig, OVRInput, OVRSpatialAnchor), avec le loader OpenXR
  et la feature Meta. L'ancien rig XR Interaction Toolkit a été retiré pour éviter les deux rigs concurrents.
- **Toutes les poses sont relatives au plateau**, jamais en coordonnées monde : c'est ce qui rend le décor
  stable et partageable entre casques.

## Ajouter le multijoueur

Tout passe par `DecorManager` et `StageFrame` ; il n'y a rien à modifier dans les autres scripts.

1. **Envoyer les changements locaux** : s'abonner à `DecorManager.ObjectAdded`, `ObjectChanged` (au plus
   4 fois par seconde pendant une manipulation) et `ObjectRemoved`. Ces événements ne concernent que les
   actions du metteur en scène local, et transportent un `DecorState` déjà en coordonnées plateau.
2. **Appliquer les changements reçus** : `DecorManager.ApplyState(state)` (crée ou met à jour) et
   `DecorManager.Remove(id, notify: false)`. Ces appels ne relancent pas les événements, donc pas de boucle.
   `CaptureAll()` donne l'état complet, par exemple pour un joueur qui arrive.
3. **Repère commun** : chaque casque doit avoir le même `StageFrame`. Deux options :
   - chaque casque se cale sur les mêmes marques au gaffeur (le plus simple, et robuste dans une grande salle) ;
   - partager l'ancre : `OVRSpatialAnchor.ShareAsync(groupUuid)` côté hôte et
     `LoadUnboundSharedAnchorsAsync(groupUuid, …)` côté invités, en remplaçant le chargement local dans
     `StageFrame.LoadSavedAnchorAsync`. Il faut alors réactiver *Shared Spatial Anchor Support* et
     *Colocation Session Support* dans *Meta XR > Project Settings* (désactivés dans ce fork).
4. Avec Photon Fusion, le plus simple est un `NetworkObject` par décor, enfant du `Plateau`, qui synchronise
   `DecorState` en propriétés `[Networked]`, plutôt que `NetworkTransform` en coordonnées monde.

Le code multijoueur d'origine (Fusion, Supabase, Firebase) est dans l'historique git et sur le dépôt d'origine.

## Ce qui a été retiré par rapport au dépôt d'origine

- Multijoueur et cloud : Photon Fusion, Netcode for GameObjects, ParrelSync, Firebase, Supabase
  (`HybridSceneManager`, `DynamicNetworkObject`, `CubeSync`, …)
- Ancien système d'interaction XRI (rig « lucile », menus, modes Move / Scale / Delete) : remplacé par `DirectorController`
- Scènes d'essai, `_Recovery`, artefacts de build (`.utmp`), fichiers Plastic SCM, doublon `Assets/System`, solutions `.slnx`

## Tester la stabilité au Châtelet

1. Caler le plateau et afficher la grille (clic stick gauche).
2. Poser un cube sur une croix au gaffeur au lointain.
3. Marcher 10 à 15 minutes sur tout le plateau, changer l'éclairage, retirer et remettre le casque.
4. Mesurer au mètre ruban l'écart entre le cube et la croix (et entre la grille et d'autres marques).

Si ça dérive trop loin du 0, la piste suivante est de répartir plusieurs ancres sur le plateau et de
rattacher chaque décor à l'ancre la plus proche.
