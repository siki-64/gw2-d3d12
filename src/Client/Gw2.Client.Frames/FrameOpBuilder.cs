using System.Collections.Generic;

namespace Gw2.Client.Frames;

public static class FrameOpBuilder
{
    public static List<FrameOp> Build(FrameTree tree, FrameDrawList drawList)
    {
        var ops = new List<FrameOp>();
        var buckets = new List<List<FrameContentEntry>>();
        int? runLevel = null;
        FrameRect? runClip = null;

        foreach (var frame in drawList.DrawOrder)
        {
            if (frame.IsHidden)
                continue;
            var clip = EffectiveClip(tree, frame);
            var clipChanged = runClip != clip;
            if (runLevel != frame.DrawLevel || clipChanged || frame.DrawsItself)
            {
                Flush(ops, buckets);
                if (clipChanged)
                    ops.Add(new FrameSetClipOp(clip));
                if (frame.DrawsItself)
                    ops.Add(new FrameDrawSelfOp(frame));
                runLevel = frame.DrawLevel;
                runClip = clip;
            }

            var bucketIndex = 0;
            foreach (var layer in frame.ContentLayers)
            {
                if (layer.Count == 0)
                    continue;
                while (buckets.Count <= bucketIndex)
                    buckets.Add(new List<FrameContentEntry>());
                buckets[bucketIndex].AddRange(layer);
                bucketIndex++;
            }
        }

        Flush(ops, buckets);
        return ops;
    }

    private static FrameRect EffectiveClip(FrameTree tree, Frame frame)
    {
        for (Frame? current = frame; current != null; current = current.Parent)
            if (current.ClipRect.HasValue)
                return current.ClipRect.Value;
        return tree.ScreenBounds;
    }

    private static void Flush(List<FrameOp> ops, List<List<FrameContentEntry>> buckets)
    {
        var models = new List<FrameContentEntry>();
        foreach (var bucket in buckets)
        {
            models.AddRange(bucket);
            bucket.Clear();
        }
        if (models.Count > 0)
            ops.Add(new FrameDrawModelsOp(models));
    }
}
