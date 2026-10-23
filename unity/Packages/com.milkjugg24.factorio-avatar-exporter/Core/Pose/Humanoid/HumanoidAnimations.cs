using System;
using System.Collections.Generic;

namespace MilkJugg24.FactorioAvatarExporter.Core
{
    /// <summary>
    /// The poses of every pack animation for a humanoid avatar. Each animation keeps the phase structure
    /// of the dragon's (tools/dragon_render/anims.py, ported in Core/Pose/Dragon/), so the pack timing
    /// stays the dragon's: feet land on run frames 1 and 7, the pick hits on mine frame 9. What differs:
    /// <list type="bullet">
    /// <item>sizes come from the avatar's measured rig: strides from its leg length, hand positions from
    /// its arm length, props from its height;</item>
    /// <item>feet are planted flat on their measured ball points and roll over the ball (the dragon walks
    /// on its toes);</item>
    /// <item>leaning forward is forward (the dragon's lean tilts it backwards, see DragonModel.TorsoFrame);</item>
    /// <item>both hands reach the gun;</item>
    /// <item>the corpse lies on its side facing the camera; the renderer puts its lowest point on the ground.</item>
    /// </list>
    /// </summary>
    public sealed class HumanoidAnimations : IPoseSource
    {
        /// <summary>Leg length of the dragon, which the gait sizes of anims.py were made for, tiles.</summary>
        private const double DragonLegLength = 0.6;

        /// <summary>Arm length (upper arm + forearm) of the dragon, tiles.</summary>
        private const double DragonArmLength = 0.45;

        /// <summary>
        /// Half the fore-aft travel of a foot over a run cycle at dragon size, tiles (anims.run uses 0.25;
        /// a little shorter reads better on plantigrade legs).
        /// </summary>
        private const double RunStride = 0.22;

        /// <summary>
        /// Tiles the vanilla engineer covers in one run cycle (two steps): 22 frames x distance_per_frame 0.13 /
        /// animation_speed 0.6.
        /// </summary>
        private const double EngineerRunCycle = 22 * 0.13 / 0.6;

        private readonly HumanoidRig rig;

        /// <summary>Gait sizes relative to the dragon's, from leg length.</summary>
        private readonly double legScale;

        /// <summary>Hand offsets relative to the dragon's, from arm length.</summary>
        private readonly double armScale;

        /// <summary>Prop sizes relative to the dragon's, from overall height.</summary>
        private readonly double sizeScale;

        /// <summary>Pelvis height when standing with the knees slightly bent.</summary>
        private readonly double standingPelvis;

        public HumanoidAnimations(HumanoidRig rig)
        {
            this.rig = rig ?? throw new ArgumentNullException(nameof(rig));
            legScale = rig.LegLength / DragonLegLength;
            armScale = rig.ArmLength / DragonArmLength;
            sizeScale = rig.HeadTopHeight / Conventions.DefaultCharacterHeight;
            standingPelvis = rig.HipsHeight - 0.015 * rig.LegLength;
            AnimationInfo run = AnimationSpec.Get(AnimationSpec.Run);
            Timing = new PackTiming
            {
                // the game moves distance_per_frame / animation_speed tiles per running frame: one cycle
                // covers the vanilla engineer's ground, longer for longer legs, like the dragon's 0.238
                DistancePerFrame = EngineerRunCycle * legScale * run.DefaultSpeed / run.FrameCount,
            };
        }

        public PackTiming Timing { get; }

