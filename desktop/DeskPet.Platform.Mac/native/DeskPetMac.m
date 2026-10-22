#import <AppKit/AppKit.h>
#import <AVFoundation/AVFoundation.h>
#import <Carbon/Carbon.h>
#import <Security/Security.h>
#import <ServiceManagement/ServiceManagement.h>
#include <math.h>
#include <string.h>
#include "DeskPetMac.h"

// This library has no initializers that request permissions or open audio devices.
static void MainSync(dispatch_block_t block) {
    if ([NSThread isMainThread]) block(); else dispatch_sync(dispatch_get_main_queue(), block);
}

int dp_keychain_key_in_store(const void *store, uint8_t *bytes, int length) {
    @autoreleasepool {
        if (!store || !bytes || length != 32) return errSecParam;
        SecKeychainRef keychain = (SecKeychainRef)store;
        NSMutableDictionary *query = [@{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,
            (__bridge id)kSecAttrService:@"com.deskpet.desktop.data-key.v1",
            (__bridge id)kSecAttrAccount:@"data-encryption-key",
            (__bridge id)kSecMatchSearchList:@[(__bridge id)keychain],
            (__bridge id)kSecReturnData:@YES,
            (__bridge id)kSecMatchLimit:(__bridge id)kSecMatchLimitOne} mutableCopy];
        CFTypeRef result = NULL;
        OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
        if (status == errSecItemNotFound) {
            uint8_t generated[32]; status = SecRandomCopyBytes(kSecRandomDefault, sizeof(generated), generated);
            if (status != errSecSuccess) return status;
            [query removeObjectForKey:(__bridge id)kSecReturnData];
            [query removeObjectForKey:(__bridge id)kSecMatchLimit];
            [query removeObjectForKey:(__bridge id)kSecMatchSearchList];
            query[(__bridge id)kSecValueData] = [NSData dataWithBytes:generated length:32];
            // macOS file-based Keychain uses the creating application's default ACL.
            // kSecAttrAccessible is a data-protection-Keychain attribute, not a file ACL.
            query[(__bridge id)kSecUseKeychain] = (__bridge id)keychain;
            query[(__bridge id)kSecAttrLabel] = @"DeskPet private data encryption key";
            status = SecItemAdd((__bridge CFDictionaryRef)query, NULL);
            memset_s(generated, sizeof(generated), 0, sizeof(generated));
            if (status != errSecSuccess && status != errSecDuplicateItem) return status;
            // A concurrent process may have won creation. Always read the stored winner.
            [query removeObjectForKey:(__bridge id)kSecValueData];
            [query removeObjectForKey:(__bridge id)kSecUseKeychain];
            [query removeObjectForKey:(__bridge id)kSecAttrLabel];
            query[(__bridge id)kSecMatchSearchList] = @[(__bridge id)keychain];
            query[(__bridge id)kSecReturnData] = @YES;
            query[(__bridge id)kSecMatchLimit] = (__bridge id)kSecMatchLimitOne;
            status = SecItemCopyMatching((__bridge CFDictionaryRef)query, &result);
        }
        if (status != errSecSuccess) { if (result) CFRelease(result); return status; }
        NSData *data = CFBridgingRelease(result);
        if (data.length != 32) return errSecDecode;
        memcpy(bytes, data.bytes, 32); return 0;
    }
}
int dp_keychain_key(uint8_t *bytes, int length) {
    if (!bytes || length != 32) return errSecParam;
    // Search the same store we write to, never another matching item on the search list.
    SecKeychainRef store = NULL;
    OSStatus status = SecKeychainCopyDefault(&store);
    if (status == errSecSuccess) status = dp_keychain_key_in_store(store, bytes, length);
    if (store) CFRelease(store);
    return status;
}

int dp_microphone_status(void) { return (int)[AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeAudio]; }
void dp_microphone_request(void (*completion)(int)) {
    if (!completion) return;
    [AVCaptureDevice requestAccessForMediaType:AVMediaTypeAudio completionHandler:^(BOOL granted) { completion(granted ? 1 : 0); }];
}

