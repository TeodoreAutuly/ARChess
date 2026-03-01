using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Positionne le GameBoard sur le marker AR "chessboard" détecté par ARTrackedImageManager.
/// L'événement <see cref="OnBoardPlaced"/> est levé une seule fois lors du premier placement.
/// </summary>
[RequireComponent(typeof(ARTrackedImageManager))]
public class BoardPlacement : MonoBehaviour
{
    [Header("GameBoard")]
    [Tooltip("Prefab du plateau de jeu à instancier sur le marker.")]
    [SerializeField] private GameObject _gameBoardPrefab;

    [Tooltip("Échelle appliquée au GameBoard après instanciation.\n" +
             "Le prefab LowPolyConcrete a ses enfants à scale 100 (import FBX).\n" +
             "Valeur de départ recommandée : 0.01  →  board ≈ 0.44 m.")]
    [SerializeField] private float _boardScale = 0.01f;

    /// <summary>Nom de l'image de référence dans la ReferenceImageLibrary.</summary>
    private const string TargetImageName = "chessboard";

    /// <summary>Déclenché une seule fois lors du premier placement réussi du plateau.</summary>
    public event Action<GameObject> OnBoardPlaced;

    private ARTrackedImageManager _trackedImageManager;
    private GameObject _gameBoardInstance;

    /// <summary>Instance courante du GameBoard (null avant le premier tracking).</summary>
    public GameObject GameBoardInstance => _gameBoardInstance;

    // -------------------------------------------------------------------------
    // Cycle de vie
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _trackedImageManager = GetComponent<ARTrackedImageManager>();
    }

    private void OnEnable()
    {
        _trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
    }

    private void OnDisable()
    {
        _trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
    }

    // -------------------------------------------------------------------------
    // Gestion du tracking
    // -------------------------------------------------------------------------

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> eventArgs)
    {
        foreach (var image in eventArgs.added)
            HandleImage(image);

        foreach (var image in eventArgs.updated)
            HandleImage(image);

        foreach (var pair in eventArgs.removed)
            OnImageRemoved(pair.Value);
    }

    private void HandleImage(ARTrackedImage image)
    {
        if (image.referenceImage.name != TargetImageName) return;

        if (_gameBoardPrefab == null)
        {
            Debug.LogError("[BoardPlacement] _gameBoardPrefab n'est pas assigné dans l'Inspector !");
            return;
        }

        if (image.trackingState == TrackingState.Tracking)
        {
            // On ne conserve que le yaw de l'image pour que le plateau
            // reste toujours horizontal, quelle que soit l'inclinaison
            // détectée par ARCore.
            Quaternion flatRotation = Quaternion.Euler(
                0f,
                image.transform.rotation.eulerAngles.y,
                0f);

            if (_gameBoardInstance == null)
            {
                // Premier placement : instancier et notifier
                _gameBoardInstance = Instantiate(
                    _gameBoardPrefab,
                    image.transform.position,
                    flatRotation);
                _gameBoardInstance.transform.localScale = Vector3.one * _boardScale;

                OnBoardPlaced?.Invoke(_gameBoardInstance);
                Debug.Log($"[BoardPlacement] GameBoard placé — scale {_boardScale}.");
            }
            else
            {
                // Marker retrouvé : réactiver et recaler (scale inchangé)
                _gameBoardInstance.SetActive(true);
                _gameBoardInstance.transform.SetPositionAndRotation(
                    image.transform.position,
                    flatRotation);
            }
        }
        else
        {
            // Tracking perdu : masquer le plateau sans le détruire
            if (_gameBoardInstance != null)
                _gameBoardInstance.SetActive(false);
        }
    }

    private void OnImageRemoved(ARTrackedImage image)
    {
        if (image.referenceImage.name == TargetImageName && _gameBoardInstance != null)
            _gameBoardInstance.SetActive(false);
    }
}