        public PoseResult Pose(string animation, double t, double move)
        {
            if (double.IsNaN(t) || double.IsInfinity(t) || double.IsNaN(move) || double.IsInfinity(move))
            {
                throw new ArgumentException("t and move must be finite numbers");
            }
            switch (animation)
            {
                case AnimationSpec.Idle: return Idle(t, false);
                case AnimationSpec.IdleGun: return Idle(t, true);
                case AnimationSpec.Run: return Run(t, false, move);
                case AnimationSpec.RunGun: return Run(t, true, move);
                case AnimationSpec.Mine: return Mine(t);
                case AnimationSpec.Corpse: return Corpse();
                case AnimationSpec.Hover: return Hover(t, false, Conventions.FlyHeight, 0);
                case AnimationSpec.HoverGun: return Hover(t, true, Conventions.FlyHeight, 0);
                case AnimationSpec.Fly: return Fly(t, false, move);
                case AnimationSpec.FlyGun: return Fly(t, true, move);
                case AnimationSpec.TakeOff: return TakeOff(t);
                default: throw new ArgumentException("unknown animation " + animation, nameof(animation));
            }
        }

        // -- frames and helpers ---------------------------------------------------------------------

        /// <summary>
        /// A body frame: pitched forward by <paramref name="pitchForward"/> radians, turned
        /// counterclockwise (seen from above) by <paramref name="yaw"/>, rolled by <paramref name="roll"/>
        /// (the parameters of model.py's torso_frame, with forward pitch actually forward).
        /// </summary>
        public static Quat Frame(double pitchForward, double yaw = 0, double roll = 0) =>
            Quat.FromAxisAngle(Vec3.UnitX, -pitchForward) * Quat.FromAxisAngle(Vec3.UnitZ, yaw) *
            Quat.FromAxisAngle(Vec3.UnitY, roll);

        /// <summary>Turns a model space vector clockwise (seen from above) by <paramref name="move"/> radians (anims._turn).</summary>
        private static Vec3 Turn(Vec3 v, double move) =>
            move == 0 ? v : Quat.FromAxisAngle(Vec3.UnitZ, -move).Rotate(v);

        private static readonly int[] Sides = { 1, -1 };

        private HumanoidPoseBuilder Torso(Vec3 pelvis, double lean, double twist = 0, double hipTwist = 0,
            double headPitch = 0, double headYaw = 0)
        {
            var b = new HumanoidPoseBuilder(rig);
            b.SetPelvis(pelvis, Frame(lean * 0.5, hipTwist));
            b.SetTorso(Frame(lean, twist));
            // the head stays closer to level than the torso and looks where the body goes
            b.SetHead(Frame(lean * 0.35 + headPitch, twist * 0.5 + headYaw));
            return b;
        }

        private static Quat ChestFrame(double lean, double twist = 0) => Frame(lean, twist);

        private Vec3 Shoulder(HumanoidPoseBuilder b, int side) => b.TorsoPoint(HumanBoneHierarchy.UpperArm(side));

        private Vec3 HipJoint(HumanoidPoseBuilder b, int side) => b.TorsoPoint(HumanBoneHierarchy.UpperLeg(side));

        /// <summary>Arms hanging at the sides (anims._relaxed_arms), slightly curled hands.</summary>
        private void RelaxedArms(HumanoidPoseBuilder b, Quat chest, double swing = 0, double spread = 0)
        {
            foreach (int side in Sides)
            {
                Vec3 s = Shoulder(b, side);
                Vec3 hand = s + chest.Right * (side * (0.055 * armScale + spread * sizeScale))
                            + chest.Forward * ((0.04 + swing * side) * armScale)
                            - chest.Up * (rig.ArmLength * 0.9);
                b.SetArm(side, hand, -chest.Forward + chest.Right * (side * 0.35), null, 0.25);
            }
        }

        /// <summary>Both feet flat on the ground on their rest ball points (anims._standing_legs).</summary>
        private void StandingFeet(HumanoidPoseBuilder b, double stance = 0, double front = 0)
        {
            foreach (int side in Sides)
            {
                Vec3 rest = rig.Ball(side);
                var ball = new Vec3(rest.X + side * stance * legScale, rest.Y + front * side * legScale, rest.Z);
                Vec3 forward = rig.FootForward(side);
                b.SetFoot(side, ball, forward, 0, forward + Vec3.UnitX * (side * 0.15));
            }
        }

