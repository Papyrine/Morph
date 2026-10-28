/// <summary>
/// Reads a Windows metafile — an EMF ([MS-EMF]) or a WMF ([MS-WMF], with or without its placeable
/// header) — into a <see cref="MetafileDrawing"/>. It plays the records a chart or a diagram is drawn
/// with: polygons, polylines, Béziers, rectangles and ellipses, paths, the pens, brushes and fonts they
/// are drawn with, and text. What it does not play — bitmaps (<c>StretchDIBits</c>), clipping, raster
/// operations, EMF+ records — it skips, so a picture made of them comes out with them missing rather
/// than wrong.
///
/// Points are mapped as GDI maps them: the world transform (EMF), then the window to the viewport for
/// the map mode, to device units; then the frame the header gives the picture (EMF <c>rclFrame</c> in
/// device units, a WMF's placeable bounding box or else its window) becomes the unit square. MS Graph's
/// charts, which a legacy PowerPoint OLE object keeps only as such a preview, are an anisotropic window
/// onto device units, drawn in polygons, polylines and <c>ExtTextOutW</c>.
/// </summary>
static class MetafileReader
{
    const uint emfSignature = 0x464D4520;
    const uint placeableKey = 0x9AC6CDD7;

    /// <summary>The drawing in <paramref name="data"/>; null when it is neither format, or draws nothing.</summary>
    public static MetafileDrawing? Read(byte[] data)
    {
        try
        {
            var player = new Player();
            if (data.Length >= 44 &&
                BitConverter.ToUInt32(data, 0) == 1 &&
                BitConverter.ToUInt32(data, 40) == emfSignature)
            {
                player.PlayEmf(data);
            }
            else if (data.Length >= 18)
            {
                player.PlayWmf(data);
            }

            return player.Finish();
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            // A truncated or malformed record ends the reading; the picture is left out.
            return null;
        }
    }

    sealed record Pen(string? ColorHex, double Width);

    sealed record Brush(string? ColorHex);

    sealed record Font(string Family, double Height, bool Bold, bool Italic, bool Underline, double Degrees);

    // An affine transform: x' = M11 x + M21 y + Dx, y' = M12 x + M22 y + Dy.
    readonly record struct Transform(double M11, double M12, double M21, double M22, double Dx, double Dy)
    {
        public static Transform Identity => new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) =>
            (M11 * x + M21 * y + Dx, M12 * x + M22 * y + Dy);

