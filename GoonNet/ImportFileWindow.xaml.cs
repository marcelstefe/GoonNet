using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace GoonNet;

public partial class ImportFileWindow : FluentWindow
{
    private const int MeterLedCount = 20;
    private readonly WaveformPreviewPlayer _previewPlayer = new();
    private readonly Ellipse[] _leftLeds = new Ellipse[MeterLedCount];
    private readonly Ellipse[] _rightLeds = new Ellipse[MeterLedCount];

    private static readonly Brush LedOffBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)));
    private static readonly Brush LedGreen    = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)));
    private static readonly Brush LedYellow   = Freeze(new SolidColorBrush(Color.FromRgb(0xea, 0xb3, 0x08)));
    private static readonly Brush LedRed      = Freeze(new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)));

    private static T Freeze<T>(T o) where T : Freezable { o.Freeze(); return o; }

    public static readonly DependencyProperty IntroSecondsProperty =
        DependencyProperty.Register(nameof(IntroSeconds), typeof(double), typeof(ImportFileWindow),
            new PropertyMetadata(double.NaN, (d, _) => ((ImportFileWindow)d).UpdateTimingHeader()));
    public double IntroSeconds
    {
        get => (double)GetValue(IntroSecondsProperty);
        set => SetValue(IntroSecondsProperty, value);
    }

    public static readonly DependencyProperty HookSecondsProperty =
        DependencyProperty.Register(nameof(HookSeconds), typeof(double), typeof(ImportFileWindow),
            new PropertyMetadata(double.NaN, (d, _) => ((ImportFileWindow)d).UpdateTimingHeader()));
    public double HookSeconds
    {
        get => (double)GetValue(HookSecondsProperty);
        set => SetValue(HookSecondsProperty, value);
    }

    public static readonly DependencyProperty OutroSecondsProperty =
        DependencyProperty.Register(nameof(OutroSeconds), typeof(double), typeof(ImportFileWindow),
            new PropertyMetadata(double.NaN, (d, _) => ((ImportFileWindow)d).UpdateTimingHeader()));
    public double OutroSeconds
    {
        get => (double)GetValue(OutroSecondsProperty);
        set => SetValue(OutroSecondsProperty, value);
    }

    public ImportFileWindow()
    {
        InitializeComponent();
        BuildMeter(_leftLeds, LeftMeter);
        BuildMeter(_rightLeds, RightMeter);

        _previewPlayer.PropertyChanged += PreviewPlayer_PropertyChanged;
        Closed += (_, _) => _previewPlayer.Dispose();
    }

    private static void BuildMeter(Ellipse[] leds, StackPanel host)
    {
        for (int i = 0; i < MeterLedCount; i++)
        {
            var e = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = LedOffBrush,
                Margin = new Thickness(0, 0, 0, 2)
            };
            leds[i] = e;
            host.Children.Add(e);
        }
    }

    private int _lastLeftActive = -1;
    private int _lastRightActive = -1;

    private void PreviewPlayer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            var prio = e.PropertyName is nameof(WaveformPreviewPlayer.PeakLeft)
                        or nameof(WaveformPreviewPlayer.PeakRight)
                        or nameof(WaveformPreviewPlayer.ChannelPosition)
                ? System.Windows.Threading.DispatcherPriority.Send
                : System.Windows.Threading.DispatcherPriority.DataBind;
            Dispatcher.BeginInvoke(new Action(() => PreviewPlayer_PropertyChanged(sender, e)), prio);
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(WaveformPreviewPlayer.PeakLeft):
                UpdateMeter(_leftLeds, _previewPlayer.PeakLeft);
                break;
            case nameof(WaveformPreviewPlayer.PeakRight):
                UpdateMeter(_rightLeds, _previewPlayer.PeakRight);
                break;
            case nameof(WaveformPreviewPlayer.ChannelPosition):
                WaveformPreview.Position = _previewPlayer.ChannelPosition;
                UpdatePositionText();
                break;
            case nameof(WaveformPreviewPlayer.ChannelLength):
            case nameof(WaveformPreviewPlayer.WaveformData):
                WaveformPreview.SetData(_previewPlayer.WaveformData, _previewPlayer.ChannelLength);
                UpdatePositionText();
                UpdateTimingHeader();
                break;
            case nameof(WaveformPreviewPlayer.IsPlaying):
                PlayPauseIcon.Source = _previewPlayer.IsPlaying
                    ? new Uri("pack://application:,,,/res/studio/pause.svg", UriKind.Absolute)
                    : new Uri("pack://application:,,,/res/window/play.svg", UriKind.Absolute);
                break;
        }
    }

    private void UpdateMeter(Ellipse[] leds, float peak)
    {
        int active = (int)Math.Round(Math.Clamp(peak, 0f, 1f) * MeterLedCount);
        int last = ReferenceEquals(leds, _leftLeds) ? _lastLeftActive : _lastRightActive;
        if (active == last) return;
        if (ReferenceEquals(leds, _leftLeds)) _lastLeftActive = active; else _lastRightActive = active;

        for (int i = 0; i < MeterLedCount; i++)
        {
            bool on = (MeterLedCount - i) <= active;
            if (!on)
            {
                if (leds[i].Fill != LedOffBrush) leds[i].Fill = LedOffBrush;
                continue;
            }
            var target = i < 3 ? LedRed : i < 7 ? LedYellow : LedGreen;
            if (leds[i].Fill != target) leds[i].Fill = target;
        }
    }

    private void UpdatePositionText()
    {
        var pos = TimeSpan.FromSeconds(_previewPlayer.ChannelPosition);
        var len = TimeSpan.FromSeconds(_previewPlayer.ChannelLength);
        PositionText.Text = $"{pos:mm\\:ss} / {len:mm\\:ss}";
    }

    private static string Fmt(double s)
    {
        if (double.IsNaN(s) || s < 0) return "--:--";
        var t = TimeSpan.FromSeconds(s);
        return t.ToString(@"mm\:ss");
    }

    private void UpdateTimingHeader()
    {
        LengthText.Text = Fmt(_previewPlayer.ChannelLength);
        IntroText.Text = Fmt(IntroSeconds);
        HookText.Text = Fmt(HookSeconds);
        OutroText.Text = Fmt(OutroSeconds);
    }

    // ---------- Buttons ----------
    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select a file to import",
            Filter = "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a|All Files|*.*"
        };

        if (dlg.ShowDialog(this) != true) return;

        FilePathBox.Text = dlg.FileName;
        var stem = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
        var dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            if (string.IsNullOrWhiteSpace(ArtistBox.Text))
                ArtistBox.Text = stem.Substring(0, dash).Trim();
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
                TitleBox.Text = stem.Substring(dash + 3).Trim();
        }
        else if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            TitleBox.Text = stem;
        }

        try
        {
            _previewPlayer.Load(dlg.FileName);
            IntroSeconds = double.NaN;
            HookSeconds = double.NaN;
            OutroSeconds = double.NaN;
            UpdatePositionText();
            UpdateTimingHeader();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Could not load preview:\n{ex.Message}",
                "Preview error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        _previewPlayer.LastError = null;
        if (_previewPlayer.IsPlaying) _previewPlayer.Pause();
        else _previewPlayer.Play();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_previewPlayer.IsPlaying && !string.IsNullOrEmpty(_previewPlayer.LastError))
            {
                System.Windows.MessageBox.Show(this, _previewPlayer.LastError,
                    "Playback error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => _previewPlayer.Stop();

    private void SetIntroButton_Click(object sender, RoutedEventArgs e)
        => IntroSeconds = _previewPlayer.ChannelPosition;

    private void SetHookButton_Click(object sender, RoutedEventArgs e)
        => HookSeconds = _previewPlayer.ChannelPosition;

    private void SetOutroButton_Click(object sender, RoutedEventArgs e)
        => OutroSeconds = _previewPlayer.ChannelPosition;

    private void ClearMarkersButton_Click(object sender, RoutedEventArgs e)
    {
        IntroSeconds = double.NaN;
        HookSeconds = double.NaN;
        OutroSeconds = double.NaN;
    }

    private void WaveformPreview_PositionRequested(object? sender, double seconds)
    {
        _previewPlayer.Seek(seconds);
    }

    private void ImportButton_Click(object sender, MouseButtonEventArgs e)
    {
        _previewPlayer.Stop();
        Result = BuildTrack();
        DialogResult = true;
        Close();
    }

    public LibraryTrack? Result { get; private set; }

    private LibraryTrack BuildTrack()
    {
        return new LibraryTrack
        {
            FilePath = FilePathBox.Text,
            Artist = ArtistBox.Text,
            Title = TitleBox.Text,
            Length = Fmt(_previewPlayer.ChannelLength),
            Event = (CategoryBox.SelectedItem as ComboBoxItem)?.Content?.ToString(),
            Intro = Fmt(IntroSeconds),
            Outro = Fmt(OutroSeconds),
            Hook = Fmt(HookSeconds),
            Note = NoteBox.Text,
        };
    }

    private void CancelButton_Click(object sender, MouseButtonEventArgs e)
    {
        _previewPlayer.Stop();
        DialogResult = false;
        Close();
    }
}
