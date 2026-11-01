// Single-window, single-frame observation. No stream, audio, AX or disk output.
#import <AppKit/AppKit.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <ImageIO/ImageIO.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <math.h>
#include <limits.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include "DeskPetMac.h"

static atomic_bool ObservationBusy;
static void ObservationMain(void (^work)(void)) {
    if (NSThread.isMainThread) work(); else dispatch_sync(dispatch_get_main_queue(), work);
}
static int ObservationAccess(void) {
    if (@available(macOS 14.0, *)) {
        if (!CGPreflightScreenCaptureAccess()) return DPObservationPermissionRequired;
        if (!dp_session_interactive()) return DPObservationNonInteractive;
        return DPObservationReady;
    }
    return DPObservationUnsupported;
}
// CG's front-to-back list is used only to select the first ordinary window of
// the frontmost application. We do not search other applications or a display.
// Non-bundled applications are skipped: never use PID/name as a persistent exclusion ID.
static NSDictionary *ObservationForeground(void) {
    __block NSDictionary *result = nil;
    ObservationMain(^{
        NSRunningApplication *front = NSWorkspace.sharedWorkspace.frontmostApplication;
        if (!front || front.processIdentifier == getpid() || front.terminated || !front.bundleIdentifier.length || !front.launchDate) return;
        if (NSBundle.mainBundle.bundleIdentifier.length && [front.bundleIdentifier isEqual:NSBundle.mainBundle.bundleIdentifier]) return;
        NSArray *windows = CFBridgingRelease(CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID));
        for (NSDictionary *window in windows) {
            if ([window[(__bridge id)kCGWindowOwnerPID] intValue] != front.processIdentifier || [window[(__bridge id)kCGWindowLayer] intValue] != 0) continue;
            // Reject this foreground window rather than silently choosing an underlying one.
            if (![window[(__bridge id)kCGWindowIsOnscreen] boolValue] || [window[(__bridge id)kCGWindowAlpha] doubleValue] <= 0 ||
                [window[(__bridge id)kCGWindowSharingState] intValue] == kCGWindowSharingNone) return;
            CGRect rect;
            if (!CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)window[(__bridge id)kCGWindowBounds], &rect) ||
                !isfinite(rect.origin.x) || !isfinite(rect.origin.y) || !isfinite(rect.size.width) || !isfinite(rect.size.height) ||
                rect.size.width < 1 || rect.size.height < 1 || rect.size.width > INT_MAX || rect.size.height > INT_MAX ||
                fabs(rect.origin.x) > INT_MAX || fabs(rect.origin.y) > INT_MAX) return;
            NSString *title = window[(__bridge id)kCGWindowName];
            if (![title isKindOfClass:NSString.class] || title.length > 4096) return;
            result = @{ @"WindowId":window[(__bridge id)kCGWindowNumber], @"ProcessId":@(front.processIdentifier),
                @"ProcessStartTicks":@((int64_t)((front.launchDate.timeIntervalSince1970 + 62135596800.0) * 10000000.0)),
                @"WindowThreadId":@0, @"ApplicationId":front.bundleIdentifier, @"WindowTitle":title, @"WindowClass":@"",
                @"X":@((int)floor(rect.origin.x)), @"Y":@((int)floor(rect.origin.y)),
                @"Width":@((int)ceil(rect.size.width)), @"Height":@((int)ceil(rect.size.height)) };
            return;
        }
    });
    return result;
}
static NSDictionary *ObservationParse(const char *json) {
    if (!json || strnlen(json, 32769) > 32768) return nil;
    NSData *data = [NSData dataWithBytes:json length:strlen(json)];
    id parsed = [NSJSONSerialization JSONObjectWithData:data options:0 error:NULL];
    if (![parsed isKindOfClass:NSDictionary.class]) return nil;
    return parsed;
}
static BOOL ObservationCurrent(NSDictionary *expected) {
    if (!expected || ObservationAccess() != DPObservationReady) return NO;
    NSDictionary *current = ObservationForeground();
    if (!current) return NO;
    for (NSString *key in current) if (![current[key] isEqual:expected[key]]) return NO;
    return YES;
}
int dp_observation_status(void) {
    @autoreleasepool {
        if (atomic_load(&ObservationBusy)) return DPObservationBusy;
        int access = ObservationAccess();
        if (access != DPObservationReady) return access;
        return ObservationForeground() ? DPObservationReady : DPObservationNoForeground;
    }
}
char *dp_observation_target(void) {
    @autoreleasepool {
        if (ObservationAccess() != DPObservationReady) return NULL;
        NSDictionary *target = ObservationForeground();
        if (!target) return NULL;
        NSData *data = [NSJSONSerialization dataWithJSONObject:target options:0 error:NULL];
        if (!data || data.length > 32768) return NULL;
        char *json = calloc(data.length + 1, 1);
        if (json) memcpy(json, data.bytes, data.length);
        return json;
    }
}
void dp_observation_target_free(char *target) {
    if (!target) return;
    size_t length = strlen(target); memset_s(target, length, 0, length); free(target);
}
int dp_observation_current(const char *target) {
    @autoreleasepool { return ObservationCurrent(ObservationParse(target)) ? 1 : 0; }
}
int dp_observation_request_access(void) {
    __block int allowed = 0;
    ObservationMain(^{ allowed = CGRequestScreenCaptureAccess() ? 1 : 0; });
    return allowed;
}

