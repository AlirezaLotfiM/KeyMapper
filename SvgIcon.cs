using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace KeyMapper
{
    /// <summary>
    /// Lightweight monochrome SVG renderer for the supplied Solar outline icons.
    /// It intentionally recolors every shape with Foreground so icons follow themes.
    /// </summary>
    public sealed class SvgIcon : FrameworkElement
    {
        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(
                nameof(Source),
                typeof(string),
                typeof(SvgIcon),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.AffectsMeasure |
                    FrameworkPropertyMetadataOptions.AffectsRender,
                    OnSourceChanged));

        public static readonly DependencyProperty ForegroundProperty =
            DependencyProperty.Register(
                nameof(Foreground),
                typeof(Brush),
                typeof(SvgIcon),
                new FrameworkPropertyMetadata(
                    Brushes.Black,
                    FrameworkPropertyMetadataOptions.Inherits |
                    FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Dictionary<string, SvgDocument> Cache =
            new(StringComparer.OrdinalIgnoreCase);
        private SvgDocument? _document;

        public string Source
        {
            get => (string)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width)
                ? 16d
                : availableSize.Width;
            double height = double.IsInfinity(availableSize.Height)
                ? 16d
                : availableSize.Height;
            return new Size(Math.Max(0d, width), Math.Max(0d, height));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            SvgDocument? document = _document;
            if (document == null || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            double scale = Math.Min(
                ActualWidth / Math.Max(1d, document.ViewBox.Width),
                ActualHeight / Math.Max(1d, document.ViewBox.Height));
            double offsetX = (ActualWidth - (document.ViewBox.Width * scale)) / 2d;
            double offsetY = (ActualHeight - (document.ViewBox.Height * scale)) / 2d;

            drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
            drawingContext.PushTransform(new ScaleTransform(scale, scale));
            drawingContext.PushTransform(
                new TranslateTransform(-document.ViewBox.X, -document.ViewBox.Y));

            foreach (SvgPrimitive primitive in document.Primitives)
            {
                Brush? fill = primitive.HasFill
                    ? WithOpacity(Foreground, primitive.FillOpacity)
                    : null;
                Pen? pen = null;
                if (primitive.HasStroke)
                {
                    pen = new Pen(
                        WithOpacity(Foreground, primitive.StrokeOpacity),
                        primitive.StrokeWidth)
                    {
                        StartLineCap = primitive.LineCap,
                        EndLineCap = primitive.LineCap,
                        LineJoin = primitive.LineJoin
                    };
                }

                drawingContext.DrawGeometry(fill, pen, primitive.Geometry);
            }

            drawingContext.Pop();
            drawingContext.Pop();
            drawingContext.Pop();
        }

        private static Brush WithOpacity(Brush source, double opacity)
        {
            if (opacity >= 0.999d)
            {
                return source;
            }

            Brush clone = source.CloneCurrentValue();
            clone.Opacity *= Math.Clamp(opacity, 0d, 1d);
            clone.Freeze();
            return clone;
        }

        private static void OnSourceChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs args)
        {
            var icon = (SvgIcon)dependencyObject;
            icon.LoadDocument(args.NewValue as string);
        }

        private void LoadDocument(string? source)
        {
            _document = null;
            if (string.IsNullOrWhiteSpace(source))
            {
                InvalidateVisual();
                return;
            }

            try
            {
                if (!Cache.TryGetValue(source, out SvgDocument? document))
                {
                    document = ParseDocument(source);
                    Cache[source] = document;
                }
                _document = document;
            }
            catch
            {
                _document = null;
            }

            InvalidateVisual();
        }

        private static SvgDocument ParseDocument(string source)
        {
            Uri uri = source.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(source, UriKind.Absolute)
                : new Uri(
                    $"pack://application:,,,/{source.TrimStart('/')}",
                    UriKind.Absolute);
            var resource = Application.GetResourceStream(uri)
                ?? throw new FileNotFoundException("SVG resource not found.", source);
            using Stream stream = resource.Stream;
            XDocument xml = XDocument.Load(stream);
            XElement root = xml.Root
                ?? throw new InvalidDataException("SVG root is missing.");
            Rect viewBox = ParseViewBox(root);
            var primitives = new List<SvgPrimitive>();
            ParseChildren(root, primitives);
            return new SvgDocument(viewBox, primitives);
        }

        private static void ParseChildren(
            XElement parent,
            ICollection<SvgPrimitive> primitives)
        {
            foreach (XElement element in parent.Elements())
            {
                string name = element.Name.LocalName;
                if (name is "defs" or "clipPath" or "mask" or "title")
                {
                    continue;
                }
                if (name == "g")
                {
                    ParseChildren(element, primitives);
                    continue;
                }

                Geometry? geometry = name switch
                {
                    "path" => ParsePath(element),
                    "rect" => ParseRectangle(element),
                    "circle" => ParseCircle(element),
                    "ellipse" => ParseEllipse(element),
                    "line" => ParseLine(element),
                    "polygon" => ParsePoints(element, true),
                    "polyline" => ParsePoints(element, false),
                    _ => null
                };
                if (geometry == null)
                {
                    continue;
                }

                string fill = InheritedAttribute(element, "fill") ?? "black";
                string stroke = InheritedAttribute(element, "stroke") ?? string.Empty;
                bool hasFill = !string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase);
                bool hasStroke = !string.IsNullOrWhiteSpace(stroke) &&
                    !string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase);
                if (!hasFill && !hasStroke)
                {
                    hasStroke = true;
                }

                primitives.Add(
                    new SvgPrimitive(
                        geometry,
                        hasFill,
                        hasStroke,
                        Number(InheritedAttribute(element, "stroke-width"), 1d),
                        Number(InheritedAttribute(element, "fill-opacity"), 1d) *
                            Number(InheritedAttribute(element, "opacity"), 1d),
                        Number(InheritedAttribute(element, "stroke-opacity"), 1d) *
                            Number(InheritedAttribute(element, "opacity"), 1d),
                        ParseLineCap(InheritedAttribute(element, "stroke-linecap")),
                        ParseLineJoin(InheritedAttribute(element, "stroke-linejoin"))));
            }
        }

        private static Geometry? ParsePath(XElement element)
        {
            string? data = element.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(data)) return null;
            Geometry geometry = Geometry.Parse(data);
            if (geometry.CanFreeze) geometry.Freeze();
            return geometry;
        }

        private static Geometry ParseRectangle(XElement element)
        {
            double x = Number(element.Attribute("x")?.Value);
            double y = Number(element.Attribute("y")?.Value);
            double width = Number(element.Attribute("width")?.Value);
            double height = Number(element.Attribute("height")?.Value);
            double radiusX = Number(element.Attribute("rx")?.Value);
            double radiusY = Number(element.Attribute("ry")?.Value, radiusX);
            return new RectangleGeometry(
                new Rect(x, y, width, height),
                radiusX,
                radiusY);
        }

        private static Geometry ParseCircle(XElement element)
        {
            double cx = Number(element.Attribute("cx")?.Value);
            double cy = Number(element.Attribute("cy")?.Value);
            double radius = Number(element.Attribute("r")?.Value);
            return new EllipseGeometry(new Point(cx, cy), radius, radius);
        }

        private static Geometry ParseEllipse(XElement element)
        {
            return new EllipseGeometry(
                new Point(
                    Number(element.Attribute("cx")?.Value),
                    Number(element.Attribute("cy")?.Value)),
                Number(element.Attribute("rx")?.Value),
                Number(element.Attribute("ry")?.Value));
        }

        private static Geometry ParseLine(XElement element)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(
                    Number(element.Attribute("x1")?.Value),
                    Number(element.Attribute("y1")?.Value)),
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(
                new LineSegment(
                    new Point(
                        Number(element.Attribute("x2")?.Value),
                        Number(element.Attribute("y2")?.Value)),
                    true));
            return new PathGeometry(new[] { figure });
        }

        private static Geometry? ParsePoints(XElement element, bool close)
        {
            string? raw = element.Attribute("points")?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            double[] values = raw
                .Replace(',', ' ')
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            if (values.Length < 4) return null;

            var figure = new PathFigure
            {
                StartPoint = new Point(values[0], values[1]),
                IsClosed = close,
                IsFilled = close
            };
            for (int index = 2; index + 1 < values.Length; index += 2)
            {
                figure.Segments.Add(
                    new LineSegment(new Point(values[index], values[index + 1]), true));
            }
            return new PathGeometry(new[] { figure });
        }

        private static Rect ParseViewBox(XElement root)
        {
            string? raw = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                double[] values = raw
                    .Replace(',', ' ')
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();
                if (values.Length == 4)
                {
                    return new Rect(values[0], values[1], values[2], values[3]);
                }
            }

            return new Rect(
                0,
                0,
                Number(root.Attribute("width")?.Value, 24d),
                Number(root.Attribute("height")?.Value, 24d));
        }

        private static string? InheritedAttribute(XElement element, string name)
        {
            for (XElement? current = element; current != null; current = current.Parent)
            {
                XAttribute? attribute = current.Attribute(name);
                if (attribute != null) return attribute.Value;
            }
            return null;
        }

        private static double Number(string? value, double fallback = 0d)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string normalized = value.Trim().Replace("px", string.Empty);
            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result)
                ? result
                : fallback;
        }

        private static PenLineCap ParseLineCap(string? value) => value switch
        {
            "round" => PenLineCap.Round,
            "square" => PenLineCap.Square,
            _ => PenLineCap.Flat
        };

        private static PenLineJoin ParseLineJoin(string? value) => value switch
        {
            "round" => PenLineJoin.Round,
            "bevel" => PenLineJoin.Bevel,
            _ => PenLineJoin.Miter
        };

        private sealed record SvgDocument(Rect ViewBox, IReadOnlyList<SvgPrimitive> Primitives);

        private sealed record SvgPrimitive(
            Geometry Geometry,
            bool HasFill,
            bool HasStroke,
            double StrokeWidth,
            double FillOpacity,
            double StrokeOpacity,
            PenLineCap LineCap,
            PenLineJoin LineJoin);
    }
}
