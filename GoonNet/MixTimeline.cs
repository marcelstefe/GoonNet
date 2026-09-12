using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GoonNet;

/// <summary>
/// Two-lane mix view: the outgoing item on top, the incoming item below, starting at
/// <see cref="NextStart"/> (seconds into the outgoing item). Each lane has a volume line
/// drawn over its waveform; the bright waveform shows the level after the volume line.
/// A click anywhere moves the player there (<see cref="SeekRequested"/>). Left-drag the lower
/// waveform to move the mix point (snaps to the outgoing outro marker). Click a volume line to
/// add a point, drag points to shape it, right-click a point to delete it.
/// Wheel zooms; Shift+wheel or right-drag pans.
/// </summary>
public sealed class MixTimeline : FrameworkElement
{
    private const double RulerHeight = 22;
    private const double LaneGap = 10;
    private const double LineTopPad = 26;    // 100% sits this far below a lane's top (under the lane title)
    private const double LineBottomPad = 10; // 0% sits this far above a lane's bottom
    private const double HandleRadius = 4.5;
    private const double HitRadius = 7;
    private const double SnapPixels = 6;
    private const double ClickSlop = 3;      // pixels the mouse may move and still count as a click

    private sealed class Lane
    {
        public WaveformPeaks? Peaks;
        public string Label = "";
        public double Marker = double.NaN;   // outgoing: outro, incoming: intro
        public List<VolumePoint> Points = new();
        public double Length => Peaks?.LengthSeconds ?? 0;
    }

    private readonly Lane _out = new();
    private readonly Lane _in = new();

    private double _viewStart;               // outgoing-item seconds at the left edge
    private double _visibleSeconds = 30;
    private double? _playhead;

    private enum DragKind { None, Click, Point, MixPoint, Pan }
    private DragKind _drag;
    private Lane? _dragLane;
    private int _dragIndex;
    private double _dragOriginX;
    private double _dragOriginValue;