// Only this owner releases encoded bytes. Callback receives a borrow valid until return.
static void ObservationFinish(DPObservationCallback callback, void *context, int status, CFMutableDataRef png) {
    @try { callback(context, status, png ? CFDataGetBytePtr(png) : NULL, png ? (int)CFDataGetLength(png) : 0); }
    @finally {
        if (png) { CFIndex length = CFDataGetLength(png); memset_s(CFDataGetMutableBytePtr(png), length, 0, length); CFRelease(png); }
        atomic_store(&ObservationBusy, false);
    }
}
static CFMutableDataRef ObservationEncode(CGImageRef image) {
    if (!image) return NULL;
    size_t width = CGImageGetWidth(image), height = CGImageGetHeight(image);
    if (!width || !height || width > 1280 || height > 1280 || width * height > 1000000) return NULL;
    size_t length = width * height * 4;
    uint8_t *pixels = calloc(length, 1);
    if (!pixels) return NULL;
    CGColorSpaceRef color = CGColorSpaceCreateDeviceRGB();
    CGContextRef bitmap = CGBitmapContextCreate(pixels, width, height, 8, width * 4, color, kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big);
    CGColorSpaceRelease(color);
    CFMutableDataRef png = NULL;
    if (bitmap) {
        CGContextDrawImage(bitmap, CGRectMake(0, 0, width, height), image);
        // Empty/black/unrenderable frames are conservative skips. Uniform images
        // are also skipped, avoiding accidental delivery of a protected placeholder.
        BOOL visible = NO, varied = NO;
        for (size_t i = 0; i < length; i += 4) {
            if (pixels[i+3] && (pixels[i] || pixels[i+1] || pixels[i+2])) visible = YES;
            if (memcmp(pixels, pixels+i, 4) != 0) varied = YES;
        }
        if (visible && varied) {
            png = CFDataCreateMutable(kCFAllocatorDefault, 0);
            CGImageDestinationRef destination = png ? CGImageDestinationCreateWithData(png, CFSTR("public.png"), 1, NULL) : NULL;
            BOOL encoded = NO;
            if (destination) { CGImageDestinationAddImage(destination, image, NULL); encoded = CGImageDestinationFinalize(destination); CFRelease(destination); }
            if (!encoded || CFDataGetLength(png) > 2097152) {
                if (png) { CFIndex size = CFDataGetLength(png); memset_s(CFDataGetMutableBytePtr(png), size, 0, size); CFRelease(png); png = NULL; }
            }
        }
        CGContextRelease(bitmap);
    }
    memset_s(pixels, length, 0, length); free(pixels);
    return png;
}
int dp_observation_begin(const char *target, DPObservationCallback callback, void *context) {
    if (!callback || !context) return 0;
    @autoreleasepool {
        NSDictionary *expected = ObservationParse(target);
        if (!expected || ![expected[@"WindowId"] isKindOfClass:NSNumber.class]) return 0;
        bool idle = false;
        if (!atomic_compare_exchange_strong(&ObservationBusy, &idle, true)) return 0;
        // ARC's copied blocks own expected and completion until the last native
        // callback. No caller timeout can destroy them or release this native slot.
        double deadline = NSProcessInfo.processInfo.systemUptime + 2.0;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            @autoreleasepool {
                if (!ObservationCurrent(expected) || NSProcessInfo.processInfo.systemUptime >= deadline) {
                    ObservationFinish(callback, context, DPObservationSkipped, NULL); return;
                }
                [SCShareableContent getShareableContentExcludingDesktopWindows:YES onScreenWindowsOnly:YES completionHandler:^(SCShareableContent *content, NSError *error) {
                    @autoreleasepool {
                        if (error || !content || !ObservationCurrent(expected) || NSProcessInfo.processInfo.systemUptime >= deadline) {
                            ObservationFinish(callback, context, DPObservationSkipped, NULL); return;
                        }
                        SCWindow *selected = nil;
                        for (SCWindow *window in content.windows) if (window.windowID == [expected[@"WindowId"] unsignedIntValue] &&
                            window.owningApplication.processID == [expected[@"ProcessId"] intValue] && window.onScreen && window.windowLayer == 0 &&
                            [window.owningApplication.bundleIdentifier isEqual:expected[@"ApplicationId"]]) { selected = window; break; }
                        CGRect selectedFrame = selected ? selected.frame : CGRectZero;
                        if (!selected || floor(selectedFrame.origin.x) != [expected[@"X"] intValue] || floor(selectedFrame.origin.y) != [expected[@"Y"] intValue] ||
                            ceil(selectedFrame.size.width) != [expected[@"Width"] intValue] || ceil(selectedFrame.size.height) != [expected[@"Height"] intValue] ||
                            !ObservationCurrent(expected)) { ObservationFinish(callback, context, DPObservationSkipped, NULL); return; }
                        double width = [expected[@"Width"] doubleValue], height = [expected[@"Height"] doubleValue];
                        double scale = MIN(1.0, MIN(1280.0 / MAX(width, height), sqrt(1000000.0 / (width * height))));
                        SCStreamConfiguration *configuration = [SCStreamConfiguration new];
                        configuration.width = MAX(1, (size_t)floor(width * scale)); configuration.height = MAX(1, (size_t)floor(height * scale));
                        configuration.showsCursor = NO; configuration.capturesAudio = NO;
                        configuration.ignoreShadowsSingleWindow = YES;
                        SCContentFilter *filter = [[SCContentFilter alloc] initWithDesktopIndependentWindow:selected];
                        [SCScreenshotManager captureImageWithFilter:filter configuration:configuration completionHandler:^(CGImageRef image, NSError *captureError) {
                            @autoreleasepool {
                                CFMutableDataRef png = !captureError && ObservationCurrent(expected) ? ObservationEncode(image) : NULL;
                                if (png && (!ObservationCurrent(expected) || NSProcessInfo.processInfo.systemUptime >= deadline)) {
                                    CFIndex size = CFDataGetLength(png); memset_s(CFDataGetMutableBytePtr(png), size, 0, size); CFRelease(png); png = NULL;
                                }
                                ObservationFinish(callback, context, png ? DPObservationReady : DPObservationSkipped, png);
                            }
                        }];
                    }
                }];
            }
        });
        return 1;
    }
}

