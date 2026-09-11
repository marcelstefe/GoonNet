using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GoonNet;

/// <summary>
/// Waveform view with framed grid background, mirrored L/R halves,
/// intro/hook/outro markers, click-drag selection, right-click pan,
/// mouse-wheel zoom.
/// Peaks are interleaved <c>[lMax, lMin, rMax, rMin]</c> per source column.
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    private float[] _peaks = Array.Empty<float>();
    private double _totalSeconds;

    private double _zoom = 1.0;
    private double _scrollSeconds;

    private Point? _rightDragOrigin;
    private double _rightDragOriginScroll;

    private bool _leftDragging;
    private double _selectionAnchor;

    private static readonly Brush FrameBg     = Freeze(new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)));
    private static readonly Pen   GridPen      = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0x99, 0x99, 0x99)), 1));
    private static readonly Pen   MajorGridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x99, 0x99, 0x99)), 1));
    private static readonly Pen   CenterPen    = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Brush LeftFill     = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xc0)));
    private static readonly Brush RightFill    = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xe6)));
    private static readonly Pen   LeftPen      = Freeze(new Pen(LeftFill, 1));
    private static readonly Pen   RightPen     = Freeze(new Pen(RightFill, 1));
    private static readonly Pen   CursorPen    = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)), 1.5));
    private static readonly Brush RulerText    = Freeze(new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)));
    private static readonly Brush SelectionFill = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0xa0, 0xe0)));
    private static readonly Pen   IntroPen     = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)), 2));
    private static readonly Pen   HookPen  = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xea, 0xb3, 0x08)), 2));
    private static readonly Pen   OutroPen     = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)), 2));
    private static readonly Typeface RulerFont = new("Segoe UI");

    private static T Freeze<T>(T o) where T : Freezable { o.Freeze(); return o; }

    public WaveformView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.IBeam;
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double), typeof(WaveformView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(double), typeof(WaveformView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public static readonly DependencyProperty SelectionEndProperty =
        DependencyProperty.Register(nameof(SelectionEnd), typeof(double), typeof(WaveformView),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public static readonly DependencyProperty IntroSecondsProperty =
        DependencyProperty.Register(nameof(IntroSeconds), typeof(double), typeof(WaveformView),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public double IntroSeconds
    {
        get => (double)GetValue(IntroSecondsProperty);
        set => SetValue(IntroSecondsProperty, value);
    }

    public static readonly DependencyProperty HookSecondsProperty =
        DependencyProperty.Register(nameof(HookSeconds), typeof(double), typeof(WaveformView),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public double HookSeconds
    {
        get => (double)GetValue(HookSecondsProperty);
        set => SetValue(HookSecondsProperty, value);
    }

    public static readonly DependencyProperty OutroSecondsProperty =
        DependencyProperty.Register(nameof(OutroSeconds), typeof(double), typeof(WaveformView),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
    public double OutroSeconds
    {
        get => (double)GetValue(OutroSecondsProperty);
        set => SetValue(OutroSecondsProperty, value);
    }

    public event EventHandler<double>? PositionRequested;

    public void SetData(float[] peaks, double totalSeconds)
    {
        _peaks = peaks ?? Array.Empty<float>();
        _totalSeconds = Math.Max(0.001, totalSeconds);
        _zoom = 1.0;
        _scrollSeconds = 0;
        SelectionStart = 0;
        SelectionEnd = 0;
        InvalidateVisual();
    }

    private double VisibleSeconds => _totalSeconds / _zoom;

    private double SecondsToX(double seconds, double width)
        => (seconds - _scrollSeconds) / VisibleSeconds * width;

    private double XToSeconds(double x, double width)
        => Math.Clamp(_scrollSeconds + x / width * VisibleSeconds, 0, _totalSeconds);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 4 || h < 4) return;

        dc.DrawRoundedRectangle(FrameBg, null, new Rect(0, 0, w, h), 6, 6);

        const double rulerHeight = 18;
        double waveTop = 0;
        double waveBottom = h - rulerHeight;
        double waveH = waveBottom - waveTop;
        double centerY = waveTop + waveH / 2.0;

        // Amplitude gridlines
        for (int i = 1; i < 4; i++)
        {
            double y = waveTop + waveH * i / 4.0;
            dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        }
        dc.DrawLine(CenterPen, new Point(0, centerY), new Point(w, centerY));

        DrawTimeAxis(dc, w, h, rulerHeight);
        DrawSelection(dc, w, waveBottom);
        DrawWaveform(dc, w, waveTop, waveBottom, centerY);
        DrawMarkers(dc, w, waveBottom);
        DrawCursor(dc, w, waveBottom);
    }

    private void DrawTimeAxis(DrawingContext dc, double w, double h, double rulerHeight)
    {
        if (_totalSeconds <= 0) return;

        double visible = VisibleSeconds;
        double step = ChooseNiceStep(visible);
        double firstTick = Math.Ceiling(_scrollSeconds / step) * step;

        for (double t = firstTick; t <= _scrollSeconds + visible + 1e-6; t += step)
        {
            double x = SecondsToX(t, w);
            dc.DrawLine(MajorGridPen, new Point(x, 0), new Point(x, h - rulerHeight));

            var text = new FormattedText(
                FormatTime(t),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                RulerFont,
                10, RulerText,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(x + 3, h - rulerHeight + 2));
        }
    }

    private static double ChooseNiceStep(double visibleSeconds)
    {
        double raw = visibleSeconds / 8.0;
        double[] steps = { 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800 };
        foreach (var s in steps) if (s >= raw) return s;
        return steps[^1];
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 60) return $"{seconds:0.##}s";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    private void DrawSelection(DrawingContext dc, double w, double bottom)
    {
        if (SelectionEnd <= SelectionStart) return;
        double x0 = Math.Max(0, SecondsToX(SelectionStart, w));
        double x1 = Math.Min(w, SecondsToX(SelectionEnd, w));
        if (x1 > x0)
            dc.DrawRectangle(SelectionFill, null, new Rect(x0, 0, x1 - x0, bottom));
    }

    private void DrawWaveform(DrawingContext dc, double w, double top, double bottom, double centerY)
    {
        if (_peaks.Length < 4 || _totalSeconds <= 0) return;

        int columns = _peaks.Length / 4;
        double secondsPerColumn = _totalSeconds / columns;
        double visible = VisibleSeconds;

        int startCol = Math.Max(0, (int)(_scrollSeconds / secondsPerColumn));
        int endCol = Math.Min(columns, (int)Math.Ceiling((_scrollSeconds + visible) / secondsPerColumn));
        if (endCol <= startCol) return;

        double halfH = (bottom - top) / 2.0;
        int pixelCols = Math.Max(1, (int)w);
        int srcCols = endCol - startCol;
        double srcPerPixel = (double)srcCols / pixelCols;

        // Envelope-style rendering: for each channel, use the max absolute
        // deviation so top-half and bottom-half look symmetric.
        for (int px = 0; px < pixelCols; px++)
        {
            int s0 = startCol + (int)(px * srcPerPixel);
            int s1 = startCol + (int)((px + 1) * srcPerPixel);
            if (s1 <= s0) s1 = s0 + 1;
            if (s1 > endCol) s1 = endCol;

            float lPeak = 0, rPeak = 0;
            for (int c = s0; c < s1; c++)
            {
                int i = c * 4;
                float lMax = _peaks[i], lMin = _peaks[i + 1];
                float rMax = _peaks[i + 2], rMin = _peaks[i + 3];
                float lAbs = Math.Max(Math.Abs(lMax), Math.Abs(lMin));
                float rAbs = Math.Max(Math.Abs(rMax), Math.Abs(rMin));
                if (lAbs > lPeak) lPeak = lAbs;
                if (rAbs > rPeak) rPeak = rAbs;
            }

            double x = px + 0.5;
            // Left: mirrored around center, only top half → line from (centerY - lPeak*halfH) to centerY
            dc.DrawLine(LeftPen,
                new Point(x, centerY - lPeak * halfH),
                new Point(x, centerY));
            // Right: same but bottom half
            dc.DrawLine(RightPen,
                new Point(x, centerY),
                new Point(x, centerY + rPeak * halfH));
        }
    }

    private void DrawMarkers(DrawingContext dc, double w, double bottom)
    {
        DrawMarker(dc, w, bottom, IntroSeconds, IntroPen, "I");
        DrawMarker(dc, w, bottom, HookSeconds, HookPen, "H");
        DrawMarker(dc, w, bottom, OutroSeconds, OutroPen, "O");
    }

    private void DrawMarker(DrawingContext dc, double w, double bottom, double seconds, Pen pen, string label)
    {
        if (double.IsNaN(seconds) || seconds < 0) return;
        double x = SecondsToX(seconds, w);
        if (x < 0 || x > w) return;
        dc.DrawLine(pen, new Point(x, 0), new Point(x, bottom));

        var text = new FormattedText(
            label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            RulerFont, 10, pen.Brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(x + 3, 2));
    }

    private void DrawCursor(DrawingContext dc, double w, double bottom)
    {
        if (_totalSeconds <= 0) return;
        double cx = SecondsToX(Position, w);
        if (cx >= 0 && cx <= w)
            dc.DrawLine(CursorPen, new Point(cx, 0), new Point(cx, bottom));
    }

    // ---------- Mouse ----------
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_totalSeconds <= 0) return;

        double pivotSeconds = XToSeconds(e.GetPosition(this).X, ActualWidth);
        double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
        double newZoom = Math.Clamp(_zoom * factor, 1.0, 500.0);
        _zoom = newZoom;
        _scrollSeconds = pivotSeconds - e.GetPosition(this).X / ActualWidth * VisibleSeconds;
        ClampScroll();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_totalSeconds <= 0) return;
        CaptureMouse();
        _leftDragging = true;
        double seconds = XToSeconds(e.GetPosition(this).X, ActualWidth);
        _selectionAnchor = seconds;
        SelectionStart = seconds;
        SelectionEnd = seconds;
        PositionRequested?.Invoke(this, seconds);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_totalSeconds <= 0) return;
        CaptureMouse();
        _rightDragOrigin = e.GetPosition(this);
        _rightDragOriginScroll = _scrollSeconds;
        Cursor = Cursors.ScrollWE;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_leftDragging)
        {
            double seconds = XToSeconds(e.GetPosition(this).X, ActualWidth);
            SelectionStart = Math.Min(_selectionAnchor, seconds);
            SelectionEnd = Math.Max(_selectionAnchor, seconds);
            PositionRequested?.Invoke(this, seconds);
        }
        else if (_rightDragOrigin is { } origin)
        {
            double dx = e.GetPosition(this).X - origin.X;
            _scrollSeconds = _rightDragOriginScroll - dx / ActualWidth * VisibleSeconds;
            ClampScroll();
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_leftDragging) return;
        _leftDragging = false;
        if (_rightDragOrigin == null) ReleaseMouseCapture();
        if (Math.Abs(SelectionEnd - SelectionStart) < 0.01)
        {
            SelectionStart = 0;
            SelectionEnd = 0;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (_rightDragOrigin == null) return;
        _rightDragOrigin = null;
        Cursor = Cursors.IBeam;
        if (!_leftDragging) ReleaseMouseCapture();
    }

    private void ClampScroll()
    {
        double maxScroll = Math.Max(0, _totalSeconds - VisibleSeconds);
        if (_scrollSeconds < 0) _scrollSeconds = 0;
        if (_scrollSeconds > maxScroll) _scrollSeconds = maxScroll;
    }
}
