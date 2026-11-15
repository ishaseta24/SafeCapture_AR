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

    // Pixel-perfect mask from relay (packed 1-bit-per-pixel, row-major).
    // maskB64 decoded into mask[] by SamRelayProvider after JSON parse.
    public string maskB64;
    public int    maskW;
    public int    maskH;

    // Set by VisionScheduler after SAM merge — not in JSON
    [System.NonSerialized] public bool   verified;      // found by both YOLO and SAM → green
    [System.NonSerialized] public bool   samOnly;       // found by SAM only → distinct color
    [System.NonSerialized] public bool   falseDetection;
    [System.NonSerialized] public bool[] mask;          // decoded from maskB64; null = no mask
}

[Serializable]
public class BoundingBox
{
    public float x1, y1, x2, y2;
}