static void ObservationTestCallback(void *context, int status, const uint8_t *bytes, int length) {
    int *checks = context;
    if (status == DPObservationReady && length == 4 && bytes && memcmp(bytes, "test", 4) == 0 && atomic_load(&ObservationBusy)) (*checks)++;
}
// Permission-free ABI/ownership checks. No ScreenCaptureKit request is started.
int dp_observation_self_test(void) {
    if (atomic_load(&ObservationBusy)) return 0;
    if (dp_observation_begin(NULL, NULL, NULL) != 0 || dp_observation_current(NULL) != 0 || ObservationParse("[]") != nil) return 0;
    char *text = strdup("private fixture"); dp_observation_target_free(text); dp_observation_target_free(NULL);
    int checks = 0;
    if (dp_observation_begin("[]", ObservationTestCallback, &checks) != 0 || checks != 0 || ObservationEncode(NULL) != NULL) return 0;
    CFMutableDataRef bytes = CFDataCreateMutable(kCFAllocatorDefault, 0);
    if (!bytes) return 0;
    CFDataAppendBytes(bytes, (const uint8_t *)"test", 4);
    CFRetain(bytes); // Test's extra reference lets it inspect zeroization after production release.
    bool idle = false;
    if (!atomic_compare_exchange_strong(&ObservationBusy, &idle, true)) { CFRelease(bytes); CFRelease(bytes); return 0; }
    ObservationFinish(ObservationTestCallback, &checks, DPObservationReady, bytes);
    const uint8_t zero[4] = {0};
    BOOL cleared = memcmp(CFDataGetBytePtr(bytes), zero, 4) == 0;
    CFRelease(bytes);
    return checks == 1 && cleared && !atomic_load(&ObservationBusy);
}
