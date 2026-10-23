using System;
using System.Collections.Generic;
using System.Linq;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
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

        /// <summary>Loose parts held to a body bone: the part's rest position and rotation in the bone's frame.</summary>
        private readonly List<(Transform part, Transform bone, Vector3 position, Quaternion rotation)> links =
            new List<(Transform, Transform, Vector3, Quaternion)>();

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
            Dictionary<Transform, Transform> constrained = ParentConstraintSources();
            Notes.Add(AvatarCleaner.Strip(Avatar));
            // the offsets are taken as the avatar was saved, where its outfits fit the body
            LinkLooseParts(constrained);
            ApplyRestPose(animator);
            FollowLinks();

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

        /// <summary>
        /// The objects parent constraints hold on to a bone of the body, and that bone: VRChat's constraints (read
        /// before the stage removes them, as they are scripts) and Unity's (switched off, as the stage moves
        /// those objects itself now).
        /// </summary>
        private Dictionary<Transform, Transform> ParentConstraintSources()
        {
            var sources = new Dictionary<Transform, Transform>();
            foreach (MonoBehaviour script in Avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (script == null || !script.GetType().Name.EndsWith("ParentConstraint", StringComparison.Ordinal))
                {
                    continue;
                }
                Transform target = script.transform, source = null;
                using (var serialized = new SerializedObject(script))
                {
                    SerializedProperty p = serialized.GetIterator();
                    while (p.Next(true))
                    {
                        if (p.propertyType != SerializedPropertyType.ObjectReference || p.name.StartsWith("m_", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        Transform t = p.objectReferenceValue is GameObject g ? g.transform : p.objectReferenceValue as Transform;
                        if (t != null && p.name == "TargetTransform")
                        {
                            target = t;
                        }
                        else if (t != null && source == null && InBody(t))
                        {
                            source = t;
                        }
                    }
                }
                if (source != null && !InBody(target))
                {
                    sources[target] = source;
                }
            }
            foreach (ParentConstraint constraint in Avatar.GetComponentsInChildren<ParentConstraint>(true))
            {
                for (int i = 0; i < constraint.sourceCount; i++)
                {
                    Transform source = constraint.GetSource(i).sourceTransform;
                    if (source != null && InBody(source) && !InBody(constraint.transform))
                    {
                        sources[constraint.transform] = source;
                        constraint.constraintActive = false;
                        break;
                    }
                }
            }
            return sources;
        }

        /// <summary>Whether <paramref name="t"/> is the hips or below them: the body the poses move.</summary>
        private bool InBody(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p == bones[HumanBone.Hips])
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Holds on to the body the parts that are only joined to it by scripts the stage removed: outfits and
        /// accessories with their own armature, which VRCFury's Armature Link or Modular Avatar's Merge Armature
        /// merge into the body only in Play mode, and objects that constraints keep on a bone. An object a parent
        /// constraint held follows that bone; a part's bones follow the body's bones of the same name (allowing a
        /// prefix or suffix the part adds to all of them, like "Chest_Hoodie"); a part with neither follows, as a
        /// whole, the bone of the trunk (hips to head) nearest to it. Each keeps its offset as it is now.
        /// </summary>
        private void LinkLooseParts(Dictionary<Transform, Transform> constrained)
        {
            Transform hips = bones[HumanBone.Hips];
            var spine = new HashSet<Transform>(); // the hips and the transforms above them
            for (Transform t = hips; t != null && t != Avatar.transform; t = t.parent)
            {
                spine.Add(t);
            }
            var body = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (Transform t in hips.GetComponentsInChildren<Transform>(true))
            {
                if (!body.ContainsKey(t.name))
                {
                    body.Add(t.name, t);
                }
            }

            // what the meshes outside the body hang on: their bones, or the mesh object itself
            var loose = new List<Transform>();
            foreach (Renderer renderer in Avatar.GetComponentsInChildren<Renderer>(true))
            {
                Transform[] meshBones = renderer is SkinnedMeshRenderer skinned ? skinned.bones : null;
                if (meshBones != null && meshBones.Length > 0)
                {
                    loose.AddRange(meshBones.Where(b => b != null && !InBody(b) && !spine.Contains(b)));
                }
                else if (!InBody(renderer.transform))
                {
                    loose.Add(renderer.transform);
                }
            }
            // grouped by part: the top-most transform holding them that is neither the body nor above it
            var parts = new Dictionary<Transform, List<Transform>>();
            foreach (Transform t in loose.Distinct())
            {
                Transform part = t;
                while (part.parent != null && part.parent != Avatar.transform && !spine.Contains(part.parent))
                {
                    part = part.parent;
                }
                if (!parts.TryGetValue(part, out List<Transform> held))
                {
                    parts.Add(part, held = new List<Transform>());
                }
                held.Add(t);
            }

            string[] humanoidNames = HumanBones.ParentFirst.Where(bones.ContainsKey).Select(b => bones[b].name).ToArray();
            HumanBone[] trunk = new[] { HumanBone.Hips, HumanBone.Spine, HumanBone.Chest, HumanBone.UpperChest, HumanBone.Head }
                .Where(bones.ContainsKey).ToArray();
            foreach (KeyValuePair<Transform, List<Transform>> part in parts)
            {
                (string prefix, string suffix) = Affixes(part.Key, humanoidNames);
                var held = new List<string>();
                int byName = 0;
                foreach (Transform t in part.Key.GetComponentsInChildren<Transform>(true))
                {
                    if (constrained.TryGetValue(t, out Transform source))
                    {
                        Link(t, source);
                        held.Add($"{t.name} to {source.name}");
                    }
                    else if (body.TryGetValue(t.name, out Transform bone) || body.TryGetValue(StripAffixes(t.name, prefix, suffix), out bone))
                    {
                        Link(t, bone);
                        byName++;
                    }
                }
                if (held.Count > 0)
                {
                    Notes.Add($"{part.Key.name}: held by its constraints ({string.Join(", ", held)})");
                }
                if (byName > 0)
                {
                    Notes.Add($"{part.Key.name}: {byName} bones follow the body's bones of the same name");
                }
                if (held.Count > 0 || byName > 0)
                {
                    continue;
                }
                Vector3 centre = Vector3.zero;
                foreach (Transform t in part.Value)
                {
                    centre += t.position;
                }
                centre /= part.Value.Count;
                HumanBone nearest = trunk.OrderBy(b => (bones[b].position - centre).sqrMagnitude).First();
                Link(part.Key, bones[nearest]);
                Notes.Add($"{part.Key.name}: follows the {nearest} bone, the nearest (no constraint or bone names join it to the body)");
            }
            // parents before children, so a child's place is set after its parent moved
            links.Sort((a, b) => Depth(a.part).CompareTo(Depth(b.part)));
        }

        private void Link(Transform part, Transform bone)
        {
            links.Add((part, bone, bone.InverseTransformPoint(part.position), Quaternion.Inverse(bone.rotation) * part.rotation));
        }

        /// <summary>Puts the linked parts back on their bones, at the offsets they were linked with.</summary>
        private void FollowLinks()
        {
            foreach ((Transform part, Transform bone, Vector3 position, Quaternion rotation) in links)
            {
                part.SetPositionAndRotation(bone.TransformPoint(position), bone.rotation * rotation);
            }
        }

        /// <summary>The prefix and suffix a part adds to the names of its copy of the body's bones ("" when none).</summary>
        private static (string prefix, string suffix) Affixes(Transform part, string[] humanoidNames)
        {
            foreach (string name in humanoidNames)
            {
                foreach (Transform t in part.GetComponentsInChildren<Transform>(true))
                {
                    int i = t.name.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                    if (i >= 0)
                    {
                        return (t.name.Substring(0, i), t.name.Substring(i + name.Length));
                    }
                }
            }
            return ("", "");
        }

        private static string StripAffixes(string name, string prefix, string suffix)
        {
            if (name.Length > prefix.Length + suffix.Length &&
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
            }
            return name;
        }

        private static int Depth(Transform t)
        {
            int depth = 0;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                depth++;
            }
            return depth;
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
        /// position, then the parts held on the body follow their bones, then the whole avatar drops onto the
        /// ground if the pose asks for it. Fingers and props are not done yet.
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
            FollowLinks();
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
