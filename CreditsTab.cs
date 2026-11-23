using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SpyTool2
{
    public static class CreditsTab
    {
        public const string AuthorNick = "DEYXZAZZ";
        public const string RoleText = "Developer & Reverse Engineer • Spy Tool (Mono / IL2CPP / Unity Assets)";
        public const string GithubUrl = "https://github.com/deyxzazz";
        public const string TelegramUrl = "https://t.me/NexBoxRe";
        public const string YoutubeUrl = "https://www.youtube.com/@deyxzazz";
        public const string DiscordTag = "deyxzazz";

        private static readonly Color[] IceGradientColors =
        {
            Color.Parse("#0284C7"),
            Color.Parse("#38BDF8"),
            Color.Parse("#7DD3FC"),
            Color.Parse("#FFFFFF"),
            Color.Parse("#7DD3FC"),
            Color.Parse("#38BDF8"),
            Color.Parse("#0284C7")
        };

        private static readonly Color CubeOuterColor = Color.Parse("#38BDF8");
        private static readonly Color CubeInnerColor = Color.Parse("#BAE6FD");

        private sealed class FloatingCube
        {
            public Grid Container { get; init; } = null!;
            public Rectangle OuterFace { get; init; } = null!;
            public Rectangle InnerFace { get; init; } = null!;
            public Line Edge1 { get; init; } = null!;
            public Line Edge2 { get; init; } = null!;
            public Line Edge3 { get; init; } = null!;
            public Line Edge4 { get; init; } = null!;
            public RotateTransform Rotate { get; init; } = null!;
            public ScaleTransform Scale { get; init; } = null!;

            public double NormX { get; set; }
            public double NormY { get; set; }
            public double VelX { get; set; }
            public double VelY { get; set; }

            public double BaseZ { get; set; }
            public double ZPhase { get; set; }
            public double ZSpeed { get; set; }
            public double ZAmplitude { get; set; }

            public double Angle { get; set; }
            public double SpinSpeed { get; set; }
            public double BaseSize { get; set; }
        }

        public static Control Build(Window owner, Action<string>? setStatus = null)
        {
            var rng = new Random(2026);
            double gradientPhase = 0.0;
            double targetMouseX = 0.0;
            double targetMouseY = 0.0;
            double smoothMouseX = 0.0;
            double smoothMouseY = 0.0;

            var rootGrid = new Grid
            {
                ClipToBounds = true,
                Background = Brushes.Transparent
            };

            var bgCanvas = new Canvas
            {
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            rootGrid.Children.Add(bgCanvas);

            var outerStrokeBrush = new SolidColorBrush(CubeOuterColor);
            var innerStrokeBrush = new SolidColorBrush(CubeInnerColor);

            var cubes = new List<FloatingCube>(24);
            for (int i = 0; i < 24; i++)
            {
                double baseZ = 0.25 + rng.NextDouble() * 0.75;
                double baseSize = 36.0 + rng.NextDouble() * 28.0;

                double moveAngle = rng.NextDouble() * Math.PI * 2.0;
                double speed = 0.015 + rng.NextDouble() * 0.032;

                var rotate = new RotateTransform(rng.NextDouble() * 360.0);
                var scale = new ScaleTransform(1.0, 1.0);
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(scale);
                transformGroup.Children.Add(rotate);

                double innerInset = baseSize * 0.24;
                double innerSize = baseSize - innerInset * 2.0;

                var outerFace = new Rectangle
                {
                    Width = baseSize,
                    Height = baseSize,
                    RadiusX = 0,
                    RadiusY = 0,
                    Fill = Brushes.Transparent,
                    Stroke = outerStrokeBrush,
                    StrokeThickness = 1.4
                };

                var innerFace = new Rectangle
                {
                    Width = innerSize,
                    Height = innerSize,
                    RadiusX = 0,
                    RadiusY = 0,
                    Fill = Brushes.Transparent,
                    Stroke = innerStrokeBrush,
                    StrokeThickness = 0.9,
                    Opacity = 0.65,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var edge1 = new Line
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(innerInset, innerInset),
                    Stroke = innerStrokeBrush,
                    StrokeThickness = 0.85,
                    Opacity = 0.55
                };

                var edge2 = new Line
                {
                    StartPoint = new Point(baseSize, 0),
                    EndPoint = new Point(baseSize - innerInset, innerInset),
                    Stroke = innerStrokeBrush,
                    StrokeThickness = 0.85,
                    Opacity = 0.55
                };

                var edge3 = new Line
                {
                    StartPoint = new Point(0, baseSize),
                    EndPoint = new Point(innerInset, baseSize - innerInset),
                    Stroke = innerStrokeBrush,
                    StrokeThickness = 0.85,
                    Opacity = 0.55
                };

                var edge4 = new Line
                {
                    StartPoint = new Point(baseSize, baseSize),
                    EndPoint = new Point(baseSize - innerInset, baseSize - innerInset),
                    Stroke = innerStrokeBrush,
                    StrokeThickness = 0.85,
                    Opacity = 0.55
                };

                var cubeContainer = new Grid
                {
                    Width = baseSize,
                    Height = baseSize,
                    RenderTransformOrigin = RelativePoint.Center,
                    RenderTransform = transformGroup
                };

                cubeContainer.Children.Add(outerFace);
                cubeContainer.Children.Add(innerFace);
                cubeContainer.Children.Add(edge1);
                cubeContainer.Children.Add(edge2);
                cubeContainer.Children.Add(edge3);
                cubeContainer.Children.Add(edge4);

                bgCanvas.Children.Add(cubeContainer);

                cubes.Add(new FloatingCube
                {
                    Container = cubeContainer,
                    OuterFace = outerFace,
                    InnerFace = innerFace,
                    Edge1 = edge1,
                    Edge2 = edge2,
                    Edge3 = edge3,
                    Edge4 = edge4,
                    Rotate = rotate,
                    Scale = scale,
                    NormX = rng.NextDouble(),
                    NormY = rng.NextDouble(),
                    VelX = Math.Cos(moveAngle) * speed,
                    VelY = Math.Sin(moveAngle) * speed,
                    BaseZ = baseZ,
                    ZPhase = rng.NextDouble() * Math.PI * 2.0,
                    ZSpeed = 0.45 + rng.NextDouble() * 0.75,
                    ZAmplitude = 0.22 + rng.NextDouble() * 0.25,
                    Angle = rotate.Angle,
                    SpinSpeed = (rng.NextDouble() * 28.0 - 14.0),
                    BaseSize = baseSize
                });
            }

            var titleGradientBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
            };

            var borderGradientBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
            };

            var buttonBorderBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
            };

            void UpdateGradients(double phase)
            {
                int count = IceGradientColors.Length;
                double shift = phase % 1.0;

                var titleStops = new GradientStops();
                var borderStops = new GradientStops();
                var btnStops = new GradientStops();

                for (int i = 0; i < count; i++)
                {
                    double baseOffset = (double)i / (count - 1);
                    double wave = 0.5 + 0.5 * Math.Sin((baseOffset + shift) * Math.PI * 2.0);
                    double colorPos = wave * (count - 1);
                    int idx0 = (int)Math.Floor(colorPos);
                    int idx1 = Math.Min(idx0 + 1, count - 1);
                    double frac = colorPos - idx0;

                    Color c0 = IceGradientColors[idx0];
                    Color c1 = IceGradientColors[idx1];
                    Color lerped = Color.FromArgb(
                        255,
                        (byte)(c0.R + (c1.R - c0.R) * frac),
                        (byte)(c0.G + (c1.G - c0.G) * frac),
                        (byte)(c0.B + (c1.B - c0.B) * frac));

                    titleStops.Add(new GradientStop(lerped, baseOffset));
                    borderStops.Add(new GradientStop(lerped, baseOffset));
                    btnStops.Add(new GradientStop(lerped, baseOffset));
                }

                titleGradientBrush.GradientStops = titleStops;
                borderGradientBrush.GradientStops = borderStops;
                buttonBorderBrush.GradientStops = btnStops;
            }

            UpdateGradients(0.0);

            var appTitleText = new TextBlock
            {
                Text = "SPY • REVERSE ENGINEERING SUITE",
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 2.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = titleGradientBrush,
                Margin = new Thickness(0, 4, 0, 6)
            };

            var authorNickText = new TextBlock
            {
                Text = AuthorNick,
                FontSize = 40,
                FontWeight = FontWeight.ExtraBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = titleGradientBrush,
                Margin = new Thickness(0, 2, 0, 8)
            };

            var roleTextBlock = new TextBlock
            {
                Text = RoleText,
                FontSize = 14,
                Opacity = 0.88,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 520,
                Margin = new Thickness(0, 0, 0, 22)
            };

            Button CreateSocialButton(string title, string subtitle, Action onClick)
            {
                var stack = new StackPanel
                {
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                stack.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13.5,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11,
                    Opacity = 0.75,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var btn = new Button
                {
                    Content = stack,
                    Width = 142,
                    Height = 52,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = buttonBorderBrush,
                    Background = new SolidColorBrush(Color.FromArgb(40, 56, 189, 248)),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = new Cursor(StandardCursorType.Hand)
                };

                btn.Click += (_, _) => onClick();
                return btn;
            }

            void OpenExternalUrl(string url, string label)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    setStatus?.Invoke($"Открыта ссылка {label}: {url}");
                }
                catch (Exception ex)
                {
                    setStatus?.Invoke($"Не удалось открыть {label}: {ex.Message}");
                }
            }

            var btnGithub = CreateSocialButton("GitHub", "Исходный код", () => OpenExternalUrl(GithubUrl, "GitHub"));
            var btnTelegram = CreateSocialButton("Telegram", "Связь и новости", () => OpenExternalUrl(TelegramUrl, "Telegram"));
            var btnYoutube = CreateSocialButton("YouTube", "Видео и гайды", () => OpenExternalUrl(YoutubeUrl, "YouTube"));
            var btnDiscord = CreateSocialButton("Discord", DiscordTag, async () =>
            {
                try
                {
                    if (owner.Clipboard != null)
                    {
                        await owner.Clipboard.SetTextAsync(DiscordTag);
                        setStatus?.Invoke($"Discord тег «{DiscordTag}» скопирован в буфер обмена!");
                    }
                    else
                    {
                        setStatus?.Invoke($"Discord: {DiscordTag}");
                    }
                }
                catch (Exception ex)
                {
                    setStatus?.Invoke($"Ошибка буфера обмена: {ex.Message}");
                }
            });

            var socialWrap = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 18)
            };
            btnGithub.Margin = new Thickness(6);
            btnTelegram.Margin = new Thickness(6);
            btnYoutube.Margin = new Thickness(6);
            btnDiscord.Margin = new Thickness(6);

            socialWrap.Children.Add(btnGithub);
            socialWrap.Children.Add(btnTelegram);
            socialWrap.Children.Add(btnYoutube);
            socialWrap.Children.Add(btnDiscord);

            var footerHint = new TextBlock
            {
                Text = "Спасибо за использование Spy! Двигайте курсором для 3D параллакс-эффекта.",
                FontSize = 11.5,
                Opacity = 0.65,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var cardContent = new StackPanel
            {
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            cardContent.Children.Add(appTitleText);
            cardContent.Children.Add(authorNickText);
            cardContent.Children.Add(roleTextBlock);
            cardContent.Children.Add(socialWrap);
            cardContent.Children.Add(footerHint);

            var centerCard = new Border
            {
                MaxWidth = 680,
                Padding = new Thickness(34, 28, 34, 28),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(2),
                BorderBrush = borderGradientBrush,
                Background = new SolidColorBrush(Color.FromArgb(200, 14, 22, 34)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20),
                BoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 32,
                    Spread = 2,
                    Color = Color.FromArgb(110, 2, 132, 199)
                }),
                Child = cardContent
            };

            rootGrid.Children.Add(centerCard);

            rootGrid.PointerMoved += (_, e) =>
            {
                var bounds = rootGrid.Bounds;
                if (bounds.Width <= 1 || bounds.Height <= 1)
                    return;

                var pos = e.GetPosition(rootGrid);
                targetMouseX = (pos.X / bounds.Width - 0.5) * 2.0;
                targetMouseY = (pos.Y / bounds.Height - 0.5) * 2.0;
            };

            rootGrid.PointerExited += (_, _) =>
            {
                targetMouseX = 0.0;
                targetMouseY = 0.0;
            };

            const double dt = 0.016;
            var animTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            animTimer.Tick += (_, _) =>
            {
                gradientPhase += dt * 0.28;
                if (gradientPhase > 1000.0)
                    gradientPhase -= 1000.0;
                UpdateGradients(gradientPhase);

                smoothMouseX += (targetMouseX - smoothMouseX) * 0.08;
                smoothMouseY += (targetMouseY - smoothMouseY) * 0.08;

                double canvasW = bgCanvas.Bounds.Width;
                double canvasH = bgCanvas.Bounds.Height;
                if (canvasW <= 1 || canvasH <= 1)
                    return;

                foreach (var cube in cubes)
                {
                    cube.ZPhase += cube.ZSpeed * dt;
                    double z = Math.Clamp(cube.BaseZ + Math.Sin(cube.ZPhase) * cube.ZAmplitude, 0.12, 1.35);

                    double zSpeedFactor = 0.55 + z * 0.65;
                    cube.NormX += cube.VelX * zSpeedFactor * dt;
                    cube.NormY += cube.VelY * zSpeedFactor * dt;

                    const double boundMin = -0.06;
                    const double boundMax = 1.06;

                    if (cube.NormX < boundMin)
                    {
                        cube.NormX = boundMin;
                        cube.VelX = Math.Abs(cube.VelX);
                    }
                    else if (cube.NormX > boundMax)
                    {
                        cube.NormX = boundMax;
                        cube.VelX = -Math.Abs(cube.VelX);
                    }

                    if (cube.NormY < boundMin)
                    {
                        cube.NormY = boundMin;
                        cube.VelY = Math.Abs(cube.VelY);
                    }
                    else if (cube.NormY > boundMax)
                    {
                        cube.NormY = boundMax;
                        cube.VelY = -Math.Abs(cube.VelY);
                    }

                    double scaleFactor = 0.45 + z * 0.85;
                    cube.Scale.ScaleX = scaleFactor;
                    cube.Scale.ScaleY = scaleFactor;

                    cube.Container.Opacity = Math.Clamp(0.08 + z * 0.32, 0.08, 0.55);

                    double outerThickness = 0.8 + z * 1.3;
                    double innerThickness = 0.5 + z * 0.7;
                    cube.OuterFace.StrokeThickness = outerThickness;
                    cube.InnerFace.StrokeThickness = innerThickness;
                    cube.Edge1.StrokeThickness = innerThickness;
                    cube.Edge2.StrokeThickness = innerThickness;
                    cube.Edge3.StrokeThickness = innerThickness;
                    cube.Edge4.StrokeThickness = innerThickness;

                    cube.Angle = (cube.Angle + cube.SpinSpeed * (0.6 + z * 0.5) * dt) % 360.0;
                    cube.Rotate.Angle = cube.Angle;

                    double parallaxOffsetX = -smoothMouseX * 48.0 * z;
                    double parallaxOffsetY = -smoothMouseY * 48.0 * z;

                    double px = cube.NormX * canvasW - cube.BaseSize * 0.5 + parallaxOffsetX;
                    double py = cube.NormY * canvasH - cube.BaseSize * 0.5 + parallaxOffsetY;

                    Canvas.SetLeft(cube.Container, px);
                    Canvas.SetTop(cube.Container, py);
                }
            };

            rootGrid.AttachedToVisualTree += (_, _) =>
            {
                if (!animTimer.IsEnabled)
                    animTimer.Start();
            };

            rootGrid.DetachedFromVisualTree += (_, _) =>
            {
                if (animTimer.IsEnabled)
                    animTimer.Stop();
            };

            return rootGrid;
        }
    }
}