@interface DPMicrophone : NSObject
@property(strong) AVAudioEngine *engine;
@property(strong) AVAudioConverter *converter;
@property(strong) NSLock *lock;
@property(strong) NSMutableData *pending;
@property(strong) id observer;
@property BOOL failed;
@property BOOL tapInstalled;
- (void)stop;
@end
@implementation DPMicrophone
- (instancetype)init { if ((self = [super init])) { _lock = [NSLock new]; _pending = [NSMutableData new]; } return self; }
- (void)stop {
    [_engine stop];
    if (_tapInstalled) { [_engine.inputNode removeTapOnBus:0]; _tapInstalled = NO; }
    if (_observer) { [[NSNotificationCenter defaultCenter] removeObserver:_observer]; _observer = nil; }
    [_lock lock]; [_pending resetBytesInRange:NSMakeRange(0, _pending.length)]; [_pending setLength:0]; [_lock unlock];
}
@end

void *dp_microphone_start(int *error) {
    if (!error) return NULL;
    @autoreleasepool {
        DPMicrophone *mic = [DPMicrophone new];
        @try {
        if (dp_microphone_status() != AVAuthorizationStatusAuthorized) { *error = -1; return NULL; }
        mic.engine = [AVAudioEngine new];
        AVAudioInputNode *input = mic.engine.inputNode;
        AVAudioFormat *source = [input outputFormatForBus:0];
        if (source.channelCount == 0 || source.sampleRate == 0) { *error = -2; return NULL; }
        AVAudioFormat *target = [[AVAudioFormat alloc] initWithCommonFormat:AVAudioPCMFormatInt16 sampleRate:16000 channels:1 interleaved:NO];
        mic.converter = [[AVAudioConverter alloc] initFromFormat:source toFormat:target];
        if (!mic.converter) { *error = -3; return NULL; }
        __weak DPMicrophone *weakMic = mic;
        [input installTapOnBus:0 bufferSize:2048 format:source block:^(AVAudioPCMBuffer *buffer, AVAudioTime *time) {
            DPMicrophone *strongMic = weakMic; if (!strongMic) return;
            @autoreleasepool {
                AVAudioFrameCount capacity = (AVAudioFrameCount)ceil(buffer.frameLength * 16000.0 / source.sampleRate) + 32;
                AVAudioPCMBuffer *output = [[AVAudioPCMBuffer alloc] initWithPCMFormat:target frameCapacity:capacity];
                __block BOOL supplied = NO; NSError *conversionError = nil;
                AVAudioConverterOutputStatus status = [strongMic.converter convertToBuffer:output error:&conversionError withInputFromBlock:^AVAudioBuffer *(AVAudioPacketCount count, AVAudioConverterInputStatus *state) {
                    if (supplied) { *state = AVAudioConverterInputStatus_NoDataNow; return nil; }
                    supplied = YES; *state = AVAudioConverterInputStatus_HaveData; return buffer;
                }];
                [strongMic.lock lock];
                if (status == AVAudioConverterOutputStatus_Error || conversionError) strongMic.failed = YES;
                else if (strongMic.pending.length + output.frameLength * 2 > 16000 * 2 * 5) strongMic.failed = YES;
                else if (output.frameLength) [strongMic.pending appendBytes:output.int16ChannelData[0] length:output.frameLength * 2];
                [strongMic.lock unlock];
            }
        }];
        mic.tapInstalled = YES;
        NSError *startError = nil; [mic.engine prepare];
        if (![mic.engine startAndReturnError:&startError]) {
            [mic stop];
            *error = (int)(startError.code ?: -4); return NULL;
        }
        mic.observer = [[NSNotificationCenter defaultCenter] addObserverForName:AVAudioEngineConfigurationChangeNotification object:mic.engine queue:nil usingBlock:^(NSNotification *note) {
            DPMicrophone *strongMic = weakMic; [strongMic.lock lock]; strongMic.failed = YES; [strongMic.lock unlock];
        }];
        *error = 0; return (__bridge_retained void *)mic;
    } @catch (NSException *exception) { [mic stop]; *error = -5; return NULL; } }
}
int dp_microphone_read(void *handle, uint8_t *data, int capacity) {
    @autoreleasepool {
        DPMicrophone *mic = (__bridge DPMicrophone *)handle;
        if (!mic || !data || capacity < 2) return -1;
        [mic.lock lock];
        if (mic.failed || !mic.engine.isRunning || dp_microphone_status() != AVAuthorizationStatusAuthorized) { [mic.lock unlock]; return -1; }
        NSUInteger count = MIN(mic.pending.length, (NSUInteger)(capacity & ~1));
        if (count) { memcpy(data, mic.pending.bytes, count); [mic.pending replaceBytesInRange:NSMakeRange(0, count) withBytes:NULL length:0]; }
        [mic.lock unlock]; return (int)count;
    }
}
void dp_microphone_stop(void *handle) {
    if (!handle) return;
    @autoreleasepool { DPMicrophone *mic = CFBridgingRelease(handle);
        [mic stop];
    }
}

