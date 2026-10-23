using System;
using MilkJugg24.FactorioAvatarExporter.Core;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MilkJugg24.FactorioAvatarExporter.Editor
{
    /// <summary>
    /// Core's <see cref="IFrameRenderer"/> drawn with the real avatar: poses the <see cref="AvatarStage"/>,
    /// turns it, and renders it through the <see cref="StageCamera"/>, on black and on white for the color
    /// pass, and as its flattened (for "shadow-flipped" also mirrored) <see cref="ShadowProxy"/> for the
    /// shadow pass.
    /// </summary>
    internal sealed class StageRenderer : IFrameRenderer, IDisposable
    {
        private readonly AvatarStage stage;
        private readonly StageCamera camera;
        private readonly ShadowProxy shadow = new ShadowProxy();
        private PoseResult posed;

        public StageRenderer(AvatarStage stage, StageCamera camera)
        {
            this.stage = stage;
            this.camera = camera;
        }

        /// <summary>
        /// Added around the measured meshes, tiles: room for what the vertices don't show, such as outlines
        /// a shader draws around the mesh.
        /// </summary>
        public double PaddingTiles { get; set; } = 0.25;

        public Bounds3 MeasureWorldBounds(PoseResult pose, double orientation, bool mirrored)
        {
            Place(pose);
            return stage.WorldBounds(orientation, mirrored).Expanded(PaddingTiles);
        }

        public RenderResult Render(FrameRequest request)
        {
            Place(request.Pose);
            camera.Aim(request.Canvas);
            if (request.Pass == RenderPass.Color)
            {
                if (request.Mirrored)
                {
                    throw new NotSupportedException("mirrored color renders are not drawn (only mirrored shadows are)");
                }
                stage.SetOrientation(request.Orientation);
                (ImageF onBlack, ImageF onWhite) = camera.RenderBlackAndWhite();
                return new RenderResult { OnBlack = onBlack, OnWhite = onWhite, Linear = camera.Linear };
            }
            shadow.Bake(stage, request.Orientation, request.Mirrored);
            return new RenderResult { Silhouette = camera.RenderSilhouette(shadow.Scene), Linear = camera.Linear };
        }

        /// <summary>Poses the avatar, unless it already holds <paramref name="pose"/> (each pose is drawn several times).</summary>
        private void Place(PoseResult pose)
        {
            if (ReferenceEquals(pose, posed))
            {
                return;
            }
            stage.SetOrientation(0);
            stage.ApplyPose(pose);
            posed = pose;
        }

        public void Dispose()
        {
            shadow.Dispose();
        }
    }

    /// <summary>
    /// The avatar's ground shadow as a flat black mesh in a preview scene of its own
    /// (<see cref="AvatarStage.BakeShadow"/>), for the shadow pass: rendered on white it is the silhouette Core
    /// turns into the shadow layer.
    /// </summary>
    internal sealed class ShadowProxy : IDisposable
    {
        private readonly Mesh mesh;
        private readonly Material material;

        public ShadowProxy()
        {
            Scene = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("Shadow proxy");
            SceneManager.MoveGameObjectToScene(holder, Scene);
            mesh = new Mesh { name = "Shadow proxy", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            // Unity's own unlit shader, always there: opaque black, both sides (flattening flips some
            // triangles), drawn whatever the order
            material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetColor("_Color", Color.black);
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public Scene Scene { get; }

        public void Bake(AvatarStage stage, double orientation, bool mirrored) => stage.BakeShadow(mesh, orientation, mirrored);

        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(Scene);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
        }
    }
}
