#import <AppKit/AppKit.h>
#import <AVFoundation/AVFoundation.h>
#import <Carbon/Carbon.h>
#import <Security/Security.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include "DeskPetMac.h"

static int checks;
static void Check(BOOL value, NSString *label) {
    if (!value) @throw [NSException exceptionWithName:@"NativeSelfTestFailure" reason:label userInfo:nil];
    printf("PASS %s\n", label.UTF8String); fflush(stdout); checks++;
}
static void HotKeyPressed(void) { }
static void Write16(uint8_t *bytes, uint16_t value) { bytes[0] = value & 255; bytes[1] = value >> 8; }
static void Write32(uint8_t *bytes, uint32_t value) { for (int i = 0; i < 4; i++) bytes[i] = (value >> (8*i)) & 255; }
static void Pump(double seconds) {
    NSDate *end = [NSDate dateWithTimeIntervalSinceNow:seconds];
    while (end.timeIntervalSinceNow > 0) [[NSRunLoop currentRunLoop] runMode:NSDefaultRunLoopMode beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
}
static void TestKeychain(void) {
    // Exercise the exact production implementation in a private temporary Keychain.
    // No production item, default Keychain, password argv, or persistent user preference.
    char directory[] = "/tmp/deskpet-keychain-test.XXXXXX";
    Check(mkdtemp(directory) != NULL, @"private Keychain test directory");
    NSString *path = [[NSString stringWithUTF8String:directory] stringByAppendingPathComponent:@"test.keychain-db"];
    CFArrayRef originalSearch = NULL;
    SecKeychainRef store = NULL;
    Boolean interaction = false;
    uint8_t password[32], original[32], reopened[32];
    Check(SecRandomCopyBytes(kSecRandomDefault, sizeof(password), password) == errSecSuccess, @"Keychain test password generated in memory");
    Check(SecKeychainCopySearchList(&originalSearch) == errSecSuccess, @"read original Keychain search list");
    Check(SecKeychainGetUserInteractionAllowed(&interaction) == errSecSuccess, @"read Keychain interaction policy");
    Check(SecKeychainSetUserInteractionAllowed(false) == errSecSuccess, @"disable prompts in self-test process");
    @try {
        OSStatus status = SecKeychainCreate(path.fileSystemRepresentation, sizeof(password), password, false, NULL, &store);
        Check(status == errSecSuccess, [NSString stringWithFormat:@"isolated Keychain created (status %d)", (int)status]);
        Check(dp_keychain_key_in_store(store, original, 32) == errSecSuccess, @"production data-key creation in isolated Keychain");
        Check(dp_keychain_key_in_store(store, reopened, 32) == errSecSuccess && memcmp(original, reopened, 32) == 0, @"existing data-key is reused");
        Check(SecKeychainLock(store) == errSecSuccess, @"lock isolated Keychain");
        Check(dp_keychain_key_in_store(store, reopened, 32) != errSecSuccess, @"locked Keychain fails without plaintext fallback or prompt");
        CFRelease(store); store = NULL;
        Check(SecKeychainOpen(path.fileSystemRepresentation, &store) == errSecSuccess, @"reopen persisted Keychain");
        Check(SecKeychainUnlock(store, sizeof(password), password, true) == errSecSuccess, @"unlock reopened test Keychain");
        Check(dp_keychain_key_in_store(store, reopened, 32) == errSecSuccess && memcmp(original, reopened, 32) == 0, @"data-key survives lock and reopen");
        Check(dp_keychain_key_in_store(NULL, reopened, 32) == errSecParam, @"Keychain null-store rejected");
        Check(dp_keychain_key_in_store(store, reopened, 31) == errSecParam, @"Keychain invalid output length rejected");
        NSDictionary *match = @{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService:@"com.deskpet.desktop.data-key.v1", (__bridge id)kSecAttrAccount:@"data-encryption-key",
            (__bridge id)kSecMatchSearchList:@[(__bridge id)store]};
        NSDictionary *broken = @{(__bridge id)kSecValueData:[NSData dataWithBytes:"x" length:1]};
        Check(SecItemUpdate((__bridge CFDictionaryRef)match, (__bridge CFDictionaryRef)broken) == errSecSuccess, @"isolate corrupted Keychain item fixture");
        Check(dp_keychain_key_in_store(store, reopened, 32) == errSecDecode, @"corrupted stored key is rejected instead of regenerated");
    } @finally {
        if (store) { SecKeychainDelete(store); CFRelease(store); }
        if (originalSearch) { SecKeychainSetSearchList(originalSearch); CFRelease(originalSearch); }
        SecKeychainSetUserInteractionAllowed(interaction);
        memset_s(password, sizeof(password), 0, sizeof(password));
        memset_s(original, sizeof(original), 0, sizeof(original));
        memset_s(reopened, sizeof(reopened), 0, sizeof(reopened));
        [[NSFileManager defaultManager] removeItemAtPath:[NSString stringWithUTF8String:directory] error:NULL];
    }
}
static void TestWindows(void) {
    [NSApplication sharedApplication];
    [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
    int count = dp_display_count(); Check(count > 0, @"AppKit display list available");
    for (int i = 0; i < count; ++i) {
        DPDisplay display;
        Check(dp_display(i, &display) == 1 && display.width > 0 && display.height > 0 && display.scale >= 1 && display.workWidth > 0 && display.workHeight > 0, @"display ABI and logical bounds");
    }
    DPDisplay display;
    Check(dp_display(-1, &display) == 0 && dp_display(0, NULL) == 0, @"invalid display arguments rejected");
    double x, y; dp_pointer(&x, &y);
    Check(isfinite(x) && isfinite(y), @"Quartz pointer ABI");
    Check(isfinite(dp_idle_seconds()) && dp_idle_seconds() >= 0, @"native idle query");
    int initialInteractive = dp_session_interactive();
    uint64_t initialLifecycle = dp_session_lifecycle_revision();
    Check(initialInteractive == 0 || initialInteractive == 1, @"session lifecycle ABI");
    NSNotificationCenter *workspaceCenter = NSWorkspace.sharedWorkspace.notificationCenter;
    // These are local process notifications; they never lock, sleep or switch the Mac.
    [workspaceCenter postNotificationName:NSWorkspaceSessionDidBecomeActiveNotification object:NSWorkspace.sharedWorkspace];
    [workspaceCenter postNotificationName:NSWorkspaceScreensDidWakeNotification object:NSWorkspace.sharedWorkspace];
    [workspaceCenter postNotificationName:NSWorkspaceDidWakeNotification object:NSWorkspace.sharedWorkspace]; Pump(.01);
    Check(dp_session_interactive() == 1, @"active awake session allows holiday animation");
    [workspaceCenter postNotificationName:NSWorkspaceScreensDidSleepNotification object:NSWorkspace.sharedWorkspace]; Pump(.01);
    Check(dp_session_interactive() == 0, @"display sleep blocks holiday animation");
    [workspaceCenter postNotificationName:NSWorkspaceScreensDidWakeNotification object:NSWorkspace.sharedWorkspace];
    [workspaceCenter postNotificationName:NSWorkspaceWillSleepNotification object:NSWorkspace.sharedWorkspace]; Pump(.01);
    Check(dp_session_interactive() == 0, @"system sleep blocks holiday animation");
    [workspaceCenter postNotificationName:NSWorkspaceDidWakeNotification object:NSWorkspace.sharedWorkspace];
    [workspaceCenter postNotificationName:NSWorkspaceSessionDidResignActiveNotification object:NSWorkspace.sharedWorkspace]; Pump(.01);
    Check(dp_session_interactive() == 0, @"inactive user session blocks holiday animation");
    Check(dp_session_lifecycle_revision() >= initialLifecycle + 3, @"sleep transitions remain observable after wake before managed tick");
    if (initialInteractive) [workspaceCenter postNotificationName:NSWorkspaceSessionDidBecomeActiveNotification object:NSWorkspace.sharedWorkspace]; Pump(.01);
    NSWindow *window = [[NSWindow alloc] initWithContentRect:NSMakeRect(20,20,64,64) styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];
    window.releasedWhenClosed = NO;
    @try {
        Check(dp_window((__bridge void *)window, 1, 1, 1) == 1 && window.level == NSFloatingWindowLevel && window.ignoresMouseEvents, @"NSWindow topmost and mouse policy");
        Check((window.collectionBehavior & NSWindowCollectionBehaviorCanJoinAllSpaces) != 0, @"join all Spaces enabled");
        Check(dp_window((__bridge void *)window.contentView, 0, 0, 0) == 1 && window.level == NSNormalWindowLevel && !window.ignoresMouseEvents, @"Avalonia NSView handle resolves to its window");
        double viewX, viewY, windowX, windowY;
        Check(dp_pointer_relative((__bridge void *)window.contentView, &viewX, &viewY) == 1 && dp_pointer_relative((__bridge void *)window, &windowX, &windowY) == 1, @"window-relative pointer accepts NSView and NSWindow");
        Check(isfinite(viewX) && isfinite(viewY) && fabs(viewX - windowX) < 2 && fabs(viewY - windowY) < 2, @"Retina pointer conversion uses content logical coordinates");
        Check((window.collectionBehavior & NSWindowCollectionBehaviorCanJoinAllSpaces) == 0, @"join all Spaces disabled");
        NSView *unattached = [NSView new];
        Check(dp_window((__bridge void *)unattached, 0, 0, 0) == 0 && dp_window(NULL, 0, 0, 0) == 0, @"missing window rejected without mutation");
        Check(dp_pointer_relative((__bridge void *)unattached, &x, &y) == 0 && dp_pointer_relative(NULL, &x, &y) == 0, @"unattached pointer target rejected");
    } @finally { [window close]; }
    Check(dp_login_status() >= 0, @"login-item status read without mutation");
    Check(dp_login_set(1) == -10001 && dp_login_set(0) == -10001, @"unbundled login changes return actionable error");
    Check(dp_fullscreen_front() == 0 || dp_fullscreen_front() == 1, @"fullscreen query does not request permission");
    int error = 0;
    Check(dp_hotkey_register(128, cmdKey, HotKeyPressed, &error) == NULL && error != 0, @"invalid virtual key rejected");
    void *registration = dp_hotkey_register(kVK_F20, cmdKey | shiftKey | optionKey | controlKey, HotKeyPressed, &error);
    Check(registration != NULL && error == 0, @"Carbon shortcut registration without accessibility permission");
    @try {
        void *duplicate = dp_hotkey_register(kVK_F20, cmdKey | shiftKey | optionKey | controlKey, HotKeyPressed, &error);
        if (duplicate) dp_hotkey_unregister(duplicate);
        Check(duplicate == NULL && error == eventHotKeyExistsErr, @"duplicate shortcut conflict reported");
    } @finally { dp_hotkey_unregister(registration); }
    registration = dp_hotkey_register(kVK_F20, cmdKey | shiftKey | optionKey | controlKey, HotKeyPressed, &error);
    Check(registration != NULL && error == 0, @"shortcut can be registered after disposal");
    dp_hotkey_unregister(registration);
}
static void TestAudio(BOOL outputHardware) {
    int permission = dp_microphone_status();
    Check(permission >= 0 && permission <= 3, @"microphone authorization status read without prompt");
    if (permission != AVAuthorizationStatusAuthorized) {
        int error = 0; void *mic = dp_microphone_start(&error);
        if (mic) dp_microphone_stop(mic);
        Check(mic == NULL && error == -1, @"not-authorized microphone start refuses without requesting permission");
    } else printf("SKIP denied-microphone path: runner already authorized; no recording requested\n");
    int error = 0;
    Check(dp_microphone_start(NULL) == NULL, @"invalid microphone error pointer rejected");
    Check(dp_microphone_read(NULL, NULL, 0) == -1, @"invalid microphone read rejected");
    Check(dp_output_start(44100, &error) == NULL && error != 0 && dp_output_start(16000, NULL) == NULL, @"unsupported PCM formats rejected before device open");
    Check(dp_output_enqueue(NULL, NULL, 0) != 0 && dp_output_pending(NULL) == -1 && dp_output_completed(NULL) == -1, @"invalid output handles return failure");
    Check(dp_wave_start(NULL, 0, 0) == NULL && dp_wave_status(NULL) == -1, @"invalid speech audio is rejected");
    uint8_t malformed[4] = {1,2,3,4};
    void *badWave = dp_wave_start(malformed, sizeof(malformed), 0);
    Check(badWave != NULL, @"speech player startup is queued without a managed-lock/main-thread deadlock");
    Pump(0.2);
    Check(dp_wave_status(badWave) == -1, @"malformed speech reports asynchronous decode failure"); dp_wave_stop(badWave); Pump(0.02);
    if (!outputHardware) { printf("SKIP hardware output: add --audio-smoke for silent-device test\n"); return; }
    void *output = dp_output_start(16000, &error);
    if (!output) { printf("SKIP hardware output: no usable output device (status %d)\n", error); Check(error != 0, @"missing output device reports an error"); return; }
    @try {
        dp_output_volume(output, 0); uint8_t silence[3200] = {0};
        Check(dp_output_enqueue(output, silence, sizeof(silence)) == 0, @"silent PCM scheduled on real output graph");
        NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:5];
        while (dp_output_pending(output) > 0 && deadline.timeIntervalSinceNow > 0) Pump(0.02);
        Check(dp_output_pending(output) == 0 && dp_output_completed(output) == sizeof(silence), @"real output graph reports played-back completion");
        Check(dp_output_interrupt(output) == 0 && dp_output_pending(output) == 0, @"output interrupt drains pending buffers");
    } @finally { dp_output_stop(output); }
    uint8_t waveBytes[44 + 3200] = {0};
    memcpy(waveBytes, "RIFF", 4); Write32(waveBytes+4, sizeof(waveBytes)-8); memcpy(waveBytes+8, "WAVEfmt ", 8);
    Write32(waveBytes+16, 16); Write16(waveBytes+20, 1); Write16(waveBytes+22, 1); Write32(waveBytes+24, 16000); Write32(waveBytes+28, 32000);
    Write16(waveBytes+32, 2); Write16(waveBytes+34, 16); memcpy(waveBytes+36, "data", 4); Write32(waveBytes+40, 3200);
    void *wave = dp_wave_start(waveBytes, sizeof(waveBytes), 0);
    Check(wave != NULL, @"cloud-speech WAV player handle created");
    @try {
        NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:5];
        while (dp_wave_status(wave) > 0 && deadline.timeIntervalSinceNow > 0) Pump(0.02);
        Check(dp_wave_status(wave) == 0, @"speech completion delegates run on the main run loop");
    } @finally { dp_wave_stop(wave); Pump(0.02); }
}
int main(int argc, const char **argv) {
    @autoreleasepool { @try {
        BOOL hardware = argc == 2 && strcmp(argv[1], "--audio-smoke") == 0;
        TestKeychain(); TestWindows(); TestAudio(hardware);
        printf("native_library_self_test: %d checks passed\n", checks); return 0;
    } @catch (NSException *exception) {
        fprintf(stderr, "FAIL native_library_self_test: %s: %s\n", exception.name.UTF8String, exception.reason.UTF8String); return 1;
    } }
}
