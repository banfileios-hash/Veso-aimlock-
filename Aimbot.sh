# Build libaimlock.so voi NDK
# aimlock.c
#include <jni.h>
#include <android/log.h>
#include <pthread.h>
#include <unistd.h>

#define LOG_TAG "Aimlock"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)

void* aimlock_thread(void* arg) {
    while (1) {
        uintptr_t camera_addr = 0x7F8A4C08;
        float target_angle = 0.0f;
        
        asm volatile(
            "mov x0, %0\n"
            "mov x1, %1\n"
            "str s0, [x0]\n"
            : 
            : "r"(camera_addr), "r"(target_angle)
            : "x0", "x1"
        );
        
        usleep(10000);
    }
    return NULL;
}

JNIEXPORT void JNICALL Java_com_aimlock_Aimlock_start(JNIEnv* env, jobject thiz) {
    pthread_t thread;
    pthread_create(&thread, NULL, aimlock_thread, NULL);
}
