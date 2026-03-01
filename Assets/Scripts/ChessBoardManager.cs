using UnityEngine;

/// <summary>
/// Gère la grille logique 8×8, instancie toutes les pièces à leurs positions de départ
/// et fournit les conversions entre coordonnées logiques et locales du GameBoard.
/// </summary>
public class ChessBoardManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Paramètres sérialisés
    // -------------------------------------------------------------------------

    [Header("Paramètres de grille")]
    [Tooltip("Si activé, _cellSize et _pieceYOffset sont calculés automatiquement\n"
           + "depuis les bornes du mesh du plateau (recommandé).")]
    [SerializeField] private bool _autoComputeFromBounds = true;

    [Tooltip("Taille d'une case en unités locales du GameBoard.\n"
           + "Ignoré si _autoComputeFromBounds est actif.")]
    [SerializeField] private float _cellSize = 0.055f;

    [Tooltip("Décalage vertical des pièces. Ignoré si _autoComputeFromBounds est actif.")]
    [SerializeField] private float _pieceYOffset = 0f;

    [Header("Prefabs — Blancs")]
    [SerializeField] private GameObject _whitePawn;
    [SerializeField] private GameObject _whiteRook;
    [SerializeField] private GameObject _whiteKnight;
    [SerializeField] private GameObject _whiteBishop;
    [SerializeField] private GameObject _whiteQueen;
    [SerializeField] private GameObject _whiteKing;

    [Header("Prefabs — Noirs")]
    [SerializeField] private GameObject _blackPawn;
    [SerializeField] private GameObject _blackRook;
    [SerializeField] private GameObject _blackKnight;
    [SerializeField] private GameObject _blackBishop;
    [SerializeField] private GameObject _blackQueen;
    [SerializeField] private GameObject _blackKing;

    // -------------------------------------------------------------------------
    // État interne
    // -------------------------------------------------------------------------

    private Transform      _boardRoot;
    private ChessPiece[,]  _grid = new ChessPiece[8, 8];

    /// <summary>Grille logique [col, row]. Peut être null (case vide ou pièce capturée).</summary>
    public ChessPiece[,] Grid => _grid;

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Appelé par <see cref="ChessGameManager"/> une fois le GameBoard instancié.
    /// Crée toutes les pièces dans le repère local du plateau.
    /// </summary>
    public void Initialize(Transform boardRoot)
    {
        _boardRoot = boardRoot;
        _grid      = new ChessPiece[8, 8];

        if (_autoComputeFromBounds)
            AutoComputeCellSize();

        EnsureBoardCollider();
        SpawnAllPieces();
    }

    // -------------------------------------------------------------------------
    // Auto-calibration depuis les bornes du mesh
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calcule <see cref="_cellSize"/> et <see cref="_pieceYOffset"/> à partir
    /// des bornes mondiales du mesh du plateau (avant l'apparition des pièces).
    /// </summary>
    private void AutoComputeCellSize()
    {
        Renderer[] renderers = _boardRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning("[ChessBoardManager] Aucun Renderer trouvé sur le plateau — _cellSize non recalculé.");
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
            bounds.Encapsulate(r.bounds);

        // Largeur du plateau en espace monde (plus petite dimension horizontale)
        float worldWidth = Mathf.Min(bounds.size.x, bounds.size.z);
        if (worldWidth <= Mathf.Epsilon) return;

        // Conversion en espace local du boardRoot (suppose une échelle uniforme)
        float lossyScale = _boardRoot.lossyScale.x;
        if (lossyScale <= Mathf.Epsilon) return;

        _cellSize     = worldWidth / 8f / lossyScale;
        // Surface supérieure du plateau en Y local
        _pieceYOffset = _boardRoot.InverseTransformPoint(
                            new Vector3(_boardRoot.position.x,
                                        bounds.max.y,
                                        _boardRoot.position.z)).y;

        Debug.Log($"[ChessBoardManager] Auto-calibration — cellSize={_cellSize:F4} | pieceYOffset={_pieceYOffset:F4}");
    }

    // -------------------------------------------------------------------------
    // Collider de surface
    // -------------------------------------------------------------------------

    /// <summary>
    /// Garantit qu'un collider plat existe sur le plateau pour que
    /// <see cref="ChessGameManager"/> puisse détecter un tap sur une case vide.
    /// </summary>
    private void EnsureBoardCollider()
    {
        if (_boardRoot.GetComponentInChildren<Collider>() != null) return;

        float side = _cellSize * 8f;
        var bc     = _boardRoot.gameObject.AddComponent<BoxCollider>();
        bc.size    = new Vector3(side, side * 0.05f, side);
        bc.center  = new Vector3(0f, _pieceYOffset, 0f);
        Debug.Log("[ChessBoardManager] BoxCollider de surface ajouté automatiquement.");
    }

    // -------------------------------------------------------------------------
    // Coordonnées
    // -------------------------------------------------------------------------

    /// <summary>
    /// Retourne la position locale par rapport au GameBoard pour la case (col, row).
    /// L'origine est le centre du plateau ; col et row vont de 0 (blanc, gauche/bas) à 7.
    /// </summary>
    public Vector3 GetLocalPosition(int col, int row)
    {
        float x = (col - 3.5f) * _cellSize;
        float z = (row - 3.5f) * _cellSize;
        return new Vector3(x, _pieceYOffset, z);
    }

    /// <summary>
    /// Convertit un point monde en case (col, row).
    /// Retourne <c>false</c> si le point est hors des limites du plateau.
    /// </summary>
    public bool TryGetBoardCell(Vector3 worldPoint, out int col, out int row)
    {
        Vector3 local = _boardRoot.InverseTransformPoint(worldPoint);
        col = Mathf.RoundToInt(local.x / _cellSize + 3.5f);
        row = Mathf.RoundToInt(local.z / _cellSize + 3.5f);
        return IsInBounds(col, row);
    }

    /// <summary>Vérifie qu'une case est dans les limites de l'échiquier.</summary>
    public static bool IsInBounds(int col, int row) =>
        col >= 0 && col < 8 && row >= 0 && row < 8;

    // -------------------------------------------------------------------------
    // Accès et manipulation de la grille
    // -------------------------------------------------------------------------

    /// <summary>Retourne la pièce en (col, row) ou null si la case est vide / hors limites.</summary>
    public ChessPiece GetPieceAt(int col, int row) =>
        IsInBounds(col, row) ? _grid[col, row] : null;

    /// <summary>
    /// Déplace la pièce de (fromCol, fromRow) vers (toCol, toRow),
    /// en capturant la pièce adverse éventuelle.
    /// </summary>
    public void MovePiece(int fromCol, int fromRow, int toCol, int toRow)
    {
        ChessPiece piece = _grid[fromCol, fromRow];
        if (piece == null) return;

        // Capture éventuelle
        ChessPiece target = _grid[toCol, toRow];
        if (target != null) target.Capture();

        _grid[fromCol, fromRow] = null;
        _grid[toCol, toRow]     = piece;
        piece.MoveTo(toCol, toRow);
    }

    // -------------------------------------------------------------------------
    // Promotion
    // -------------------------------------------------------------------------

    /// <summary>
    /// Remplace le pion en (col, row) par une dame de la même couleur.
    /// Appelé par <see cref="ChessGameManager"/> après qu'un pion
    /// atteint la dernière rangée.
    /// </summary>
    public void PromotePawn(int col, int row)
    {
        ChessPiece pawn = _grid[col, row];
        if (pawn == null || pawn.Type != PieceType.Pawn) return;

        PieceColor color  = pawn.Color;
        GameObject prefab = color == PieceColor.White ? _whiteQueen : _blackQueen;

        Destroy(pawn.gameObject);
        _grid[col, row] = null;

        Spawn(prefab, PieceType.Queen, color, col, row);
        Debug.Log($"[ChessBoardManager] Promotion \u2192 {color} Dame en ({col},{row}).");
    }

    // -------------------------------------------------------------------------
    // Réinitialisation
    // -------------------------------------------------------------------------

    /// <summary>Détruit toutes les pièces et réinitialise l'échiquier.</summary>
    public void ResetBoard()
    {
        foreach (ChessPiece p in _grid)
        {
            if (p != null) Destroy(p.gameObject);
        }
        _grid = new ChessPiece[8, 8];
        SpawnAllPieces();
    }

    // -------------------------------------------------------------------------
    // Spawn
    // -------------------------------------------------------------------------

    private void SpawnAllPieces()
    {
        // ── Blancs (rangées 0 et 1) ──────────────────────────────────────────
        Spawn(_whiteRook,   PieceType.Rook,   PieceColor.White, 0, 0);
        Spawn(_whiteKnight, PieceType.Knight, PieceColor.White, 1, 0);
        Spawn(_whiteBishop, PieceType.Bishop, PieceColor.White, 2, 0);
        Spawn(_whiteQueen,  PieceType.Queen,  PieceColor.White, 3, 0);
        Spawn(_whiteKing,   PieceType.King,   PieceColor.White, 4, 0);
        Spawn(_whiteBishop, PieceType.Bishop, PieceColor.White, 5, 0);
        Spawn(_whiteKnight, PieceType.Knight, PieceColor.White, 6, 0);
        Spawn(_whiteRook,   PieceType.Rook,   PieceColor.White, 7, 0);
        for (int c = 0; c < 8; c++)
            Spawn(_whitePawn, PieceType.Pawn, PieceColor.White, c, 1);

        // ── Noirs (rangées 7 et 6) ───────────────────────────────────────────
        Spawn(_blackRook,   PieceType.Rook,   PieceColor.Black, 0, 7);
        Spawn(_blackKnight, PieceType.Knight, PieceColor.Black, 1, 7);
        Spawn(_blackBishop, PieceType.Bishop, PieceColor.Black, 2, 7);
        Spawn(_blackQueen,  PieceType.Queen,  PieceColor.Black, 3, 7);
        Spawn(_blackKing,   PieceType.King,   PieceColor.Black, 4, 7);
        Spawn(_blackBishop, PieceType.Bishop, PieceColor.Black, 5, 7);
        Spawn(_blackKnight, PieceType.Knight, PieceColor.Black, 6, 7);
        Spawn(_blackRook,   PieceType.Rook,   PieceColor.Black, 7, 7);
        for (int c = 0; c < 8; c++)
            Spawn(_blackPawn, PieceType.Pawn, PieceColor.Black, c, 6);
    }

    private void Spawn(GameObject prefab, PieceType type, PieceColor color, int col, int row)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"[ChessBoardManager] Prefab manquant : {color} {type}");
            return;
        }

        // Les noirs font face aux blancs (rotation 180° sur Y)
        Quaternion localRot = color == PieceColor.Black
            ? Quaternion.Euler(0f, 180f, 0f)
            : Quaternion.identity;

        GameObject go = Instantiate(prefab, _boardRoot);
        go.transform.localPosition = GetLocalPosition(col, row);
        go.transform.localRotation = localRot;
        go.name = $"{color}_{type}_{col}{row}";

        // Garantit la présence d'un collider pour Physics.Raycast
        if (go.GetComponentInChildren<Collider>() == null)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.size   = new Vector3(_cellSize * 0.8f, _cellSize * 1.5f, _cellSize * 0.8f);
            bc.center = new Vector3(0f, _cellSize * 0.75f, 0f);
        }

        ChessPiece piece = go.AddComponent<ChessPiece>();
        piece.Initialize(type, color, col, row, this);
        _grid[col, row] = piece;
    }
}
