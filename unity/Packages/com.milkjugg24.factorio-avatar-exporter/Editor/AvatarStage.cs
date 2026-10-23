using System;
using System.Collections.Generic;
using System.Linq;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using HumanBone = MilkJugg24.FactorioAvatarExporter.Core.HumanBone;
using Object = UnityEngine.Object;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// A copy of an avatar in its own preview scene, ready to be posed by Core. Nothing in the open scene is
    /// changed.
    ///
    /// The copy hangs under <see cref="Pivot"/> at the world origin. Its scripts (VRChat components,
    /// PhysBones and other tools' components) and EditorOnly objects are removed, its humanoid bones are set
    /// to the rest pose stored in its humanoid avatar (the pose the rig was configured in), and it is measured
    /// for Core (<see cref="Rig"/>). It is then placed so that Core's model space maps onto the pivot's space:
    /// a model point p (tiles) is at pivot-local <c>UnitySpace.ToUnity(p * MetersPerTile)</c>, the origin being
    /// the ground under the hips.
    /// </summary>
    internal sealed class AvatarStage : IDisposable
    {
        private readonly List<Object> temporary = new List<Object>();
        private readonly Dictionary<HumanBone, Transform> bones = new Dictionary<HumanBone, Transform>();
        private readonly Dictionary<HumanBone, Quaternion> restRotations = new Dictionary<HumanBone, Quaternion>();
        private readonly List<(Transform t, Vector3 position, Quaternion rotation, Vector3 scale)> rest =
            new List<(Transform, Vector3, Quaternion, Vector3)>();

        private AvatarStage(Scene scene)
        {
            Scene = scene;
        }

        public Scene Scene { get; }
        public Transform Pivot { get; private set; }
        public GameObject Avatar { get; private set; }
        public HumanoidRig Rig { get; private set; }
        public double MetersPerTile { get; private set; }

        /// <summary>What was found and done, one line each, for reports.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// Copies <paramref name="source"/> (a prefab or a scene object with a humanoid Animator) into a new
        /// preview scene and measures it. Throws <see cref="ArgumentException"/> when it is not a humanoid.
        /// </summary>
        public static AvatarStage Load(GameObject source)
        {
            var stage = new AvatarStage(EditorSceneManager.NewPreviewScene());
            try
            {
                stage.Build(source);
                return stage;
            }
            catch
            {
                stage.Dispose();
                throw;
            }
        }

        private void Build(GameObject source)
        {
            var pivot = new GameObject("Pivot");
            SceneManager.MoveGameObjectToScene(pivot, Scene);
            Pivot = pivot.transform;
            Avatar = Object.Instantiate(source);
            Avatar.name = source.name;
            SceneManager.MoveGameObjectToScene(Avatar, Scene);
            Avatar.transform.SetParent(Pivot, false);
            Avatar.transform.localPosition = Vector3.zero;
            Avatar.transform.localRotation = Quaternion.identity;

            Animator animator = Avatar.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                throw new ArgumentException(source.name + " has no humanoid rig: its Animator needs an Avatar with " +
                    "Animation Type Humanoid (the model's Rig tab in the import settings)");
            }
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                Transform t = animator.GetBoneTransform((HumanBodyBones)(int)bone);
                if (t != null)
                {
                    bones.Add(bone, t);
                }
            }
            Notes.Add(AvatarCleaner.Strip(Avatar));
            ApplyRestPose(animator);

            // measure the rest pose: bone positions and the meshes' top and bottom, meters, in pivot space
            List<Vector3> points = MeshPoints();
            if (points.Count == 0)
            {
                throw new ArgumentException(source.name + " has no visible meshes");
            }
            var input = new RigInput { MeshTop = points.Max(p => p.y), MeshBottom = points.Min(p => p.y) };
            foreach (KeyValuePair<HumanBone, Transform> bone in bones)
            {
                input.Bones.Add(bone.Key, ToCore(Pivot.InverseTransformPoint(bone.Value.position)));
            }
            Rig = HumanoidRig.Measure(input);
            MetersPerTile = 1 / Rig.TilesPerMeter;
            Notes.Add($"height: {Rig.Height:0.000} m, drawn {Conventions.DefaultCharacterHeight} tiles tall " +
                $"({MetersPerTile:0.000} m per tile); legs: {Rig.LegStyle}");
            List<HumanBone> missing = HumanBones.Optional.Where(b => !bones.ContainsKey(b)).ToList();
            if (missing.Count > 0)
            {
                Notes.Add("optional bones not mapped (estimated instead): " + string.Join(", ", missing));
            }
            foreach (string warning in Rig.Warnings)
            {
                Notes.Add("warning: " + warning);
            }

            // Core's model origin is the ground under the hips: move the avatar so it sits on the pivot
            Vec3 hips = input.Bones[HumanBone.Hips];
            Avatar.transform.localPosition = -ToUnity(new Vec3(hips.X, hips.Y, input.MeshBottom), 1);

            foreach (Transform t in Avatar.GetComponentsInChildren<Transform>(true))
            {
                rest.Add((t, t.localPosition, t.localRotation, t.localScale));
            }
            foreach (KeyValuePair<HumanBone, Transform> bone in bones)
            {
                restRotations.Add(bone.Key, Quaternion.Inverse(Pivot.rotation) * bone.Value.rotation);
            }
        }

        /// <summary>
        /// Sets the humanoid bones to the rotations stored in the humanoid avatar's skeleton (the pose the rig
        /// was configured in, usually a T-pose). Positions and scales stay as the avatar has them, so bone
        /// adjustments made in the prefab are kept.
        /// </summary>
        private void ApplyRestPose(Animator animator)
        {
            HumanDescription description = animator.avatar.humanDescription;
            var skeleton = new Dictionary<string, SkeletonBone>();
            foreach (SkeletonBone b in description.skeleton)
            {
                if (!skeleton.ContainsKey(b.name))
                {
                    skeleton.Add(b.name, b);
                }
            }
            int applied = 0;
            foreach (Transform t in bones.Values)
            {
                if (skeleton.TryGetValue(t.name, out SkeletonBone b))
                {
                    t.localRotation = b.rotation;
                    applied++;
                }
            }
            Notes.Add(applied == bones.Count
                ? "rest pose: the rig's configured pose"
                : $"rest pose: the rig's configured pose for {applied} of {bones.Count} bones, the rest as placed");
        }

        /// <summary>Faces the model <paramref name="orientation"/> (a fraction of a clockwise turn from north).</summary>
        public void SetOrientation(double orientation)
        {
            Pivot.rotation = Quaternion.Euler(0, (float)(360 * orientation), 0);
        }

        /// <summary>Puts every transform back where it was in the rest pose.</summary>
        public void ApplyRest()
        {
            foreach ((Transform t, Vector3 position, Quaternion rotation, Vector3 scale) in rest)
            {
                if (t != null)
                {
                    t.localPosition = position;
                    t.localRotation = rotation;
                    t.localScale = scale;
                }
            }
        }

        /// <summary>
        /// Poses the avatar as PoseResult documents: from the rest pose, parent first, every listed bone gets
        /// the rotation <c>delta * rest</c> (in the pivot's space), then the hips move to the pose's hips
        /// position, then the whole avatar drops onto the ground if the pose asks for it. Fingers and props are
        /// not done yet.
        /// </summary>
        public void ApplyPose(PoseResult pose)
        {
            ApplyRest();
            foreach (HumanBone bone in HumanBones.ParentFirst)
            {
                if (bones.TryGetValue(bone, out Transform t) && pose.WorldDeltas.TryGetValue(bone, out Quat delta))
                {
                    t.rotation = Pivot.rotation * (ToUnity(delta) * restRotations[bone]);
                }
            }
            bones[HumanBone.Hips].position = Pivot.TransformPoint(ToUnity(pose.HipsPosition, MetersPerTile));
            if (pose.GroundToLowestPoint)
            {
                float lowest = MeshPoints().Min(p => p.y);
                Avatar.transform.localPosition -= new Vector3(0, lowest, 0);
            }
        }

        /// <summary>
        /// How far, in tiles, the posed bones are from where Core put the matching joints of
        /// <paramref name="pose"/> (shoulders, elbows, wrists, hips, knees, ankles, pelvis, waist, neck top): 0 when
        /// the Unity posing does exactly what Core computed. Call after <see cref="ApplyPose"/>; not meaningful for
        /// poses that are dropped onto the ground.
        /// </summary>
        public double JointError(PoseResult pose)
        {
            var pairs = new List<(string joint, HumanBone bone)>
            {
                (JointNames.Pelvis, HumanBone.Hips), (JointNames.Waist, HumanBone.Spine), (JointNames.NeckTop, HumanBone.Head),
            };
            foreach (int side in new[] { 1, -1 })
            {
                pairs.Add((JointNames.Shoulder(side), HumanBoneHierarchy.UpperArm(side)));
                pairs.Add((JointNames.Elbow(side), HumanBoneHierarchy.LowerArm(side)));
                pairs.Add((JointNames.Hand(side), HumanBoneHierarchy.Hand(side)));
                pairs.Add((JointNames.Hip(side), HumanBoneHierarchy.UpperLeg(side)));
                pairs.Add((JointNames.Knee(side), HumanBoneHierarchy.LowerLeg(side)));
                pairs.Add((JointNames.Ankle(side), HumanBoneHierarchy.Foot(side)));
            }
            double worst = 0;
            foreach ((string joint, HumanBone bone) in pairs)
            {
                if (bones.TryGetValue(bone, out Transform t) && pose.Joints.TryGetValue(joint, out Vec3 expected))
                {
                    Vec3 actual = ToCore(Pivot.InverseTransformPoint(t.position)) * Rig.TilesPerMeter;
                    worst = Math.Max(worst, Vec3.Distance(actual, expected));
                }
            }
            return worst;
        }

        /// <summary>
        /// The vertices of every enabled renderer, in the pivot's space, meters: skinned meshes as posed now.
        /// </summary>
        public List<Vector3> MeshPoints()
        {
            var points = new List<Vector3>();
            Matrix4x4 toPivot = Pivot.worldToLocalMatrix;
            var baked = new Mesh();
            try
            {
                foreach (Renderer renderer in Avatar.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled)
                    {
                        continue;
                    }
                    Mesh mesh;
                    Matrix4x4 toWorld;
                    if (renderer is SkinnedMeshRenderer skinned)
                    {
                        if (skinned.sharedMesh == null)
                        {
                            continue;
                        }
                        skinned.BakeMesh(baked, true);
                        mesh = baked;
                        toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                    }
                    else if (renderer is MeshRenderer)
                    {
                        MeshFilter filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null)
                        {
                            continue;
                        }
                        mesh = filter.sharedMesh;
                        toWorld = renderer.transform.localToWorldMatrix;
                    }
                    else
                    {
                        continue; // particles, lines and trails are not measured
                    }
                    foreach (Vector3 v in mesh.vertices)
                    {
                        points.Add(toPivot.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(v)));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
            return points;
        }

        /// <summary>World space bounds (tiles) of the meshes as posed now, facing <paramref name="orientation"/>.</summary>
        public Bounds3 WorldBounds(double orientation)
        {
            SetOrientation(orientation);
            Matrix4x4 toWorld = Pivot.localToWorldMatrix;
            Bounds3 bounds = Bounds3.Empty;
            foreach (Vector3 p in MeshPoints())
            {
                Vector3 w = toWorld.MultiplyPoint3x4(p);
                bounds = bounds.Encapsulate(ToCore(w) * Rig.TilesPerMeter);
            }
            return bounds;
        }

        public static Vec3 ToCore(Vector3 v) => UnitySpace.ToCore(v.x, v.y, v.z);

        public static Vector3 ToUnity(Vec3 v, double scale)
        {
            (double x, double y, double z) = UnitySpace.ToUnity(v * scale);
            return new Vector3((float)x, (float)y, (float)z);
        }

        public static Quaternion ToUnity(Quat q)
        {
            (double x, double y, double z, double w) = UnitySpace.RotationToUnity(q);
            return new Quaternion((float)x, (float)y, (float)z, (float)w);
        }

        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(Scene);
            foreach (Object o in temporary)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }
        }
    }
}
