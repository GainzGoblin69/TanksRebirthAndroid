using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.GameContent.Globals.Assets;
using TanksRebirth.Graphics;

namespace TanksRebirth.GameContent.UI;

/// <summary>
/// The player's own tank (their colour or skin, painted skins included) drawn in 3D: the turning tanks in the Tank Color
/// screen, and a side-on picture of each player's tank for the screen before a mission (instead of the flat blue tank).
/// </summary>
public static class TankPortrait {
    private static Matrix[] _bones = [];

    /// <summary>Draws the player tank model with <paramref name="tex"/> into <paramref name="rect"/> of the current render target.</summary>
    /// <param name="yaw">How far the tank is turned.</param><param name="eye">Where the camera looks from (at the tank's middle).</param>
    /// <param name="viewHeight">How much of the world fits top to bottom (smaller = a bigger tank).</param>
    public static void DrawModel(GraphicsDevice gd, Rectangle rect, Texture2D tex, float yaw, Vector3 eye, float viewHeight, int targetW, int targetH) {
        var model = ModelGlobals.TankPlayer.Asset;
        if (model is null || tex is null || tex.IsDisposed || rect.Width <= 0 || rect.Height <= 0)
            return;
        rect = Rectangle.Intersect(rect, new Rectangle(0, 0, targetW, targetH));
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var oldViewport = gd.Viewport;
        gd.Viewport = new Viewport(rect);
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
        gd.SamplerStates[0] = SamplerState.PointClamp;

        float aspect = rect.Width / (float)rect.Height;
        var projection = Matrix.CreateOrthographic(viewHeight * aspect, viewHeight, -500f, 500f);
        var view = Matrix.CreateLookAt(eye, new Vector3(0, 6, 0), Vector3.Up);

        var oldRoot = model.Root.Transform;
        var cannon = model.Meshes["Cannon"];
        var oldCannon = cannon.ParentBone.Transform;
        model.Root.Transform = Matrix.CreateRotationY(yaw);
        cannon.ParentBone.Transform = Matrix.Identity;
        if (_bones.Length != model.Bones.Count)
            _bones = new Matrix[model.Bones.Count];
        model.CopyAbsoluteBoneTransformsTo(_bones);

        // a painted skin is drawn with the unwrapped tank
        var skinMesh = TankPaint.MeshFor(tex);
        if (skinMesh is not null && (!skinMesh.IsPlayerModel || !skinMesh.Begin(model)))
            skinMesh = null;
        try {
            foreach (var mesh in model.Meshes) {
                if (mesh.Name == "Shadow")
                    continue;
                foreach (BasicEffect effect in mesh.Effects) {
                    effect.World = _bones[mesh.ParentBone.Index];
                    effect.View = view;
                    effect.Projection = projection;
                    effect.TextureEnabled = true;
                    effect.Texture = tex;
                    effect.Alpha = 1f;
                    effect.EmissiveColor = Vector3.Zero;
                    effect.SetDefaultGameLighting_IngameEntities(specular: true, ambientMultiplier: 2f);
                }
                mesh.Draw();
            }
        }
        finally {
            skinMesh?.End();
            model.Root.Transform = oldRoot;
            cannon.ParentBone.Transform = oldCannon;
            gd.Viewport = oldViewport;
        }
    }

    // ------------------------------------------------------------------ the picture before a mission

    public const int PORTRAIT_W = 360, PORTRAIT_H = 200;
    private static readonly RenderTarget2D?[] _targets = new RenderTarget2D?[4];
    private static readonly Texture2D?[] _skinTex = new Texture2D?[4];
    private static readonly int[] _skinIndex = [-2, -2, -2, -2];
    /// <summary>The side-on tank angle (cannon pointing right, like the old picture).</summary>
    public static float SideYaw = MathHelper.PiOver2;

    /// <summary>The texture player <paramref name="player"/>'s tank uses: their colour or skin, or the standard one.</summary>
    public static Texture2D? TextureFor(GraphicsDevice gd, int player) {
        if (player is < 0 or > 3)
            return null;
        int index = PlayerColors.ColorIndexFor(player);
        if (index < 0) {
            string key = "plrtank_" + PlayerID.Collection.GetKey(player)!.ToLower();
            return Tank.Assets.TryGetValue(key, out var std) ? std : null;
        }
        if (_skinIndex[player] != index || _skinTex[player] is null or { IsDisposed: true }) {
            _skinTex[player]?.Dispose();
            _skinTex[player] = PlayerColors.MakeTexture(gd, index, playerModel: true);
            _skinIndex[player] = index;
        }
        if (PlayerColors.IsAnimated(index))
            PlayerColors.Animate(_skinTex[player]!, index);
        return _skinTex[player];
    }

    /// <summary>
    /// Draws player <paramref name="player"/>'s tank side-on into its own picture (transparent around it) and returns it,
    /// or null if there's nothing to draw. Call it outside any SpriteBatch, before switching to the render target the
    /// picture goes on (switching targets clears them).
    /// </summary>
    public static Texture2D? RenderSide(GraphicsDevice gd, int player) {
        var tex = TextureFor(gd, player);
        if (tex is null || ModelGlobals.TankPlayer.Asset is null)
            return null;
        ref var rt = ref _targets[player];
        if (rt is null || rt.IsDisposed)
            rt = new RenderTarget2D(gd, PORTRAIT_W, PORTRAIT_H, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);
        var oldTargets = gd.GetRenderTargets();
        gd.SetRenderTarget(rt);
        gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);
        // nearly side-on, a little from above so the top of the tank shows too
        DrawModel(gd, new Rectangle(0, 0, PORTRAIT_W, PORTRAIT_H), tex, SideYaw, new Vector3(0, 22, 100), 26f, PORTRAIT_W, PORTRAIT_H);
        gd.SetRenderTargets(oldTargets);
        return rt;
    }
}
