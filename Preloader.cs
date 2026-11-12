using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using GrassDistanceFix;
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
    private const string GrassInclude = "#include \"GrassFoliage.hlsli\"";
    private const string FoliageInclude = "#include \"Foliage.hlsli\"";
    private const string TexcoordField = "float3 texcoord : TEXCOORD0;";
    private const string BillboardParameter = "uint textureIndex, inout TriangleStream<RenderingPixelInput>";
    private const string BillboardVertex = "RenderingPixelInput vertex;";
    private const string BillboardCall = "onb[0], textureIndex, triangle_stream);";
    private const string LodShrink = "scale *= distFraction;";
    private const string ObjectDither = "Dither(input.position.xyz, object_.custom_alpha);";

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
            harmony.Patch(compile, prefix: new HarmonyMethod(typeof(Preloader), nameof(ApplyOptions)),
                transpiler: new HarmonyMethod(typeof(Preloader), nameof(PatchCompile)));
        }
        catch
        {
            harmony.Unpatch(preprocess, HarmonyPatchType.Transpiler, HarmonyId);
            harmony.Unpatch(compile, HarmonyPatchType.Transpiler, HarmonyId);
            harmony.Unpatch(compile, HarmonyPatchType.Prefix, HarmonyId);
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

    private static void ApplyOptions(string filepath, ref ShaderMacro[] macros)
    {
        if (!IsFoliageShader(filepath) || macros.Any(macro => macro.Name == "ROCK_FOLIAGE"))
            return;

        // Snapshot once so cache lookup and bytecode compilation use the same options.
        var options = Options.Current;
        if (options == Options.Default)
            return;

        var selected = new List<ShaderMacro>(macros);
        if (!options.HasFlag(FadeOptions.Enabled))
            selected.Add(new ShaderMacro("GDF_DISABLED", null));
        else
        {
            if (!options.HasFlag(FadeOptions.DrawLimit))
                selected.Add(new ShaderMacro("GDF_NO_DRAW_LIMIT_FADE", null));
            if (!options.HasFlag(FadeOptions.Lod))
                selected.Add(new ShaderMacro("GDF_NO_LOD_FADE", null));
        }
        macros = selected.ToArray();
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
        if (!IsFoliageShader(path) || !source.Contains(GrassInclude) || !source.Contains(Bump)
            || !source.Contains(DistanceGrowth) || !source.Contains(LodShrink)
            || !source.Contains(BillboardCall) || !source.Contains(ObjectDither))
            return source;

        // Inline the two sibling includes only in the grass variant of this shader.
        // The renderer resolves their nested includes from the same directory as before.
        var directory = Path.GetDirectoryName(path);
        var grass = File.ReadAllText(Path.Combine(directory, "GrassFoliage.hlsli"));
        var foliage = File.ReadAllText(Path.Combine(directory, "Foliage.hlsli"));
        if (!grass.Contains(FoliageInclude) || !grass.Contains(BillboardParameter)
            || !grass.Contains(BillboardVertex) || !foliage.Contains(TexcoordField))
            return source;

        foliage = foliage.Replace(TexcoordField,
            TexcoordField + "\n    nointerpolation float coverage : TEXCOORD1;");
        grass = grass.Replace(FoliageInclude, foliage)
            .Replace(BillboardParameter, "float coverage, " + BillboardParameter)
            .Replace(BillboardVertex, BillboardVertex + "\n    vertex.coverage = coverage;");

        var patched = source.Replace(GrassInclude, grass)
            .Replace(Bump, "#ifdef ROCK_FOLIAGE\n        " + Bump +
                "\n#else\n        float coverage = 1.0f;\n#ifndef GDF_NO_DRAW_LIMIT_FADE\n" +
                "        coverage = smoothstep(0, far_clip - critical_point, far_clip - viewVectorLength);\n#endif\n#endif")
            .Replace(DistanceGrowth, string.Empty)
            .Replace(LodShrink, "#ifndef GDF_NO_LOD_FADE\n            coverage *= distFraction;\n#endif")
            .Replace(BillboardCall, "onb[0], coverage, textureIndex, triangle_stream);")
            .Replace(ObjectDither, ObjectDither +
                "\n#ifndef ROCK_FOLIAGE\n    Dither(input.position.xyz, float2(1.0f - input.coverage, 0.0f));\n#endif");
        return "#ifdef GDF_DISABLED\n" + source + "\n#else\n" + patched + "\n#endif";
    }
}