        /// <summary>
        /// The ball target that puts the ankle exactly at <paramref name="ankle"/> for a foot facing
        /// <paramref name="forward"/> with <paramref name="heelLift"/>: the inverse of how
        /// <see cref="HumanoidPoseBuilder.SetFoot"/> places the ankle.
        /// </summary>
        private Vec3 BallForAnkle(int side, Vec3 ankle, Vec3 forward, double heelLift)
        {
            Vec3 restForward = rig.FootForward(side);
            Vec3 f = forward.Horizontal.Normalized;
            Quat yaw = Quat.FromAxisAngle(Vec3.UnitZ, Math.Atan2(Vec3.Cross(restForward, f).Z, Vec3.Dot(restForward, f)));
            Quat foot = yaw * Quat.FromAxisAngle(Vec3.Cross(restForward, Vec3.UnitZ), -heelLift);
            return ankle + foot.Rotate(rig.Ball(side) - rig.Rest(HumanBoneHierarchy.Foot(side)));
        }

        /// <summary>
        /// Legs hanging from the hips while flying (anims._dangling_legs): nearly straight, tilted back by
        /// <paramref name="backAngle"/> radians from straight down in the moving direction, toes pointed (a
        /// digitigrade foot already points down, so it only tilts back with the leg).
        /// <paramref name="blend"/> moves them there from standing (take-off).
        /// </summary>
        private void DanglingLegs(HumanoidPoseBuilder b, double backAngle, double move = 0, double blend = 1)
        {
            foreach (int side in Sides)
            {
                Vec3 hip = HipJoint(b, side);
                Vec3 forward = Turn(rig.FootForward(side), move);
                Vec3 down = Turn(new Vec3(side * 0.05, -Math.Sin(backAngle), -Math.Cos(backAngle)), move).Normalized;
                Vec3 ankle = hip + down * ((rig.ThighLength(side) + rig.ShinLength(side)) * 0.95);
                double pointed = (rig.LegStyle == LegStyle.Digitigrade ? 0 : 1.0) + backAngle;
                Vec3 ball = Vec3.Lerp(rig.Ball(side), BallForAnkle(side, ankle, forward, pointed), blend);
                double heelLift = pointed * blend;
                b.SetFoot(side, ball, forward, heelLift, forward, heelLift);
            }
        }

        /// <summary>Distance from the wrist to the middle of a closed fist, as a fraction of the arm length.</summary>
        private const double FistPerArmLength = 0.14;

        /// <summary>
        /// How far from the wrist, along the fingers, a closed hand holds a handle (the middle of the fist), tiles:
        /// <see cref="FistPerArmLength"/> of the arm length.
        /// </summary>
        public double GripOffset => FistPerArmLength * rig.ArmLength;

        /// <summary>
        /// Closes the hand of <paramref name="side"/> round a handle running through <paramref name="point"/>
        /// along <paramref name="axis"/> (unit, towards the pick's head or the muzzle), the elbow bent towards
        /// <paramref name="pole"/>. The fingers point across the handle away from the elbow, the wrist
        /// <see cref="GripOffset"/> short of the handle, and the thumb side of the fist faces back along the
        /// handle: the handle goes on from the hands away from the body, and turning the thumbs towards its far
        /// end would roll the wrists further than forearms turn.
        /// </summary>
        private void HoldHandle(HumanoidPoseBuilder b, int side, Vec3 point, Vec3 axis, Vec3 pole)
        {
            Vec3 fingers = GripFingers(axis, pole);
            b.SetArm(side, point - fingers * GripOffset, pole, fingers, 1, -axis);
        }

        /// <summary>
        /// The direction across a handle along <paramref name="axis"/> (unit) away from <paramref name="pole"/>: where a
        /// hand reaching for the handle with its elbow bent towards the pole points its fingers.
        /// </summary>
        private static Vec3 GripFingers(Vec3 axis, Vec3 pole) => -(pole - axis * Vec3.Dot(pole, axis)).Normalized;

