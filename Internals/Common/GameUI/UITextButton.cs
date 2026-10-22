using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals.UI;

namespace TanksRebirth.Internals.Common.GameUI;

public class UITextButton : UIImage
{
    public string Text { get; set; }

    public SpriteFontBase Font { get; set; }

    public new Color Color { get; set; }

    public Color HoverColor { get; set; } = Color.CornflowerBlue;

    public Func<Vector2> TextScale { get; set; }

    public float TextRotation;

    public static bool AutoResolutionHandle = true;
    public bool DrawText = true;
    public UITextButton(string text, SpriteFontBase font, Color color, Func<Vector2> textScale) : base(null, new(1), null)
    {
        Text = text;
        Font = font;
        Color = color;
        TextScale = textScale;
    }
    public UITextButton(string text, SpriteFontBase font, Color color, float textScale = 1f) : base(null, new(1), null)
    {
        Text = text;
        Font = font;
        Color = color;
        TextScale = () => new(textScale);
    }

    public override void DrawSelf(SpriteBatch spriteBatch)
    {
        if (Framework.Input.TouchControls.Enabled) {
            DrawPhone(spriteBatch);
            return;
        }
        DrawUtils.DrawNineSliced(spriteBatch, UIPanelBackground, 12, Hitbox, MouseHovering ? HoverColor : Color, GameUtils.GetAnchor(Anchor, UIPanelBackground.Size()));
        SpriteFontBase font = FontGlobals.RebirthFont;
        Vector2 drawOrigin = font.MeasureString(Text) / 2f;
        if (TextScale != null && DrawText)
            spriteBatch.DrawString(font, Text, Hitbox.Center.ToVector2() + TextOffset, Color.Black, AutoResolutionHandle ? TextScale.Invoke().ToResolution() : TextScale.Invoke(), TextRotation, drawOrigin);
        // extra decoration on top of the button (e.g. an on/off switch)
        UniqueDraw?.Invoke(this, spriteBatch);
    }

    public static readonly Color PhoneFill = new(250, 246, 236);
    public static readonly Color PhonePressed = new(214, 228, 255);
    public static readonly Color PhoneEdge = new(120, 96, 70);

    /// <summary>Phones: a rounded, finger-sized button with a raised edge and a label sized to fit.</summary>
    private void DrawPhone(SpriteBatch spriteBatch) {
        var box = Hitbox;
        bool pressed = MouseHovering && InputUtils.CurrentMouseSnapshot.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
        float radius = MathF.Min(box.Height * 0.32f, 28f.ToResolutionY() * 1.4f);
        int lip = Math.Max(3, (int)(box.Height * 0.07f));
        var face = pressed ? new Rectangle(box.X, box.Y + lip / 2, box.Width, box.Height - lip / 2) : new Rectangle(box.X, box.Y, box.Width, box.Height - lip);

        // raised edge, then the face
        ToggleSwitch.DrawRoundedRect(spriteBatch, new Rectangle(box.X, box.Y + lip, box.Width, box.Height - lip), radius, PhoneEdge);
        var fill = Color == Color.WhiteSmoke || Color == Color.White ? (pressed ? PhonePressed : PhoneFill) : Color;
        ToggleSwitch.DrawRoundedRect(spriteBatch, face, radius, fill);

        if (TextScale != null && DrawText && !string.IsNullOrEmpty(Text)) {
            SpriteFontBase font = FontGlobals.RebirthFont;
            var size = font.MeasureString(Text);
            float availW = face.Width - 2f * MathF.Abs(TextOffset.X) - face.Height * 0.5f;
            float scale = MathF.Min(face.Height * 0.42f / MathF.Max(1f, size.Y), availW / MathF.Max(1f, size.X));
            // never larger than the label would be at twice its desktop size
            var desktop = AutoResolutionHandle ? TextScale.Invoke().ToResolution() : TextScale.Invoke();
            scale = MathF.Min(scale, desktop.Y * 2.2f);
            spriteBatch.DrawString(font, Text, face.Center.ToVector2() + TextOffset, new Color(40, 30, 22), new Vector2(scale), TextRotation, size / 2f);
        }
        UniqueDraw?.Invoke(this, spriteBatch);
    }

    /// <summary>Shifts the label from the button's center, in pixels (e.g. to make room for a switch).</summary>
    public Vector2 TextOffset { get; set; }
}