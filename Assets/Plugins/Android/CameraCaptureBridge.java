package com.ohlone.quest.camera;

import android.content.Context;
import android.graphics.ImageFormat;
import android.graphics.SurfaceTexture;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;

import java.nio.ByteBuffer;
import java.util.Arrays;

/**
 * Java Camera2 bridge for Unity on Quest 3.
 * Uses the Java Camera2 API instead of the NDK (libcamera2ndk.so) to avoid
 * the CFI crash that occurs when OVR's passthrough runtime holds the camera.
 * Java Camera2 failures throw CameraAccessException gracefully instead of SIGABRT.
 */
public class CameraCaptureBridge {
    private static final String TAG = "CameraCaptureBridge";

    private CameraDevice      mDevice;
    private CameraCaptureSession mSession;
    private ImageReader       mImageReader;
    private HandlerThread     mThread;
    private Handler           mHandler;

    private volatile boolean  mOpened      = false;
    private volatile String   mLastError   = null;
    private volatile byte[]   mLatestRgba  = null;
    private volatile int      mFrameWidth  = 0;
    private volatile int      mFrameHeight = 0;
    private volatile boolean  mHasNewFrame = false;

    // ── Public API (called from Unity C# via AndroidJavaObject) ───────────────

    /**
     * Opens the camera at the given index using Camera2 Java API.
     * Does NOT throw — errors are reported via getLastError().
     */
    public void open(Context context, int cameraIndex, int width, int height) {
        mThread = new HandlerThread("CameraCaptureBridge");
        mThread.start();
        mHandler = new Handler(mThread.getLooper());

        mImageReader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 2);
        mImageReader.setOnImageAvailableListener(mFrameListener, mHandler);