        /// <summary>
        /// The gun held at the chest with both hands, aiming horizontally ahead of the torso
        /// (model.hold_gun, with the support hand kept within the arm's reach). The trigger hand points along
        /// the gun as on a pistol grip; the support hand closes round the gun (<see cref="HoldHandle"/>).
        /// </summary>
        private void HoldGun(HumanoidPoseBuilder b, Quat chest)
        {
            Vec3 aim = chest.Forward.Horizontal.Normalized;
            Vec3 right = Vec3.Cross(aim, Vec3.UnitZ).Normalized;
            Vec3 rightShoulder = Shoulder(b, 1);
            Vec3 leftShoulder = Shoulder(b, -1);
            // the stock against the right side of the chest, a little below the shoulder
            Vec3 stock = rightShoulder - right * (0.04 * sizeScale) + aim * (0.05 * sizeScale) - Vec3.UnitZ * (0.07 * sizeScale);
            Vec3 trigger = stock + aim * (0.13 * sizeScale) - Vec3.UnitZ * (0.035 * sizeScale);
            // the support hand holds the gun as far forward as its arm reaches (short-armed avatars hold it
            // nearer the trigger)
            Vec3 supportPole = -chest.Forward * 0.2 - chest.Right * 0.7 - Vec3.UnitZ * 0.8;
            Vec3 toWrist = -GripFingers(aim, supportPole) * GripOffset;
            double reach = 0.95 * (rig.UpperArmLength(-1) + rig.ForearmLength(-1));
            Vec3 support = stock + aim * (0.31 * sizeScale) - Vec3.UnitZ * (0.02 * sizeScale);
            for (double along = 0.31; along > 0.13 && Vec3.Distance(support + toWrist, leftShoulder) > reach; along -= 0.01)
            {
                support = stock + aim * (along * sizeScale) - Vec3.UnitZ * (0.02 * sizeScale);
            }
            // still out of reach (short arms, wide shoulders): the whole gun moves towards the left shoulder,
            // sideways and then towards the middle of the shoulders, as long as that brings the support hand
            // nearer and the trigger stays within the right arm's reach (or gets no further out of it)
            double triggerReach = 0.95 * (rig.UpperArmLength(1) + rig.ForearmLength(1));
            void Shift(Vec3 direction)
            {
                Vec3 step = direction.Normalized * (0.002 * sizeScale);
                while (Vec3.Distance(support + toWrist, leftShoulder) > reach
                       && Vec3.Distance(support + toWrist + step, leftShoulder) < Vec3.Distance(support + toWrist, leftShoulder)
                       && Vec3.Distance(trigger + step, rightShoulder)
                       <= Math.Max(triggerReach, Vec3.Distance(trigger, rightShoulder)))
                {
                    stock += step;
                    trigger += step;
                    support += step;
                }
            }
            Shift(-right);
            Shift((rightShoulder + leftShoulder) * 0.5 - support);
            b.SetArm(1, trigger, -chest.Forward * 0.4 + chest.Right * 0.6 - Vec3.UnitZ * 0.8, aim, 1);
            HoldHandle(b, -1, support, aim, supportPole);
            Vec3 up = Vec3.Cross(right, aim).Normalized;
            b.SetProp(new PropPose(PropKind.Gun, stock, stock + aim * (0.52 * sizeScale), up));
        }

        /// <summary>
        /// How much of the hands' reach behind the head goes over it instead when the pick is raised all the way
        /// (they also rise by 0.12 arm lengths times this).
        /// </summary>
        private const double PickWindUp = 0.6;

        /// <summary>How far the hands hold the pick from between the shoulders, arm lengths (they reach further in the blow).</summary>
        private const double PickReach = 0.73;

        /// <summary>Length of the pick's handle from the hands to the head, and behind the hands, tiles at the default height.</summary>
        private const double PickAhead = 0.62, PickBehind = 0.10;

