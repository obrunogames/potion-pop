// Potion Pop! — native Google Sign-In for iOS (GoogleSignIn SDK ~> 8.0, added by EDM4U from
// Assets/_Game/Editor/PotionPopDependencies.xml).
//
// C# side: PotionPop.Services.NativeSignIn (DllImport "__Internal").
// Result is sent back with UnitySendMessage(objectName, method, payload):
//   "OK|<google id token>"   or   "ERR|<code>|<message>"   (code: cancelled | network | config | failed)
//
// Requirements (done by the iOS build post-process): the REVERSED iOS client id
// (com.googleusercontent.apps.XXXX) must be registered as a URL scheme in Info.plist, otherwise GIDSignIn throws.
// The configuration (client ids) is set in code, so GIDClientID in Info.plist is optional.
//
// The OAuth redirect comes back through UnityAppController's application:openURL:options:, which posts the
// "kUnityOnOpenURL" notification (userInfo[@"url"]). We observe it from +load instead of subclassing the app
// controller (AppleAuth / Google Mobile Ads may already do that) or swizzling.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <GoogleSignIn/GoogleSignIn.h>

extern "C" UIViewController* UnityGetGLViewController();
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* SPString(const char* value)
{
    return value != NULL ? [NSString stringWithUTF8String:value] : @"";
}

static NSString* SPClean(NSString* value)
{
    if (value == nil) return @"";
    NSString* clean = [value stringByReplacingOccurrencesOfString:@"\n" withString:@" "];
    return [clean stringByReplacingOccurrencesOfString:@"\r" withString:@" "];
}

static void SPSend(NSString* objectName, NSString* method, NSString* payload)
{
    UnitySendMessage(objectName.UTF8String, method.UTF8String, payload.UTF8String);
}

static void SPSendError(NSString* objectName, NSString* method, NSString* code, NSString* message)
{
    SPSend(objectName, method, [NSString stringWithFormat:@"ERR|%@|%@", code, SPClean(message)]);
}

static UIViewController* SPTopViewController()
{
    UIViewController* controller = UnityGetGLViewController();
    while (controller.presentedViewController != nil && !controller.presentedViewController.isBeingDismissed)
        controller = controller.presentedViewController;
    return controller;
}

static NSString* SPErrorCode(NSError* error)
{
    if ([error.domain isEqualToString:kGIDSignInErrorDomain] && error.code == kGIDSignInErrorCodeCanceled) return @"cancelled";
    if ([error.domain isEqualToString:NSURLErrorDomain]) return @"network";
    return @"failed";
}

extern "C" void _SP_GoogleSignIn(const char* clientId, const char* serverClientId, const char* objectName, const char* method)
{
    // Copy the C strings now: the marshaled buffers are freed when this call returns.
    NSString* client = SPString(clientId);
    NSString* serverClient = SPString(serverClientId);
    NSString* target = SPString(objectName);
    NSString* callback = SPString(method);

    dispatch_async(dispatch_get_main_queue(), ^{
        if (client.length == 0)
        {
            SPSendError(target, callback, @"config", @"missing iOS client id");
            return;
        }
        UIViewController* presenter = SPTopViewController();
        if (presenter == nil)
        {
            SPSendError(target, callback, @"failed", @"no view controller to present from");
            return;
        }
        @try
        {
            GIDConfiguration* configuration = serverClient.length > 0
                ? [[GIDConfiguration alloc] initWithClientID:client serverClientID:serverClient]
                : [[GIDConfiguration alloc] initWithClientID:client];
            GIDSignIn.sharedInstance.configuration = configuration;
            [GIDSignIn.sharedInstance signInWithPresentingViewController:presenter
                                                              completion:^(GIDSignInResult* result, NSError* error) {
                if (error != nil)
                {
                    SPSendError(target, callback, SPErrorCode(error), error.localizedDescription);
                    return;
                }
                NSString* token = result.user.idToken.tokenString;
                if (token.length == 0)
                {
                    SPSendError(target, callback, @"failed", @"no id token");
                    return;
                }
                SPSend(target, callback, [@"OK|" stringByAppendingString:token]);
            }];
        }
        @catch (NSException* exception)
        {
            // Typically the URL scheme (reversed client id) is missing from Info.plist.
            SPSendError(target, callback, @"config", exception.reason);
        }
    });
}

extern "C" void _SP_GoogleSignOut()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        [GIDSignIn.sharedInstance signOut];
    });
}

// Forwards the OAuth redirect URL to GoogleSignIn (needed when the flow falls back to the browser).
@interface SPGoogleSignInURLObserver : NSObject
@end

@implementation SPGoogleSignInURLObserver

+ (void)load
{
    [[NSNotificationCenter defaultCenter] addObserverForName:@"kUnityOnOpenURL"
                                                      object:nil
                                                       queue:[NSOperationQueue mainQueue]
                                                  usingBlock:^(NSNotification* note) {
        id url = note.userInfo[@"url"];
        if ([url isKindOfClass:[NSURL class]]) [GIDSignIn.sharedInstance handleURL:(NSURL*)url];
    }];
}

@end
