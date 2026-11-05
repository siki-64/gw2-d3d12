using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Exe.Live;
using Gw2.Exe.Objects;

namespace Gw2.LiveMemory
{
    internal static class CompassEdgeProbe
    {
        private const ulong MarkerPinEdgeFlagsOffset = 104;
        private const ulong SubmodelSize = 232;
        private const ulong SubmodelOtherGeometryOffset = 0xD8;
        private const ulong SubmodelGeosetOffset = 0xE0;
        private const ulong ContentQueueOffset = 0x108;
        private const ulong ContentLayersOffset = 0x30;
        private const ulong ArrayDataOffset = 0x08;
        private const ulong ArrayCountOffset = 0x14;
        private const ulong ContentLayerSize = 0x20;
        private const ulong ContentEntrySize = 0x18;
        private const ulong ModelTypeOffset = 0x14;
        private const ulong ModelSubmodelsOffset = 0x58;
        private const ulong ModelSubmodelCountOffset = 0x64;

        public static int Run(LiveProcess process)
        {
            UiFrameTree? tree = UiFrameTree.TryRead(process, process.ImageBase + process.Addresses.FrameIdManagerRva);
            if (tree == null)
            {
                Console.WriteLine("no frame tree");
                return 1;
            }

            ulong markerProcedure = process.ImageBase + process.Addresses.CompassMarkerControlProcedureRva;
            var kindsByFrameCount = new SortedDictionary<uint, int>();
            int markerPins = 0;
            foreach (UiFrame frame in tree.Frames)
            {
                List<ulong> models = ContentModels(process, frame.Address);
                foreach (ulong model in models)
                foreach (var submodel in SubmodelKinds(process, model))
                {
                    kindsByFrameCount[submodel.Kind] = kindsByFrameCount.GetValueOrDefault(submodel.Kind) + 1;
                    if (submodel.Kind == 2)
                    {
                        process.TryRead(submodel.OtherGeometry, out ulong geometryTable);
                        process.TryRead(submodel.OtherGeometry + ModelTypeOffset, out uint geometryType);
                        UiFrameRectangle k = frame.ScreenRectangle;
                        Console.WriteLine($"kind 2 in frame {frame.Id} ({k.Left:0.#}, {k.Top:0.#}, {k.Right:0.#}, {k.Bottom:0.#}) model 0x{model:X} geometry 0x{submodel.OtherGeometry:X} vtable rva 0x{geometryTable - process.ImageBase:X} type {geometryType}");
                    }
                }

                ulong? control = UiFrameTree.TryReadControlOf(process, frame.Address, markerProcedure);
                if (control == null) continue;
                markerPins++;
                process.TryRead(control.Value + MarkerPinEdgeFlagsOffset, out uint edgeFlags);
                if ((edgeFlags & 1) == 0 && models.Count == 0) continue;
                UiFrameRectangle r = frame.ScreenRectangle;
                Console.WriteLine($"marker pin frame {frame.Id} ({r.Left:0.#}, {r.Top:0.#}, {r.Right:0.#}, {r.Bottom:0.#}) edge flags 0x{edgeFlags:X} models {models.Count}");
                foreach (ulong model in models)
                foreach (var submodel in SubmodelKinds(process, model))
                    Console.WriteLine($"  model 0x{model:X} submodel kind {submodel.Kind} geometry +0xD8 0x{submodel.OtherGeometry:X} geoset +0xE0 0x{submodel.Geoset:X}");
            }

            Console.WriteLine($"{markerPins} marker pins; submodel kinds over all frame content: {string.Join(" ", kindsByFrameCount.Select(pair => $"kind {pair.Key} x{pair.Value}"))}");
            return 0;
        }

        private static List<ulong> ContentModels(LiveProcess process, ulong frameAddress)
        {
            var models = new List<ulong>();
            ulong layersHeader = frameAddress + ContentQueueOffset + ContentLayersOffset;
            if (!process.TryRead(layersHeader + ArrayCountOffset, out uint layerCount) || layerCount > FrFrameContentQueue.LayerLimit) return models;
            if (!process.TryRead(layersHeader + ArrayDataOffset, out ulong layers) || layers == 0) return models;
            for (ulong layer = 0; layer < layerCount; layer++)
            {
                ulong contentLayer = layers + layer * ContentLayerSize;
                if (!process.TryRead(contentLayer + ArrayCountOffset, out uint entryCount) || !process.TryRead(contentLayer + ArrayDataOffset, out ulong entries) || entries == 0) continue;
                for (ulong entryIndex = 0; entryIndex < entryCount && entryIndex < 256; entryIndex++)
                    if (process.TryRead(entries + entryIndex * ContentEntrySize, out ulong model) && model != 0) models.Add(model);
            }
            return models;
        }

        private static List<(uint Kind, ulong OtherGeometry, ulong Geoset)> SubmodelKinds(LiveProcess process, ulong modelAddress)
        {
            var submodelsRead = new List<(uint Kind, ulong OtherGeometry, ulong Geoset)>();
            if (!process.TryRead(modelAddress + ModelTypeOffset, out uint type) || type != GrModel.ModelType) return submodelsRead;
            if (!process.TryRead(modelAddress + ModelSubmodelsOffset, out ulong submodels) || submodels == 0) return submodelsRead;
            if (!process.TryRead(modelAddress + ModelSubmodelCountOffset, out uint count)) return submodelsRead;
            for (ulong index = 0; index < count && index < 64; index++)
            {
                ulong submodel = submodels + index * SubmodelSize;
                process.TryRead(submodel, out uint kind);
                process.TryRead(submodel + SubmodelOtherGeometryOffset, out ulong otherGeometry);
                process.TryRead(submodel + SubmodelGeosetOffset, out ulong geoset);
                submodelsRead.Add((kind, otherGeometry, geoset));
            }
            return submodelsRead;
        }
    }
}