        /// <summary>The point between the shoulders the pick swings round.</summary>
        private Vec3 PickPivot(HumanoidPoseBuilder b, Quat chest) =>
            (Shoulder(b, 1) + Shoulder(b, -1)) * 0.5 + chest.Forward.Horizontal.Normalized * (0.03 * sizeScale) - chest.Up * (0.015 * sizeScale);

        /// <summary>
        /// The handle angle (radians from forward-horizontal, up positive) at which the pick's point, swung
        /// down in front with the hands <paramref name="reach"/> arm lengths out, touches the ground; the lowest
        /// it reaches when the ground is too far down.
        /// </summary>
        private double GroundAlpha(HumanoidPoseBuilder b, Quat chest, double reach)
        {
            // the point: pivot + handle(alpha) * along - head(alpha) * point, so its height is
            // pivot.Z + along * sin(alpha) - point * cos(alpha) = pivot.Z + r * sin(alpha - phi)
            (double r, double phi) = PickSwing(reach);
            double height = PickPivot(b, chest).Z;
            return phi + (height >= r ? -Math.PI / 2 : Math.Asin(-height / r));
        }

        /// <summary>
        /// How far the pick's point is from the pivot with the hands <paramref name="reach"/> arm lengths out, and
        /// the angle between the handle and the line to the point.
        /// </summary>
        private (double radius, double angle) PickSwing(double reach)
        {
            double along = reach * rig.ArmLength + PickAhead * sizeScale;
            double point = StickFigure.PickHeadPoint * (PickAhead + PickBehind) * sizeScale;
            return (Math.Sqrt(along * along + point * point), Math.Atan2(point, along));
        }

        /// <summary>
        /// The pickaxe with its handle at <paramref name="alpha"/> radians from forward-horizontal, up
        /// positive (model.hold_pick), both hands closed round it (<see cref="HoldHandle"/>) <paramref name="reach"/>
        /// arm lengths out. As the pick is raised (<paramref name="raise"/> from 0 to 1), the hands, which
        /// model.hold_pick moves along the handle to behind the shoulders, stay over the head
        /// (<see cref="PickWindUp"/>), and the elbows turn from out, back and down to out, up and a little
        /// forward: shoulders do not reach further back.
        /// </summary>
        private void HoldPick(HumanoidPoseBuilder b, Quat chest, double alpha, double raise, double reach = PickReach)
        {
            Vec3 forward = chest.Forward.Horizontal.Normalized;
            Vec3 handle = forward * Math.Cos(alpha) + Vec3.UnitZ * Math.Sin(alpha);
            Vec3 head = -forward * Math.Sin(alpha) + Vec3.UnitZ * Math.Cos(alpha);
            Vec3 side = Vec3.Cross(forward, Vec3.UnitZ).Normalized;
            Vec3 pivot = PickPivot(b, chest);
            Vec3 hands = handle * (reach * rig.ArmLength);
            double back = Math.Min(0, Vec3.Dot(hands, forward));
            Vec3 grip = pivot + hands - forward * (back * PickWindUp * raise)
                        + Vec3.UnitZ * (0.12 * rig.ArmLength * PickWindUp * raise);
            Vec3 bend = Vec3.Lerp(-forward * 0.3 - Vec3.UnitZ * 0.5, forward * 0.2 + Vec3.UnitZ * 0.6, raise);
            HoldHandle(b, 1, grip, handle, side * 0.8 + bend);
            HoldHandle(b, -1, grip - handle * (0.07 * sizeScale), handle, -side * 0.8 + bend);
            b.SetProp(new PropPose(PropKind.Pickaxe, grip - handle * (PickBehind * sizeScale), grip + handle * (PickAhead * sizeScale), head));
        }

        // -- animations -----------------------------------------------------------------------------

