using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns selected detection instance IDs and resolves browser image clicks against
/// the most recently published detection result.
/// </summary>
public sealed class ObjectSelectionManager : MonoBehaviour, IDetectionConsumer
{
    [SerializeField, Min(0)] private int _lostIntervalsBeforeFailClosed = 2;

    private readonly HashSet<int> _selectedTrackIds = new HashSet<int>();
    private readonly Dictionary<int, int> _missedIntervals = new Dictionary<int, int>();
    private readonly Dictionary<int, LastKnownDetection> _lastKnownDetections =
        new Dictionary<int, LastKnownDetection>();

    private VisionResult _latestResult;
    private string _lastProcessedFrameId;

    private struct LastKnownDetection
    {
        public BoundingBox box;
        public int width;
        public int height;
    }

    public void OnDetectionResult(VisionResult result)
    {
        if (result == null)
            return;

        _latestResult = result;

        // SAM verification republishes the same YOLO result. Only count misses
        // once per inference frame.
        if (result.frame_id == _lastProcessedFrameId)
            return;
        _lastProcessedFrameId = result.frame_id;

        foreach (int trackId in _selectedTrackIds)
        {
            VisionObject current = FindObject(result, trackId);
            if (current != null)
            {
                _lastKnownDetections[trackId] = new LastKnownDetection
                {
                    box = current.box,
                    width = result.width,
                    height = result.height
                };
                _missedIntervals[trackId] = 0;
            }
            else
            {
                int missed;
                _missedIntervals.TryGetValue(trackId, out missed);
                _missedIntervals[trackId] = missed + 1;
            }
        }
    }

    public void NotifyInferenceUnavailable()
    {
        foreach (int trackId in _selectedTrackIds)
        {
            int missed;
            _missedIntervals.TryGetValue(trackId, out missed);
            _missedIntervals[trackId] = missed + 1;
        }
    }

    public bool ToggleAtNormalizedPoint(float x, float y)
    {
        if (!IsNormalizedCoordinate(x) || !IsNormalizedCoordinate(y)
            || _latestResult == null || _latestResult.width <= 0 || _latestResult.height <= 0
            || _latestResult.objects == null)
            return false;

        float imageX = x * _latestResult.width;
        float imageY = y * _latestResult.height;
        VisionObject best = null;
        float bestArea = float.MaxValue;

        foreach (var detection in _latestResult.objects)
        {
            if (detection == null || detection.track_id <= 0 || detection.box == null)
                continue;

            var box = detection.box;
            if (imageX < box.x1 || imageX > box.x2 || imageY < box.y1 || imageY > box.y2)
                continue;

            float area = Mathf.Max(0f, box.x2 - box.x1) * Mathf.Max(0f, box.y2 - box.y1);
            if (area < bestArea
                || (Mathf.Approximately(area, bestArea)
                    && best != null && detection.confidence > best.confidence))
            {
                best = detection;
                bestArea = area;
            }
        }

        if (best == null)
            return false;

        int trackId = best.track_id;
        if (!_selectedTrackIds.Add(trackId))
        {
            _selectedTrackIds.Remove(trackId);
            _missedIntervals.Remove(trackId);
            _lastKnownDetections.Remove(trackId);
        }
        else
        {
            _missedIntervals[trackId] = 0;
            _lastKnownDetections[trackId] = new LastKnownDetection
            {
                box = best.box,
                width = _latestResult.width,
                height = _latestResult.height
            };
        }

        return true;
    }

    public void ClearSelection()
    {
        _selectedTrackIds.Clear();
        _missedIntervals.Clear();
        _lastKnownDetections.Clear();
    }

    public bool IsSelected(int trackId) => _selectedTrackIds.Contains(trackId);

    public bool HasSelection => _selectedTrackIds.Count > 0;

    public bool ShouldMaskFullFrame
    {
        get
        {
            foreach (int trackId in _selectedTrackIds)
            {
                int missed;
                _missedIntervals.TryGetValue(trackId, out missed);
                if (missed > _lostIntervalsBeforeFailClosed)
                    return true;
            }
            return false;
        }
    }

    public void CopySelectedTrackIds(List<int> destination)
    {
        destination.Clear();
        destination.AddRange(_selectedTrackIds);
    }

    public bool TryGetLastKnownDetection(int trackId, out BoundingBox box, out int width, out int height)
    {
        LastKnownDetection detection;
        if (_lastKnownDetections.TryGetValue(trackId, out detection))
        {
            box = detection.box;
            width = detection.width;
            height = detection.height;
            return true;
        }

        box = null;
        width = 0;
        height = 0;
        return false;
    }

    private static VisionObject FindObject(VisionResult result, int trackId)
    {
        if (result.objects == null)
            return null;

        foreach (var detection in result.objects)
            if (detection != null && detection.track_id == trackId)
                return detection;
        return null;
    }

    private static bool IsNormalizedCoordinate(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }
}
