using System;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// The dragon as an <see cref="IPoseSource"/>, for previews and tests of the export pipeline: poses come
    /// from <see cref="DragonPoses"/> through <see cref="DragonPose.ToPoseResult"/> (joints and prop only,
    /// no bone rotations). Its timing is the dragon's, which are <see cref="PackTiming"/>'s defaults.
    /// </summary>
    public sealed class DragonPoseSource : IPoseSource
    {
        public DragonPoseSource(BodyShape body)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public BodyShape Body { get; }

        public PackTiming Timing { get; } = new PackTiming();

        public PoseResult Pose(string animation, double t, double move) =>
            DragonPoses.Pose(animation, Body, t, move).ToPoseResult();
    }
}
