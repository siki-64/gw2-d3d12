using System;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Exe.Live;

namespace Gw2.LiveMemory
{
    internal static class UiPackStateProbe
    {
        public static int Run(LiveProcess process)
        {
            foreach (var (shader, rva, size) in process.Addresses.UiMaterialPacks)
            {
                var bytes = new byte[size];
                if (!process.TryReadInto(process.ImageBase + rva, bytes))
                {
                    Console.WriteLine($"{shader}: unreadable");
                    continue;
                }
                var material = Gw2AmatMaterial.Parse(Gw2AssetDocument.Parse(bytes));
                for (var technique = 0; technique < material.Techniques.Count; technique++)
                for (var pass = 0; pass < material.Techniques[technique].Passes.Count; pass++)
                foreach (var effect in material.Techniques[technique].Passes[pass].Effects)
                {
                    var state = new Gw2AmatRenderState(effect.RenderState);
                    Console.WriteLine($"{shader} t{technique} p{pass} effect {effect.Token:X} state {effect.RenderState:X16} blended {state.IsBlended} colour {state.ColorSource}/{state.ColorDestination}/{state.ColorEquation} alpha {state.AlphaSource}/{state.AlphaDestination}/{state.AlphaEquation} alphaRef {state.AlphaReference:0.###} ps {effect.PixelShaderIndex}");
                }
            }
            return 0;
        }
    }
}