        /// <summary>anims.idle: breathing, the head looking around a little.</summary>
        private PoseResult Idle(double t, bool gun)
        {
            double breath = Math.Sin(MathUtil.Tau * t);
            const double lean = 0.03;
            var pelvis = new Vec3(0, -0.005 * legScale, standingPelvis + 0.004 * legScale * breath);
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: 0.02 * Math.Sin(MathUtil.Tau * t + 1.2),
                headYaw: 0.05 * Math.Sin(MathUtil.Tau * t));
            Quat chest = ChestFrame(lean);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else
            {
                RelaxedArms(b, chest);
            }
            StandingFeet(b, 0.01);
            return b.Build();
        }

        /// <summary>
        /// anims.run: <paramref name="move"/> turns the hips and legs clockwise from the facing direction
        /// (running with a gun aimed elsewhere). Each foot is on the ground for half the cycle, rolling onto
        /// its ball before it lifts.
        /// </summary>
        private PoseResult Run(double t, bool gun, double move)
        {
            double ph = MathUtil.Tau * t;
            double lean = gun ? 0.12 : 0.22;
            double twist = 0.10 * Math.Sin(ph);
            // low enough that the leg reaching forward to land is still bent at the knee
            var pelvis = new Vec3(0, 0, rig.HipsHeight - 0.08 * rig.LegLength - 0.028 * legScale * Math.Cos(2 * ph));
            HumanoidPoseBuilder b = Torso(pelvis, lean, twist, -0.12 * Math.Sin(ph) - move,
                headPitch: gun ? -0.06 : -0.12, headYaw: -twist * 0.5);
            foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
            {
                double phi = MathUtil.PositiveModulo(phase, 1.0);
                double y = 0.02 * legScale + RunStride * legScale * Math.Cos(MathUtil.Tau * phi);
                double swing = phi > 0.5 ? Math.Sin(MathUtil.Tau * (phi - 0.5)) : 0.0;
                double z = 0.16 * legScale * swing;
                // the heel comes up as the foot pushes off behind the body, the toes flat on the ground; in the
                // air the toes stay in line with the foot, which trails while it swings
                double heelLift = phi <= 0.5 ? 0.7 * MathUtil.Smoothstep(0.3, 0.5, phi) : 0.7 * (1 - MathUtil.Smoothstep(0.5, 0.9, phi)) + 0.4 * swing;
                Vec3 rest = rig.Ball(side);
                Vec3 ball = Turn(new Vec3(rest.X - side * 0.01 * legScale, rest.Y + y, rest.Z + z), move);
                Vec3 forward = Turn(rig.FootForward(side), move);
                b.SetFoot(side, ball, forward, heelLift, forward + Vec3.UnitZ * 0.2, phi > 0.5 ? heelLift : 0);
            }
            Quat chest = ChestFrame(lean, twist);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else
            {
                foreach ((int side, double phase) in new[] { (-1, t), (1, t + 0.5) })
                {
                    double a = Math.Cos(MathUtil.Tau * (phase + 0.5));
                    Vec3 s = Shoulder(b, side);
                    Vec3 hand = s + chest.Right * (side * 0.07 * armScale) + chest.Forward * ((0.10 + 0.20 * a) * armScale)
                                - chest.Up * ((0.30 - 0.06 * a) * armScale);
                    b.SetArm(side, hand, -chest.Forward + chest.Right * (side * 0.3) - chest.Up * 0.2, null, 0.6);
                }
            }
            return b.Build();
        }

        /// <summary>
        /// Handle angle of every mine frame, degrees, and the forward lean that goes with it: anims.mine's
        /// tables, with the blow from frame 9 on leaned into further, since a human's longer body needs that
        /// for the pick to reach the ground. In the blow and the dig (<see cref="FirstDigFrame"/> to
        /// <see cref="LastDigFrame"/>) the arms reach out and the angle is the one that puts the pick's point
        /// on the ground in front (<see cref="GroundAlpha"/>), as far ahead as the body allows.
        /// </summary>
        private static readonly int[] MineAlpha = { -30, 5, 45, 90, 125, 148, 152, 75, -62, -66, -60, -48 };
        private static readonly double[] MineLean = { 0.30, 0.22, 0.12, 0.03, -0.05, -0.09, -0.09, 0.15, 0.95, 0.98, 0.93, 0.60 };
        private const int FirstDigFrame = 8, LastDigFrame = 10;