    private static readonly Brush LaneBg      = Freeze(new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)));
    private static readonly Brush ItemBg      = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)));
    private static readonly Brush OverlapBg   = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen   OutWavePen  = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xe6)), 1));
    private static readonly Pen   OutFaintPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x33, 0x99, 0xe6)), 1));
    private static readonly Pen   InWavePen   = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xa8, 0x55, 0xf7)), 1));
    private static readonly Pen   InFaintPen  = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xa8, 0x55, 0xf7)), 1));
    private static readonly Pen   VolumePen   = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xea, 0xb3, 0x08)), 2));
    private static readonly Brush HandleFill  = Freeze(new SolidColorBrush(Color.FromRgb(0xea, 0xb3, 0x08)));
    private static readonly Pen   HandlePen   = Freeze(new Pen(Brushes.Black, 1.5));
    private static readonly Pen   IntroPen    = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)), 2));
    private static readonly Pen   OutroPen    = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)), 2));
    private static readonly Brush MixBrush    = Brushes.White;
    private static readonly Pen   MixPen      = Freeze(new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen   PlayheadPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)), 1.5));
    private static readonly Pen   TickPen     = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x99, 0x99, 0x99)), 1));
    private static readonly Brush RulerText   = Freeze(new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)));
    private static readonly Typeface RulerFont = new("Segoe UI");
    private static readonly Typeface LabelFont = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static T Freeze<T>(T o) where T : Freezable { o.Freeze(); return o; }

    public MixTimeline()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Seconds into the outgoing item at which the incoming item starts.</summary>
    public double NextStart { get; private set; }
    public double LengthA => _out.Length;
    public double LengthB => _in.Length;
    public IReadOnlyList<VolumePoint> PointsA => _out.Points;
    public IReadOnlyList<VolumePoint> PointsB => _in.Points;

    /// <summary>Player position in outgoing-item seconds, or null to hide the player line.</summary>
    public double? Playhead
    {
        get => _playhead;
        set { _playhead = value; InvalidateVisual(); }
    }

    /// <summary>Raised whenever the mix point or a volume line is edited.</summary>
    public event Action? Changed;

    /// <summary>Raised on a click (not a drag): move the player to this outgoing-item time.</summary>
    public event Action<double>? SeekRequested;

    public void SetItems(string labelA, WaveformPeaks peaksA, double outroA, IEnumerable<VolumePoint> pointsA,
                         string labelB, WaveformPeaks peaksB, double introB, IEnumerable<VolumePoint> pointsB,
                         double nextStart)
    {
        _out.Label = labelA;
        _out.Peaks = peaksA;
        _out.Marker = outroA;
        _out.Points = pointsA.ToList();
        _in.Label = labelB;
        _in.Peaks = peaksB;
        _in.Marker = introB;
        _in.Points = pointsB.ToList();
        NextStart = Math.Clamp(nextStart, 0, LengthA);
        FrameMixPoint();
    }

    /// <summary>Moves the mix point back and clears both volume lines.</summary>
    public void Reset(double nextStart)
    {
        NextStart = Math.Clamp(nextStart, 0, LengthA);
        _out.Points.Clear();
        _in.Points.Clear();
        FrameMixPoint();
        Changed?.Invoke();
    }

    // Show the end of the outgoing item and the start of the incoming one.
    private void FrameMixPoint()
    {
        _visibleSeconds = 30;
        _viewStart = NextStart - _visibleSeconds * 0.6;
        InvalidateVisual();
    }

    // ---------- Geometry ----------
    private double X(double seconds) => (seconds - _viewStart) / _visibleSeconds * ActualWidth;
    private double TimeAt(double x) => _viewStart + x / ActualWidth * _visibleSeconds;
    private double LaneHeight => Math.Max(0, (ActualHeight - RulerHeight - LaneGap) / 2);
    private double LaneTop(Lane lane) => ReferenceEquals(lane, _out) ? RulerHeight : RulerHeight + LaneHeight + LaneGap;
    private double Offset(Lane lane) => ReferenceEquals(lane, _out) ? 0 : NextStart;
    private double LineSpan => Math.Max(1, LaneHeight - LineTopPad - LineBottomPad);
    private double GainY(Lane lane, double gain) => LaneTop(lane) + LineTopPad + (1 - gain) * LineSpan;

    private double GainFromY(Lane lane, double y)
        => Math.Clamp(1 - (y - LaneTop(lane) - LineTopPad) / LineSpan, 0, 1);

    private Lane? LaneAt(double y)
    {
        foreach (var lane in new[] { _out, _in })
        {
            var top = LaneTop(lane);
            if (y >= top && y < top + LaneHeight) return lane;
        }
        return null;
    }

    private bool InItem(Lane lane, double x)
        => lane.Peaks is not null && x >= X(Offset(lane)) && x <= X(Offset(lane) + lane.Length);

    // ---------- Rendering ----------
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < RulerHeight + LaneGap + 40) return;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // hit-testable everywhere
        DrawRuler(dc, w);
        DrawLane(dc, _out, w, OutFaintPen, OutWavePen, OutroPen, "O");
        DrawLane(dc, _in, w, InFaintPen, InWavePen, IntroPen, "I");

        if (_out.Peaks is null) return;

        // Mix point: dashed line through both lanes with a handle in the ruler.
        double mx = X(NextStart);
        dc.DrawLine(MixPen, new Point(mx, RulerHeight), new Point(mx, h));
        var handle = new StreamGeometry();
        using (var g = handle.Open())
        {
            g.BeginFigure(new Point(mx - 5, RulerHeight - 9), true, true);
            g.LineTo(new Point(mx + 5, RulerHeight - 9), false, false);
            g.LineTo(new Point(mx, RulerHeight - 1), false, false);
        }
        handle.Freeze();
        dc.DrawGeometry(MixBrush, null, handle);

        if (_playhead is double p)
        {
            double px = X(p);
            dc.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
        }
    }

    private void DrawRuler(DrawingContext dc, double w)
    {
        double step = ChooseStep(_visibleSeconds);
        double first = Math.Max(0, Math.Ceiling(_viewStart / step) * step);
        for (double t = first; t <= _viewStart + _visibleSeconds; t += step)
        {
            double x = X(t);
            dc.DrawLine(TickPen, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));
            DrawText(dc, TimeSpan.FromSeconds(t).ToString(step < 1 ? @"m\:ss\.ff" : @"m\:ss"),
                RulerText, x + 3, 3, 10, RulerFont);
        }
    }

    private void DrawLane(DrawingContext dc, Lane lane, double w, Pen faintPen, Pen wavePen, Pen markerPen, string markerLabel)
    {
        double top = LaneTop(lane), lh = LaneHeight;
        var laneRect = new Rect(0, top, w, lh);
        dc.DrawRoundedRectangle(LaneBg, null, laneRect, 6, 6);
        if (lane.Peaks is null) return;

        double offset = Offset(lane);
        double x0 = X(offset), x1 = X(offset + lane.Length);
        if (x1 <= 0 || x0 >= w) return;

        dc.PushClip(new RectangleGeometry(laneRect, 6, 6));
        dc.DrawRectangle(ItemBg, null, new Rect(new Point(Math.Max(0, x0), top), new Point(Math.Min(w, x1), top + lh)));

        // Overlap: both items playing.
        double ox0 = Math.Max(0, X(NextStart)), ox1 = Math.Min(w, X(LengthA));
        if (ox1 > ox0)
            dc.DrawRectangle(OverlapBg, null, new Rect(new Point(ox0, top), new Point(ox1, top + lh)));

        // Waveform: faint = level in the file, bright = level after the volume line.
        double mid = top + lh / 2, half = lh / 2 - 4, secPerPx = _visibleSeconds / w;
        var faint = new StreamGeometry();
        var bright = new StreamGeometry();
        using (var fc = faint.Open())
        using (var bc = bright.Open())
        {
            int px0 = Math.Max(0, (int)Math.Floor(x0)), px1 = Math.Min((int)w, (int)Math.Ceiling(x1));
            for (int px = px0; px < px1; px++)
            {
                double t = TimeAt(px) - offset;
                float peak = lane.Peaks.Max(t, t + secPerPx);
                if (peak <= 0) continue;
                double x = px + 0.5;
                double a = peak * half;
                double b = a * VolumeEnvelope.GainAt(lane.Points, t + secPerPx / 2);
                fc.BeginFigure(new Point(x, mid - a), false, false);
                fc.LineTo(new Point(x, mid + a), true, false);
                if (b < 0.25) continue;
                bc.BeginFigure(new Point(x, mid - b), false, false);
                bc.LineTo(new Point(x, mid + b), true, false);
            }
        }
        faint.Freeze();
        bright.Freeze();
        dc.DrawGeometry(null, faintPen, faint);
        dc.DrawGeometry(null, wavePen, bright);

        if (!double.IsNaN(lane.Marker))
        {
            double mx = X(offset + lane.Marker);
            dc.DrawLine(markerPen, new Point(mx, top), new Point(mx, top + lh));
            DrawText(dc, markerLabel, markerPen.Brush, mx + 3, top + lh - 16, 10, RulerFont);
        }

        DrawText(dc, lane.Label, Brushes.White, Math.Max(x0, 0) + 8, top + 6, 12, LabelFont);
        DrawVolumeLine(dc, lane, x0, x1, w);
        dc.Pop();
    }

    private void DrawVolumeLine(DrawingContext dc, Lane lane, double x0, double x1, double w)
    {
        double offset = Offset(lane);
        double startX = Math.Max(x0, -10), endX = Math.Min(x1, w + 10);
        if (endX <= startX) return;

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(new Point(startX, GainY(lane, VolumeEnvelope.GainAt(lane.Points, TimeAt(startX) - offset))), false, false);
            foreach (var p in lane.Points)
            {
                double px = X(offset + p.Seconds);
                if (px > startX && px < endX) g.LineTo(new Point(px, GainY(lane, p.Gain)), true, true);
            }
            g.LineTo(new Point(endX, GainY(lane, VolumeEnvelope.GainAt(lane.Points, TimeAt(endX) - offset))), true, true);
        }
        line.Freeze();
        dc.DrawGeometry(null, VolumePen, line);

        foreach (var p in lane.Points)
            dc.DrawEllipse(HandleFill, HandlePen, new Point(X(offset + p.Seconds), GainY(lane, p.Gain)), HandleRadius, HandleRadius);
    }

    private void DrawText(DrawingContext dc, string text, Brush brush, double x, double y, double size, Typeface font)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            font, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(x, y));
    }

    private static double ChooseStep(double visibleSeconds)
    {
        double raw = visibleSeconds / 8;
        foreach (var s in new[] { 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300 })
            if (s >= raw) return s;
        return 600;
    }

    // ---------- Mouse ----------
    private bool HitPoint(Point pos, out Lane lane, out int index)
    {
        foreach (var l in new[] { _out, _in })
        {
            for (int i = 0; i < l.Points.Count; i++)
            {
                var p = l.Points[i];
                var centre = new Point(X(Offset(l) + p.Seconds), GainY(l, p.Gain));
                if ((centre - pos).Length > HitRadius) continue;
                lane = l;
                index = i;
                return true;
            }
        }
        lane = _out;
        index = -1;
        return false;
    }

    private bool HitLine(Point pos, out Lane lane)
    {
        var hit = LaneAt(pos.Y);
        lane = hit ?? _out;
        if (hit is null || !InItem(hit, pos.X)) return false;
        double gain = VolumeEnvelope.GainAt(hit.Points, TimeAt(pos.X) - Offset(hit));
        return Math.Abs(GainY(hit, gain) - pos.Y) <= HitRadius;
    }

    private Cursor? CursorFor(Point pos)
    {
        if (_out.Peaks is null) return null;
        if (HitPoint(pos, out _, out _) || HitLine(pos, out _)) return Cursors.Hand;
        if (LaneAt(pos.Y) == _in && InItem(_in, pos.X)) return Cursors.SizeWE;
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_out.Peaks is null) return;
        var pos = e.GetPosition(this);

        if (HitPoint(pos, out var lane, out var index))
        {
            BeginDrag(DragKind.Point, lane, index, pos.X, 0);
        }
        else if (HitLine(pos, out lane))
        {
            // Add a point on the line (so it doesn't jump), then drag it.
            double t = Math.Clamp(TimeAt(pos.X) - Offset(lane), 0, lane.Length);
            var point = new VolumePoint(t, VolumeEnvelope.GainAt(lane.Points, t));
            index = lane.Points.FindIndex(p => p.Seconds > t);
            if (index < 0) index = lane.Points.Count;
            lane.Points.Insert(index, point);
            BeginDrag(DragKind.Point, lane, index, pos.X, 0);
            InvalidateVisual();
            Changed?.Invoke();
        }
        else
        {
            // A click moves the player here; dragging the lower waveform moves the mix point instead.
            var onIncoming = LaneAt(pos.Y) == _in && InItem(_in, pos.X);
            BeginDrag(DragKind.Click, onIncoming ? _in : null, -1, pos.X, NextStart);
        }
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var pos = e.GetPosition(this);
        if (HitPoint(pos, out var lane, out var index))
        {
            lane.Points.RemoveAt(index);
            InvalidateVisual();
            Changed?.Invoke();
        }
        else
        {
            BeginDrag(DragKind.Pan, null, -1, pos.X, _viewStart);
            Cursor = Cursors.ScrollWE;
        }
        e.Handled = true;
    }

    private void BeginDrag(DragKind kind, Lane? lane, int index, double x, double originValue)
    {
        _drag = kind;
        _dragLane = lane;
        _dragIndex = index;
        _dragOriginX = x;
        _dragOriginValue = originValue;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        double dt = (pos.X - _dragOriginX) / ActualWidth * _visibleSeconds;
        switch (_drag)
        {
            case DragKind.Point:
                MovePoint(pos);
                break;

            case DragKind.Click:
                // Past the click slop on the lower waveform, it's a mix-point drag.
                if (_dragLane == _in && Math.Abs(pos.X - _dragOriginX) > ClickSlop)
                {
                    _drag = DragKind.MixPoint;
                    MoveMixPoint(dt);
                }
                break;

            case DragKind.MixPoint:
                MoveMixPoint(dt);
                break;

            case DragKind.Pan:
                _viewStart = _dragOriginValue - dt;
                InvalidateVisual();
                break;

            default:
                Cursor = CursorFor(pos);
                break;
        }
    }

    private void MoveMixPoint(double dt)
    {
        var next = Math.Clamp(_dragOriginValue + dt, 0, LengthA);
        if (!double.IsNaN(_out.Marker) && Math.Abs(X(next) - X(_out.Marker)) < SnapPixels)
            next = _out.Marker;
        NextStart = next;
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void MovePoint(Point pos)
    {
        var lane = _dragLane!;
        var points = lane.Points;
        int i = _dragIndex;
        double min = i > 0 ? points[i - 1].Seconds + 0.01 : 0;
        double max = i < points.Count - 1 ? points[i + 1].Seconds - 0.01 : lane.Length;
        double t = Math.Clamp(TimeAt(pos.X) - Offset(lane), min, Math.Max(min, max));
        points[i] = new VolumePoint(t, (float)GainFromY(lane, pos.Y));
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag == DragKind.Click)
            SeekRequested?.Invoke(Math.Max(0, TimeAt(e.GetPosition(this).X)));
        EndDrag();
    }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_drag == DragKind.None) return;
        _drag = DragKind.None;
        _dragLane = null;
        Cursor = null;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = DragKind.None;
        _dragLane = null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double x = e.GetPosition(this).X;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewStart -= e.Delta / 120.0 * _visibleSeconds * 0.1;
        }
        else
        {
            double pivot = TimeAt(x);
            _visibleSeconds = Math.Clamp(_visibleSeconds * (e.Delta > 0 ? 1 / 1.25 : 1.25), 2, 900);
            _viewStart = pivot - x / ActualWidth * _visibleSeconds;
        }
        InvalidateVisual();
        e.Handled = true;
    }
}