@interface DPOutput : NSObject
@property(strong) AVAudioEngine *engine;
@property(strong) AVAudioPlayerNode *player;
@property(strong) AVAudioFormat *format;
@property(strong) NSLock *lock;
@property int64_t pending;
@property int64_t completed;
@property NSUInteger generation;
@end
@implementation DPOutput
@end
void *dp_output_start(int rate, int *error) {
    if (!error) return NULL;
    @autoreleasepool { @try {
        if (rate != 16000 && rate != 24000 && rate != 48000) { *error = -1; return NULL; }
        DPOutput *output = [DPOutput new]; output.lock = [NSLock new]; output.engine = [AVAudioEngine new]; output.player = [AVAudioPlayerNode new];
        // AVAudioEngine mixer uses Float32; incoming mono Int16 is converted explicitly below.
        output.format = [[AVAudioFormat alloc] initWithCommonFormat:AVAudioPCMFormatFloat32 sampleRate:rate channels:1 interleaved:NO];
        [output.engine attachNode:output.player]; [output.engine connect:output.player to:output.engine.mainMixerNode format:output.format];
        NSError *failure = nil; [output.engine prepare];
        if (![output.engine startAndReturnError:&failure]) { *error = (int)(failure.code ?: -2); return NULL; }
        [output.player play]; *error = 0; return (__bridge_retained void *)output;
    } @catch (NSException *exception) { *error = -3; return NULL; } }
}
int dp_output_enqueue(void *handle, const uint8_t *data, int length) {
    @autoreleasepool { @try {
        DPOutput *output = (__bridge DPOutput *)handle;
        if (!output || !data || !output.engine.isRunning || length < 2 || length % 2) return -1;
        AVAudioPCMBuffer *buffer = [[AVAudioPCMBuffer alloc] initWithPCMFormat:output.format frameCapacity:length / 2]; buffer.frameLength = length / 2;
        float *samples = buffer.floatChannelData[0];
        for (int i = 0; i < length / 2; ++i) { int16_t sample = (int16_t)((uint16_t)data[i * 2] | ((uint16_t)data[i * 2 + 1] << 8)); samples[i] = sample / 32768.0f; }
        [output.lock lock]; output.pending += length; NSUInteger generation = output.generation; [output.lock unlock];
        __weak DPOutput *weakOutput = output;
        [output.player scheduleBuffer:buffer completionCallbackType:AVAudioPlayerNodeCompletionDataPlayedBack completionHandler:^(AVAudioPlayerNodeCompletionCallbackType type) {
            DPOutput *strongOutput = weakOutput; if (!strongOutput) return;
            [strongOutput.lock lock];
            if (generation == strongOutput.generation) {
                strongOutput.pending = MAX((int64_t)0, strongOutput.pending - length);
                strongOutput.completed += length;
            }
            [strongOutput.lock unlock];
        }];
        return 0;
    } @catch (NSException *exception) { return -2; } }
}
int64_t dp_output_pending(void *handle) {
    DPOutput *output = (__bridge DPOutput *)handle; if (!output || !output.engine.isRunning) return -1;
    [output.lock lock]; int64_t result = output.pending; [output.lock unlock]; return result;
}
int64_t dp_output_completed(void *handle) {
    DPOutput *output = (__bridge DPOutput *)handle; if (!output) return -1;
    [output.lock lock]; int64_t result = output.completed; [output.lock unlock]; return result;
}
void dp_output_volume(void *handle, float volume) { DPOutput *output = (__bridge DPOutput *)handle; output.player.volume = volume; }
int dp_output_interrupt(void *handle) {
    if (!handle) return -1;
    @try {
        DPOutput *output = (__bridge DPOutput *)handle;
        [output.lock lock]; output.generation++; output.pending = 0; [output.lock unlock];
        [output.player stop];
        if (!output.engine.isRunning) return -1;
        [output.player play]; return 0;
    } @catch (NSException *exception) { return -1; }
}
void dp_output_stop(void *handle) { if (!handle) return; @autoreleasepool { DPOutput *output = CFBridgingRelease(handle); [output.player stop]; [output.engine stop]; } }

