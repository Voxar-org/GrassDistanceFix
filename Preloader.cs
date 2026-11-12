using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using SharpDX.Direct3D;
using SharpDX.D3DCompiler;

// Pulsar discovers this type by its unqualified name.
public static class Preloader
{
    private const string HarmonyId = "GrassDistanceFix.ShaderCompiler";

    private const string Bump =
        "scale *= min(mad(bump - 1.0f, smoothstep(0, critical_point, viewVectorLength), 1.0f), bump * smoothstep(0, far_clip - critical_point, far_clip - viewVectorLength));";
    private const string DistanceGrowth =
        "scale *= lerp(1, frame_.Foliage.clipping_scaling.w, saturate((viewVectorLength - frame_.Foliage.clipping_scaling.y) / frame_.Foliage.clipping_scaling.z));";
    private static readonly Regex LodShrink = new Regex(
        @"bool skipNext = GetSkip\(seed, part \+ 1\);\s*if \(skip != skipNext\)\s*\{\s*" +
        @"float distFraction = 1 - \(dist \* PARTS_COUNT - part\);\s*scale \*= distFraction;\s*\}");

    private static bool installed;

    public static void Finish() => Install();

    internal static bool Install()
    {
        if (installed)
            return false;

        var compiler = AccessTools.TypeByName("VRageRender.MyShaderCompiler")
            ?? Assembly.Load("VRage.Render11").GetType("VRageRender.MyShaderCompiler", throwOnError: true);
        var profile = compiler.Assembly.GetType("VRageRender.MyShaderProfile", throwOnError: true);
        var byRefString = typeof(string).MakeByRefType();
        var preprocess = AccessTools.DeclaredMethod(compiler, "PreprocessShader",
            new[] { typeof(string), typeof(ShaderMacro[]), byRefString });
        var compile = AccessTools.DeclaredMethod(compiler, "Compile", new[]
        {
            typeof(string), typeof(ShaderMacro[]), profile, typeof(string), typeof(bool), typeof(bool),
            typeof(bool).MakeByRefType(), byRefString, byRefString, typeof(bool), typeof(bool)
        });
        if (preprocess == null || compile == null)
            throw new MissingMethodException("Grass Distance Fix could not identify the shader compiler methods.");

        var harmony = new Harmony(HarmonyId);
        try
        {
            harmony.Patch(preprocess, transpiler: new HarmonyMethod(typeof(Preloader), nameof(PatchPreprocess)));
            harmony.Patch(compile, transpiler: new HarmonyMethod(typeof(Preloader), nameof(PatchCompile)));
        }
        catch
        {
            harmony.Unpatch(preprocess, HarmonyPatchType.Transpiler, HarmonyId);
            harmony.Unpatch(compile, HarmonyPatchType.Transpiler, HarmonyId);
            throw;
        }

        installed = true;
        return true;
    }

    internal static void Uninstall()
    {
        new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        installed = false;
    }

    private static IEnumerable<CodeInstruction> PatchPreprocess(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(ShaderBytecode), nameof(ShaderBytecode.PreprocessFromFile),
            new[] { typeof(string), typeof(ShaderMacro[]), typeof(Include), typeof(string).MakeByRefType() });
        return ReplaceCall(instructions, original, nameof(PreprocessSource));
    }

    private static IEnumerable<CodeInstruction> PatchCompile(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(File), nameof(File.ReadAllText), new[] { typeof(string) });
        return ReplaceCall(instructions, original, nameof(ReadSource));
    }

    private static IEnumerable<CodeInstruction> ReplaceCall(IEnumerable<CodeInstruction> instructions,
        MethodInfo original, string replacement)
    {
        var code = instructions.ToList();
        var calls = code.Where(instruction => instruction.Calls(original)).ToList();
        if (calls.Count != 1)
            throw new InvalidOperationException($"Grass Distance Fix expected one call to {original?.Name}, found {calls.Count}.");

        calls[0].operand = AccessTools.DeclaredMethod(typeof(Preloader), replacement);
        return code;
    }

    private static string PreprocessSource(string path, ShaderMacro[] macros, Include include, out string errors)
    {
        if (!IsFoliageShader(path))
            return ShaderBytecode.PreprocessFromFile(path, macros, include, out errors);

        // VRage hashes the preprocessed text, then separately reads the source to compile it.
        return ShaderBytecode.Preprocess(ReadSource(path), macros, include, out errors, path);
    }

    private static bool IsFoliageShader(string path)
    {
        return path.Replace('\\', '/').EndsWith("/Shaders/Foliage/Foliage.hlsl", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(string path)
    {
        var source = File.ReadAllText(path);
        if (!IsFoliageShader(path) || !source.Contains(Bump) || !source.Contains(DistanceGrowth)
            || !LodShrink.IsMatch(source))
            return source;

        var withoutGrowth = source.Replace(Bump,
                "#ifdef ROCK_FOLIAGE\n        " + Bump + "\n#endif")
            .Replace(DistanceGrowth, string.Empty);
        return LodShrink.Replace(withoutGrowth, string.Empty);
    }
}
