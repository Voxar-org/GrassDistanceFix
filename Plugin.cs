using VRage.Plugins;
using VRageRender;
using Sandbox.Graphics.GUI;

namespace GrassDistanceFix
{
    public sealed class Plugin : IPlugin
    {
        private bool loadedLive;

        public void Init(object gameInstance)
        {
            // Pulsar's preloader installs before shader creation. A live load must recompile.
            loadedLive = Preloader.Install();
            if (loadedLive)
                MyRenderProxy.ReloadEffects();
        }

        public void Update() { }

        public void OpenConfigDialog() => MyGuiSandbox.AddScreen(new SettingsScreen());

        public void Dispose()
        {
            if (!loadedLive)
                return;

            Preloader.Uninstall();
            loadedLive = false;
            MyRenderProxy.ReloadEffects();
        }
    }
}