@interface DPWave : NSObject <AVAudioPlayerDelegate>
@property(strong) AVAudioPlayer *player;
@property(atomic) int status;
@property(atomic) BOOL stopped;
@property(atomic) float volume;
@end
@implementation DPWave
- (void)audioPlayerDidFinishPlaying:(AVAudioPlayer *)player successfully:(BOOL)flag { self.status = flag ? 0 : -1; }
- (void)audioPlayerDecodeErrorDidOccur:(AVAudioPlayer *)player error:(NSError *)error { self.status = -1; }
@end
void *dp_wave_start(const uint8_t *bytes, int length, float volume) {
    @autoreleasepool {
        if (!bytes || length < 1) return NULL;
        DPWave *wave = [DPWave new]; wave.volume = volume; wave.status = 1;
        NSData *data = [NSData dataWithBytes:bytes length:length];
        // AVAudioPlayer delegates require a live run loop. Do not create it on a managed
        // pool thread, and do not synchronously dispatch while C# holds its lifecycle lock.
        dispatch_async(dispatch_get_main_queue(), ^{ @autoreleasepool { @try {
            if (wave.stopped) return;
            NSError *error = nil;
            wave.player = [[AVAudioPlayer alloc] initWithData:data error:&error];
            if (!wave.player || error) { wave.status = -1; return; }
            wave.player.delegate = wave; wave.player.volume = wave.volume;
            if (![wave.player prepareToPlay] || ![wave.player play]) wave.status = -1;
        } @catch (NSException *exception) { wave.status = -1; } } });
        return (__bridge_retained void *)wave;
    }
}
int dp_wave_status(void *handle) {
    DPWave *wave = (__bridge DPWave *)handle;
    return wave ? wave.status : -1;
}
void dp_wave_volume(void *handle, float volume) {
    if (!handle) return;
    DPWave *wave = (__bridge DPWave *)handle; wave.volume = volume;
    dispatch_async(dispatch_get_main_queue(), ^{ if (!wave.stopped) wave.player.volume = wave.volume; });
}
void dp_wave_stop(void *handle) {
    if (!handle) return;
    @autoreleasepool { DPWave *wave = CFBridgingRelease(handle); wave.stopped = YES; wave.status = 0;
        dispatch_async(dispatch_get_main_queue(), ^{ [wave.player stop]; wave.player.delegate = nil; wave.player = nil; });
    }
}

double dp_idle_seconds(void) { return CGEventSourceSecondsSinceLastEventType(kCGEventSourceStateCombinedSessionState, kCGAnyInputEventType); }

