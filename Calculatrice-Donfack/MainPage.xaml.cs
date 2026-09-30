using System.Globalization;

namespace Calculatrice_Donfack;

public partial class MainPage : ContentPage
{
    // Nombre maximal de chiffres que l'on peut saisir
    private const int ChiffresMax = 15;
    private const string MessageDivisionParZero = "Division par zéro impossible";
    private const string MessageTropGrand = "Résultat trop grand";

    // Texte du nombre en cours de saisie (le point sert de séparateur interne)
    private string _entree = "0";

    // Premier opérande et opérateur en attente
    private double? _operande;
    private string? _operateur;

    // Opération affichée au-dessus du résultat
    private string _expression = "";

    // Vrai quand le prochain chiffre doit démarrer un nouveau nombre
    private bool _nouvelleSaisie;

    // Vrai quand l'écran affiche un message d'erreur
    private bool _erreur;

    // Orientation actuelle (null tant qu'elle n'est pas connue)
    private bool? _paysage;

    // Vrai une fois la fenêtre de bureau configurée en portrait
    private bool _fenetreConfiguree;

    public MainPage()
    {
        InitializeComponent();
        MettreAJourAffichage();
    }

    // ------------------------------------------------------------------
    // Fenêtre de bureau en format portrait (Windows / Mac Catalyst)
    // ------------------------------------------------------------------
    protected override void OnAppearing()
    {
        base.OnAppearing();

#if WINDOWS || MACCATALYST
        if (_fenetreConfiguree || Window is not { } fenetre)
            return;
        _fenetreConfiguree = true;

        var ecran = DeviceDisplay.Current.MainDisplayInfo;
        double ecranL = ecran.Width / ecran.Density;
        double ecranH = ecran.Height / ecran.Density;

        double hauteur = Math.Min(800, ecranH - 100);
        double largeur = Math.Min(440, hauteur * 0.55);

        fenetre.MinimumWidth = 340;
        fenetre.MinimumHeight = 560;
        fenetre.Width = largeur;
        fenetre.Height = hauteur;
        fenetre.X = (ecranL - largeur) / 2;
        fenetre.Y = Math.Max(0, (ecranH - hauteur) / 2 - 20);
#endif
    }

