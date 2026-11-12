# Project guidance

- Build and load instructions are in [README.md](README.md). Builds write only local build output and do not deploy to the game.
- Pulsar calls the global `Preloader.Finish` before the renderer compiles foliage. `IPlugin.Init` runs after renderer startup.
- VRage hashes preprocessed HLSL but compiles a separate read of the raw source. Apply the same transformation to both paths so the shader cache cannot return vanilla grass for a patched source.
- Verify against the installed game's shader and compiler. Check grass and rock variants separately, including the grass draw-limit fade. A build alone does not verify the in-game appearance.
