package com.aimlock;

public class Aimlock {
    static {
        System.loadLibrary("aimlock");
    }
    
    public native int start();
    public native void stop();
    public native boolean isRunning();
    public native void setFov(float fov);
    public native void setSmoothness(float smooth);
    public native void setAimMode(int mode);
    public native void toggle();
    public native int getCurrentTarget();
    public native float getCurrentAngleX();
    public native float getCurrentAngleY();
    public native void setHeadshotOnly(boolean enable);
}
