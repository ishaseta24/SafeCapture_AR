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
    public int         id;
    public BoundingBox box;
}

[Serializable]
public class BoundingBox
{
    public float x1, y1, x2, y2;
}