static uint64_t DPWorkspaceLifecycleRevision;
int dp_session_interactive(void) {
    __block int result = 0;
    MainSync(^{
        static BOOL sessionActive = YES, screensAwake = YES, systemAwake = YES;
        static NSMutableArray *observers;
        static dispatch_once_t once;
        dispatch_once(&once, ^{
            CFDictionaryRef session = CGSessionCopyCurrentDictionary();
            if (session) {
                CFTypeRef console = CFDictionaryGetValue(session, kCGSessionOnConsoleKey);
                CFTypeRef login = CFDictionaryGetValue(session, kCGSessionLoginDoneKey);
                sessionActive = console == kCFBooleanTrue && login == kCFBooleanTrue;
                CFRelease(session);
            } else sessionActive = NO;
            observers = [NSMutableArray array];
            NSNotificationCenter *center = NSWorkspace.sharedWorkspace.notificationCenter;
            [observers addObject:[center addObserverForName:NSWorkspaceSessionDidResignActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) { sessionActive = NO; DPWorkspaceLifecycleRevision++; }]];
            [observers addObject:[center addObserverForName:NSWorkspaceSessionDidBecomeActiveNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) { sessionActive = YES; }]];
            [observers addObject:[center addObserverForName:NSWorkspaceScreensDidSleepNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) { screensAwake = NO; DPWorkspaceLifecycleRevision++; }]];
            [observers addObject:[center addObserverForName:NSWorkspaceScreensDidWakeNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) { screensAwake = YES; }]];
            [observers addObject:[center addObserverForName:NSWorkspaceWillSleepNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) { systemAwake = NO; DPWorkspaceLifecycleRevision++; }]];
            [observers addObject:[center addObserverForName:NSWorkspaceDidWakeNotification object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) { systemAwake = YES; }]];
        });
        result = sessionActive && screensAwake && systemAwake ? 1 : 0;
    });
    return result;
}
uint64_t dp_session_lifecycle_revision(void) {
    dp_session_interactive();
    __block uint64_t result;
    MainSync(^{ result = DPWorkspaceLifecycleRevision; });
    return result;
}
void dp_pointer(double *x, double *y) { CGEventRef event = CGEventCreate(NULL); CGPoint point = event ? CGEventGetLocation(event) : CGPointZero; if (event) CFRelease(event); *x = point.x; *y = point.y; }
int dp_pointer_relative(void *handle, double *x, double *y) {
    if (!handle || !x || !y) return 0;
    __block int result = 0;
    MainSync(^{
        id object = (__bridge id)handle;
        NSView *view = [object isKindOfClass:NSView.class] ? object : ([object isKindOfClass:NSWindow.class] ? ((NSWindow *)object).contentView : nil);
        if (!view.window) return;
        NSPoint point = [view convertPoint:[view.window convertPointFromScreen:NSEvent.mouseLocation] fromView:nil];
        *x = point.x - NSMinX(view.bounds);
        *y = view.isFlipped ? point.y - NSMinY(view.bounds) : NSMaxY(view.bounds) - point.y;
        result = 1;
    }); return result;
}
int dp_display_count(void) { __block int count; MainSync(^{ count = (int)NSScreen.screens.count; }); return count; }
int dp_display(int index, DPDisplay *result) {
    if (!result) return 0;
    __block int found = 0;
    MainSync(^{ NSArray<NSScreen *> *screens = NSScreen.screens;
        if (index < 0 || index >= (int)screens.count) return;
        NSScreen *screen = screens[index]; NSRect frame = screen.frame, work = screen.visibleFrame;
        double top = NSMaxY(screens[0].frame);
        *result = (DPDisplay){frame.origin.x,top-NSMaxY(frame),frame.size.width,frame.size.height,work.origin.x,top-NSMaxY(work),work.size.width,work.size.height,screen.backingScaleFactor}; found = 1;
    }); return found;
}
int dp_window(void *handle, int topmost, int ignoreMouse, int allSpaces) {
    if (!handle) return 0;
    __block int result = 0;
    MainSync(^{ id object = (__bridge id)handle;
        if ([object isKindOfClass:[NSView class]]) object = ((NSView *)object).window;
        if (![object isKindOfClass:[NSWindow class]]) return;
        NSWindow *window = object; window.level = topmost ? NSFloatingWindowLevel : NSNormalWindowLevel; window.ignoresMouseEvents = ignoreMouse != 0;
        NSWindowCollectionBehavior flags = window.collectionBehavior;
        flags &= ~(NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorMoveToActiveSpace | NSWindowCollectionBehaviorFullScreenAuxiliary);
        if (allSpaces) { flags &= ~NSWindowCollectionBehaviorFullScreenPrimary; flags |= NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary; }
        window.collectionBehavior = flags; result = 1;
    }); return result;
}
int dp_fullscreen_front(void) {
    @autoreleasepool {
        pid_t front = NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier;
        NSArray *windows = CFBridgingRelease(CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID));
        for (NSDictionary *window in windows) {
            if ([window[(__bridge id)kCGWindowOwnerPID] intValue] != front || [window[(__bridge id)kCGWindowLayer] intValue] != 0) continue;
            CGRect bounds; if (!CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)window[(__bridge id)kCGWindowBounds], &bounds)) continue;
            uint32_t count = 0; CGGetActiveDisplayList(0, NULL, &count);
            CGDirectDisplayID displays[32]; count = MIN(count, 32u); CGGetActiveDisplayList(count, displays, &count);
            for (uint32_t i = 0; i < count; ++i) { CGRect screen = CGDisplayBounds(displays[i]);
                if (fabs(bounds.origin.x-screen.origin.x)<2 && fabs(bounds.origin.y-screen.origin.y)<2 && fabs(bounds.size.width-screen.size.width)<2 && fabs(bounds.size.height-screen.size.height)<2) return 1;
            }
        } return 0;
    }
}

