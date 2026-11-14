using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Assigns stable instance IDs to detections by matching same-label boxes across
/// consecutive inference results. This is intentionally lightweight for Quest.
/// </summary>
public sealed class VisionObjectTracker
{
    private sealed class Track
    {
        public int id;
        public string label;
        public BoundingBox box;
        public int missedFrames;
    }

    private struct Match
    {
        public int trackIndex;
        public int detectionIndex;
        public float iou;
    }

    private readonly List<Track> _tracks = new List<Track>();
    private int _nextTrackId;

    private const float MatchIouThreshold = 0.20f;
    private const int MaxMissedFrames = 10;

    public void Update(VisionResult result)
    {
        if (result == null)
            return;

        var detections = result.objects ?? new VisionObject[0];
        var matches = new List<Match>();

        for (int trackIndex = 0; trackIndex < _tracks.Count; trackIndex++)
        {
            var track = _tracks[trackIndex];
            for (int detectionIndex = 0; detectionIndex < detections.Length; detectionIndex++)
            {
                var detection = detections[detectionIndex];
                if (detection == null || !string.Equals(track.label, detection.label))
                    continue;

                float overlap = IoU(track.box, detection.box);
                if (overlap >= MatchIouThreshold)
                    matches.Add(new Match
                    {
                        trackIndex = trackIndex,
                        detectionIndex = detectionIndex,
                        iou = overlap
                    });
            }
        }

        matches.Sort((a, b) => b.iou.CompareTo(a.iou));
        var matchedTracks = new bool[_tracks.Count];
        var matchedDetections = new bool[detections.Length];

        foreach (var match in matches)
        {
            if (matchedTracks[match.trackIndex] || matchedDetections[match.detectionIndex])
                continue;

            var track = _tracks[match.trackIndex];
            var detection = detections[match.detectionIndex];
            track.box = detection.box;
            track.missedFrames = 0;
            detection.track_id = track.id;
            matchedTracks[match.trackIndex] = true;
            matchedDetections[match.detectionIndex] = true;
        }

        for (int i = 0; i < _tracks.Count; i++)
        {
            if (!matchedTracks[i])
                _tracks[i].missedFrames++;
        }

        _tracks.RemoveAll(track => track.missedFrames > MaxMissedFrames);

        for (int i = 0; i < detections.Length; i++)
        {
            var detection = detections[i];
            if (detection == null || matchedDetections[i])
                continue;

            int trackId = ++_nextTrackId;
            detection.track_id = trackId;
            _tracks.Add(new Track
            {
                id = trackId,
                label = detection.label,
                box = detection.box,
                missedFrames = 0
            });
        }
    }

    private static float IoU(BoundingBox a, BoundingBox b)
    {
        float ix1 = Mathf.Max(a.x1, b.x1);
        float iy1 = Mathf.Max(a.y1, b.y1);
        float ix2 = Mathf.Min(a.x2, b.x2);
        float iy2 = Mathf.Min(a.y2, b.y2);
        float intersection = Mathf.Max(0f, ix2 - ix1) * Mathf.Max(0f, iy2 - iy1);
        float areaA = Mathf.Max(0f, a.x2 - a.x1) * Mathf.Max(0f, a.y2 - a.y1);
        float areaB = Mathf.Max(0f, b.x2 - b.x1) * Mathf.Max(0f, b.y2 - b.y1);
        float union = areaA + areaB - intersection;
        return union <= 0f ? 0f : intersection / union;
    }
}
