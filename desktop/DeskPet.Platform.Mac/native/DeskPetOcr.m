#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
#import <Vision/Vision.h>
#include <math.h>
#include <string.h>

static CGImageRef DPCreateOCRImage(NSURL *url, int page) {
    if (page == 0) {
        CGImageSourceRef source = CGImageSourceCreateWithURL((__bridge CFURLRef)url, NULL);
        if (!source) return NULL;
        NSDictionary *options = @{ (__bridge NSString *)kCGImageSourceCreateThumbnailFromImageAlways: @YES,
                                   (__bridge NSString *)kCGImageSourceCreateThumbnailWithTransform: @YES,
                                   (__bridge NSString *)kCGImageSourceThumbnailMaxPixelSize: @3400 };
        CGImageRef image = CGImageSourceCreateThumbnailAtIndex(source, 0, (__bridge CFDictionaryRef)options);
        CFRelease(source);
        return image;
    }
    CGPDFDocumentRef document = CGPDFDocumentCreateWithURL((__bridge CFURLRef)url);
    if (!document) return NULL;
    CGPDFPageRef current = CGPDFDocumentGetPage(document, (size_t)page);
    if (!current || (CGPDFDocumentIsEncrypted(document) && !CGPDFDocumentIsUnlocked(document))) {
        CGPDFDocumentRelease(document);
        return NULL;
    }
    CGRect box = CGPDFPageGetBoxRect(current, kCGPDFCropBox);
    int rotation = CGPDFPageGetRotationAngle(current);
    if (rotation % 180 != 0) { CGFloat width = box.size.width; box.size.width = box.size.height; box.size.height = width; }
    CGFloat longest = MAX(box.size.width, box.size.height);
    if (!isfinite(longest) || !isfinite(box.size.width) || !isfinite(box.size.height) ||
        longest <= 0 || box.size.width <= 0 || box.size.height <= 0) {
        CGPDFDocumentRelease(document);
        return NULL;
    }
    CGFloat scale = MIN((CGFloat)2.5, (CGFloat)3400 / longest);
    size_t width = (size_t)MAX(1, ceil(box.size.width * scale));
    size_t height = (size_t)MAX(1, ceil(box.size.height * scale));
    CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
    CGContextRef context = CGBitmapContextCreate(NULL, width, height, 8, width * 4, space, kCGImageAlphaPremultipliedLast);
    CGColorSpaceRelease(space);
    if (!context) { CGPDFDocumentRelease(document); return NULL; }
    CGRect canvas = CGRectMake(0, 0, width, height);
    CGContextSetRGBFillColor(context, 1, 1, 1, 1);
    CGContextFillRect(context, canvas);
    CGContextConcatCTM(context, CGPDFPageGetDrawingTransform(current, kCGPDFCropBox, canvas, 0, true));
    CGContextDrawPDFPage(context, current);
    CGImageRef image = CGBitmapContextCreateImage(context);
    CGContextRelease(context);
    CGPDFDocumentRelease(document);
    return image;
}

int dp_recognize_file(const char *path, int page, const char *language, char *output, int capacity) {
    @autoreleasepool {
        if (!path || !language || !output || page < 0 || capacity < 1 || capacity > 32768) return -1;
        NSString *name = [NSString stringWithUTF8String:path];
        NSString *tag = [NSString stringWithUTF8String:language];
        if (!name || !tag || (![@"zh-Hans" isEqualToString:tag] && ![@"en-US" isEqualToString:tag])) return -1;
        CGImageRef image = DPCreateOCRImage([NSURL fileURLWithPath:name], page);
        if (!image) return -1;
        VNRecognizeTextRequest *request = [[VNRecognizeTextRequest alloc] init];
        request.recognitionLevel = VNRequestTextRecognitionLevelAccurate;
        NSError *error = nil;
        NSArray<NSString *> *languages = [request supportedRecognitionLanguagesAndReturnError:&error];
        if (![languages containsObject:tag]) { CGImageRelease(image); return -2; }
        request.recognitionLanguages = [tag isEqualToString:@"zh-Hans"] ? @[@"zh-Hans", @"en-US"] : @[@"en-US"];
        request.usesLanguageCorrection = YES;
        VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithCGImage:image options:@{}];
        BOOL recognized = [handler performRequests:@[request] error:&error];
        CGImageRelease(image);
        if (!recognized) return -1;
        int length = 0;
        for (VNRecognizedTextObservation *observation in request.results) {
            VNRecognizedText *candidate = [observation topCandidates:1].firstObject;
            NSData *bytes = [candidate.string dataUsingEncoding:NSUTF8StringEncoding];
            if (bytes.length == 0) continue;
            if (bytes.length + (size_t)length + 1 > (size_t)capacity) break;
            if (length != 0) output[length++] = '\n';
            memcpy(output + length, bytes.bytes, bytes.length);
            length += (int)bytes.length;
        }
        return length;
    }
}
