using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>How the exporter renders.</summary>
    public sealed class ExportSettings
    {
        /// <summary>Pixels per tile of the sheets: 64 (sprites drawn at scale 0.5) or 32.</summary>
        public int PixelsPerTile { get; set; } = Conventions.DefaultPixelsPerTile;

        /// <summary>The renderer draws this many times the final resolution per axis (antialiasing).</summary>
        public int Supersampling { get; set; } = 2;

        /// <summary>Also render the flight animations (Space Age mech armor).</summary>
        public bool Flight { get; set; } = true;

        /// <summary>Largest sheet file, px; larger sheets are split by rows.</summary>
        public int MaxSheetSize { get; set; } = PackFormat.MaxSheetSize;

        /// <summary>Empty pixels kept around everything drawn when planning canvases.</summary>
        public int CanvasMarginPixels { get; set; } = CanvasPlanner.DefaultMarginPixels;

        public void Validate()
        {
            if (PixelsPerTile != 64 && PixelsPerTile != 32)
            {
                throw new ArgumentException("pixels per tile must be 64 or 32, not " + PixelsPerTile);
            }
            if (Supersampling < 1 || Supersampling > 8)
            {
                throw new ArgumentException("supersampling must be between 1 and 8, not " + Supersampling);
            }
            if (MaxSheetSize < 1 || MaxSheetSize > PackFormat.MaxSheetSize)
            {
                throw new ArgumentException("the largest sheet must be between 1 and " + PackFormat.MaxSheetSize + " px");
            }
            if (CanvasMarginPixels < 0)
            {
                throw new ArgumentException("the canvas margin must not be negative");
            }
        }
    }

    /// <summary>Progress of an export, reported after every render.</summary>
    public readonly struct ExportProgress
    {
        public ExportProgress(string animation, int done, int total)
        {
            Animation = animation;
            Done = done;
            Total = total;
        }

        /// <summary>The animation being rendered.</summary>
        public string Animation { get; }

        /// <summary>Renders finished and planned in the whole export.</summary>
        public int Done { get; }
        public int Total { get; }

        public double Fraction => Total == 0 ? 1 : (double)Done / Total;
    }

    /// <summary>Everything rendered for a pack: sheets of every animation and layer, the icon and the thumbnail.</summary>
    public sealed class ExportedArt
    {
        public ExportedArt(Dictionary<string, Dictionary<string, SheetSet>> animations, Image8 icon, Image8 thumbnail)
        {
            Animations = animations;
            Icon = icon;
            Thumbnail = thumbnail;
        }

        /// <summary>Animation name -> layer name (<see cref="PackLayers"/>) -> sheets.</summary>
        public Dictionary<string, Dictionary<string, SheetSet>> Animations { get; }
        public Image8 Icon { get; }
        public Image8 Thumbnail { get; }
    }

    /// <summary>
    /// Renders every frame of every animation of a pack and builds its sprite sheets: the part of the
    /// exporter that is the same whether frames come from Unity or from a software renderer.
    ///
    /// For each animation: pose every frame (<see cref="IPoseSource"/>), measure what each pose draws to
    /// plan one canvas per layer (<see cref="CanvasPlanner"/>), then for every frame render the character
    /// on black and on white (difference matting), and its ground shadow; for run-gun also the shadow of
    /// the mirrored character ("shadow-flipped", what Factorio draws while it mirrors the sprite for a gun
    /// aiming west, as tools/dragon_render/generate.py does). Each layer's frames become sheets
    /// (<see cref="SheetBuilder"/>).
    /// </summary>
    public sealed class AvatarExporter
    {
        /// <summary>The sheet row whose figure faces the viewer (south) in 8-direction animations.</summary>
        private const int SouthRow = 4;

        private readonly IPoseSource poses;
        private readonly IFrameRenderer renderer;
        private readonly ExportSettings settings;

        public AvatarExporter(IPoseSource poses, IFrameRenderer renderer, ExportSettings settings)
        {
            this.poses = poses ?? throw new ArgumentNullException(nameof(poses));
            this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
        }

        /// <summary>The animations this export renders, in order.</summary>
        public IEnumerable<AnimationInfo> Animations => AnimationSpec.ForPack(settings.Flight);

        /// <summary>Renders a frame takes in total (color, shadow and shadow-flipped passes).</summary>
        public int TotalRenders
        {
            get
            {
                int total = 0;
                foreach (AnimationInfo a in Animations)
                {
                    total += a.DirectionCount * a.FrameCount * PassesPerFrame(a.Name);
                }
                return total;
            }
        }

        private static int PassesPerFrame(string animation) =>
            PackLayers.OptionalFor(animation).Contains(PackLayers.ShadowFlipped) ? 3 : 2;

        /// <summary>Renders everything. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
        public ExportedArt Render(IProgress<ExportProgress> progress = null, CancellationToken cancel = default)
        {
            var sheets = new Dictionary<string, Dictionary<string, SheetSet>>(StringComparer.Ordinal);
            int total = TotalRenders;
            int done = 0;
            Image8 portrait = null;
            foreach (AnimationInfo animation in Animations)
            {
                var job = new AnimationJob(this, animation);
                sheets[animation.Name] = job.Render(progress, cancel, ref done, total);
                if (animation.Name == AnimationSpec.Idle)
                {
                    portrait = job.BaseFrame(Math.Min(SouthRow, animation.DirectionCount - 1), 0);
                }
            }
            if (portrait == null)
            {
                throw new InvalidOperationException("the idle animation was not rendered");
            }
            return new ExportedArt(sheets, PackArt.Icon(portrait), PackArt.Thumbnail(portrait));
        }

        /// <summary>Copies rendered art and the pose source's timing into a pack.</summary>
        public void Fill(PackContent content, ExportedArt art)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (art == null)
            {
                throw new ArgumentNullException(nameof(art));
            }
            content.PixelsPerTile = settings.PixelsPerTile;
            content.Flight = settings.Flight;
            content.Timing = poses.Timing;
            content.Icon = art.Icon;
            content.Thumbnail = art.Thumbnail;
            content.Animations.Clear();
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in art.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    content.SetLayer(animation.Key, layer.Key, layer.Value);
                }
            }
        }

        /// <summary>The renders of one animation.</summary>
        private sealed class AnimationJob
        {
            private readonly AvatarExporter owner;
            private readonly AnimationInfo animation;
            private readonly bool flippedShadow;
            private readonly PoseResult[,] framePoses;
            private readonly DirectionInfo[] directions;
            private Image8[][] baseFrames;

            public AnimationJob(AvatarExporter owner, AnimationInfo animation)
            {
                this.owner = owner;
                this.animation = animation;
                flippedShadow = PackLayers.OptionalFor(animation.Name).Contains(PackLayers.ShadowFlipped);
                directions = new DirectionInfo[animation.DirectionCount];
                framePoses = new PoseResult[animation.DirectionCount, animation.FrameCount];
                for (int d = 0; d < animation.DirectionCount; d++)
                {
                    directions[d] = AnimationSpec.Direction(animation.Name, d);
                    for (int f = 0; f < animation.FrameCount; f++)
                    {
                        double t = AnimationSpec.FramePhase(animation.Name, f);
                        framePoses[d, f] = owner.poses.Pose(animation.Name, t, directions[d].Move)
                            ?? throw new InvalidOperationException(
                                $"the pose source returned no pose for {animation.Name} frame {f + 1}");
                    }
                }
            }

            public Image8 BaseFrame(int direction, int frame) => baseFrames[direction][frame];

            /// <summary>The orientation of the mirrored character whose shadow is "shadow-flipped".</summary>
            private static double MirroredOrientation(double orientation) => MathUtil.PositiveModulo(1.0 - orientation, 1.0);

            public Dictionary<string, SheetSet> Render(IProgress<ExportProgress> progress, CancellationToken cancel,
                ref int done, int total)
            {
                ExportSettings s = owner.settings;
                var colorBounds = new List<Bounds3>();
                var mirroredBounds = new List<Bounds3>();
                for (int d = 0; d < animation.DirectionCount; d++)
                {
                    for (int f = 0; f < animation.FrameCount; f++)
                    {
                        colorBounds.Add(owner.renderer.MeasureWorldBounds(framePoses[d, f], directions[d].Orientation, false));
                        if (flippedShadow)
                        {
                            mirroredBounds.Add(owner.renderer.MeasureWorldBounds(framePoses[d, f],
                                MirroredOrientation(directions[d].Orientation), true));
                        }
                    }
                }
                CanvasSpec colorCanvas = CanvasPlanner.PlanColor(colorBounds, s.PixelsPerTile, s.Supersampling, s.CanvasMarginPixels);
                CanvasSpec shadowCanvas = CanvasPlanner.PlanShadow(colorBounds, s.PixelsPerTile, s.Supersampling, s.CanvasMarginPixels);
                CanvasSpec flippedCanvas = flippedShadow
                    ? CanvasPlanner.PlanShadow(mirroredBounds, s.PixelsPerTile, s.Supersampling, s.CanvasMarginPixels)
                    : default;

                baseFrames = NewFrames();
                Image8[][] shadowFrames = NewFrames();
                Image8[][] flippedFrames = flippedShadow ? NewFrames() : null;
                for (int d = 0; d < animation.DirectionCount; d++)
                {
                    for (int f = 0; f < animation.FrameCount; f++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        double orientation = directions[d].Orientation;
                        baseFrames[d][f] = RenderColor(d, f, orientation, colorCanvas);
                        Report(progress, ref done, total);
                        shadowFrames[d][f] = RenderShadow(d, f, orientation, false, shadowCanvas);
                        Report(progress, ref done, total);
                        if (flippedShadow)
                        {
                            flippedFrames[d][f] = RenderShadow(d, f, MirroredOrientation(orientation), true, flippedCanvas);
                            Report(progress, ref done, total);
                        }
                    }
                }

                var layers = new Dictionary<string, SheetSet>(StringComparer.Ordinal)
                {
                    [PackLayers.Base] = Sheets(baseFrames, colorCanvas),
                    [PackLayers.Shadow] = Sheets(shadowFrames, shadowCanvas),
                };
                if (flippedShadow)
                {
                    layers[PackLayers.ShadowFlipped] = Sheets(flippedFrames, flippedCanvas);
                }
                return layers;
            }

            private void Report(IProgress<ExportProgress> progress, ref int done, int total)
            {
                done++;
                progress?.Report(new ExportProgress(animation.Name, done, total));
            }

            private Image8[][] NewFrames()
            {
                var frames = new Image8[animation.DirectionCount][];
                for (int d = 0; d < frames.Length; d++)
                {
                    frames[d] = new Image8[animation.FrameCount];
                }
                return frames;
            }

            private FrameRequest Request(int direction, int frame, double orientation, bool mirrored, RenderPass pass,
                CanvasSpec canvas) => new FrameRequest
                {
                    Animation = animation.Name,
                    Direction = direction,
                    Frame = frame,
                    Pose = framePoses[direction, frame],
                    Orientation = orientation,
                    Mirrored = mirrored,
                    Pass = pass,
                    Canvas = canvas,
                };

            private Image8 RenderColor(int direction, int frame, double orientation, CanvasSpec canvas)
            {
                RenderResult result = owner.renderer.Render(Request(direction, frame, orientation, false, RenderPass.Color, canvas));
                string what = Describe(direction, frame, "color pass");
                CheckSize(result?.OnBlack, canvas, what + " on black");
                CheckSize(result.OnWhite, canvas, what + " on white");
                ImageF premultiplied = Matting.FromBlackAndWhite(result.OnBlack, result.OnWhite, result.Linear);
                return ImageConvert.ToImage8(Downsample.Box(premultiplied, canvas.Supersampling));
            }

            private Image8 RenderShadow(int direction, int frame, double orientation, bool mirrored, CanvasSpec canvas)
            {
                RenderResult result = owner.renderer.Render(Request(direction, frame, orientation, mirrored, RenderPass.Shadow, canvas));
                CheckSize(result?.Silhouette, canvas, Describe(direction, frame, mirrored ? "flipped shadow pass" : "shadow pass"));
                return ImageConvert.ShadowFromSilhouette(result.Silhouette, canvas.Supersampling);
            }

            private string Describe(int direction, int frame, string pass) =>
                $"{animation.Name} direction {direction + 1} frame {frame + 1} {pass}";

            private static void CheckSize(ImageF image, CanvasSpec canvas, string what)
            {
                if (image == null)
                {
                    throw new InvalidOperationException("the renderer returned no image for " + what);
                }
                if (image.Width != canvas.RenderWidth || image.Height != canvas.RenderHeight)
                {
                    throw new InvalidOperationException(
                        $"the renderer returned a {image.Width}x{image.Height} image for {what}; expected {canvas.RenderWidth}x{canvas.RenderHeight}");
                }
            }

            private SheetSet Sheets(Image8[][] frames, CanvasSpec canvas) =>
                SheetBuilder.Build(frames, canvas.AnchorX, canvas.AnchorY, canvas.PixelsPerTile, owner.settings.MaxSheetSize);
        }
    }
}