        /// <summary>How far out the hands hold the pick in the blow, arm lengths.</summary>
        private const double PickBlowReach = 0.85;

        /// <summary>anims.mine: raise the pick over the shoulder, strike on frame 9, lean into the blow.</summary>
        private PoseResult Mine(double t)
        {
            int i = (int)(Math.Round(t * MineAlpha.Length, MidpointRounding.ToEven) % MineAlpha.Length);
            double alpha = MathUtil.Radians(MineAlpha[i]);
            double lean = MineLean[i];
            double raise = MathUtil.Smoothstep(40, 150, MineAlpha[i]);
            // crouch and shift the weight forward into the blow
            double crouch = MathUtil.Smoothstep(0.3, 0.6, lean);
            var pelvis = new Vec3(0, -0.02 * legScale + 0.06 * legScale * crouch,
                rig.HipsHeight - 0.06 * rig.LegLength - 0.16 * rig.LegLength * crouch + 0.03 * legScale * raise);
            double headPitch = 0.15 - 0.25 * (lean - 0.1);
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: headPitch);
            Quat chest = ChestFrame(lean);
            bool dig = i >= FirstDigFrame && i <= LastDigFrame;
            double reach = dig ? PickBlowReach : PickReach;
            if (dig)
            {
                // a body too tall for its arms and pick crouches lower, so the point reaches the ground ahead
                double tooHigh = PickPivot(b, chest).Z - 0.85 * PickSwing(reach).radius;
                if (tooHigh > 0)
                {
                    pelvis -= Vec3.UnitZ * Math.Min(tooHigh, 0.15 * rig.LegLength);
                    b = Torso(pelvis, lean, headPitch: headPitch);
                }
            }
            // the point never goes into the ground
            double ground = GroundAlpha(b, chest, reach);
            HoldPick(b, chest, dig ? ground : Math.Max(alpha, ground), raise, reach);
            foreach (int side in Sides)
            {
                Vec3 rest = rig.Ball(side);
                var ball = new Vec3(rest.X + side * 0.04 * legScale, rest.Y + (side < 0 ? 0.18 : -0.08) * legScale, rest.Z);
                Vec3 forward = rig.FootForward(side);
                b.SetFoot(side, ball, forward, 0, forward + Vec3.UnitX * (side * 0.2));
            }
            return b.Build();
        }

        /// <summary>How far back the legs hang while hovering, radians.</summary>
        private const double HoverLegAngle = 0.08;

        /// <summary>anims.hover: hanging in the air, legs dangling, gently bobbing.</summary>
        private PoseResult Hover(double t, bool gun, double elevation, double phaseShift)
        {
            double ph = MathUtil.Tau * t + phaseShift;
            const double lean = 0.10;
            var pelvis = new Vec3(0, 0, standingPelvis + elevation + 0.035 * legScale * Math.Sin(ph));
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: -0.05);
            Quat chest = ChestFrame(lean);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else
            {
                RelaxedArms(b, chest, 0, 0.03);
            }
            DanglingLegs(b, HoverLegAngle);
            return b.Build();
        }

        /// <summary>anims.fly: leaning into the flight, legs trailing in the flight direction.</summary>
        private PoseResult Fly(double t, bool gun, double move)
        {
            double ph = MathUtil.Tau * t;
            const double lean = 0.55;
            var pelvis = new Vec3(0, 0, standingPelvis + Conventions.FlyHeight + 0.03 * legScale * Math.Sin(ph));
            HumanoidPoseBuilder b = Torso(pelvis, lean, hipTwist: -move, headPitch: -0.45);
            Quat chest = ChestFrame(lean);
            if (gun)
            {
                HoldGun(b, chest);
            }
            else
            {
                RelaxedArms(b, chest, 0, 0.02);
            }
            DanglingLegs(b, lean + 0.1, move);
            return b.Build();
        }

        /// <summary>
        /// anims.take_off: crouch, spring up, and end exactly in the first hover-with-gun frame. The gun stays in
        /// the hands, as on the game's own mech armor sprites: Factorio has no take-off without a gun, and keeps
        /// firing while a character takes off, with the muzzle flash where the gun would be.
        /// </summary>
        private PoseResult TakeOff(double t)
        {
            double crouch = MathUtil.Smoothstep(0.0, 0.28, t) * (1 - MathUtil.Smoothstep(0.28, 0.55, t));
            double lift = MathUtil.Smoothstep(0.3, 1.0, t);
            double lean = 0.03 + 0.25 * crouch + 0.07 * lift;
            var pelvis = new Vec3(0, -0.005 * legScale * (1 - lift),
                standingPelvis - 0.11 * legScale * crouch + Conventions.FlyHeight * lift);
            HumanoidPoseBuilder b = Torso(pelvis, lean, headPitch: -0.05 * lift);
            HoldGun(b, ChestFrame(lean));
            if (lift < 0.05)
            {
                StandingFeet(b, 0.01);
            }
            else
            {
                DanglingLegs(b, HoverLegAngle, 0, MathUtil.Smoothstep(0.05, 0.7, lift));
            }
            return b.Build();
        }

        /// <summary>
        /// The corpse: a relaxed pose turned to face the camera (south) and laid on its side, centred on
        /// the entity position; the renderer lowers it onto the ground.
        /// </summary>
        private PoseResult Corpse()
        {
            var pelvis = new Vec3(0, 0, standingPelvis);
            HumanoidPoseBuilder b = Torso(pelvis, 0.15, headPitch: 0.2, headYaw: 0.3);
            Quat chest = ChestFrame(0.15);
            foreach (int side in Sides)
            {
                Vec3 s = Shoulder(b, side);
                Vec3 hand = s + chest.Forward * (0.25 * armScale) - chest.Up * (0.25 * armScale) + chest.Right * (side * 0.1 * armScale);
                b.SetArm(side, hand, -chest.Forward + chest.Right * (side * 0.2) - chest.Up, null, 0.4);
                Vec3 hip = HipJoint(b, side);
                Vec3 ball = hip + new Vec3(side * 0.02 * legScale, (side > 0 ? 0.25 : 0.05) * legScale, -0.8 * rig.LegLength);
                b.SetFoot(side, ball, rig.FootForward(side), 0.3, Vec3.UnitY);
            }
            b.SetGroundToLowestPoint(true);
            PoseResult standing = b.Build();
            // face the camera (south), then lie down on the left side: up becomes east
            Quat lying = Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2) * Quat.FromAxisAngle(Vec3.UnitZ, Math.PI);
            // the figure now runs from the feet at x = -pelvis height to the head at x = top - pelvis height
            var centre = new Vec3(pelvis.Z - rig.HeadTopHeight / 2, 0, 0);
            return PoseTransforms.Rigid(standing, lying, pelvis, centre);
        }

        /// <summary>Every animation's frames, for tests and previews: (animation, row, frame) -> pose.</summary>
        public IEnumerable<(string animation, int row, int frame, PoseResult pose)> AllFrames(bool flight)
        {
            foreach (AnimationInfo a in AnimationSpec.ForPack(flight))
            {
                for (int row = 0; row < a.DirectionCount; row++)
                {
                    double move = AnimationSpec.Direction(a.Name, row).Move;
                    for (int f = 0; f < a.FrameCount; f++)
                    {
                        yield return (a.Name, row, f, Pose(a.Name, AnimationSpec.FramePhase(a.Name, f), move));
                    }
                }
            }
        }
    }
}