        CameraManager manager = (CameraManager) context.getSystemService(Context.CAMERA_SERVICE);
        try {
            String[] ids = manager.getCameraIdList();
            Log.d(TAG, "Camera IDs available: " + ids.length + " → requesting index " + cameraIndex);
            if (cameraIndex >= ids.length) {
                mLastError = "Camera index " + cameraIndex + " out of range (found " + ids.length + ")";
                Log.e(TAG, mLastError);
                return;
            }
            manager.openCamera(ids[cameraIndex], mDeviceCallback, mHandler);
            Log.d(TAG, "openCamera() called for id=" + ids[cameraIndex]);
        } catch (CameraAccessException e) {
            // This is the graceful failure path that Camera2 NDK turns into SIGABRT
            mLastError = "CameraAccessException: " + e.getMessage();
            Log.e(TAG, mLastError);
        } catch (SecurityException e) {
            mLastError = "SecurityException (need CAMERA permission): " + e.getMessage();
            Log.e(TAG, mLastError);
        } catch (Exception e) {
            mLastError = "Unexpected error: " + e.getMessage();
            Log.e(TAG, mLastError, e);
        }
    }

    public boolean isOpened()     { return mOpened; }
    public boolean hasNewFrame()  { return mHasNewFrame; }
    public String  getLastError() { return mLastError; }
    public int     getWidth()     { return mFrameWidth; }
    public int     getHeight()    { return mFrameHeight; }

    /** Returns the latest RGBA frame bytes, or null if none available. */
    public byte[] acquireLatestRgba() {
        if (!mHasNewFrame) return null;
        mHasNewFrame = false;
        return mLatestRgba;
    }

    public void close() {
        mOpened = false;
        try { if (mSession    != null) { mSession.close();    mSession    = null; } } catch (Exception ignored) {}
        try { if (mDevice     != null) { mDevice.close();     mDevice     = null; } } catch (Exception ignored) {}
        try { if (mImageReader!= null) { mImageReader.close();mImageReader= null; } } catch (Exception ignored) {}
        if (mThread != null) { mThread.quitSafely(); mThread = null; }
        mHandler = null;
        Log.d(TAG, "Camera closed.");
    }

    // ── Camera2 callbacks ─────────────────────────────────────────────────────

    private final CameraDevice.StateCallback mDeviceCallback = new CameraDevice.StateCallback() {
        @Override
        public void onOpened(CameraDevice device) {
            Log.d(TAG, "CameraDevice opened");
            mDevice = device;
            startCaptureSession();
        }
        @Override
        public void onDisconnected(CameraDevice device) {
            Log.w(TAG, "CameraDevice disconnected");
            mLastError = "Camera disconnected (may be held by another process)";
            device.close();
            mOpened = false;
        }
        @Override
        public void onError(CameraDevice device, int error) {
            // Error codes: 1=IN_USE, 2=MAX_CAMERAS, 3=DISABLED, 4=DEVICE_LOST, 5=SERVICE_DIED
            mLastError = "Camera error " + error + (error == 1 ? " (IN_USE — held by OVR passthrough)" : "");
            Log.e(TAG, mLastError);
            device.close();
            mOpened = false;
        }
    };

    private void startCaptureSession() {
        try {
            mDevice.createCaptureSession(
                Arrays.asList(mImageReader.getSurface()),
                new CameraCaptureSession.StateCallback() {
                    @Override
                    public void onConfigured(CameraCaptureSession session) {
                        mSession = session;
                        try {
                            CaptureRequest.Builder b = mDevice.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
                            b.addTarget(mImageReader.getSurface());
                            session.setRepeatingRequest(b.build(), null, mHandler);
                            mOpened = true;
                            Log.d(TAG, "Capture session running");
                        } catch (CameraAccessException e) {
                            mLastError = "setRepeatingRequest: " + e.getMessage();
                            Log.e(TAG, mLastError);
                        }
                    }
                    @Override
                    public void onConfigureFailed(CameraCaptureSession session) {
                        mLastError = "CaptureSession configure failed";
                        Log.e(TAG, mLastError);
                    }
                },
                mHandler
            );
        } catch (CameraAccessException e) {
            mLastError = "createCaptureSession: " + e.getMessage();
            Log.e(TAG, mLastError);
        }
    }

    // ── Frame processing ──────────────────────────────────────────────────────

    private final ImageReader.OnImageAvailableListener mFrameListener =
        reader -> {
            Image image = null;
            try {
                image = reader.acquireLatestImage();
                if (image == null) return;
                byte[] rgba = yuv420ToRgba(image);
                if (rgba != null) {
                    mLatestRgba  = rgba;
                    mFrameWidth  = image.getWidth();
                    mFrameHeight = image.getHeight();
                    mHasNewFrame = true;
                }
            } catch (Exception e) {
                Log.w(TAG, "Frame processing error: " + e.getMessage());
            } finally {
                if (image != null) image.close();
            }
        };

    // Minimal YUV420 → RGBA conversion (runs on camera background thread).
    private static byte[] yuv420ToRgba(Image image) {
        int w = image.getWidth();
        int h = image.getHeight();
        Image.Plane[] planes = image.getPlanes();
        if (planes.length < 3) return null;

        ByteBuffer yBuf  = planes[0].getBuffer();
        ByteBuffer uBuf  = planes[1].getBuffer();
        ByteBuffer vBuf  = planes[2].getBuffer();
        int yRowStride   = planes[0].getRowStride();
        int uvRowStride  = planes[1].getRowStride();
        int uvPixelStride = planes[1].getPixelStride();

        byte[] rgba = new byte[w * h * 4];
        int idx = 0;
        for (int row = 0; row < h; row++) {
            for (int col = 0; col < w; col++) {
                int yIdx = row * yRowStride + col;
                int uvRow = (row >> 1) * uvRowStride;
                int uvCol = (col >> 1) * uvPixelStride;
                int uIdx  = uvRow + uvCol;
                int vIdx  = uvRow + uvCol;

                int Y = (yBuf.get(yIdx) & 0xFF);
                int U = (uBuf.get(uIdx) & 0xFF) - 128;
                int V = (vBuf.get(vIdx) & 0xFF) - 128;

                int r = clamp(Y + (int)(1.370705f * V));
                int g = clamp(Y - (int)(0.337633f * U) - (int)(0.698001f * V));
                int b = clamp(Y + (int)(1.732446f * U));

                rgba[idx++] = (byte) r;
                rgba[idx++] = (byte) g;
                rgba[idx++] = (byte) b;
                rgba[idx++] = (byte) 255;
            }
        }
        return rgba;
    }

    private static int clamp(int v) {
        return v < 0 ? 0 : (v > 255 ? 255 : v);
    }
}