typedef struct { EventHotKeyRef hotkey; EventHandlerRef handler; void (*callback)(void); UInt32 identifier; uint64_t combination; } DPHotKey;
static NSMutableSet<NSNumber *> *HotKeyCombinations(void) {
    static NSMutableSet<NSNumber *> *combinations; static dispatch_once_t once;
    dispatch_once(&once, ^{ combinations = [NSMutableSet new]; }); return combinations;
}
static OSStatus HotKeyEvent(EventHandlerCallRef next, EventRef event, void *context) {
    DPHotKey *hotkey = context; EventHotKeyID identifier;
    if (GetEventParameter(event, kEventParamDirectObject, typeEventHotKeyID, NULL, sizeof(identifier), NULL, &identifier) == noErr && identifier.signature == 'DPet' && identifier.id == hotkey->identifier) { hotkey->callback(); return noErr; }
    return eventNotHandledErr;
}
void *dp_hotkey_register(uint32_t key, uint32_t modifiers, void (*callback)(void), int *error) {
    if (!error) return NULL;
    if (!callback || key > 127 || modifiers == 0 || (modifiers & ~(cmdKey | shiftKey | optionKey | controlKey))) { *error = paramErr; return NULL; }
    __block DPHotKey *hotkey = NULL;
    MainSync(^{
        static UInt32 nextId = 1;
        uint64_t combination = ((uint64_t)modifiers << 32) | key;
        if ([HotKeyCombinations() containsObject:@(combination)]) { *error = eventHotKeyExistsErr; return; }
        DPHotKey *entry = calloc(1, sizeof(DPHotKey));
        if (!entry) { *error = memFullErr; return; }
        entry->callback = callback; entry->identifier = nextId++; entry->combination = combination;
        EventTypeSpec type = {kEventClassKeyboard, kEventHotKeyPressed};
        *error = InstallApplicationEventHandler(HotKeyEvent, 1, &type, entry, &entry->handler);
        if (*error == noErr) *error = RegisterEventHotKey(key, modifiers, (EventHotKeyID){'DPet', entry->identifier}, GetApplicationEventTarget(), kEventHotKeyExclusive, &entry->hotkey);
        if (*error != noErr) { if (entry->handler) RemoveEventHandler(entry->handler); free(entry); }
        else { hotkey = entry; [HotKeyCombinations() addObject:@(combination)]; }
    }); return hotkey;
}
void dp_hotkey_unregister(void *handle) { if (!handle) return; MainSync(^{ DPHotKey *entry = handle; UnregisterEventHotKey(entry->hotkey); RemoveEventHandler(entry->handler); [HotKeyCombinations() removeObject:@(entry->combination)]; free(entry); }); }
int dp_login_status(void) { __block int status = -1; MainSync(^{ @try { status = (int)SMAppService.mainAppService.status; } @catch (NSException *exception) { status = -1; } }); return status; }
int dp_login_set(int enabled) {
    __block int result = 0;
    MainSync(^{ @try {
        NSBundle *bundle = NSBundle.mainBundle;
        if (!bundle.bundleIdentifier.length || ![bundle.bundleURL.pathExtension.lowercaseString isEqualToString:@"app"]) { result = -10001; return; }
        SMAppService *service = SMAppService.mainAppService;
        if ((enabled && service.status == SMAppServiceStatusEnabled) || (!enabled && service.status == SMAppServiceStatusNotRegistered)) return;
        if (enabled && service.status == SMAppServiceStatusRequiresApproval) { result = -10002; return; }
        NSError *error = nil;
        BOOL ok = enabled ? [service registerAndReturnError:&error] : [service unregisterAndReturnError:&error];
        if (!ok) result = (int)(error.code ?: -1);
        else if (enabled && service.status == SMAppServiceStatusRequiresApproval) result = -10002;
    } @catch (NSException *exception) { result = -10003; }
    }); return result;
}
