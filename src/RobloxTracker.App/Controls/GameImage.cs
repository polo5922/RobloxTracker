using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobloxTracker.App.Converters;

namespace RobloxTracker.App.Controls;

/// <summary>
/// Image de jeu aux coins arrondis, avec l'initiale du jeu en remplacement
/// tant que l'image n'est pas téléchargée. Les images sont décodées hors du thread UI et mises en cache.
/// </summary>
public sealed class GameImage : Border
{
    private static readonly InitialConverter Initial = new();
    private static readonly ConcurrentDictionary<string, Task<ImageSource?>> Cache = new();

    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(GameImage), new PropertyMetadata(null, (d, _) => ((GameImage)d).ScheduleUpdate()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(GameImage), new PropertyMetadata(null, (d, _) => ((GameImage)d).ScheduleUpdate()));

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(
        nameof(DecodeWidth), typeof(int), typeof(GameImage), new PropertyMetadata(256, (d, _) => ((GameImage)d).ScheduleUpdate()));

    private bool _updatePending;
    private int _version;

    public string? ImagePath
    {
        get => (string?)GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public int DecodeWidth
    {
        get => (int)GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    public GameImage()
    {
        CornerRadius = new CornerRadius(8);
        ScheduleUpdate();
    }

    /// <summary>Regroupe les changements de propriétés (liaisons) en une seule mise à jour.</summary>
    private void ScheduleUpdate()
    {
        if (_updatePending) return;
        _updatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, Update);
    }

    private async void Update()
    {
        _updatePending = false;
        var version = ++_version;

        var task = Load(ImagePath, DecodeWidth);
        if (!task.IsCompleted) ShowPlaceholder();

        var image = await task;
        if (version != _version) return; // une autre image a été demandée entre-temps (conteneur recyclé)

        if (image is null)
        {
            ShowPlaceholder();
            return;
        }
        Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        Child = null;
    }

    private void ShowPlaceholder()
    {
        Background = (Brush)Application.Current.FindResource("SurfaceHoverBrush");
        var text = (string)Initial.Convert(Title ?? "?", typeof(string), null!, CultureInfo.InvariantCulture);
        if (Child is Viewbox { Child: TextBlock existing })
        {
            existing.Text = text;
            return;
        }
        Child = new Viewbox
        {
            Margin = new Thickness(8),
            MaxHeight = 64,
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)Application.Current.FindResource("MutedBrush"),
            },
        };
    }

    private static Task<ImageSource?> Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrEmpty(path)) return Task.FromResult<ImageSource?>(null);

        // La date de modification fait partie de la clé : une image re-téléchargée est relue.
        DateTime written;
        try { written = File.GetLastWriteTimeUtc(path); }
        catch (IOException) { return Task.FromResult<ImageSource?>(null); }

        var key = $"{path}|{decodeWidth}|{written.Ticks}";
        var task = Cache.GetOrAdd(key, _ => Task.Run(() => Decode(path, decodeWidth)));
        if (task.IsCompleted && task.Result is null) Cache.TryRemove(key, out _); // fichier absent : on réessaiera
        return task;
    }

    private static ImageSource? Decode(string path, int decodeWidth)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = decodeWidth;
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
}
