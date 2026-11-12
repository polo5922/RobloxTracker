using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobloxTracker.App.Converters;

/// <summary>TimeSpan → « 2 h 05 min », « 12 min » ou « 45 s ».</summary>
public sealed class DurationConverter : IValueConverter
{
    public static string Format(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:00} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min";
        return $"{Math.Max(0, (int)t.TotalSeconds)} s";
    }

    /// <summary>Forme compacte pour les étiquettes de graphe : « 2h05 », « 45m », vide si nul.</summary>
    public static string Short(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h{t.Minutes:00}";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m";
        return t > TimeSpan.Zero ? "<1m" : "";
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TimeSpan t ? Format(t) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>DateTime UTC → texte en heure locale (format en paramètre, défaut « g »).</summary>
public sealed class LocalDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime d
            ? d.ToLocalTime().ToString(parameter as string ?? "g", CultureInfo.GetCultureInfo("fr-FR"))
            : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Chemin de fichier → image. Chargée entièrement en mémoire pour ne pas verrouiller
/// le fichier (il est remplacé lors des mises à jour du jeu).
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (parameter is string s && int.TryParse(s, out var width)) image.DecodePixelWidth = width;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && ColorConverter.ConvertFromString(hex) is Color c)
                return new SolidColorBrush(c);
        }
        catch (FormatException) { }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true / non-null / chaîne non vide → Visible. « invert » en paramètre inverse le résultat.</summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i > 0,
            null => false,
            _ => true,
        };
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Première lettre d'un nom, pour les vignettes de remplacement.</summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s ? s.TrimStart('[', '(', ' ').FirstOrDefault(char.IsLetterOrDigit).ToString().ToUpperInvariant() : "?";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
