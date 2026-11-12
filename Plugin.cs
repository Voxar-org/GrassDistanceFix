using VRage.Plugins;
using VRageRender;

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
