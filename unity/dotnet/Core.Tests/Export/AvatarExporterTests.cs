using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Export
{
    public class AvatarExporterTests
    {
        /// <summary>
        /// Draws an opaque red square around the anchor (and a black one on the shadow pass) and records
        /// every request.
        /// </summary>
        private sealed class RecordingRenderer : IFrameRenderer
        {
            public readonly List<FrameRequest> Requests = new List<FrameRequest>();
            public int WrongSizeAfter = int.MaxValue;

            public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored) =>
                new Bounds3(new Vec3(-0.3, -0.3, 0), new Vec3(0.3, 0.3, 1.5));

            public RenderResult Render(FrameRequest request)
            {
                Requests.Add(request);
                CanvasSpec c = request.Canvas;
                int w = Requests.Count > WrongSizeAfter ? c.RenderWidth + 1 : c.RenderWidth;
                if (request.Pass == RenderPass.Shadow)
                {
                    var silhouette = new ImageF(w, c.RenderHeight);
                    silhouette.Fill(1, 1, 1, 1);
                    Square(silhouette, c, 0, 0, 0);
                    return new RenderResult { Silhouette = silhouette };
                }
                var black = new ImageF(w, c.RenderHeight);
                black.Fill(0, 0, 0, 1);
                var white = new ImageF(w, c.RenderHeight);
                white.Fill(1, 1, 1, 1);
                Square(black, c, 1, 0, 0);
                Square(white, c, 1, 0, 0);
                return new RenderResult { OnBlack = black, OnWhite = white, Linear = false };
            }

            private static void Square(ImageF image, CanvasSpec c, float r, float g, float b)
            {
                int ax = Math.Clamp(c.AnchorX * c.Supersampling, 4, image.Width - 4);
                int ay = Math.Clamp(c.AnchorY * c.Supersampling, 4, image.Height - 4);
                for (int y = ay - 4; y < ay; y++)
                {
                    for (int x = ax - 2; x < ax + 2; x++)
                    {
                        image[x, y, 0] = r;
                        image[x, y, 1] = g;
                        image[x, y, 2] = b;
                    }
                }
            }
        }

        private static ExportSettings Small(bool flight = false) =>
            new ExportSettings { PixelsPerTile = 32, Supersampling = 1, Flight = flight };

        [Test]
        public void RendersEveryPassOfEveryFrame()
        {
            var renderer = new RecordingRenderer();
            var exporter = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small(true));
            var reports = new List<ExportProgress>();
            ExportedArt art = exporter.Render(new ListProgress(reports));

            Assert.That(renderer.Requests.Count, Is.EqualTo(exporter.TotalRenders));
            Assert.That(reports.Count, Is.EqualTo(exporter.TotalRenders));
            Assert.That(reports.Last().Done, Is.EqualTo(reports.Last().Total));
            Assert.That(art.Animations.Keys, Is.EquivalentTo(AnimationSpec.All.Select(a => a.Name)));
            foreach (AnimationInfo a in AnimationSpec.All)
            {
                Dictionary<string, SheetSet> layers = art.Animations[a.Name];
                string[] expected = a.Name == AnimationSpec.RunGun
                    ? new[] { PackLayers.Base, PackLayers.Shadow, PackLayers.ShadowFlipped }
                    : new[] { PackLayers.Base, PackLayers.Shadow };
                Assert.That(layers.Keys, Is.EquivalentTo(expected), a.Name);
                Assert.That(layers[PackLayers.Base].DirectionCount, Is.EqualTo(a.DirectionCount));
                Assert.That(layers[PackLayers.Base].FrameCount, Is.EqualTo(a.FrameCount));
            }
            Assert.That(art.Icon.Width, Is.EqualTo(PackArt.IconSize));
            Assert.That(art.Thumbnail.Width, Is.EqualTo(PackFormat.ThumbnailSize));
        }

        [Test]
        public void FlippedShadowsMirrorTheCharacterAndItsOrientation()
        {
            var renderer = new RecordingRenderer();
            new AvatarExporter(new DragonPoseSource(BodyShape.Female), renderer, Small()).Render();
            List<FrameRequest> runGun = renderer.Requests.Where(r => r.Animation == AnimationSpec.RunGun).ToList();
            List<FrameRequest> mirrored = runGun.Where(r => r.Mirrored).ToList();
            Assert.That(mirrored.Count, Is.EqualTo(18 * 12));
            Assert.That(mirrored.All(r => r.Pass == RenderPass.Shadow));
            foreach (FrameRequest r in mirrored)
            {
                double original = AnimationSpec.Direction(AnimationSpec.RunGun, r.Direction).Orientation;
                Assert.That(r.Orientation, Is.EqualTo(MathUtil.PositiveModulo(1 - original, 1)).Within(1e-12));
            }
            Assert.That(renderer.Requests.Where(r => r.Animation != AnimationSpec.RunGun).All(r => !r.Mirrored));
        }

        [Test]
        public void EachLayerKeepsOneCanvasForAllItsFrames()
        {
            var renderer = new RecordingRenderer();
            new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small()).Render();
            foreach (IGrouping<(string, RenderPass, bool), FrameRequest> layer in
                     renderer.Requests.GroupBy(r => (r.Animation, r.Pass, r.Mirrored)))
            {
                Assert.That(layer.Select(r => (r.Canvas.Width, r.Canvas.Height, r.Canvas.AnchorX, r.Canvas.AnchorY)).Distinct().Count(),
                    Is.EqualTo(1), layer.Key.ToString());
            }
        }

        [Test]
        public void WrongImageSizesAreReported()
        {
            var renderer = new RecordingRenderer { WrongSizeAfter = 5 };
            var exporter = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small());
            var e = Assert.Throws<InvalidOperationException>(() => exporter.Render());
            Assert.That(e.Message, Does.Contain("idle direction"));
            Assert.That(e.Message, Does.Contain("expected"));
        }

        [Test]
        public void CanBeCancelled()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var renderer = new RecordingRenderer();
                var exporter = new AvatarExporter(new DragonPoseSource(BodyShape.Male), renderer, Small());
                var progress = new ListProgress(new List<ExportProgress>(), p =>
                {
                    if (p.Done == 10)
                    {
                        cancel.Cancel();
                    }
                });
                Assert.Throws<OperationCanceledException>(() => exporter.Render(progress, cancel.Token));
                Assert.That(renderer.Requests.Count, Is.LessThan(13));
            }
        }

        [Test]
        public void RejectsBadSettings()
        {
            var renderer = new RecordingRenderer();
            var poses = new DragonPoseSource(BodyShape.Male);
            Assert.Throws<ArgumentException>(() => new AvatarExporter(poses, renderer, new ExportSettings { PixelsPerTile = 48 }));
            Assert.Throws<ArgumentException>(() => new AvatarExporter(poses, renderer, new ExportSettings { Supersampling = 0 }));
            Assert.Throws<ArgumentException>(() => new AvatarExporter(poses, renderer, new ExportSettings { MaxSheetSize = 5000 }));
            Assert.Throws<ArgumentNullException>(() => new AvatarExporter(null, renderer, new ExportSettings()));
        }

        [Test]
        public void HumanoidPackFromTheStickRendererIsValid()
        {
            var rig = HumanoidRig.Measure(SyntheticRigs.Generic(1.6));
            var poses = new HumanoidAnimations(rig);
            var exporter = new AvatarExporter(poses, new StickFigureRenderer(), Small(true));
            ExportedArt art = exporter.Render();
            var content = new PackContent
            {
                Id = "exporter-test",
                Title = "Exporter test",
                SourceAvatar = "synthetic rig",
                Permission = "test data",
                GeneratorVersion = "0.0.1",
            };
            exporter.Fill(content, art);
            Assert.That(content.Timing.DistancePerFrame, Is.EqualTo(poses.Timing.DistancePerFrame));
            Assert.That(content.PixelsPerTile, Is.EqualTo(32));
            Assert.That(content.Flight, Is.True);
            Assert.That(PackValidator.Validate(content), Is.Empty);
            // every layer has something drawn: no 1x1 placeholder frames
            foreach (KeyValuePair<string, Dictionary<string, SheetSet>> animation in content.Animations)
            {
                foreach (KeyValuePair<string, SheetSet> layer in animation.Value)
                {
                    Assert.That(layer.Value.FrameWidth, Is.GreaterThan(4), animation.Key + " " + layer.Key);
                }
            }

            PythonTools.RequirePython();
            string dir = Path.Combine(Path.GetTempPath(), "exporter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var output = new DirectoryPackOutput(dir, false);
                new PackWriter(PngWriter.Encode).Write(content, output);
                string printed = PythonTools.Run(
                    "import subprocess, sys\n" +
                    "r = subprocess.run([sys.executable, 'tools/validate_sprites.py', '--pack', sys.argv[1]], capture_output=True, text=True)\n" +
                    "print(r.returncode, r.stdout, r.stderr)",
                    "", output.PackDirectory);
                Assert.That(printed, Does.StartWith("0 OK:"), printed);
                Assert.That(printed, Does.Not.Contain("warning"), printed);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class ListProgress : IProgress<ExportProgress>
        {
            private readonly List<ExportProgress> list;
            private readonly Action<ExportProgress> also;

            public ListProgress(List<ExportProgress> list, Action<ExportProgress> also = null)
            {
                this.list = list;
                this.also = also;
            }

            public void Report(ExportProgress value)
            {
                list.Add(value);
                also?.Invoke(value);
            }
        }
    }

    public class PackArtTests
    {
        private static Image8 Figure()
        {
            var image = new Image8(60, 100);
            for (int y = 10; y < 90; y++)
            {
                for (int x = 20; x < 40; x++)
                {
                    image[x, y, 0] = (byte)(y < 30 ? 250 : 40);
                    image[x, y, 3] = 255;
                }
            }
            return image;
        }

        [Test]
        public void IconShowsTheTopOfTheFigure()
        {
            Image8 icon = PackArt.Icon(Figure());
            Assert.That((icon.Width, icon.Height), Is.EqualTo((64, 64)));
            // the bright head (top 20 of 80 rows) is in the upper part of the icon
            Assert.That(icon[32, 5, 0], Is.GreaterThan(200));
            Assert.That(icon[32, 5, 3], Is.EqualTo(255));
        }

        [Test]
        public void ThumbnailFitsTheWholeFigureOnADarkSquare()
        {
            Image8 thumbnail = PackArt.Thumbnail(Figure());
            Assert.That((thumbnail.Width, thumbnail.Height), Is.EqualTo((144, 144)));
            Assert.That(thumbnail[0, 0, 3], Is.EqualTo(255), "opaque background");
            Assert.That(thumbnail[72, 72, 0], Is.EqualTo(40), "the figure's body in the middle");
            Assert.That(thumbnail[72, 12, 0], Is.GreaterThan(200), "its head near the top");
        }

        [Test]
        public void EmptyFramesAreRejected()
        {
            Assert.Throws<InvalidOperationException>(() => PackArt.Icon(new Image8(8, 8)));
            Assert.Throws<ArgumentNullException>(() => PackArt.Thumbnail(null));
        }
    }
}