    // ------------------------------------------------------------------
    // Adaptation de la disposition à l'orientation et à la taille de l'écran
    // ------------------------------------------------------------------
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0 || height <= 0)
            return;

        bool paysage = width > height;
        if (_paysage == paysage)
            return;
        _paysage = paysage;

        RacineGrid.RowDefinitions.Clear();
        RacineGrid.ColumnDefinitions.Clear();

        if (paysage)
        {
            // Paysage : écran à gauche, clavier à droite
            RacineGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            RacineGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            RacineGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));

            Grid.SetRow(AffichageBorder, 0);
            Grid.SetColumn(AffichageBorder, 0);
            Grid.SetRow(ClavierGrid, 0);
            Grid.SetColumn(ClavierGrid, 1);
        }
        else
        {
            // Portrait : écran en haut, clavier en bas
            RacineGrid.RowDefinitions.Add(new RowDefinition(new GridLength(2, GridUnitType.Star)));
            RacineGrid.RowDefinitions.Add(new RowDefinition(new GridLength(5, GridUnitType.Star)));
            RacineGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            Grid.SetRow(AffichageBorder, 0);
            Grid.SetColumn(AffichageBorder, 0);
            Grid.SetRow(ClavierGrid, 1);
            Grid.SetColumn(ClavierGrid, 0);
        }
    }

    // ------------------------------------------------------------------
    // Gestionnaires d'événements
    // ------------------------------------------------------------------
    private void OnChiffreClicked(object? sender, EventArgs e)
    {
        if (sender is not Button bouton)
            return;

        if (_erreur)
            Reinitialiser();

        DemarrerNouvelleSaisieSiNecessaire();

        if (_entree == "0")
            _entree = bouton.Text;
        else if (NombreDeChiffres() < ChiffresMax)
            _entree += bouton.Text;

        MettreAJourAffichage();
    }

    private void OnVirguleClicked(object? sender, EventArgs e)
    {
        if (_erreur)
            Reinitialiser();

        DemarrerNouvelleSaisieSiNecessaire();

        if (!_entree.Contains('.'))
            _entree += ".";

        MettreAJourAffichage();
    }

    private void OnOperateurClicked(object? sender, EventArgs e)
    {
        if (sender is not Button bouton)
            return;

        if (_erreur)
            Reinitialiser();

        string operateur = bouton.Text;
        double valeur = LireEntree();

        if (_operateur != null && _operande.HasValue && !_nouvelleSaisie)
        {
            // Enchaînement : 2 + 3 + ... calcule d'abord 2 + 3
            double resultat = Calculer(_operande.Value, _operateur, valeur);
            if (double.IsNaN(resultat) || double.IsInfinity(resultat))
            {
                string expression = $"{Formater(_operande.Value)} {_operateur} {Formater(valeur)}";
                AfficherErreur(resultat, expression);
                return;
            }

            _operande = resultat;
            _entree = Formater(resultat);
        }
        else if (_operateur == null)
        {
            // Premier opérateur, ou opérateur après un "="
            _operande = valeur;
        }
        // Sinon : l'utilisateur change simplement d'opérateur

        _operateur = operateur;
        _nouvelleSaisie = true;
        _expression = $"{Formater(_operande!.Value)} {operateur}";

        MettreAJourAffichage();
    }

    private void OnEgalClicked(object? sender, EventArgs e)
    {
        if (_erreur || _operateur == null || !_operande.HasValue)
            return;

        double a = _operande.Value;
        double b = LireEntree();
        string operateur = _operateur;
        string expression = $"{Formater(a)} {operateur} {Formater(b)}";

        double resultat = Calculer(a, operateur, b);
        if (double.IsNaN(resultat) || double.IsInfinity(resultat))
        {
            AfficherErreur(resultat, expression);
            return;
        }

        _expression = expression + " =";
        _entree = Formater(resultat);
        _operande = null;
        _operateur = null;
        _nouvelleSaisie = true;

        MettreAJourAffichage();
    }

    private void OnToutEffacerClicked(object? sender, EventArgs e)
    {
        Reinitialiser();
        MettreAJourAffichage();
    }

    private void OnRetourArriereClicked(object? sender, EventArgs e)
    {
        if (_erreur)
        {
            Reinitialiser();
            MettreAJourAffichage();
            return;
        }

        // On n'efface pas un résultat déjà calculé
        if (_nouvelleSaisie)
            return;

        _entree = _entree.Length <= 1 ? "0" : _entree[..^1];
        if (_entree == "-" || _entree == "-0")
            _entree = "0";

        MettreAJourAffichage();
    }

    private void OnChangementSigneClicked(object? sender, EventArgs e)
    {
        if (_erreur)
        {
            Reinitialiser();
            MettreAJourAffichage();
            return;
        }

        if (_entree == "0")
            return;

        _entree = _entree.StartsWith('-') ? _entree[1..] : "-" + _entree;

        // Le nombre affiché devient le second opérande
        if (_operateur != null)
            _nouvelleSaisie = false;

        MettreAJourAffichage();
    }

    private void OnPourcentageClicked(object? sender, EventArgs e)
    {
        if (_erreur)
        {
            Reinitialiser();
            MettreAJourAffichage();
            return;
        }

        double valeur = LireEntree();

        // 200 + 10 % => 10 % de 200 = 20 ; sinon 50 % => 0,5
        if (_operande.HasValue && (_operateur == "+" || _operateur == "−"))
            valeur = _operande.Value * valeur / 100;
        else
            valeur /= 100;

        _entree = Formater(valeur);
        _nouvelleSaisie = _operateur == null;

        MettreAJourAffichage();
    }

    // ------------------------------------------------------------------
    // Méthodes utilitaires
    // ------------------------------------------------------------------
    private void DemarrerNouvelleSaisieSiNecessaire()
    {
        if (!_nouvelleSaisie)
            return;

        // Après un "=", on efface l'ancienne opération affichée
        if (_operateur == null)
            _expression = "";

        _entree = "0";
        _nouvelleSaisie = false;
    }

    private static double Calculer(double a, string operateur, double b)
    {
        return operateur switch
        {
            "+" => a + b,
            "−" => a - b,
            "×" => a * b,
            "÷" => b == 0 ? double.NaN : a / b,
            _ => b
        };
    }

    private void AfficherErreur(double resultat, string expression)
    {
        _entree = double.IsNaN(resultat) ? MessageDivisionParZero : MessageTropGrand;
        _expression = expression;
        _operande = null;
        _operateur = null;
        _nouvelleSaisie = true;
        _erreur = true;

        MettreAJourAffichage();
    }

    private void Reinitialiser()
    {
        _entree = "0";
        _operande = null;
        _operateur = null;
        _expression = "";
        _nouvelleSaisie = false;
        _erreur = false;
    }

    private double LireEntree()
    {
        return double.Parse(_entree, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private int NombreDeChiffres()
    {
        return _entree.Count(char.IsDigit);
    }

    private static string Formater(double valeur)
    {
        if (valeur == 0)
            return "0";

        // Notation scientifique uniquement pour les très grands nombres
        string format = Math.Abs(valeur) >= 1e15 ? "G10" : "0.############";
        return valeur.ToString(format, CultureInfo.InvariantCulture);
    }

    private void MettreAJourAffichage()
    {
        // À l'écran, on affiche la virgule française
        ExpressionLabel.Text = _expression.Replace('.', ',');
        ResultatLabel.Text = _entree.Replace('.', ',');

        // Réduit la taille de la police quand le nombre s'allonge
        int longueur = ResultatLabel.Text.Length;
        ResultatLabel.FontSize = _erreur ? 24
            : longueur <= 7 ? 64
            : longueur <= 10 ? 50
            : longueur <= 13 ? 38
            : 30;
    }
}