        // This transform followed by the other.
        public Transform Then(Transform other) =>
            new(
                M11 * other.M11 + M12 * other.M21,
                M11 * other.M12 + M12 * other.M22,
                M21 * other.M11 + M22 * other.M21,
                M21 * other.M12 + M22 * other.M22,
                Dx * other.M11 + Dy * other.M21 + other.Dx,
                Dx * other.M12 + Dy * other.M22 + other.Dy);
    }

    // What SaveDC keeps and RestoreDC brings back.
    sealed record State
    {
        public Pen Pen { get; init; } = new("000000", 0);

        public Brush Brush { get; init; } = new("FFFFFF");

        public Font Font { get; init; } = new("Arial", 12, false, false, false, 0);

        public string TextColor { get; init; } = "000000";

        public uint TextAlign { get; init; }

        public int MapMode { get; init; } = 1;

        public (double X, double Y) WindowOrigin { get; init; }

        public (double X, double Y) WindowExtent { get; init; } = (1, 1);

        public (double X, double Y) ViewportOrigin { get; init; }

        public (double X, double Y) ViewportExtent { get; init; } = (1, 1);

        public Transform World { get; init; } = Transform.Identity;

        public (double X, double Y) Position { get; init; }
    }

    sealed class Player
    {
        State state = new();
        readonly Stack<State> saved = new();
        readonly Dictionary<uint, object> objects = [];
        readonly List<MetafileItem> items = [];

        // The frame, in device units, that becomes the unit square.
        double frameLeft;
        double frameTop;
        double frameWidth = 1;
        double frameHeight = 1;

        // EMF fixed map modes are scaled by the reference device's pixels per millimetre.
        double pixelsPerMillimetreX = 96 / 25.4;
        double pixelsPerMillimetreY = 96 / 25.4;

        // An open path bracket (BeginPath .. EndPath) and the figure being drawn into it.
        List<List<(double X, double Y)>>? path;
        bool pathClosed;

        // A WMF's frame is its placeable box or, without one, the first window it sets, both in its
        // logical units.
        bool wmfFramed;
        bool logicalFrame;

        public MetafileDrawing? Finish()
        {
            if (items.Count == 0 ||
                frameWidth <= 0 ||
                frameHeight <= 0)
            {
                return null;
            }

            return new([.. items.Select(Normalize)]);
        }

        // Device units to the unit square; sizes and widths to fractions of the frame's height.
        MetafileItem Normalize(MetafileItem item)
        {
            (double X, double Y) Unit((double X, double Y) point) =>
                ((point.X - frameLeft) / frameWidth, (point.Y - frameTop) / frameHeight);

            return item switch
            {
                MetafilePath path => path with
                {
                    Figures = [.. path.Figures.Select(figure => (IReadOnlyList<(double X, double Y)>) [.. figure.Select(Unit)])],
                    LineWidth = path.LineWidth / frameHeight
                },
                MetafileText text => text with
                {
                    X = (text.X - frameLeft) / frameWidth,
                    Y = (text.Y - frameTop) / frameHeight,
                    Size = text.Size / frameHeight
                },
                _ => item
            };
        }

        // A logical point in device units: the world transform, then the window onto the viewport. A WMF
        // has no device: its frame is in its logical units, which therefore stand as they are.
        (double X, double Y) Device(double x, double y)
        {
            if (logicalFrame)
            {
                return (x, y);
            }

            var (worldX, worldY) = state.World.Apply(x, y);
            var (scaleX, scaleY) = PageScale();
            return ((worldX - state.WindowOrigin.X) * scaleX + state.ViewportOrigin.X, (worldY - state.WindowOrigin.Y) * scaleY + state.ViewportOrigin.Y);
        }

        // How far one logical unit runs in device units, along x and along y, for the map mode.
        (double X, double Y) PageScale()
        {
            return state.MapMode switch
            {
                // MM_ISOTROPIC, MM_ANISOTROPIC: the window's extent onto the viewport's.
                7 or 8 => (Ratio(state.ViewportExtent.X, state.WindowExtent.X), Ratio(state.ViewportExtent.Y, state.WindowExtent.Y)),
                // MM_LOMETRIC, MM_HIMETRIC, MM_LOENGLISH, MM_HIENGLISH, MM_TWIPS: fixed units, y up.
                2 => (pixelsPerMillimetreX / 10, -pixelsPerMillimetreY / 10),
                3 => (pixelsPerMillimetreX / 100, -pixelsPerMillimetreY / 100),
                4 => (pixelsPerMillimetreX * 0.254, -pixelsPerMillimetreY * 0.254),
                5 => (pixelsPerMillimetreX * 0.0254, -pixelsPerMillimetreY * 0.0254),
                6 => (pixelsPerMillimetreX * 25.4 / 1440, -pixelsPerMillimetreY * 25.4 / 1440),
                _ => (1, 1)
            };

            static double Ratio(double viewport, double window) =>
                window == 0 ? 1 : viewport / window;
        }

        // A logical length in device units: its run along x and along y, averaged.
        double DeviceLength(double length)
        {
            var origin = Device(0, 0);
            var alongX = Device(length, 0);
            var alongY = Device(0, length);
            return (Distance(origin, alongX) + Distance(origin, alongY)) / 2;
        }

        // A logical height (a font's) in device units, along y.
        double DeviceHeight(double height) =>
            Distance(Device(0, 0), Device(0, height));

        static double Distance((double X, double Y) from, (double X, double Y) to) =>
            Math.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));

        // A COLORREF: 0x00BBGGRR.
        static string Color(uint value) =>
            $"{value & 0xFF:X2}{(value >> 8) & 0xFF:X2}{(value >> 16) & 0xFF:X2}";

        // Draws figures with the current pen and brush, or adds them to the open path.
        void Draw(List<List<(double X, double Y)>> figures, bool closed, bool fill = true, bool stroke = true)
        {
            if (path != null)
            {
                path.AddRange(figures);
                pathClosed |= closed;
                return;
            }

            var fillHex = closed && fill ? state.Brush.ColorHex : null;
            var lineHex = stroke ? state.Pen.ColorHex : null;
            if (fillHex == null && lineHex == null)
            {
                return;
            }

            // A zero-width pen is a cosmetic one, a single device pixel wide.
            var width = state.Pen.Width > 0 ? DeviceLength(state.Pen.Width) : 1;
            items.Add(new MetafilePath([.. figures.Where(_ => _.Count >= 2).Select(IReadOnlyList<(double X, double Y)> (_) => _)], closed, fillHex, lineHex, width));
        }

        void Text(double x, double y, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var (deviceX, deviceY) = Device(x, y);
            var font = state.Font;

            // A negative height is the em; a positive one the cell, of which the em is about seven eighths.
            var size = font.Height < 0 ? DeviceHeight(-font.Height) : DeviceHeight(font.Height) * 0.875;
            if (size <= 0)
            {
                size = DeviceHeight(12);
            }

            var horizontal = (state.TextAlign & 6) switch
            {
                6 => MetafileTextAlign.Centre,
                2 => MetafileTextAlign.Right,
                _ => MetafileTextAlign.Left
            };
            var vertical = (state.TextAlign & 24) switch
            {
                24 => MetafileTextBaseline.Baseline,
                8 => MetafileTextBaseline.Bottom,
                _ => MetafileTextBaseline.Top
            };

            // A y axis that runs up (a fixed map mode, a flipped viewport) turns the escapement round.
            var degrees = PageScale().Y < 0 ? -font.Degrees : font.Degrees;
            items.Add(new MetafileText(text, deviceX, deviceY, font.Family, size, font.Bold, font.Italic, font.Underline, state.TextColor, horizontal, vertical, degrees));
        }

        static Font ReadFont(double height, double escapement, int weight, bool italic, bool underline, string face) =>
            new(string.IsNullOrWhiteSpace(face) ? "Arial" : face, height, weight >= 600, italic, underline, escapement / 10);

        void Select(uint index)
        {
            if ((index & 0x80000000) != 0)
            {
                // A stock object.
                switch (index & 0x7FFFFFFF)
                {
                    case 0:
                        state = state with {Brush = new("FFFFFF")};
                        break;
                    case 1:
                        state = state with {Brush = new("C0C0C0")};
                        break;
                    case 2:
                        state = state with {Brush = new("808080")};
                        break;
                    case 3:
                        state = state with {Brush = new("404040")};
                        break;
                    case 4:
                        state = state with {Brush = new("000000")};
                        break;
                    case 5:
                        state = state with {Brush = new(null)};
                        break;
                    case 6:
                        state = state with {Pen = new("FFFFFF", 0)};
                        break;
                    case 7:
                        state = state with {Pen = new("000000", 0)};
                        break;
                    case 8:
                        state = state with {Pen = new(null, 0)};
                        break;
                    case >= 10 and <= 17:
                        state = state with {Font = new("Arial", 12, false, false, false, 0)};
                        break;
                }

                return;
            }

            switch (objects.GetValueOrDefault(index))
            {
                case Pen pen:
                    state = state with {Pen = pen};
                    break;
                case Brush brush:
                    state = state with {Brush = brush};
                    break;
                case Font font:
                    state = state with {Font = font};
                    break;
            }
        }

        void Save() =>
            saved.Push(state);

        void Restore(int which)
        {
            // A negative index counts back from the latest save; a positive one is an absolute level.
            var count = which < 0 ? -which : saved.Count - which + 1;
            for (var index = 0; index < count && saved.Count > 0; index++)
            {
                state = saved.Pop();
            }
        }

        // A cubic Bézier through its control points, as straight segments.
        static void Bezier(List<(double X, double Y)> figure, (double X, double Y) start, (double X, double Y) control1, (double X, double Y) control2, (double X, double Y) end)
        {
            const int steps = 12;
            for (var step = 1; step <= steps; step++)
            {
                var t = (double) step / steps;
                var u = 1 - t;
                figure.Add((
                    u * u * u * start.X + 3 * u * u * t * control1.X + 3 * u * t * t * control2.X + t * t * t * end.X,
                    u * u * u * start.Y + 3 * u * u * t * control1.Y + 3 * u * t * t * control2.Y + t * t * t * end.Y));
            }
        }

        static List<(double X, double Y)> Ellipse(double left, double top, double right, double bottom)
        {
            var centreX = (left + right) / 2;
            var centreY = (top + bottom) / 2;
            return [.. Enumerable.Range(0, 48).Select(_ => (centreX + (right - left) / 2 * Math.Cos(_ * Math.PI / 24), centreY + (bottom - top) / 2 * Math.Sin(_ * Math.PI / 24)))];
        }

        List<(double X, double Y)> DeviceFigure(IEnumerable<(double X, double Y)> logical) =>
            [.. logical.Select(_ => Device(_.X, _.Y))];

        public void PlayEmf(byte[] data)
        {
            // EMR_HEADER: the frame in hundredths of a millimetre, and the reference device's size in
            // pixels and millimetres, which turn it into device units.
            var frame = (L: BitConverter.ToInt32(data, 24), T: BitConverter.ToInt32(data, 28), R: BitConverter.ToInt32(data, 32), B: BitConverter.ToInt32(data, 36));
            var devicePixels = (X: BitConverter.ToInt32(data, 72), Y: BitConverter.ToInt32(data, 76));
            var deviceMillimetres = (X: BitConverter.ToInt32(data, 80), Y: BitConverter.ToInt32(data, 84));
            if (deviceMillimetres is {X: > 0, Y: > 0})
            {
                pixelsPerMillimetreX = (double) devicePixels.X / deviceMillimetres.X;
                pixelsPerMillimetreY = (double) devicePixels.Y / deviceMillimetres.Y;
            }

            frameLeft = frame.L / 100.0 * pixelsPerMillimetreX;
            frameTop = frame.T / 100.0 * pixelsPerMillimetreY;
            frameWidth = (frame.R - frame.L) / 100.0 * pixelsPerMillimetreX;
            frameHeight = (frame.B - frame.T) / 100.0 * pixelsPerMillimetreY;
            if (frameWidth <= 0 || frameHeight <= 0)
            {
                // No frame: the bounds of what is drawn, in device units, stand in.
                frameLeft = BitConverter.ToInt32(data, 8);
                frameTop = BitConverter.ToInt32(data, 12);
                frameWidth = BitConverter.ToInt32(data, 16) - frameLeft;
                frameHeight = BitConverter.ToInt32(data, 20) - frameTop;
            }

            var offset = 0;
            while (offset + 8 <= data.Length)
            {
                var type = BitConverter.ToUInt32(data, offset);
                var size = (int) BitConverter.ToUInt32(data, offset + 4);
                if (size < 8 || offset + size > data.Length)
                {
                    break;
                }

                if (type == 14)
                {
                    break;
                }

                PlayEmfRecord(data, offset, type, size);
                offset += size;
            }
        }

        void PlayEmfRecord(byte[] data, int at, uint type, int size)
        {
            int I32(int offset) => BitConverter.ToInt32(data, at + offset);
            uint U32(int offset) => BitConverter.ToUInt32(data, at + offset);
            short I16(int offset) => BitConverter.ToInt16(data, at + offset);
            float F32(int offset) => BitConverter.ToSingle(data, at + offset);

            // Points from offset on: count of them, 32-bit or 16-bit pairs.
            List<(double X, double Y)> Points(int offset, int count, bool small) =>
                [.. Enumerable.Range(0, count).Select(_ => small ? (I16(offset + _ * 4), I16(offset + _ * 4 + 2)) : ((double) I32(offset + _ * 8), (double) I32(offset + _ * 8 + 4)))];

            switch (type)
            {
                case 9:
                    state = state with {WindowExtent = (I32(8), I32(12))};
                    break;
                case 10:
                    state = state with {WindowOrigin = (I32(8), I32(12))};
                    break;
                case 11:
                    state = state with {ViewportExtent = (I32(8), I32(12))};
                    break;
                case 12:
                    state = state with {ViewportOrigin = (I32(8), I32(12))};
                    break;
                case 17:
                    state = state with {MapMode = (int) U32(8)};
                    break;
                case 22:
                    state = state with {TextAlign = U32(8)};
                    break;
                case 24:
                    state = state with {TextColor = Color(U32(8))};
                    break;
                case 33:
                    Save();
                    break;
                case 34:
                    Restore(I32(8));
                    break;
                case 35:
                    state = state with {World = new(F32(8), F32(12), F32(16), F32(20), F32(24), F32(28))};
                    break;
                case 36:
                {
                    var transform = new Transform(F32(8), F32(12), F32(16), F32(20), F32(24), F32(28));
                    state = state with
                    {
                        World = U32(32) switch
                        {
                            1 => Transform.Identity,
                            2 => transform.Then(state.World),
                            3 => state.World.Then(transform),
                            4 => transform,
                            _ => state.World
                        }
                    };
                    break;
                }
                case 38:
                    // A pen of style PS_NULL (5) draws nothing.
                    objects[U32(8)] = new Pen((U32(12) & 0xF) == 5 ? null : Color(U32(24)), I32(16));
                    break;
                case 95:
                    objects[U32(8)] = new Pen((U32(28) & 0xF) == 5 ? null : Color(U32(40)), U32(32));
                    break;
                case 39:
                    // BS_NULL (1) fills nothing; a hatch is drawn as its colour, which is what it mostly shows.
                    objects[U32(8)] = new Brush(U32(12) == 1 ? null : Color(U32(16)));
                    break;
                case 82:
                {
                    var face = Encoding.Unicode.GetString(data, at + 40, 64).Split('\0')[0];
                    objects[U32(8)] = ReadFont(I32(12), I32(20), I32(28), data[at + 32] != 0, data[at + 33] != 0, face);
                    break;
                }
                case 37:
                    Select(U32(8));
                    break;
                case 40:
                    objects.Remove(U32(8));
                    break;
                case 27:
                    state = state with {Position = (I32(8), I32(12))};
                    path?.Add([Device(I32(8), I32(12))]);
                    break;
                case 54:
                {
                    var to = (X: (double) I32(8), Y: (double) I32(12));
                    if (path != null)
                    {
                        if (path.Count == 0)
                        {
                            path.Add([Device(state.Position.X, state.Position.Y)]);
                        }

                        path[^1].Add(Device(to.X, to.Y));
                    }
                    else
                    {
                        Draw([[Device(state.Position.X, state.Position.Y), Device(to.X, to.Y)]], false);
                    }

                    state = state with {Position = to};
                    break;
                }
                case 3 or 4 or 86 or 87:
                {
                    var small = type is 86 or 87;
                    Draw([DeviceFigure(Points(28, (int) U32(24), small))], type is 3 or 86);
                    break;
                }
                case 7 or 8 or 90 or 91:
                {
                    var small = type is 90 or 91;
                    var polygons = (int) U32(24);
                    var counts = Enumerable.Range(0, polygons).Select(_ => (int) U32(32 + _ * 4)).ToList();
                    var pointsAt = 32 + polygons * 4;
                    var figures = new List<List<(double X, double Y)>>();
                    foreach (var count in counts)
                    {
                        figures.Add(DeviceFigure(Points(pointsAt, count, small)));
                        pointsAt += count * (small ? 4 : 8);
                    }

                    Draw(figures, type is 8 or 91);
                    break;
                }
                case 2 or 85:
                {
                    var points = Points(28, (int) U32(24), type == 85);
                    if (points.Count >= 4)
                    {
                        var figure = new List<(double X, double Y)> {points[0]};
                        for (var index = 1; index + 2 < points.Count; index += 3)
                        {
                            Bezier(figure, figure[^1], points[index], points[index + 1], points[index + 2]);
                        }

                        Draw([DeviceFigure(figure)], false);
                    }

                    break;
                }
                case 5 or 6 or 88 or 89:
                {
                    // From the current position, which they move on to their last point.
                    var points = Points(28, (int) U32(24), type is 88 or 89);
                    if (points.Count == 0)
                    {
                        break;
                    }

                    var figure = new List<(double X, double Y)> {state.Position};
                    if (type is 6 or 89)
                    {
                        figure.AddRange(points);
                    }
                    else
                    {
                        for (var index = 0; index + 2 < points.Count; index += 3)
                        {
                            Bezier(figure, figure[^1], points[index], points[index + 1], points[index + 2]);
                        }
                    }

                    state = state with {Position = points[^1]};
                    if (path != null)
                    {
                        if (path.Count == 0)
                        {
                            path.Add([]);
                        }

                        path[^1].AddRange(DeviceFigure(figure.Skip(path[^1].Count > 0 ? 1 : 0)));
                    }
                    else
                    {
                        Draw([DeviceFigure(figure)], false);
                    }

                    break;
                }
                case 43 or 44:
                {
                    var (left, top, right, bottom) = (I32(8), I32(12), I32(16), I32(20));
                    Draw([DeviceFigure([(left, top), (right, top), (right, bottom), (left, bottom)])], true);
                    break;
                }
                case 42:
                    Draw([DeviceFigure(Ellipse(I32(8), I32(12), I32(16), I32(20)))], true);
                    break;
                case 59:
                    path = [];
                    pathClosed = false;
                    break;
                case 61:
                    pathClosed = true;
                    break;
                case 62 or 63 or 64:
                {
                    var figures = path ?? [];
                    path = null;
                    Draw(figures, type != 64 || pathClosed, fill: type != 64, stroke: type != 62);
                    break;
                }
                case 60:
                    break;
                case 84 or 83:
                {
                    // EMRTEXT: the reference point, then the string, UTF-16 or 8-bit.
                    var count = (int) U32(44);
                    var stringAt = (int) U32(48);
                    if (count <= 0 || stringAt + count > size)
                    {
                        break;
                    }

                    var text = type == 84
                        ? Encoding.Unicode.GetString(data, at + stringAt, count * 2)
                        : Encoding.Latin1.GetString(data, at + stringAt, count);
                    Text(I32(36), I32(40), text);
                    break;
                }
            }
        }

        public void PlayWmf(byte[] data)
        {
            logicalFrame = true;
            var offset = 0;
            if (BitConverter.ToUInt32(data, 0) == placeableKey)
            {
                // The placeable header's box, in the metafile's logical units, is its frame.
                var (left, top, right, bottom) = (BitConverter.ToInt16(data, 6), BitConverter.ToInt16(data, 8), BitConverter.ToInt16(data, 10), BitConverter.ToInt16(data, 12));
                SetFrame(left, top, right - left, bottom - top);
                offset = 22;
            }

            // The META_HEADER: its size in words, then the records.
            var headerWords = BitConverter.ToUInt16(data, offset + 2);
            if (headerWords != 9)
            {
                return;
            }

            offset += 18;
            while (offset + 6 <= data.Length)
            {
                var words = (int) BitConverter.ToUInt32(data, offset);
                var function = BitConverter.ToUInt16(data, offset + 4);
                if (words < 3 || offset + words * 2 > data.Length || function == 0)
                {
                    break;
                }

                PlayWmfRecord(data, offset + 6, function, words * 2 - 6);
                offset += words * 2;
            }
        }

        void SetFrame(double left, double top, double width, double height)
        {
            if (wmfFramed || width == 0 || height == 0)
            {
                return;
            }

            // A flipped box (a y axis running up) is the same frame read the other way.
            frameLeft = Math.Min(left, left + width);
            frameTop = Math.Min(top, top + height);
            frameWidth = Math.Abs(width);
            frameHeight = Math.Abs(height);
            wmfFramed = true;
        }

        // The lowest free slot in a WMF's object table, where the next object it creates goes.
        uint FreeSlot()
        {
            uint slot = 0;
            while (objects.ContainsKey(slot))
            {
                slot++;
            }

            return slot;
        }

        void PlayWmfRecord(byte[] data, int at, ushort function, int length)
        {
            short I16(int offset) => BitConverter.ToInt16(data, at + offset);
            ushort U16(int offset) => BitConverter.ToUInt16(data, at + offset);
            uint U32(int offset) => BitConverter.ToUInt32(data, at + offset);

            List<(double X, double Y)> Points(int offset, int count) =>
                [.. Enumerable.Range(0, count).Select(_ => ((double) I16(offset + _ * 4), (double) I16(offset + _ * 4 + 2)))];

            switch (function)
            {
                case 0x020B:
                    state = state with {WindowOrigin = (I16(2), I16(0))};
                    break;
                case 0x020C:
                    state = state with {WindowExtent = (I16(2), I16(0))};
                    SetFrame(state.WindowOrigin.X, state.WindowOrigin.Y, I16(2), I16(0));
                    break;
                case 0x012E:
                    state = state with {TextAlign = U16(0)};
                    break;
                case 0x0209:
                    state = state with {TextColor = Color(U32(0))};
                    break;
                case 0x001E:
                    Save();
                    break;
                case 0x0127:
                    Restore(I16(0));
                    break;
                case 0x02FA:
                    objects[FreeSlot()] = new Pen((U16(0) & 0xF) == 5 ? null : Color(U32(6)), I16(2));
                    break;
                case 0x02FC:
                    objects[FreeSlot()] = new Brush(U16(0) == 1 ? null : Color(U32(2)));
                    break;
                case 0x02FB:
                {
                    var face = Encoding.Latin1.GetString(data, at + 18, Math.Max(0, Math.Min(32, length - 18))).Split('\0')[0];
                    objects[FreeSlot()] = ReadFont(I16(0), I16(4), I16(8), data[at + 10] != 0, data[at + 11] != 0, face);
                    break;
                }
                case 0x00F7 or 0x01F9 or 0x0142 or 0x06FF:
                    // Palettes, pattern brushes and regions take a slot too, though nothing here uses them.
                    objects[FreeSlot()] = new Brush(null);
                    break;
                case 0x012D:
                    Select(U16(0));
                    break;
                case 0x01F0:
                    objects.Remove(U16(0));
                    break;
                case 0x0214:
                    state = state with {Position = (I16(2), I16(0))};
                    break;
                case 0x0213:
                {
                    var to = (X: (double) I16(2), Y: (double) I16(0));
                    Draw([[state.Position, to]], false);
                    state = state with {Position = to};
                    break;
                }
                case 0x0324 or 0x0325:
                    Draw([Points(2, I16(0))], function == 0x0324);
                    break;
                case 0x0538:
                {
                    var polygons = U16(0);
                    var pointsAt = 2 + polygons * 2;
                    var figures = new List<List<(double X, double Y)>>();
                    for (var index = 0; index < polygons; index++)
                    {
                        var count = U16(2 + index * 2);
                        figures.Add(Points(pointsAt, count));
                        pointsAt += count * 4;
                    }

                    Draw(figures, true);
                    break;
                }
                case 0x041B or 0x061C:
                {
                    // Bottom, right, top, left — after a round rectangle's corner size.
                    var skip = function == 0x061C ? 4 : 0;
                    var (bottom, right, top, left) = (I16(skip), I16(skip + 2), I16(skip + 4), I16(skip + 6));
                    Draw([[(left, top), (right, top), (right, bottom), (left, bottom)]], true);
                    break;
                }
                case 0x0418:
                    Draw([Ellipse(I16(6), I16(4), I16(2), I16(0))], true);
                    break;
                case 0x0521:
                {
                    var count = U16(0);
                    var padded = count + (count & 1);
                    Text(I16(2 + padded + 2), I16(2 + padded), Encoding.Latin1.GetString(data, at + 2, count));
                    break;
                }
                case 0x0A32:
                {
                    var (y, x, count, options) = (I16(0), I16(2), I16(4), U16(6));
                    var textAt = 8 + ((options & 0x6) != 0 ? 8 : 0);
                    if (count > 0 && textAt + count <= length)
                    {
                        Text(x, y, Encoding.Latin1.GetString(data, at + textAt, count));
                    }

                    break;
                }
            }
        }
    }
}
