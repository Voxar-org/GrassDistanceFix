using System;
using System.IO;
using System.Security;
using System.Text;
using Sandbox.Graphics.GUI;
using VRageMath;

namespace GrassDistanceFix
{
    internal sealed class SettingsScreen : MyGuiScreenBase
    {
        private readonly MyGuiControlCheckbox enabled;
        private readonly MyGuiControlCheckbox drawLimit;
        private readonly MyGuiControlCheckbox lod;

        internal SettingsScreen() : base(new Vector2(0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR,
            new Vector2(0.34f, 0.34f))
        {
            EnabledBackgroundFade = true;
            CloseButtonEnabled = true;
            AddCaption("Grass Distance Fix");
            var options = Options.Current;
            enabled = AddOption(-0.072f, "Enable grass size fix", options.HasFlag(FadeOptions.Enabled));
            drawLimit = AddOption(-0.027f, "Fade near the draw limit", options.HasFlag(FadeOptions.DrawLimit));
            lod = AddOption(0.018f, "Fade during LOD transitions", options.HasFlag(FadeOptions.Lod));
            enabled.IsCheckedChanged += _ => UpdateFadeControls();
            UpdateFadeControls();
            Controls.Add(new MyGuiControlButton(position: new Vector2(0, 0.104f),
                text: new StringBuilder("Apply"), onButtonClick: _ => Apply()));
        }

        public override string GetFriendlyName() => "GrassDistanceFixSettings";

        private MyGuiControlCheckbox AddOption(float y, string label, bool value)
        {
            var checkbox = new MyGuiControlCheckbox(new Vector2(-0.115f, y), isChecked: value);
            checkbox.Name = label;
            Controls.Add(checkbox);
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.083f, y), text: label));
            return checkbox;
        }

        private void UpdateFadeControls()
        {
            drawLimit.Enabled = enabled.IsChecked;
            lod.Enabled = enabled.IsChecked;
        }

        private void Apply()
        {
            FadeOptions options = 0;
            if (enabled.IsChecked) options |= FadeOptions.Enabled;
            if (drawLimit.IsChecked) options |= FadeOptions.DrawLimit;
            if (lod.IsChecked) options |= FadeOptions.Lod;

            try
            {
                Options.Apply(options);
                CloseScreen();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(MyMessageBoxStyleEnum.Error,
                    MyMessageBoxButtonsType.OK, new StringBuilder(ex.Message),
                    new StringBuilder("Could not save Grass Distance Fix settings")));
            }
        }
    }
}
