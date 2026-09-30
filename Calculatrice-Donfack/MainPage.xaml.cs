using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Calculatrice_Donfack;

public partial class MainPage : ContentPage
{
    // Nombre maximal de chiffres que l'on peut saisir
    private const int ChiffresMax = 15;
    private const int HistoriqueMax = 30;
    private const string MessageDivisionParZero = "Division par zéro impossible";
    private const string MessageTropGrand = "Résultat trop grand";
    private const string MessageEntreeInvalide = "Entrée invalide";

    // Texte du nombre en cours de saisie (le point sert de séparateur interne)
    private string _entree = "0";

    // Premier opérande et opérateur en attente
    private double? _operande;
    private string? _operateur;

    // Opération affichée au-dessus du résultat
    private string _expression = "";

    // Vrai quand le prochain chiffre doit démarrer un nouveau nombre
    private bool _nouvelleSaisie;

    // Vrai quand le nombre affiché vient d'être calculé (mémoire, √, x²...) :
    // le prochain chiffre le remplace, sans interrompre une opération en attente
    private bool _ecraser;

    // Vrai quand l'écran affiche un message d'erreur
    private bool _erreur;

    // Mémoire de la calculatrice (MC, MR, M+, M−)
    private double _memoire;
    private bool _aMemoire;

    // Panneaux affichés ou non
    private bool _historiqueVisible;
    private bool _avanceVisible;

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
    // Gestionnaires d'événements : saisie et opérations de base
    // ------------------------------------------------------------------
    private void OnChiffreClicked(object? sender, EventArgs e)
    {
        if (sender is not Button bouton)
            return;

        Vibrer();

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
        Vibrer();

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

        Vibrer();

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
        _ecraser = false;
        _expression = $"{Formater(_operande!.Value)} {operateur}";

        MettreAJourAffichage();
    }

    private void OnEgalClicked(object? sender, EventArgs e)
    {
        if (_erreur || _operateur == null || !_operande.HasValue)
            return;

        Vibrer();

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
        _ecraser = false;

        AjouterHistorique(_expression, _entree);
        MettreAJourAffichage();
    }

    private void OnToutEffacerClicked(object? sender, EventArgs e)
    {
        Vibrer();
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
        if (_nouvelleSaisie || _ecraser)
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
        _ecraser = _operateur != null;

        MettreAJourAffichage();
    }

