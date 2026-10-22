using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static System.FormattableString;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// The sprite sheets of one layer of an animation (docs/avatar-pack-format.md, "Sheets"): one row
    /// per direction, one column per frame, every frame <see cref="FrameWidth"/> x
    /// <see cref="FrameHeight"/> px. A layer too tall for one file is split by rows into several files
    /// of <see cref="LinesPerFile"/> rows each; the last file may be shorter.
    /// </summary>
    public sealed class SheetSet
    {
        /// <summary>
        /// Checks that <paramref name="files"/> hold exactly the rows and columns described: one file
        /// when <paramref name="linesPerFile"/> is null, otherwise ceil(directions / lines) files.
        /// The list is copied; the images are not.
        /// </summary>
        public SheetSet(int directionCount, int frameCount, int frameWidth, int frameHeight, double shiftX,
            double shiftY, IReadOnlyList<Image8> files, int? linesPerFile)
        {
            if (directionCount < 1 || frameCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(directionCount), "direction and frame counts must be positive");
            }
            if (frameWidth < 1 || frameHeight < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(frameWidth), "frame sizes must be positive");
            }
            if (!IsFinite(shiftX) || !IsFinite(shiftY))
            {
                throw new ArgumentException("the shift must be finite", nameof(shiftX));
            }
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("a sheet set needs at least one file", nameof(files));
            }
            if (linesPerFile.HasValue && (linesPerFile.Value < 1 || linesPerFile.Value >= directionCount))
            {
                throw new ArgumentOutOfRangeException(nameof(linesPerFile),
                    "lines per file must be between 1 and the direction count - 1 (null for a single file)");
            }
            DirectionCount = directionCount;
            FrameCount = frameCount;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            ShiftX = shiftX;
            ShiftY = shiftY;
            LinesPerFile = linesPerFile;
            int expectedFiles = linesPerFile.HasValue ? (directionCount + linesPerFile.Value - 1) / linesPerFile.Value : 1;
            if (files.Count != expectedFiles)
            {
                throw new ArgumentException(
                    Invariant($"{files.Count} files given, but {directionCount} directions need {expectedFiles}"), nameof(files));
            }
            var copy = new Image8[files.Count];
            int width = frameCount * frameWidth;
            for (int k = 0; k < copy.Length; k++)
            {
                Image8 file = files[k];
                int height = RowsInFile(k) * frameHeight;
                if (file == null || file.Width != width || file.Height != height)
                {
                    string actual = file == null ? "missing" : Invariant($"{file.Width}x{file.Height}");
                    throw new ArgumentException(Invariant($"file {k + 1} is {actual}, expected {width}x{height}"), nameof(files));
                }
                copy[k] = file;
            }
            Files = new ReadOnlyCollection<Image8>(copy);
        }

        /// <summary>Rows of the sheet: the animation's directions.</summary>
        public int DirectionCount { get; }

        /// <summary>Columns of the sheet: the animation's frames.</summary>
        public int FrameCount { get; }

        /// <summary>Size of one frame, px.</summary>
        public int FrameWidth { get; }
        public int FrameHeight { get; }

        /// <summary>
        /// Offset of the frame centre from the entity position, tiles, x to the right and y down
        /// (Factorio's <c>shift</c>).
        /// </summary>
        public double ShiftX { get; }
        public double ShiftY { get; }

        /// <summary>The sheet images, top rows first.</summary>
        public IReadOnlyList<Image8> Files { get; }

        /// <summary>Rows per file when the sheet is split, null for a single file.</summary>
        public int? LinesPerFile { get; }

        /// <summary>Number of rows (directions) in file <paramref name="fileIndex"/> (0-based).</summary>
        public int RowsInFile(int fileIndex)
        {
            int lines = LinesPerFile ?? DirectionCount;
            int first = fileIndex * lines;
            if (fileIndex < 0 || first >= DirectionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            }
            return Math.Min(lines, DirectionCount - first);
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
