using UnityEngine;

public enum PieceType  { King, Queen, Rook, Bishop, Knight, Pawn }
public enum PieceColor { White, Black }

/// <summary>
/// Composant porté par chaque pièce instanciée sur l'échiquier.
/// Gère sa position logique (col, row), son déplacement physique et le retour visuel de sélection.
/// </summary>
[DisallowMultipleComponent]
public class ChessPiece : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Propriétés publiques
    // -------------------------------------------------------------------------

    public PieceType  Type      { get; private set; }
    public PieceColor Color     { get; private set; }
    public int        Col       { get; private set; }
    public int        Row       { get; private set; }
    public bool       IsCaptured { get; private set; }

    // -------------------------------------------------------------------------
    // Privé
    // -------------------------------------------------------------------------

    private ChessBoardManager _boardManager;
    private Renderer[]        _renderers;
    private Color[]           _originalColors;

    [SerializeField]
    [Tooltip("Couleur de surbrillance lors de la sélection.")]
    private Color _selectedHighlight = new Color(1f, 0.85f, 0f); // or

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Appelé par <see cref="ChessBoardManager"/> après Instantiate.
    /// </summary>
    public void Initialize(PieceType type, PieceColor color,
                           int col, int row,
                           ChessBoardManager boardManager)
    {
        Type          = type;
        Color         = color;
        Col           = col;
        Row           = row;
        _boardManager = boardManager;

        // Mémorise les couleurs d'origine pour restaurer après sélection
        _renderers     = GetComponentsInChildren<Renderer>();
        _originalColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
            _originalColors[i] = _renderers[i].material.color;
    }

    // -------------------------------------------------------------------------
    // Déplacement
    // -------------------------------------------------------------------------

    /// <summary>
    /// Met à jour la position logique et déplace physiquement la pièce
    /// vers la case (col, row) en coordonnées locales du GameBoard.
    /// </summary>
    public void MoveTo(int col, int row)
    {
        Col = col;
        Row = row;
        transform.localPosition = _boardManager.GetLocalPosition(col, row);
    }

    /// <summary>
    /// Met à jour Col/Row sans déplacer le transform.
    /// Utilisé par <see cref="ChessGameManager"/> pour simuler un coup
    /// lors de la vérification d'échec, sans effet visuel.
    /// </summary>
    public void SimulateMoveTo(int col, int row)
    {
        Col = col;
        Row = row;
    }

    // -------------------------------------------------------------------------
    // Retour visuel
    // -------------------------------------------------------------------------

    /// <summary>
    /// Active ou désactive la surbrillance de sélection.
    /// </summary>
    public void SetSelected(bool selected)
    {
        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].material.color = selected ? _selectedHighlight : _originalColors[i];
    }

    // -------------------------------------------------------------------------
    // Capture
    // -------------------------------------------------------------------------

    /// <summary>
    /// Marque la pièce comme capturée et la désactive.
    /// </summary>
    public void Capture()
    {
        IsCaptured = true;
        gameObject.SetActive(false);
    }
}