    // ------------------------------------------------------------------
    // Fonctionnalités supplémentaires : fonctions avancées
    // ------------------------------------------------------------------
    private void OnFonctionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button bouton)
            return;

        Vibrer();

        if (_erreur)
            Reinitialiser();

        double v = LireEntree();
        string texte = Formater(v);
        string expression;
        double resultat;

        switch (bouton.Text)
        {
            case "x²":
                expression = $"({texte})²";
                resultat = v * v;
                break;

            case "√":
                expression = $"√({texte})";
                if (v < 0)
                {
                    AfficherMessage(MessageEntreeInvalide, expression);
                    return;
                }
                resultat = Math.Sqrt(v);
                break;

            case "1/x":
                expression = $"1/({texte})";
                if (v == 0)
                {
                    AfficherMessage(MessageDivisionParZero, expression);
                    return;
                }
                resultat = 1 / v;
                break;

            case "π":
                // Insère la constante, sans passer par l'historique
                AppliquerResultat(Math.PI, null);
                return;

            default:
                return;
        }

        if (double.IsNaN(resultat) || double.IsInfinity(resultat))
        {
            AfficherErreur(resultat, expression);
            return;
        }

        AppliquerResultat(resultat, expression);
    }

    // ------------------------------------------------------------------
    // Fonctionnalités supplémentaires : mémoire
    // ------------------------------------------------------------------
    private void OnMemoireClicked(object? sender, EventArgs e)
    {
        if (sender is not Button bouton)
            return;

        Vibrer();

        if (_erreur)
            Reinitialiser();

        switch (bouton.Text)
        {
            case "MC":
                _memoire = 0;
                _aMemoire = false;
                break;

            case "MR":
                AppliquerResultat(_memoire, null);
                return;

            case "M+":
                _memoire += LireEntree();
                _aMemoire = true;
                _ecraser = true;
                break;

            case "M−":
                _memoire -= LireEntree();
                _aMemoire = true;
                _ecraser = true;
                break;
        }

        MettreAJourAffichage();
    }

    // ------------------------------------------------------------------
    // Fonctionnalités supplémentaires : historique, panneau avancé, copie
    // ------------------------------------------------------------------
    private void OnHistoriqueClicked(object? sender, EventArgs e)
    {
        BasculerHistorique(!_historiqueVisible);
    }

    private void OnViderHistoriqueClicked(object? sender, EventArgs e)
    {
        HistoriqueListe.Clear();
        HistoriqueVideLabel.IsVisible = true;
    }

    private void OnAvanceClicked(object? sender, EventArgs e)
    {
        _avanceVisible = !_avanceVisible;
        AvanceGrid.IsVisible = _avanceVisible;
        ColorerBoutonActif(AvanceBouton, _avanceVisible);
    }

    // Un appui sur le résultat le copie dans le presse-papiers
    private async void OnResultatTapped(object? sender, TappedEventArgs e)
    {
        if (_erreur)
            return;

        try
        {
            await Clipboard.Default.SetTextAsync(_entree.Replace('.', ','));
            Vibrer();

            ExpressionLabel.Text = "Résultat copié ✓";
            await Task.Delay(1300);
            MettreAJourAffichage();
        }
        catch
        {
            // Presse-papiers indisponible : on ignore
        }
    }

    private void BasculerHistorique(bool visible)
    {
        _historiqueVisible = visible;
        HistoriquePanel.IsVisible = visible;
        ResultatStack.IsVisible = !visible;
        ColorerBoutonActif(HistoriqueBouton, visible);
    }

    private static void ColorerBoutonActif(Button bouton, bool actif)
    {
        if (actif)
        {
            bouton.BackgroundColor = Color.FromArgb("#5B3DF5");
            bouton.TextColor = Colors.White;
        }
        else
        {
            bouton.ClearValue(VisualElement.BackgroundColorProperty);
            bouton.ClearValue(Button.TextColorProperty);
        }
    }

    private void AjouterHistorique(string expression, string resultat)
    {
        var ligne = new VerticalStackLayout { Spacing = 0 };

        ligne.Add(new Label
        {
            Text = Affichable(expression),
            FontSize = 12,
            TextColor = Color.FromArgb("#8B95B5"),
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.HeadTruncation
        });
        ligne.Add(new Label
        {
            Text = Affichable(resultat),
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.HeadTruncation
        });

        // Un appui sur une ligne réutilise son résultat
        var geste = new TapGestureRecognizer();
        geste.Tapped += (_, _) => ReprendreDepuisHistorique(resultat);
        ligne.GestureRecognizers.Add(geste);

        HistoriqueListe.Insert(0, ligne);
        while (HistoriqueListe.Count > HistoriqueMax)
            HistoriqueListe.RemoveAt(HistoriqueListe.Count - 1);

        HistoriqueVideLabel.IsVisible = false;
    }

    private void ReprendreDepuisHistorique(string valeur)
    {
        if (_erreur)
            Reinitialiser();

        _entree = valeur;

        if (_operateur == null)
        {
            _expression = "";
            _nouvelleSaisie = true;
            _ecraser = false;
        }
        else
        {
            _nouvelleSaisie = false;
            _ecraser = true;
        }

        BasculerHistorique(false);
        MettreAJourAffichage();
    }

    // ------------------------------------------------------------------
    // Méthodes utilitaires
    // ------------------------------------------------------------------
    private void DemarrerNouvelleSaisieSiNecessaire()
    {
        // Un nombre calculé (mémoire, √...) est remplacé par le prochain chiffre
        if (_ecraser)
        {
            _entree = "0";
            _ecraser = false;
        }

        if (!_nouvelleSaisie)
            return;

        // Après un "=", on efface l'ancienne opération affichée
        if (_operateur == null)
            _expression = "";

        _entree = "0";
        _nouvelleSaisie = false;
    }

    // Affiche un résultat calculé par une fonction (√, x², 1/x, π, MR)
    private void AppliquerResultat(double resultat, string? expression)
    {
        _entree = Formater(resultat);

        if (_operateur == null)
        {
            // Pas d'opération en attente : le résultat est final
            _expression = expression == null ? "" : expression + " =";
            _nouvelleSaisie = true;
            _ecraser = false;
        }
        else
        {
            // Opération en attente : le résultat devient le second opérande
            _nouvelleSaisie = false;
            _ecraser = true;
        }

        if (expression != null)
            AjouterHistorique(expression + " =", _entree);

        MettreAJourAffichage();
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
        AfficherMessage(double.IsNaN(resultat) ? MessageDivisionParZero : MessageTropGrand, expression);
    }

    private void AfficherMessage(string message, string expression)
    {
        _entree = message;
        _expression = expression;
        _operande = null;
        _operateur = null;
        _nouvelleSaisie = true;
        _ecraser = false;
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
        _ecraser = false;
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

    // Retour haptique léger sur mobile (ignoré sur ordinateur)
    private static void Vibrer()
    {
        try
        {
            if (HapticFeedback.Default.IsSupported)
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Non pris en charge : on ignore
        }
    }

    // Sépare les milliers (1 234 567) sans toucher aux décimales
    private static string GrouperMilliers(string texte)
    {
        return Regex.Replace(texte, @"(?<![\d.])\d{4,}", m =>
        {
            string chiffres = m.Value;
            var sb = new StringBuilder();
            for (int i = 0; i < chiffres.Length; i++)
            {
                if (i > 0 && (chiffres.Length - i) % 3 == 0)
                    sb.Append('\u00A0');
                sb.Append(chiffres[i]);
            }
            return sb.ToString();
        });
    }

    // Texte interne -> texte affiché : milliers séparés et virgule française
    private static string Affichable(string texte)
    {
        return GrouperMilliers(texte).Replace('.', ',');
    }

    private void MettreAJourAffichage()
    {
        ExpressionLabel.Text = Affichable(_expression);
        ResultatLabel.Text = Affichable(_entree);
        MemoireLabel.IsVisible = _aMemoire;

        // Réduit la taille de la police quand le nombre s'allonge
        int longueur = ResultatLabel.Text.Length;
        ResultatLabel.FontSize = _erreur ? 24
            : longueur <= 7 ? 64
            : longueur <= 10 ? 50
            : longueur <= 13 ? 38
            : 30;
    }
}