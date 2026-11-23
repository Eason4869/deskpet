#import <AppKit/AppKit.h>
#import <AVFoundation/AVFoundation.h>
#import <ImageIO/ImageIO.h>
#include <math.h>
#include <stdint.h>
#include <string.h>

int dp_extract_video_frame(const char *path, int64_t milliseconds, uint8_t *output, int capacity, int64_t *actualMilliseconds) {
    @autoreleasepool {
        if (!path || !output || !actualMilliseconds || milliseconds < 0 || capacity <= 0 || capacity > 4 * 1024 * 1024)
            return -1;
        NSString *name = [NSString stringWithUTF8String:path];
        if (!name || ![name.pathExtension.lowercaseString isEqualToString:@"mp4"]) return -1;
        AVURLAsset *asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:name]
            options:@{ AVURLAssetReferenceRestrictionsKey: @(AVAssetReferenceRestrictionForbidAll),
                       AVURLAssetPreferPreciseDurationAndTimingKey: @YES }];
        AVAssetImageGenerator *generator = [[AVAssetImageGenerator alloc] initWithAsset:asset];
        generator.appliesPreferredTrackTransform = YES;
        generator.maximumSize = CGSizeMake(960, 960);
        generator.requestedTimeToleranceBefore = kCMTimeZero;
        generator.requestedTimeToleranceAfter = CMTimeMake(100, 1000);
        CMTime actual = kCMTimeInvalid;
        NSError *error = nil;
        CGImageRef image = [generator copyCGImageAtTime:CMTimeMake(milliseconds, 1000) actualTime:&actual error:&error];
        if (!image) return -1;
        NSMutableData *encoded = [NSMutableData data];
        CGImageDestinationRef destination = CGImageDestinationCreateWithData((__bridge CFMutableDataRef)encoded,
            CFSTR("public.png"), 1, NULL);
        if (!destination) { CGImageRelease(image); return -1; }
        CGImageDestinationAddImage(destination, image, NULL);
        BOOL success = CGImageDestinationFinalize(destination);
        CFRelease(destination);
        CGImageRelease(image);
        if (!success || encoded.length == 0 || encoded.length > (size_t)capacity) return -1;
        memcpy(output, encoded.bytes, encoded.length);
        double seconds = CMTimeGetSeconds(actual);
        *actualMilliseconds = isfinite(seconds) ? (int64_t)llround(seconds * 1000.0) : -1;
        return (int)encoded.length;
    }
}
