using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// Orchestre la partie d'échecs en AR :
/// - attend le placement du plateau via <see cref="BoardPlacement.OnBoardPlaced"/> ;
/// - gère les tours (Blancs / Noirs) ;
/// - capte les touches via Enhanced Touch et sélectionne les pièces par Physics.Raycast ;
/// - valide les mouvements selon les règles standard (hors roque et en passant).
/// </summary>
public class ChessGameManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Références sérialisées
    // -------------------------------------------------------------------------

    [Header("Références")]
    [SerializeField] private BoardPlacement    _boardPlacement;
    [SerializeField] private ChessBoardManager _boardManager;
    [SerializeField] private Camera            _arCamera;

    // -------------------------------------------------------------------------
    // État de jeu
    // -------------------------------------------------------------------------

    /// <summary>Couleur du joueur dont c'est actuellement le tour.</summary>
    public PieceColor CurrentTurn { get; private set; } = PieceColor.White;

    /// <summary>Vrai si le jeu est terminé (roi capturé).</summary>
    public bool IsGameOver { get; private set; }

    private ChessPiece _selectedPiece;
    private bool       _initialized;

    // -------------------------------------------------------------------------
    // Cycle de vie
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (_arCamera == null)
            _arCamera = Camera.main;
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        _boardPlacement.OnBoardPlaced += HandleBoardPlaced;

        // Si le plateau était déjà placé avant l'activation de ce composant
        if (!_initialized && _boardPlacement.GameBoardInstance != null)
            HandleBoardPlaced(_boardPlacement.GameBoardInstance);
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        _boardPlacement.OnBoardPlaced -= HandleBoardPlaced;
    }

    // -------------------------------------------------------------------------
    // Initialisation du plateau
    // -------------------------------------------------------------------------

    private void HandleBoardPlaced(GameObject boardInstance)
    {
        if (_initialized) return;
        _initialized = true;
        _boardManager.Initialize(boardInstance.transform);
        Debug.Log("[ChessGameManager] Échiquier prêt — tour : Blancs");
    }

    // -------------------------------------------------------------------------
    // Boucle principale
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!_initialized || IsGameOver) return;

        var touches = Touch.activeTouches;
        if (touches.Count == 0) return;

        var touch = touches[0];
        if (touch.phase != UnityEngine.InputSystem.TouchPhase.Began) return;

        Ray ray = _arCamera.ScreenPointToRay(touch.screenPosition);
        HandleRaycast(ray);
    }

    // -------------------------------------------------------------------------
    // Traitement du tap
    // -------------------------------------------------------------------------

    private void HandleRaycast(Ray ray)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return;

        ChessPiece tapped = hit.collider.GetComponentInParent<ChessPiece>();

        if (_selectedPiece == null)
        {
            // ── Phase de sélection ────────────────────────────────────────────
            if (tapped != null && tapped.Color == CurrentTurn && !tapped.IsCaptured)
                Select(tapped);
        }
        else
        {
            // ── Phase de déplacement ──────────────────────────────────────────

            // Tap sur une autre pièce alliée → changer la sélection
            if (tapped != null && tapped.Color == CurrentTurn && tapped != _selectedPiece)
            {
                Deselect();
                Select(tapped);
                return;
            }

            // Déterminer la case cible
            int toCol, toRow;
            if (tapped != null)
            {
                toCol = tapped.Col;
                toRow = tapped.Row;
            }
            else if (!_boardManager.TryGetBoardCell(hit.point, out toCol, out toRow))
            {
                Deselect();
                return;
            }

            if (IsValidMove(_selectedPiece, toCol, toRow))
                ExecuteMove(_selectedPiece, toCol, toRow);
            else
                Deselect();
        }
    }

    // -------------------------------------------------------------------------
    // Sélection
    // -------------------------------------------------------------------------

    private void Select(ChessPiece piece)
    {
        _selectedPiece = piece;
        piece.SetSelected(true);
    }

    private void Deselect()
    {
        _selectedPiece?.SetSelected(false);
        _selectedPiece = null;
    }

    // -------------------------------------------------------------------------
    // Exécution du mouvement
    // -------------------------------------------------------------------------

    private void ExecuteMove(ChessPiece piece, int toCol, int toRow)
    {
        // Capture du roi → fin de partie
        ChessPiece target = _boardManager.GetPieceAt(toCol, toRow);
        if (target != null && target.Type == PieceType.King)
        {
            _boardManager.MovePiece(piece.Col, piece.Row, toCol, toRow);
            Deselect();
            IsGameOver = true;
            Debug.Log($"[ChessGameManager] ÉCHEC ET MAT — {CurrentTurn} gagne !");
            return;
        }

        _boardManager.MovePiece(piece.Col, piece.Row, toCol, toRow);

        // Promotion du pion
        if (piece.Type == PieceType.Pawn)
        {
            int promRow = piece.Color == PieceColor.White ? 7 : 0;
            if (toRow == promRow)
                _boardManager.PromotePawn(toCol, toRow);
        }

        Deselect();
        SwitchTurn();
    }

    private void SwitchTurn()
    {
        CurrentTurn = CurrentTurn == PieceColor.White ? PieceColor.Black : PieceColor.White;
        Debug.Log($"[ChessGameManager] Tour : {CurrentTurn}");
    }

    // -------------------------------------------------------------------------
    // Réinitialisation
    // -------------------------------------------------------------------------

    /// <summary>Remet l'échiquier et la logique de jeu à zéro.</summary>
    public void ResetGame()
    {
        if (!_initialized) return;   // plateau pas encore placé
        Deselect();
        IsGameOver  = false;
        CurrentTurn = PieceColor.White;
        _boardManager.ResetBoard();
        Debug.Log("[ChessGameManager] Nouvelle partie.");
    }

    // =========================================================================
    // Validation des mouvements
    // =========================================================================

    private bool IsValidMove(ChessPiece piece, int toCol, int toRow)
    {
        if (!ChessBoardManager.IsInBounds(toCol, toRow)) return false;

        // Interdiction de capturer une pièce alliée
        ChessPiece target = _boardManager.GetPieceAt(toCol, toRow);
        if (target != null && target.Color == piece.Color) return false;

        // Doit au moins bouger
        if (toCol == piece.Col && toRow == piece.Row) return false;

        bool geometryOk = piece.Type switch
        {
            PieceType.Pawn   => IsValidPawnMove(piece, toCol, toRow),
            PieceType.Rook   => IsValidRookMove(piece, toCol, toRow),
            PieceType.Knight => IsValidKnightMove(piece, toCol, toRow),
            PieceType.Bishop => IsValidBishopMove(piece, toCol, toRow),
            PieceType.Queen  => IsValidRookMove(piece, toCol, toRow)
                             || IsValidBishopMove(piece, toCol, toRow),
            PieceType.King   => IsValidKingMove(piece, toCol, toRow),
            _                => false
        };

        if (!geometryOk) return false;

        // Interdit tout mouvement qui laisse son propre roi en échec
        return !LeavesKingInCheck(piece, toCol, toRow);
    }

    // ── Pion ──────────────────────────────────────────────────────────────────

    private bool IsValidPawnMove(ChessPiece pawn, int toCol, int toRow)
    {
        int dir      = pawn.Color == PieceColor.White ? 1 : -1;
        int startRow = pawn.Color == PieceColor.White ? 1 : 6;

        // Avance d'une case (case cible vide)
        if (toCol == pawn.Col && toRow == pawn.Row + dir
            && _boardManager.GetPieceAt(toCol, toRow) == null)
            return true;

        // Avance de deux cases depuis la position initiale
        if (toCol == pawn.Col && pawn.Row == startRow && toRow == pawn.Row + 2 * dir
            && _boardManager.GetPieceAt(pawn.Col, pawn.Row + dir) == null
            && _boardManager.GetPieceAt(toCol, toRow) == null)
            return true;

        // Prise diagonale
        if (Mathf.Abs(toCol - pawn.Col) == 1 && toRow == pawn.Row + dir)
        {
            ChessPiece target = _boardManager.GetPieceAt(toCol, toRow);
            if (target != null && target.Color != pawn.Color) return true;
        }

        return false;
    }

    // ── Tour ──────────────────────────────────────────────────────────────────

    private bool IsValidRookMove(ChessPiece rook, int toCol, int toRow)
    {
        if (rook.Col != toCol && rook.Row != toRow) return false;
        return !IsPathBlocked(rook.Col, rook.Row, toCol, toRow);
    }

    // ── Cavalier ──────────────────────────────────────────────────────────────

    private bool IsValidKnightMove(ChessPiece knight, int toCol, int toRow)
    {
        int dc = Mathf.Abs(toCol - knight.Col);
        int dr = Mathf.Abs(toRow - knight.Row);
        return (dc == 2 && dr == 1) || (dc == 1 && dr == 2);
    }

    // ── Fou ───────────────────────────────────────────────────────────────────

    private bool IsValidBishopMove(ChessPiece bishop, int toCol, int toRow)
    {
        if (Mathf.Abs(toCol - bishop.Col) != Mathf.Abs(toRow - bishop.Row)) return false;
        return !IsPathBlocked(bishop.Col, bishop.Row, toCol, toRow);
    }

    // ── Roi ───────────────────────────────────────────────────────────────────

    private bool IsValidKingMove(ChessPiece king, int toCol, int toRow)
    {
        return Mathf.Abs(toCol - king.Col) <= 1
            && Mathf.Abs(toRow - king.Row) <= 1;
    }

    // ── Chemin dégagé (Tour / Fou / Dame) ─────────────────────────────────────

    /// <summary>
    /// Retourne <c>true</c> si une pièce occupe une case intermédiaire
    /// sur le trajet en ligne droite ou diagonale de (fromCol,fromRow) vers (toCol,toRow).
    /// </summary>
    private bool IsPathBlocked(int fromCol, int fromRow, int toCol, int toRow)
    {
        int dc  = (int)Mathf.Sign(toCol - fromCol);
        int dr  = (int)Mathf.Sign(toRow - fromRow);
        int col = fromCol + dc;
        int row = fromRow + dr;

        while (col != toCol || row != toRow)
        {
            if (_boardManager.GetPieceAt(col, row) != null) return true;
            col += dc;
            row += dr;
        }
        return false;
    }

    // =========================================================================
    // Vérification d'échec
    // =========================================================================

    /// <summary>
    /// Retourne <c>true</c> si jouer <paramref name="piece"/> en (toCol,toRow)
    /// laisse — ou met — le roi du joueur courant en échec.
    /// Simule le mouvement sur la grille logique puis l'annule.
    /// </summary>
    private bool LeavesKingInCheck(ChessPiece piece, int toCol, int toRow)
    {
        var grid     = _boardManager.Grid;
        int fromCol  = piece.Col;
        int fromRow  = piece.Row;
        ChessPiece captured = grid[toCol, toRow];

        // Simuler
        grid[fromCol, fromRow] = null;
        grid[toCol,   toRow]   = piece;
        piece.SimulateMoveTo(toCol, toRow);

        bool inCheck = IsKingInCheck(piece.Color);

        // Annuler
        piece.SimulateMoveTo(fromCol, fromRow);
        grid[fromCol, fromRow] = piece;
        grid[toCol,   toRow]   = captured;

        return inCheck;
    }

    /// <summary>
    /// Retourne <c>true</c> si le roi de <paramref name="color"/> est actuellement en échec.
    /// </summary>
    private bool IsKingInCheck(PieceColor color)
    {
        // Localiser le roi
        ChessPiece king = null;
        for (int c = 0; c < 8 && king == null; c++)
            for (int r = 0; r < 8 && king == null; r++)
            {
                var p = _boardManager.GetPieceAt(c, r);
                if (p != null && p.Type == PieceType.King && p.Color == color)
                    king = p;
            }
        if (king == null) return false;

        // Vérifier si une pièce ennemie peut atteindre le roi
        PieceColor enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        for (int c = 0; c < 8; c++)
            for (int r = 0; r < 8; r++)
            {
                var p = _boardManager.GetPieceAt(c, r);
                if (p != null && p.Color == enemy && IsValidMoveRaw(p, king.Col, king.Row))
                    return true;
            }
        return false;
    }

    /// <summary>
    /// Variante de <see cref="IsValidMove"/> sans vérification d'échec,
    /// utilisée à l'intérieur de <see cref="IsKingInCheck"/> pour éviter la récursion infinie.
    /// </summary>
    private bool IsValidMoveRaw(ChessPiece piece, int toCol, int toRow)
    {
        if (!ChessBoardManager.IsInBounds(toCol, toRow)) return false;
        ChessPiece target = _boardManager.GetPieceAt(toCol, toRow);
        if (target != null && target.Color == piece.Color) return false;
        if (toCol == piece.Col && toRow == piece.Row)     return false;

        return piece.Type switch
        {
            PieceType.Pawn   => IsValidPawnMove(piece, toCol, toRow),
            PieceType.Rook   => IsValidRookMove(piece, toCol, toRow),
            PieceType.Knight => IsValidKnightMove(piece, toCol, toRow),
            PieceType.Bishop => IsValidBishopMove(piece, toCol, toRow),
            PieceType.Queen  => IsValidRookMove(piece, toCol, toRow)
                             || IsValidBishopMove(piece, toCol, toRow),
            PieceType.King   => IsValidKingMove(piece, toCol, toRow),
            _                => false
        };
    }
}
