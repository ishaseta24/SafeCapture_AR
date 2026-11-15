using System;

[Serializable]
public class VisionResult
{
    public string        frame_id;
    public int           width;
    public int           height;
    public float         latency_ms;
    public VisionObject[] objects;
}

[Serializable]
public class VisionObject
{
    public string      label;
    public int         id; // Legacy alias for the model class ID.
    public int         class_id;
    public int         track_id;
    public float       confidence;
    public BoundingBox box;

    // Set by VisionScheduler after SAM verification — not in JSON
    [System.NonSerialized] public bool verified;
    [System.NonSerialized] public bool falseDetection;
}

[Serializable]
public class BoundingBox
{
    public float x1, y1, x2, y2;
}
