#pragma once
#include <stdint.h>

typedef struct { double x,y,width,height,workX,workY,workWidth,workHeight,scale; } DPDisplay;
int dp_keychain_key(uint8_t *bytes, int length);
// Explicit store dependency, for embedding and isolated integration tests. No secret data in paths/argv.
int dp_keychain_key_in_store(const void *store, uint8_t *bytes, int length);
int dp_microphone_status(void);
void dp_microphone_request(void (*completion)(int));
void *dp_microphone_start(int *error);
int dp_microphone_read(void *handle, uint8_t *data, int capacity);
void dp_microphone_stop(void *handle);
void *dp_output_start(int rate, int *error);
int dp_output_enqueue(void *handle, const uint8_t *data, int length);
int64_t dp_output_pending(void *handle);
int64_t dp_output_completed(void *handle);
void dp_output_volume(void *handle, float volume);
int dp_output_interrupt(void *handle);
void dp_output_stop(void *handle);
void *dp_wave_start(const uint8_t *bytes, int length, float volume);
int dp_wave_status(void *handle);
void dp_wave_volume(void *handle, float volume);
void dp_wave_stop(void *handle);
double dp_idle_seconds(void);
int dp_session_interactive(void);
uint64_t dp_session_lifecycle_revision(void);
void dp_pointer(double *x, double *y);
int dp_pointer_relative(void *handle, double *x, double *y);
int dp_display_count(void);
int dp_display(int index, DPDisplay *result);
int dp_window(void *handle, int topmost, int ignoreMouse, int allSpaces);
int dp_fullscreen_front(void);
void *dp_hotkey_register(uint32_t key, uint32_t modifiers, void (*callback)(void), int *error);
void dp_hotkey_unregister(void *handle);
int dp_login_status(void);
int dp_login_set(int enabled);
